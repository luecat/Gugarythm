using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Gugarythm
{
    public sealed class GgrPackageException : Exception
    {
        public GgrPackageException(string message) : base(message) { }
    }

    public sealed class GgrPackage
    {
        public byte[] ManifestBytes;
        public byte[] ChartBytes;
        public string AudioName;
        public byte[] AudioBytes;
        public byte[] MetadataBytes;
        public string CoverName;
        public byte[] CoverBytes;
    }

    public static class GgrPackageReader
    {
        public const string InvalidZip = "不是有效的 GGR ZIP 封包。";
        public const string MissingManifest = "GGR 缺少 manifest.json。";
        public const string UnsafePath = "GGR 包含不安全的檔案路徑。";
        public const string Oversized = "GGR 封包過大或壓縮資料異常。";

        const int MaxEntries = 8;
        const long MaxBytes = 256L * 1024 * 1024;
        const long RatioAllowance = 1024L * 1024;
        static readonly UTF8Encoding Utf8 = new(false, true);

        public static GgrPackage Read(byte[] archiveBytes)
        {
            if (archiveBytes == null || archiveBytes.Length == 0) throw new GgrPackageException(InvalidZip);
            var metadata = ReadCentralDirectory(archiveBytes);
            if (metadata.Count > MaxEntries) throw new GgrPackageException(Oversized);
            var entries = new Dictionary<string, EntryMetadata>(StringComparer.Ordinal);
            var audioCount = 0;
            var coverCount = 0;
            foreach (var entry in metadata)
            {
                ValidateEntryMetadata(entry);
                if (!entries.TryAdd(entry.Name, entry)) throw new GgrPackageException(UnsafePath);
                if (entry.Name.StartsWith("audio.", StringComparison.Ordinal) && ++audioCount > 1) throw new GgrPackageException(UnsafePath);
                if (entry.Name.StartsWith("cover.", StringComparison.Ordinal) && ++coverCount > 1) throw new GgrPackageException(UnsafePath);
            }
            if (!entries.ContainsKey("manifest.json")) throw new GgrPackageException(MissingManifest);

            try
            {
                using var input = new MemoryStream(archiveBytes, false);
                using var archive = new ZipArchive(input, ZipArchiveMode.Read, false, Utf8);
                if (archive.Entries.Count != entries.Count) throw new GgrPackageException(InvalidZip);
                var extracted = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                foreach (var pair in entries)
                {
                    var zipEntry = archive.GetEntry(pair.Key);
                    if (zipEntry == null || zipEntry.FullName != pair.Key ||
                        zipEntry.CompressedLength != pair.Value.CompressedSize ||
                        zipEntry.Length != pair.Value.UncompressedSize)
                        throw new GgrPackageException(InvalidZip);
                    extracted[pair.Key] = ReadEntry(zipEntry, pair.Value);
                }
                return new GgrPackage
                {
                    ManifestBytes = extracted["manifest.json"],
                    ChartBytes = extracted.TryGetValue("chart.usc", out var chart) ? chart : null,
                    AudioName = FindByPrefix(entries, "audio."),
                    AudioBytes = FindBytes(extracted, entries, "audio."),
                    MetadataBytes = extracted.TryGetValue("metadata.json", out var metadataBytes) ? metadataBytes : null,
                    CoverName = FindByPrefix(entries, "cover."),
                    CoverBytes = FindBytes(extracted, entries, "cover."),
                };
            }
            catch (GgrPackageException) { throw; }
            catch (InvalidDataException) { throw new GgrPackageException(InvalidZip); }
            catch (IOException) { throw new GgrPackageException(InvalidZip); }
        }

        static byte[] FindBytes(IReadOnlyDictionary<string, byte[]> extracted, IReadOnlyDictionary<string, EntryMetadata> entries, string prefix)
        {
            var name = FindByPrefix(entries, prefix);
            return name != null && extracted.TryGetValue(name, out var bytes) ? bytes : null;
        }

        static string FindByPrefix(IReadOnlyDictionary<string, EntryMetadata> entries, string prefix)
        {
            foreach (var name in entries.Keys) if (name.StartsWith(prefix, StringComparison.Ordinal)) return name;
            return null;
        }

        static void ValidateEntryMetadata(EntryMetadata entry)
        {
            if (entry.Encrypted || entry.CompressionMethod is not 0 and not 8) throw new GgrPackageException(InvalidZip);
            if (!IsSafeCanonicalName(entry.Name)) throw new GgrPackageException(UnsafePath);
            if (entry.CompressedSize > MaxBytes || entry.UncompressedSize > MaxBytes) throw new GgrPackageException(Oversized);
            if (entry.CompressedSize == 0 && entry.UncompressedSize != 0) throw new GgrPackageException(Oversized);
            if (entry.CompressedSize > 0 && entry.UncompressedSize > entry.CompressedSize * 100 + RatioAllowance)
                throw new GgrPackageException(Oversized);
        }

        static bool IsSafeCanonicalName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.Contains("..", StringComparison.Ordinal)) return false;
            if (name is "manifest.json" or "chart.usc" or "metadata.json") return true;
            if (name.StartsWith("audio.", StringComparison.Ordinal)) return IsAllowedExtension(name, "audio.", "mp3", "ogg", "wav", "m4a", "aac", "flac");
            if (name.StartsWith("cover.", StringComparison.Ordinal)) return IsAllowedExtension(name, "cover.", "png", "jpg", "jpeg", "webp");
            return false;
        }

        static bool IsAllowedExtension(string name, string prefix, params string[] extensions)
        {
            var extension = name[prefix.Length..];
            foreach (var allowed in extensions) if (extension == allowed) return true;
            return false;
        }

        static byte[] ReadEntry(ZipArchiveEntry entry, EntryMetadata metadata)
        {
            using var source = entry.Open();
            using var output = new MemoryStream((int)Math.Min(metadata.UncompressedSize, 1024 * 1024));
            var buffer = new byte[81920];
            long total = 0;
            while (true)
            {
                var read = source.Read(buffer, 0, buffer.Length);
                if (read == 0) break;
                total += read;
                if (total > MaxBytes || total > metadata.UncompressedSize ||
                    (metadata.CompressedSize == 0 ? total != 0 : total > metadata.CompressedSize * 100 + RatioAllowance))
                    throw new GgrPackageException(Oversized);
                output.Write(buffer, 0, read);
            }
            if (total != metadata.UncompressedSize) throw new GgrPackageException(InvalidZip);
            return output.ToArray();
        }

        static List<EntryMetadata> ReadCentralDirectory(byte[] bytes)
        {
            try
            {
                var eocd = FindEndOfCentralDirectory(bytes);
                if (eocd < 0 || ReadU16(bytes, eocd + 4) != 0 || ReadU16(bytes, eocd + 6) != 0)
                    throw new GgrPackageException(InvalidZip);

                var entriesOnDisk = ReadU16(bytes, eocd + 8);
                var entryCount = ReadU16(bytes, eocd + 10);
                var directorySize = ReadU32(bytes, eocd + 12);
                var directoryOffset = ReadU32(bytes, eocd + 16);
                if (entriesOnDisk != entryCount || entryCount == ushort.MaxValue ||
                    directorySize == uint.MaxValue || directoryOffset == uint.MaxValue)
                    throw new GgrPackageException(InvalidZip);
                if (entryCount > MaxEntries) throw new GgrPackageException(Oversized);

                var directoryEnd = (long)directoryOffset + directorySize;
                if (directoryEnd > eocd) throw new GgrPackageException(InvalidZip);

                var result = new List<EntryMetadata>(entryCount);
                var offset = checked((int)directoryOffset);
                var end = checked((int)directoryEnd);
                for (var index = 0; index < entryCount; index++)
                {
                    if (offset + 46 > end || ReadU32(bytes, offset) != 0x02014b50) throw new GgrPackageException(InvalidZip);
                    var flags = ReadU16(bytes, offset + 8);
                    var method = ReadU16(bytes, offset + 10);
                    var compressed = ReadU32(bytes, offset + 20);
                    var uncompressed = ReadU32(bytes, offset + 24);
                    var nameLength = ReadU16(bytes, offset + 28);
                    var extraLength = ReadU16(bytes, offset + 30);
                    var commentLength = ReadU16(bytes, offset + 32);
                    var localOffset = ReadU32(bytes, offset + 42);
                    var recordEnd = checked(offset + 46 + nameLength + extraLength + commentLength);
                    if (recordEnd > end || compressed == uint.MaxValue || uncompressed == uint.MaxValue || localOffset == uint.MaxValue)
                        throw new GgrPackageException(InvalidZip);
                    string name;
                    try { name = Utf8.GetString(bytes, offset + 46, nameLength); }
                    catch (DecoderFallbackException) { throw new GgrPackageException(UnsafePath); }
                    result.Add(new EntryMetadata(name, flags, method, compressed, uncompressed));
                    offset = recordEnd;
                }
                if (offset != end) throw new GgrPackageException(InvalidZip);
                return result;
            }
            catch (GgrPackageException) { throw; }
            catch (OverflowException) { throw new GgrPackageException(InvalidZip); }
        }

        static int FindEndOfCentralDirectory(byte[] bytes)
        {
            var start = Math.Max(0, bytes.Length - 65557);
            for (var offset = bytes.Length - 22; offset >= start; offset--)
                if (ReadU32(bytes, offset) == 0x06054b50 && offset + 22 + ReadU16(bytes, offset + 20) == bytes.Length) return offset;
            return -1;
        }

        static ushort ReadU16(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 2 > bytes.Length) throw new GgrPackageException(InvalidZip);
            return (ushort)(bytes[offset] | bytes[offset + 1] << 8);
        }

        static uint ReadU32(byte[] bytes, int offset)
        {
            if (offset < 0 || offset + 4 > bytes.Length) throw new GgrPackageException(InvalidZip);
            return (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);
        }

        readonly struct EntryMetadata
        {
            public readonly string Name;
            public readonly bool Encrypted;
            public readonly ushort CompressionMethod;
            public readonly long CompressedSize;
            public readonly long UncompressedSize;

            public EntryMetadata(string name, ushort flags, ushort compressionMethod, uint compressedSize, uint uncompressedSize)
            {
                Name = name;
                // Bit 13 masks local-header values and is only meaningful with
                // central-directory encryption, so it is unsafe as well.
                Encrypted = (flags & 0x2041) != 0;
                CompressionMethod = compressionMethod;
                CompressedSize = compressedSize;
                UncompressedSize = uncompressedSize;
            }
        }
    }
}
