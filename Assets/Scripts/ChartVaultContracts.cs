using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Gugarhythm
{
    public enum ChartLibrarySource
    {
        Local,
        Online,
    }

    [Serializable]
    public sealed class RemoteChartSummary
    {
        [JsonProperty("chartId")] public string ChartId;
        [JsonProperty("version")] public int Version;
        [JsonProperty("title")] public string Title;
        [JsonProperty("artist")] public string Artist;
        [JsonProperty("author")] public string Author;
        [JsonProperty("difficulty")] public string Difficulty;
        [JsonProperty("rating")] public float Rating;
        [JsonProperty("offset")] public double Offset;
        [JsonProperty("visibility")] public string Visibility;
        [JsonProperty("updatedAt")] public string UpdatedAt;
        [JsonProperty("sha256")] public string Sha256;
        [JsonProperty("sizeBytes")] public long SizeBytes;
        [JsonProperty("coverUrl")] public string CoverUrl;
        [JsonProperty("downloadUrl")] public string DownloadUrl;

        public bool IsPrivate => Visibility == "private";
    }

    [Serializable]
    public sealed class RemoteChartCatalog
    {
        [JsonProperty("charts")] public List<RemoteChartSummary> Charts = new();
        [JsonProperty("nextCursor")] public string NextCursor;
        [JsonProperty("cachedAtUnixMilliseconds")] public long CachedAtUnixMilliseconds;
    }

    public enum RemoteChartCatalogScope
    {
        Public,
        Private,
    }

    public readonly struct ChartVaultCatalogResult
    {
        public readonly RemoteChartCatalog Catalog;
        public readonly string Error;
        public readonly bool Unauthorized;
        public bool Success => Catalog != null && string.IsNullOrEmpty(Error);

        public ChartVaultCatalogResult(RemoteChartCatalog catalog, string error, bool unauthorized = false)
        {
            Catalog = catalog;
            Error = error;
            Unauthorized = unauthorized;
        }
    }

    public readonly struct ChartVaultDownloadResult
    {
        public readonly string Error;
        public readonly long ContentLength;
        public readonly string Sha256Header;
        public readonly bool Unauthorized;
        public bool Success => string.IsNullOrEmpty(Error);

        public ChartVaultDownloadResult(string error, long contentLength, string sha256Header, bool unauthorized = false)
        {
            Error = error;
            ContentLength = contentLength;
            Sha256Header = sha256Header;
            Unauthorized = unauthorized;
        }
    }

    public readonly struct ChartVaultSessionResult
    {
        public readonly string SessionToken;
        public readonly string Error;
        public bool Success => !string.IsNullOrEmpty(SessionToken) && string.IsNullOrEmpty(Error);

        public ChartVaultSessionResult(string sessionToken, string error)
        {
            SessionToken = sessionToken;
            Error = error;
        }
    }

    public readonly struct ChartVaultAppSessionResult
    {
        public readonly string DisplayName;
        public readonly string ExpiresAt;
        public readonly int DeviceCount;
        public readonly string Error;
        public readonly bool Unauthorized;
        public bool Success => !string.IsNullOrEmpty(DisplayName) && string.IsNullOrEmpty(Error);

        public ChartVaultAppSessionResult(string displayName, string expiresAt, int deviceCount, string error,
            bool unauthorized = false)
        {
            DisplayName = displayName;
            ExpiresAt = expiresAt;
            DeviceCount = deviceCount;
            Error = error;
            Unauthorized = unauthorized;
        }
    }

    public readonly struct RemoteChartImportResult
    {
        public readonly LocalChartEntry LocalEntry;
        public readonly bool AlreadyDownloaded;
        public readonly string Error;
        public readonly bool Unauthorized;
        public bool Success => LocalEntry != null && string.IsNullOrEmpty(Error);

        public RemoteChartImportResult(LocalChartEntry localEntry, bool alreadyDownloaded, string error,
            bool unauthorized = false)
        {
            LocalEntry = localEntry;
            AlreadyDownloaded = alreadyDownloaded;
            Error = error;
            Unauthorized = unauthorized;
        }
    }

    public interface IChartVaultClient
    {
        IEnumerator FetchPublicCatalog(Action<ChartVaultCatalogResult> complete);
        IEnumerator FetchCatalog(RemoteChartCatalogScope scope,
            Action<ChartVaultCatalogResult> complete, string sessionToken);
        IEnumerator DownloadGgr(RemoteChartSummary chart, string destinationPath,
            Action<ChartVaultDownloadResult> complete);
        IEnumerator DownloadGgr(RemoteChartSummary chart, string destinationPath,
            Action<ChartVaultDownloadResult> complete, string sessionToken);
        IEnumerator DownloadCover(RemoteChartSummary chart, Action<Texture2D, string> complete);
        IEnumerator DownloadCover(RemoteChartSummary chart, Action<Texture2D, string> complete,
            string sessionToken);
        IEnumerator ExchangeAppLoginHandoff(string code, string codeVerifier,
            Action<ChartVaultSessionResult> complete);
        IEnumerator LogoutAppSession(string sessionToken, Action<bool> complete);
        IEnumerator GetAppSession(string sessionToken, Action<ChartVaultAppSessionResult> complete);
    }

    public interface IChartVaultFileStore
    {
        string CreateTemporaryPath(string extension);
        bool TryGetLength(string path, out long length);
        bool TryComputeSha256(string path, out string sha256);
        bool TryReadAllBytes(string path, out byte[] bytes);
        void DeleteIfExists(string path);
    }

    public interface ILocalChartLibraryGateway
    {
        IReadOnlyList<LocalChartEntry> Load();
        string FindMatchingGroupId(string title, string artist);
        string NewGroupId();
        LocalChartEntry Save(string fileName, byte[] bytes, RuntimeChart chart, string groupId);
    }

    public interface IRemoteChartLinkStore
    {
        bool TryGet(string chartId, int version, out RemoteChartLink link);
        bool TryGetLatestForChart(string chartId, out RemoteChartLink link);
        void Upsert(RemoteChartLink link);
    }

    [Serializable]
    public sealed class RemoteChartLink
    {
        [JsonProperty("chartId")] public string ChartId;
        [JsonProperty("version")] public int Version;
        [JsonProperty("sha256")] public string Sha256;
        [JsonProperty("localEntryId")] public string LocalEntryId;
        [JsonProperty("downloadedAtUnixMilliseconds")] public long DownloadedAtUnixMilliseconds;
    }

    public static class ChartVaultApiSettings
    {
        public const string ApiOrigin = "https://gugarhythm.luecat.com";
        public const string PublicCatalogPath = "/api/v1/charts?scope=public&limit=30";
        public const string PrivateCatalogPath = "/api/v1/charts?scope=mine&visibility=private&limit=30";
        public const long MaxGgrBytes = 48L * 1024L * 1024L;

        const string ApiPathPrefix = "/api/v1/";
        const int MaxApiPathLength = 4096;
        static readonly Uri ApiOriginUri = new(ApiOrigin, UriKind.Absolute);

        public static string BuildCatalogPath(RemoteChartCatalogScope scope, int limit, string cursor)
        {
            if (limit < 1 || limit > 50) throw new ArgumentOutOfRangeException(nameof(limit));
            var path = scope == RemoteChartCatalogScope.Private
                ? "/api/v1/charts?scope=mine&visibility=private&limit=" + limit
                : "/api/v1/charts?scope=public&limit=" + limit;
            if (!string.IsNullOrWhiteSpace(cursor))
                path += "&cursor=" + Uri.EscapeDataString(cursor);
            return path;
        }

        public static bool TryResolveApiPath(string path, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrEmpty(path) || path.Length > MaxApiPathLength ||
                !path.StartsWith(ApiPathPrefix, StringComparison.Ordinal) ||
                path.StartsWith("//", StringComparison.Ordinal) ||
                path.IndexOf('\\') >= 0 || path.IndexOf('#') >= 0 ||
                ContainsControlOrSpace(path) || !HasValidPercentEncoding(path) ||
                ContainsTraversalOrEscapedSeparator(path))
                return false;

            if (!Uri.TryCreate(path, UriKind.Relative, out var relative) ||
                !Uri.TryCreate(ApiOriginUri, relative, out var resolved))
                return false;

            if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(resolved.IdnHost, ApiOriginUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
                resolved.Port != ApiOriginUri.Port ||
                !string.IsNullOrEmpty(resolved.UserInfo) ||
                !string.IsNullOrEmpty(resolved.Fragment) ||
                !resolved.AbsolutePath.StartsWith(ApiPathPrefix, StringComparison.Ordinal))
                return false;

            uri = resolved;
            return true;
        }

        static bool ContainsControlOrSpace(string value)
        {
            foreach (var character in value)
                if (character <= ' ' || character == '\u007f')
                    return true;
            return false;
        }

        static bool HasValidPercentEncoding(string value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != '%') continue;
                if (index + 2 >= value.Length || !IsHex(value[index + 1]) || !IsHex(value[index + 2]))
                    return false;
                index += 2;
            }
            return true;
        }

        static bool ContainsTraversalOrEscapedSeparator(string value)
        {
            var queryIndex = value.IndexOf('?');
            var rawPath = queryIndex >= 0 ? value.Substring(0, queryIndex) : value;
            foreach (var segment in rawPath.Split('/'))
            {
                var decoded = segment;
                for (var pass = 0; pass <= segment.Length; pass++)
                {
                    string next;
                    try
                    {
                        next = Uri.UnescapeDataString(decoded);
                    }
                    catch (UriFormatException)
                    {
                        return true;
                    }

                    if (string.Equals(next, ".", StringComparison.Ordinal) ||
                        string.Equals(next, "..", StringComparison.Ordinal) ||
                        next.IndexOf('/') >= 0 || next.IndexOf('\\') >= 0)
                        return true;
                    if (string.Equals(next, decoded, StringComparison.Ordinal)) break;
                    decoded = next;
                }
            }
            return false;
        }

        static bool IsHex(char value) =>
            value >= '0' && value <= '9' ||
            value >= 'a' && value <= 'f' ||
            value >= 'A' && value <= 'F';
    }

    public static class RemoteChartCatalogCodec
    {
        const int MaxChartIdLength = 128;

        public static bool TryParse(string json, out RemoteChartCatalog catalog, out string error)
        {
            catalog = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
                return Fail(out error, "遠端譜面清單是空的。");

            RemoteChartCatalog parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<RemoteChartCatalog>(json);
            }
            catch (Exception exception) when (exception is JsonException ||
                                              exception is FormatException ||
                                              exception is OverflowException)
            {
                return Fail(out error, "遠端譜面清單格式錯誤。");
            }

            if (parsed == null)
                return Fail(out error, "遠端譜面清單格式錯誤。");
            if (parsed.Charts == null)
                return Fail(out error, "遠端譜面清單缺少 charts 陣列。");

            foreach (var chart in parsed.Charts)
            {
                if (chart == null)
                    return Fail(out error, "遠端譜面清單包含空白項目。");
                if (!IsValidChartId(chart.ChartId))
                    return Fail(out error, "遠端譜面 ID 無效。");
                if (chart.Version < 1)
                    return Fail(out error, "遠端譜面版本無效。");
                if (chart.SizeBytes < 0 || chart.SizeBytes > ChartVaultApiSettings.MaxGgrBytes)
                    return Fail(out error, "遠端譜面檔案大小無效。");
                if (chart.Visibility != "public" && chart.Visibility != "private")
                    return Fail(out error, "遠端譜面可見度無效。");
                if (!IsFinite(chart.Rating) || !IsFinite(chart.Offset))
                    return Fail(out error, "遠端譜面數值無效。");
                if (!IsLowercaseSha256(chart.Sha256))
                    return Fail(out error, "遠端譜面 SHA-256 無效。");
                if (!IsExpectedResourcePath(chart, chart.DownloadUrl, "ggr"))
                    return Fail(out error, "遠端譜面下載路徑無效。");
                if (chart.CoverUrl != null && !IsExpectedResourcePath(chart, chart.CoverUrl, "cover"))
                    return Fail(out error, "遠端譜面封面路徑無效。");
            }

            // The API does not own the local cache timestamp, even if a future
            // response happens to contain a field with the same name.
            parsed.CachedAtUnixMilliseconds = 0;
            catalog = parsed;
            return true;
        }

        static bool IsValidChartId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= "chart_".Length ||
                value.Length > MaxChartIdLength || !value.StartsWith("chart_", StringComparison.Ordinal))
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

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                    return false;
            return true;
        }

        static bool IsExpectedResourcePath(RemoteChartSummary chart, string path, string resource)
        {
            if (!ChartVaultApiSettings.TryResolveApiPath(path, out var uri) || !string.IsNullOrEmpty(uri.Query))
                return false;
            var expected = "/api/v1/charts/" + chart.ChartId + "/versions/" + chart.Version + "/" + resource;
            return string.Equals(uri.AbsolutePath, expected, StringComparison.Ordinal);
        }

        static bool Fail(out string error, string message)
        {
            error = message;
            return false;
        }
    }
}
