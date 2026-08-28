using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Gugarhythm
{
    public static class InputDiagnosticsChartLoader
    {
        public const string RelativePath = "DebugCharts/Input-Diagnostics.ggr";

        public static string BuildStreamingAssetUrl(string streamingAssetsPath, string relativePath = RelativePath)
        {
            if (string.IsNullOrWhiteSpace(streamingAssetsPath)) return string.Empty;
            var normalizedRelativePath = (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
            if (streamingAssetsPath.Contains("://", StringComparison.Ordinal))
                return streamingAssetsPath.TrimEnd('/') + "/" + normalizedRelativePath;
            return new Uri(Path.Combine(streamingAssetsPath, normalizedRelativePath)).AbsoluteUri;
        }

        public static IEnumerator Load(Action<byte[], string> completed)
        {
            var url = BuildStreamingAssetUrl(Application.streamingAssetsPath);
            if (string.IsNullOrEmpty(url))
            {
                completed?.Invoke(null, "找不到 StreamingAssets 路徑。");
                yield break;
            }

            using var request = UnityWebRequest.Get(url);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                completed?.Invoke(null, $"測試譜面載入失敗：{request.error}");
                yield break;
            }

            var bytes = request.downloadHandler?.data;
            if (bytes == null || bytes.Length == 0)
            {
                completed?.Invoke(null, "測試譜面內容是空的。");
                yield break;
            }
            completed?.Invoke(bytes, string.Empty);
        }
    }
}
