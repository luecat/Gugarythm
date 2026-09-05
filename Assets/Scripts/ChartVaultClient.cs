using System;
using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

namespace Gugarhythm
{
    public sealed class ChartVaultClient : IChartVaultClient
    {
        const ulong MaxCatalogResponseBytes = 1024UL * 1024UL;
        const int MaxChartIdLength = 128;
        const string CatalogDownloadError = "無法取得遠端譜面清單，請稍後再試。";
        const string CatalogFormatError = "遠端譜面清單格式錯誤。";
        const string InvalidChartError = "遠端譜面資料無效。";
        const string InvalidDownloadPathError = "遠端譜面下載路徑無效。";
        const string InvalidDestinationError = "遠端譜面暫存路徑無效。";
        const string SizeLimitError = "遠端譜面檔案大小超過 48 MiB 上限。";
        const string GgrDownloadError = "遠端譜面下載失敗，請稍後再試。";
        const string CoverDownloadError = "遠端譜面封面下載失敗，請稍後再試。";
        const string AppLoginExchangeError = "登入交接失敗，請回到遊戲後重新登入。";
        const string AppSessionInfoError = "無法取得帳號資訊，請重新登入。";

        readonly int timeoutSeconds;

        public ChartVaultClient(int timeoutSeconds = 30)
        {
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            this.timeoutSeconds = timeoutSeconds;
        }

        public IEnumerator FetchPublicCatalog(Action<ChartVaultCatalogResult> complete)
        {
            var operation = FetchCatalog(RemoteChartCatalogScope.Public, complete, string.Empty);
            while (operation.MoveNext()) yield return operation.Current;
        }

        public IEnumerator FetchCatalog(RemoteChartCatalogScope scope,
            Action<ChartVaultCatalogResult> complete, string sessionToken)
        {
            var completion = new CompletionGate<ChartVaultCatalogResult>(complete);
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation;
            try
            {
                var uri = new Uri(ChartVaultApiSettings.ApiOrigin +
                                  ChartVaultApiSettings.BuildCatalogPath(scope, 30, null), UriKind.Absolute);
                request = UnityWebRequest.Get(uri);
                ApplyBearer(request, sessionToken);
                request.timeout = timeoutSeconds;
                request.disposeDownloadHandlerOnDispose = true;
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                SafeDispose(request);
                completion.Invoke(CatalogFailure(CatalogDownloadError));
                yield break;
            }

            try
            {
                yield return operation;
                completion.Invoke(ReadCatalogResult(request));
            }
            finally
            {
                SafeDispose(request);
                if (!completion.Invoked) completion.Invoke(CatalogFailure(CatalogDownloadError));
            }
        }

        public IEnumerator DownloadGgr(RemoteChartSummary chart, string destinationPath,
            Action<ChartVaultDownloadResult> complete)
        {
            var operation = DownloadGgr(chart, destinationPath, complete, string.Empty);
            while (operation.MoveNext()) yield return operation.Current;
        }

        public IEnumerator DownloadGgr(RemoteChartSummary chart, string destinationPath,
            Action<ChartVaultDownloadResult> complete, string sessionToken)
        {
            var completion = new CompletionGate<ChartVaultDownloadResult>(complete);
            if (!TryPrepareDownload(chart, destinationPath, out var uri, out var error))
            {
                completion.Invoke(DownloadFailure(error));
                yield break;
            }

            DownloadHandlerFile handler = null;
            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation;
            try
            {
                handler = new DownloadHandlerFile(destinationPath) { removeFileOnAbort = true };
                request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, handler, null)
                {
                    timeout = timeoutSeconds,
                    disposeDownloadHandlerOnDispose = true,
                };
                handler = null;
                ApplyBearer(request, sessionToken);
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                SafeDispose(request);
                SafeDispose(handler);
                completion.Invoke(DownloadFailure(GgrDownloadError));
                yield break;
            }

            try
            {
                yield return operation;
                completion.Invoke(ReadDownloadResult(request));
            }
            finally
            {
                SafeDispose(request);
                if (!completion.Invoked) completion.Invoke(DownloadFailure(GgrDownloadError));
            }
        }

        public IEnumerator DownloadCover(RemoteChartSummary chart, Action<Texture2D, string> complete)
        {
            var operation = DownloadCover(chart, complete, string.Empty);
            while (operation.MoveNext()) yield return operation.Current;
        }

        public IEnumerator DownloadCover(RemoteChartSummary chart, Action<Texture2D, string> complete,
            string sessionToken)
        {
            var completion = new CompletionGate<CoverResult>(result =>
                complete?.Invoke(result.Texture, result.Error));
            if (chart != null && chart.CoverUrl == null)
            {
                completion.Invoke(new CoverResult(null, string.Empty));
                yield break;
            }
            if (!TryPrepareCover(chart, out var uri))
            {
                completion.Invoke(new CoverResult(null, InvalidDownloadPathError));
                yield break;
            }

            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation;
            try
            {
                request = UnityWebRequestTexture.GetTexture(uri, true);
                request.timeout = timeoutSeconds;
                request.disposeDownloadHandlerOnDispose = true;
                ApplyBearer(request, sessionToken);
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                SafeDispose(request);
                completion.Invoke(new CoverResult(null, CoverDownloadError));
                yield break;
            }

            try
            {
                yield return operation;
                completion.Invoke(ReadCoverResult(request));
            }
            finally
            {
                SafeDispose(request);
                if (!completion.Invoked)
                    completion.Invoke(new CoverResult(null, CoverDownloadError));
            }
        }

        public IEnumerator ExchangeAppLoginHandoff(string code, string codeVerifier,
            Action<ChartVaultSessionResult> complete)
        {
            var completion = new CompletionGate<ChartVaultSessionResult>(complete);
            if (!IsSessionToken(code) || !IsSessionToken(codeVerifier) ||
                !ChartVaultApiSettings.TryResolveApiPath("/api/v1/app-auth/handoffs/exchange", out var uri))
            {
                completion.Invoke(new ChartVaultSessionResult(null, AppLoginExchangeError));
                yield break;
            }

            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation;
            try
            {
                var body = JsonConvert.SerializeObject(new { code, codeVerifier });
                request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST)
                {
                    uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                    downloadHandler = new DownloadHandlerBuffer(),
                    timeout = timeoutSeconds,
                    disposeUploadHandlerOnDispose = true,
                    disposeDownloadHandlerOnDispose = true,
                };
                request.SetRequestHeader("Content-Type", "application/json");
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                SafeDispose(request);
                completion.Invoke(new ChartVaultSessionResult(null, AppLoginExchangeError));
                yield break;
            }

            yield return operation;
            try
            {
                if (!RequestSucceeded(request) || request.downloadHandler == null)
                    completion.Invoke(new ChartVaultSessionResult(null, AppLoginExchangeError));
                else
                {
                    var payload = JsonConvert.DeserializeObject<AppLoginExchangeResponse>(request.downloadHandler.text);
                    completion.Invoke(payload != null && IsSessionToken(payload.SessionToken)
                        ? new ChartVaultSessionResult(payload.SessionToken, string.Empty)
                        : new ChartVaultSessionResult(null, AppLoginExchangeError));
                }
            }
            catch (Exception)
            {
                completion.Invoke(new ChartVaultSessionResult(null, AppLoginExchangeError));
            }
            finally
            {
                SafeDispose(request);
                if (!completion.Invoked) completion.Invoke(new ChartVaultSessionResult(null, AppLoginExchangeError));
            }
        }

        public IEnumerator LogoutAppSession(string sessionToken, Action<bool> complete)
        {
            if (!IsSessionToken(sessionToken) ||
                !ChartVaultApiSettings.TryResolveApiPath("/api/v1/app-auth/sessions/logout", out var uri))
            {
                complete?.Invoke(false);
                yield break;
            }
            using var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
                disposeDownloadHandlerOnDispose = true,
            };
            request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
            yield return request.SendWebRequest();
            complete?.Invoke(request.result == UnityWebRequest.Result.Success);
        }

        public IEnumerator GetAppSession(string sessionToken, Action<ChartVaultAppSessionResult> complete)
        {
            var completion = new CompletionGate<ChartVaultAppSessionResult>(complete);
            if (!IsSessionToken(sessionToken) ||
                !ChartVaultApiSettings.TryResolveApiPath("/api/v1/app-session", out var uri))
            {
                completion.Invoke(new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError));
                yield break;
            }

            UnityWebRequest request = null;
            UnityWebRequestAsyncOperation operation;
            try
            {
                request = UnityWebRequest.Get(uri);
                request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
                request.timeout = timeoutSeconds;
                request.disposeDownloadHandlerOnDispose = true;
                operation = request.SendWebRequest();
            }
            catch (Exception)
            {
                SafeDispose(request);
                completion.Invoke(new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError));
                yield break;
            }

            yield return operation;
            try
            {
                completion.Invoke(ReadAppSessionResult(request));
            }
            catch (Exception)
            {
                completion.Invoke(new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError));
            }
            finally
            {
                SafeDispose(request);
                if (!completion.Invoked)
                    completion.Invoke(new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError));
            }
        }

        static ChartVaultAppSessionResult ReadAppSessionResult(UnityWebRequest request)
        {
            if (request != null && request.responseCode == 401)
                return new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError, true);
            if (!RequestSucceeded(request) || request.downloadHandler == null)
                return new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError);
            var payload = JsonConvert.DeserializeObject<AppSessionResponse>(request.downloadHandler.text);
            return payload?.Player != null && !string.IsNullOrEmpty(payload.Player.DisplayName)
                ? new ChartVaultAppSessionResult(payload.Player.DisplayName, payload.ExpiresAt, payload.DeviceCount,
                    string.Empty)
                : new ChartVaultAppSessionResult(null, null, 0, AppSessionInfoError);
        }

        internal static bool TryPrepareDownload(RemoteChartSummary chart, string destinationPath,
            out Uri uri, out string error)
        {
            uri = null;
            error = InvalidChartError;
            if (chart == null || !IsValidChartId(chart.ChartId) || chart.Version < 1 ||
                !IsLowercaseSha256(chart.Sha256) || chart.SizeBytes < 0)
                return false;
            if (chart.SizeBytes > ChartVaultApiSettings.MaxGgrBytes)
            {
                error = SizeLimitError;
                return false;
            }
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                error = InvalidDestinationError;
                return false;
            }
            if (!TryResolveExactResource(chart, chart.DownloadUrl, "ggr", out uri))
            {
                error = InvalidDownloadPathError;
                return false;
            }

            error = string.Empty;
            return true;
        }

        ChartVaultCatalogResult ReadCatalogResult(UnityWebRequest request)
        {
            try
            {
                if (request != null && request.responseCode == 401)
                    return new ChartVaultCatalogResult(null, CatalogDownloadError, true);
                if (!RequestSucceeded(request) || request.downloadHandler == null ||
                    request.downloadedBytes == 0 || request.downloadedBytes > MaxCatalogResponseBytes)
                    return CatalogFailure(CatalogDownloadError);
                if (!RemoteChartCatalogCodec.TryParse(request.downloadHandler.text,
                        out var catalog, out _) || catalog == null)
                    return CatalogFailure(CatalogFormatError);
                catalog.CachedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return new ChartVaultCatalogResult(catalog, string.Empty);
            }
            catch (Exception)
            {
                return CatalogFailure(CatalogDownloadError);
            }
        }

        static ChartVaultDownloadResult ReadDownloadResult(UnityWebRequest request)
        {
            try
            {
                if (request != null && request.responseCode == 401)
                    return new ChartVaultDownloadResult(GgrDownloadError, -1, null, true);
                if (!RequestSucceeded(request)) return DownloadFailure(GgrDownloadError);
                var contentLength = TryParseGgrSizeHeaders(
                    request.GetResponseHeader("Content-Length"), request.GetResponseHeader("X-GGR-Size"),
                    out var parsedLength)
                    ? parsedLength
                    : -1;
                return new ChartVaultDownloadResult(string.Empty, contentLength,
                    request.GetResponseHeader("X-GGR-SHA256"));
            }
            catch (Exception)
            {
                return DownloadFailure(GgrDownloadError);
            }
        }

        static CoverResult ReadCoverResult(UnityWebRequest request)
        {
            try
            {
                if (!RequestSucceeded(request) || request.downloadHandler is not DownloadHandlerTexture)
                    return new CoverResult(null, CoverDownloadError);
                var texture = DownloadHandlerTexture.GetContent(request);
                return texture == null
                    ? new CoverResult(null, CoverDownloadError)
                    : new CoverResult(texture, string.Empty);
            }
            catch (Exception)
            {
                return new CoverResult(null, CoverDownloadError);
            }
        }

        static bool TryPrepareCover(RemoteChartSummary chart, out Uri uri)
        {
            uri = null;
            return chart != null && IsValidChartId(chart.ChartId) && chart.Version >= 1 &&
                   !string.IsNullOrEmpty(chart.CoverUrl) &&
                   TryResolveExactResource(chart, chart.CoverUrl, "cover", out uri);
        }

        static bool TryResolveExactResource(RemoteChartSummary chart, string path, string resource, out Uri uri)
        {
            uri = null;
            if (!ChartVaultApiSettings.TryResolveApiPath(path, out var resolved) ||
                !string.IsNullOrEmpty(resolved.Query))
                return false;
            var expected = string.Join("/", string.Empty, "api", "v1", "charts", chart.ChartId,
                "versions", chart.Version.ToString(CultureInfo.InvariantCulture), resource);
            if (!string.Equals(resolved.AbsolutePath, expected, StringComparison.Ordinal)) return false;
            uri = resolved;
            return true;
        }

        static bool TryParseCanonicalContentLength(string value, out long length)
        {
            length = -1;
            if (string.IsNullOrEmpty(value) || value.Length > 19 || value.Length > 1 && value[0] == '0')
                return false;
            long parsed = 0;
            foreach (var character in value)
            {
                if (character < '0' || character > '9') return false;
                var digit = character - '0';
                if (parsed > (long.MaxValue - digit) / 10) return false;
                parsed = parsed * 10 + digit;
            }
            length = parsed;
            return true;
        }

        internal static bool TryParseGgrSizeHeaders(string contentLength, string ggrSize, out long length)
        {
            length = -1;
            if (string.IsNullOrEmpty(contentLength))
                return TryParseCanonicalContentLength(ggrSize, out length);
            if (!TryParseCanonicalContentLength(contentLength, out var standardLength))
                return false;
            if (!string.IsNullOrEmpty(ggrSize) &&
                (!TryParseCanonicalContentLength(ggrSize, out var fallbackLength) ||
                 fallbackLength != standardLength))
                return false;
            length = standardLength;
            return true;
        }

        static bool RequestSucceeded(UnityWebRequest request) =>
            request != null && request.result == UnityWebRequest.Result.Success;

        // App sessions are Bearer-only. They must never be sent as the website's
        // __Host-ggr_session Cookie: that cookie authenticates full read/write
        // website sessions, while a Bearer token only resolves through
        // resolveReader() into the read-only charts:read scope. Sending an App
        // token as a Cookie would not upgrade its privileges (the backend keeps
        // app_sessions and sessions in separate tables), but it also must never
        // be relied upon — Bearer is the only supported channel for the App.
        static void ApplyBearer(UnityWebRequest request, string sessionToken)
        {
            if (request == null || !IsSessionToken(sessionToken)) return;
            request.SetRequestHeader("Authorization", "Bearer " + sessionToken);
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

        static bool IsLowercaseSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var character in value)
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                    return false;
            return true;
        }

        static bool IsSessionToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 43) return false;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') &&
                    character != '-' && character != '_')
                    return false;
            return true;
        }

        static ChartVaultCatalogResult CatalogFailure(string error) =>
            new(null, string.IsNullOrWhiteSpace(error) ? CatalogDownloadError : error);

        static ChartVaultDownloadResult DownloadFailure(string error) =>
            new(string.IsNullOrWhiteSpace(error) ? GgrDownloadError : error, -1, null);

        static void SafeDispose(IDisposable disposable)
        {
            try { disposable?.Dispose(); }
            catch (Exception) { }
        }

        readonly struct CoverResult
        {
            public readonly Texture2D Texture;
            public readonly string Error;

            public CoverResult(Texture2D texture, string error)
            {
                Texture = texture;
                Error = error;
            }
        }

        [Serializable]
        sealed class AppLoginExchangeResponse
        {
            [JsonProperty("sessionToken")] public string SessionToken;
        }

        [Serializable]
        sealed class AppSessionResponse
        {
            [JsonProperty("player")] public AppSessionPlayer Player;
            [JsonProperty("expiresAt")] public string ExpiresAt;
            [JsonProperty("deviceCount")] public int DeviceCount;
        }

        [Serializable]
        sealed class AppSessionPlayer
        {
            [JsonProperty("displayName")] public string DisplayName;
        }

        sealed class CompletionGate<T>
        {
            readonly Action<T> complete;
            bool invoked;

            public bool Invoked => invoked;

            public CompletionGate(Action<T> complete) => this.complete = complete;

            public void Invoke(T result)
            {
                if (invoked) return;
                invoked = true;
                complete?.Invoke(result);
            }
        }
    }
}
