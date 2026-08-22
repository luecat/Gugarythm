using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Gugarhythm
{
    public sealed class ChartPackage
    {
        public string ChartName;
        public byte[] ChartBytes;
        public IReadOnlyDictionary<string, byte[]> Files;
    }

    public static class ChartPackageReader
    {
        const int MaxEntries = 512;
        const long MaxEntryBytes = 64L * 1024 * 1024;
        const long MaxPackageBytes = 256L * 1024 * 1024;
        static readonly string[] PreferredExtensions = { ".usc" };

        public static ChartPackage ReadZip(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new InvalidDataException("ZIP 是空的。");
            using var input = new MemoryStream(bytes, false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, false);
            if (archive.Entries.Count > MaxEntries) throw new InvalidDataException($"ZIP 檔案數超過 {MaxEntries} 個。");
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.Length < 0 || entry.Length > MaxEntryBytes) throw new InvalidDataException("ZIP 內單一檔案過大：" + entry.FullName);
                total += entry.Length;
                if (total > MaxPackageBytes) throw new InvalidDataException("ZIP 解壓後總大小超過 256 MiB。");
                using var stream = entry.Open();
                using var output = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
                stream.CopyTo(output);
                files[Normalize(entry.FullName)] = output.ToArray();
            }

            return SelectChart(files);
        }

        public static ChartPackage ReadFolder(string root)
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (files.Count >= MaxEntries) throw new InvalidDataException($"資料夾檔案數超過 {MaxEntries} 個。");
                var info = new FileInfo(file);
                if (info.Length > MaxEntryBytes) throw new InvalidDataException("資料夾內單一檔案過大：" + info.Name);
                total += info.Length;
                if (total > MaxPackageBytes) throw new InvalidDataException("資料夾內容超過 256 MiB。");
                var fullPath = Path.GetFullPath(file);
                if (!fullPath.StartsWith(rootPath, StringComparison.Ordinal)) throw new InvalidDataException("資料夾含有不安全路徑。");
                files[Normalize(fullPath[rootPath.Length..])] = File.ReadAllBytes(fullPath);
            }
            return SelectChart(files);
        }

        static ChartPackage SelectChart(IReadOnlyDictionary<string, byte[]> files)
        {
            var chartName = PreferredExtensions.SelectMany(extension => files.Keys.Where(path => Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(path => path.Count(character => character == '/')).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (chartName == null) throw new InvalidDataException("ZIP 中找不到 USC 譜面。");
            return new ChartPackage { ChartName = chartName, ChartBytes = files[chartName], Files = files };
        }

        static string Normalize(string path)
        {
            var normalized = path.Replace('\\', '/').TrimStart('/');
            if (normalized.Split('/').Any(part => part == "..")) throw new InvalidDataException("ZIP 含有不安全路徑。");
            return normalized;
        }
    }
}
