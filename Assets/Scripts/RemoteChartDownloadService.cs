using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Gugarhythm
{
    public sealed class RemoteChartDownloadService
    {
        const string InvalidChartError = "遠端譜面資料無效。";
        const string InvalidDownloadPathError = "遠端譜面下載路徑無效。";
        const string SizeLimitError = "遠端譜面檔案大小超過 48 MiB 上限。";
        const string DownloadError = "遠端譜面下載失敗，請稍後再試。";
        const string MissingLengthError = "伺服器未提供有效的譜面檔案大小。";
        const string LengthMismatchError = "下載的譜面檔案大小不一致。";
        const string MissingHashError = "伺服器未提供有效的譜面 SHA-256。";
        const string HashMismatchError = "下載的譜面 SHA-256 不一致。";
        const string ImportError = "GGR 譜面格式無效，無法匯入。";
        const string SaveError = "無法將遠端譜面儲存到本機曲庫。";
        const string UnexpectedError = "遠端譜面處理失敗，請稍後再試。";

        readonly IChartVaultClient client;
        readonly IChartVaultFileStore files;
        readonly IChartImporter importer;
        readonly ILocalChartLibraryGateway library;
        readonly IRemoteChartLinkStore links;
        readonly Func<long> nowUnixMilliseconds;

        public RemoteChartDownloadService(IChartVaultClient client, IChartVaultFileStore files,
            IChartImporter importer, ILocalChartLibraryGateway library, IRemoteChartLinkStore links,
            Func<long> nowUnixMilliseconds)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.files = files ?? throw new ArgumentNullException(nameof(files));
            this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.links = links ?? throw new ArgumentNullException(nameof(links));
            this.nowUnixMilliseconds = nowUnixMilliseconds ??
                throw new ArgumentNullException(nameof(nowUnixMilliseconds));
        }

        public IEnumerator DownloadAndImport(RemoteChartSummary chart, Action<RemoteChartImportResult> complete)
        {
            var completion = new CompletionGate(complete);
            if (!TryValidateChart(chart, out var validationError))
            {
                completion.Invoke(Failure(validationError));
                yield break;
            }

            if (!TryFindExistingLocalLink(chart, out var existingEntry))
            {
                completion.Invoke(Failure(UnexpectedError));
                yield break;
            }
            if (existingEntry != null)
            {
                completion.Invoke(new RemoteChartImportResult(existingEntry, true, string.Empty));
                yield break;
            }

            if (!TryCreateTemporaryPath(out var temporaryPath))
            {
                completion.Invoke(Failure(UnexpectedError));
                yield break;
            }

            IEnumerator download = null;
            try
            {
                var response = default(ChartVaultDownloadResult);
                var responseReceived = false;
                if (!TryCreateDownload(chart, temporaryPath, result =>
                    {
                        if (responseReceived) return;
                        response = result;
                        responseReceived = true;
                    }, out download, out var createDownloadFailed) || download == null)
                {
                    completion.Invoke(Failure(createDownloadFailed ? UnexpectedError : DownloadError));
                    yield break;
                }

                while (true)
                {
                    if (!TryAdvance(download, out var hasNext, out var current))
                    {
                        completion.Invoke(Failure(UnexpectedError));
                        yield break;
                    }
                    if (!hasNext) break;
                    yield return current;
                }

                if (!responseReceived || !response.Success)
                {
                    completion.Invoke(Failure(DownloadError));
                    yield break;
                }
                if (response.ContentLength < 0)
                {
                    completion.Invoke(Failure(MissingLengthError));
                    yield break;
                }
                if (response.ContentLength != chart.SizeBytes)
                {
                    completion.Invoke(Failure(LengthMismatchError));
                    yield break;
                }
                if (!TryGetLength(temporaryPath, out var actualLength, out var lengthAdapterFailed) ||
                    actualLength != chart.SizeBytes)
                {
                    completion.Invoke(Failure(lengthAdapterFailed ? UnexpectedError : LengthMismatchError));
                    yield break;
                }
                if (!IsLowercaseSha256(response.Sha256Header))
                {
                    completion.Invoke(Failure(MissingHashError));
                    yield break;
                }
                if (!string.Equals(response.Sha256Header, chart.Sha256, StringComparison.Ordinal))
                {
                    completion.Invoke(Failure(HashMismatchError));
                    yield break;
                }
                if (!TryComputeSha256(temporaryPath, out var actualSha256, out var hashAdapterFailed) ||
                    !string.Equals(actualSha256, chart.Sha256, StringComparison.Ordinal))
                {
                    completion.Invoke(Failure(hashAdapterFailed ? UnexpectedError : HashMismatchError));
                    yield break;
                }
                if (!TryReadAllBytes(temporaryPath, out var bytes, out var readAdapterFailed) || bytes == null ||
                    bytes.LongLength != chart.SizeBytes)
                {
                    completion.Invoke(Failure(readAdapterFailed ? UnexpectedError : LengthMismatchError));
                    yield break;
                }
                if (!TryComputeBytesSha256(bytes, out var importedBytesSha256) ||
                    !string.Equals(importedBytesSha256, chart.Sha256, StringComparison.Ordinal))
                {
                    completion.Invoke(Failure(HashMismatchError));
                    yield break;
                }
                if (!TryImport(chart.Title + ".ggr", bytes, out var imported, out var importerFailed) ||
                    imported == null || !imported.Success)
                {
                    completion.Invoke(Failure(importerFailed ? UnexpectedError : ImportError));
                    yield break;
                }
                if (!TrySaveAndLink(chart, bytes, imported.Chart, out var savedEntry, out var saveError))
                {
                    completion.Invoke(Failure(saveError));
                    yield break;
                }

                completion.Invoke(new RemoteChartImportResult(savedEntry, false, string.Empty));
            }
            finally
            {
                SafeDispose(download);
                SafeDelete(temporaryPath);
            }
        }

        bool TryFindExistingLocalLink(RemoteChartSummary chart, out LocalChartEntry existingEntry)
        {
            existingEntry = null;
            try
            {
                if (!links.TryGet(chart.ChartId, chart.Version, out var link) || link == null ||
                    !string.Equals(link.ChartId, chart.ChartId, StringComparison.Ordinal) ||
                    link.Version != chart.Version ||
                    !string.Equals(link.Sha256, chart.Sha256, StringComparison.Ordinal) ||
                    !string.Equals(link.LocalEntryId, chart.Sha256, StringComparison.Ordinal))
                    return true;

                var entries = library.Load();
                if (entries == null) return true;
                foreach (var entry in entries)
                {
                    if (entry == null ||
                        !string.Equals(entry.Id, link.LocalEntryId, StringComparison.Ordinal) ||
                        !string.Equals(entry.Id, chart.Sha256, StringComparison.Ordinal))
                        continue;
                    existingEntry = entry;
                    break;
                }
                return true;
            }
            catch (Exception)
            {
                existingEntry = null;
                return false;
            }
        }

        bool TryCreateTemporaryPath(out string path)
        {
            path = null;
            try
            {
                path = files.CreateTemporaryPath(".ggr");
                return !string.IsNullOrWhiteSpace(path);
            }
            catch (Exception)
            {
                path = null;
                return false;
            }
        }

        bool TryCreateDownload(RemoteChartSummary chart, string path,
            Action<ChartVaultDownloadResult> complete, out IEnumerator download, out bool adapterFailed)
        {
            download = null;
            adapterFailed = false;
            try
            {
                download = client.DownloadGgr(chart, path, complete);
                return download != null;
            }
            catch (Exception)
            {
                download = null;
                adapterFailed = true;
                return false;
            }
        }

        static bool TryAdvance(IEnumerator enumerator, out bool hasNext, out object current)
        {
            hasNext = false;
            current = null;
            try
            {
                hasNext = enumerator.MoveNext();
                if (hasNext) current = enumerator.Current;
                return true;
            }
            catch (Exception)
            {
                hasNext = false;
                current = null;
                return false;
            }
        }

        bool TryGetLength(string path, out long length, out bool adapterFailed)
        {
            length = 0;
            adapterFailed = false;
            try { return files.TryGetLength(path, out length); }
            catch (Exception) { adapterFailed = true; return false; }
        }

        bool TryComputeSha256(string path, out string sha256, out bool adapterFailed)
        {
            sha256 = null;
            adapterFailed = false;
            try { return files.TryComputeSha256(path, out sha256); }
            catch (Exception) { adapterFailed = true; return false; }
        }

        bool TryReadAllBytes(string path, out byte[] bytes, out bool adapterFailed)
        {
            bytes = null;
            adapterFailed = false;
            try { return files.TryReadAllBytes(path, out bytes); }
            catch (Exception) { adapterFailed = true; return false; }
        }

        bool TryImport(string fileName, byte[] bytes, out ImportResult result, out bool adapterFailed)
        {
            result = null;
            adapterFailed = false;
            try
            {
                result = importer.Import(fileName, bytes, null);
                return true;
            }
            catch (Exception)
            {
                result = null;
                adapterFailed = true;
                return false;
            }
        }

        static bool TryComputeBytesSha256(byte[] bytes, out string sha256)
        {
            sha256 = null;
            if (bytes == null) return false;
            try
            {
                using var algorithm = SHA256.Create();
                var hash = algorithm.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) builder.Append(value.ToString("x2"));
                sha256 = builder.ToString();
                return true;
            }
            catch (Exception)
            {
                sha256 = null;
                return false;
            }
        }

        bool TrySaveAndLink(RemoteChartSummary remote, byte[] bytes, RuntimeChart imported,
            out LocalChartEntry savedEntry, out string error)
        {
            savedEntry = null;
            error = UnexpectedError;
            try
            {
                var groupId = library.FindMatchingGroupId(imported.Title, imported.Artist);
                if (string.IsNullOrWhiteSpace(groupId)) groupId = library.NewGroupId();
                savedEntry = library.Save(remote.Title + ".ggr", bytes, imported, groupId);
                if (savedEntry == null ||
                    !string.Equals(savedEntry.Id, remote.Sha256, StringComparison.Ordinal))
                {
                    savedEntry = null;
                    error = SaveError;
                    return false;
                }

                var downloadedAt = nowUnixMilliseconds();
                if (downloadedAt < 0)
                {
                    savedEntry = null;
                    return false;
                }
                links.Upsert(new RemoteChartLink
                {
                    ChartId = remote.ChartId,
                    Version = remote.Version,
                    Sha256 = remote.Sha256,
                    LocalEntryId = savedEntry.Id,
                    DownloadedAtUnixMilliseconds = downloadedAt,
                });
                error = string.Empty;
                return true;
            }
            catch (Exception)
            {
                savedEntry = null;
                error = UnexpectedError;
                return false;
            }
        }

        void SafeDelete(string path)
        {
            try { files.DeleteIfExists(path); }
            catch (Exception) { }
        }

        static void SafeDispose(IEnumerator enumerator)
        {
            try { (enumerator as IDisposable)?.Dispose(); }
            catch (Exception) { }
        }

        static bool TryValidateChart(RemoteChartSummary chart, out string error)
        {
            error = InvalidChartError;
            if (chart == null || !IsValidChartId(chart.ChartId) || chart.Version < 1 ||
                !IsLowercaseSha256(chart.Sha256) || chart.SizeBytes < 0)
                return false;
            if (chart.SizeBytes > ChartVaultApiSettings.MaxGgrBytes)
            {
                error = SizeLimitError;
                return false;
            }
            if (!ChartVaultApiSettings.TryResolveApiPath(chart.DownloadUrl, out var downloadUri) ||
                !string.IsNullOrEmpty(downloadUri.Query) ||
                !string.Equals(downloadUri.AbsolutePath,
                    "/api/v1/charts/" + chart.ChartId + "/versions/" + chart.Version + "/ggr",
                    StringComparison.Ordinal))
            {
                error = InvalidDownloadPathError;
                return false;
            }
            error = string.Empty;
            return true;
        }

        static bool IsValidChartId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= "chart_".Length ||
                value.Length > 128 || !value.StartsWith("chart_", StringComparison.Ordinal))
                return false;
            for (var index = "chart_".Length; index < value.Length; index++)
            {
                var character = value[index];
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '-' && character != '_')
                    return false;
            }
            return true;
        }

        static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                    return false;
            return true;
        }

        static RemoteChartImportResult Failure(string error) =>
            new(null, false, string.IsNullOrWhiteSpace(error) ? UnexpectedError : error);

        sealed class CompletionGate
        {
            readonly Action<RemoteChartImportResult> complete;
            bool invoked;

            public CompletionGate(Action<RemoteChartImportResult> complete) => this.complete = complete;

            public void Invoke(RemoteChartImportResult result)
            {
                if (invoked) return;
                invoked = true;
                complete?.Invoke(result);
            }
        }
    }

    public sealed class LocalChartLibraryGateway : ILocalChartLibraryGateway
    {
        public IReadOnlyList<LocalChartEntry> Load() => LocalChartLibrary.Load();

        public string FindMatchingGroupId(string title, string artist) =>
            LocalChartLibrary.FindMatchingGroupId(title, artist);

        public string NewGroupId() => LocalChartLibrary.NewGroupId();

        public LocalChartEntry Save(string fileName, byte[] bytes, RuntimeChart chart, string groupId) =>
            LocalChartLibrary.Save(fileName, bytes, chart, groupId);
    }

    public sealed class PhysicalChartVaultFileStore : IChartVaultFileStore
    {
        const string FilePrefix = "chart-vault-";

        public string CreateTemporaryPath(string extension)
        {
            if (!string.Equals(extension, ".ggr", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only GGR temporary files are supported.", nameof(extension));
            var root = CacheRoot();
            Directory.CreateDirectory(root);
            return Path.Combine(root, FilePrefix + Guid.NewGuid().ToString("N") + ".ggr");
        }

        public bool TryGetLength(string path, out long length)
        {
            length = 0;
            if (!TryResolveOwnedPath(path, out var fullPath)) return false;
            try
            {
                var file = new FileInfo(fullPath);
                if (!file.Exists) return false;
                length = file.Length;
                return length >= 0;
            }
            catch (Exception)
            {
                length = 0;
                return false;
            }
        }

        public bool TryComputeSha256(string path, out string sha256)
        {
            sha256 = null;
            if (!TryResolveOwnedPath(path, out var fullPath)) return false;
            try
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.SequentialScan);
                using var algorithm = SHA256.Create();
                var hash = algorithm.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var value in hash) builder.Append(value.ToString("x2"));
                sha256 = builder.ToString();
                return true;
            }
            catch (Exception)
            {
                sha256 = null;
                return false;
            }
        }

        public bool TryReadAllBytes(string path, out byte[] bytes)
        {
            bytes = null;
            if (!TryResolveOwnedPath(path, out var fullPath)) return false;
            try
            {
                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.SequentialScan);
                var length = stream.Length;
                if (length < 0 || length > ChartVaultApiSettings.MaxGgrBytes || length > int.MaxValue)
                    return false;
                var result = new byte[(int)length];
                var offset = 0;
                while (offset < result.Length)
                {
                    var read = stream.Read(result, offset, result.Length - offset);
                    if (read <= 0) return false;
                    offset += read;
                }
                if (stream.ReadByte() >= 0 || stream.Length != length) return false;
                bytes = result;
                return true;
            }
            catch (Exception)
            {
                bytes = null;
                return false;
            }
        }

        public void DeleteIfExists(string path)
        {
            if (!TryResolveOwnedPath(path, out var fullPath)) return;
            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
            }
            catch (Exception) { }
        }

        static bool TryResolveOwnedPath(string path, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                var root = CacheRoot();
                var candidate = Path.GetFullPath(path);
                var comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
                if (!string.Equals(Path.GetDirectoryName(candidate), root, comparison)) return false;
                var fileName = Path.GetFileName(candidate);
                if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) ||
                    !fileName.EndsWith(".ggr", StringComparison.OrdinalIgnoreCase))
                    return false;
                fullPath = candidate;
                return true;
            }
            catch (Exception)
            {
                fullPath = null;
                return false;
            }
        }

        static string CacheRoot()
        {
            if (string.IsNullOrWhiteSpace(Application.temporaryCachePath))
                throw new InvalidOperationException("Unity temporary cache path is unavailable.");
            return Path.GetFullPath(Application.temporaryCachePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
