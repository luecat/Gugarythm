using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Gugarythm
{
    public static class BundledChartLibraryImporter
    {
        public const string Version = "2026-08-22-bundled-ggr-1";

        const string PreferenceKey = "gugarythm-bundled-charts-version";
        const string ManifestPath = "BundledCharts/bundled-ggr.txt";

        public static IEnumerator ImportAll(Action<string> report = null)
        {
            if (PlayerPrefs.GetString(PreferenceKey, string.Empty) == Version) yield break;

            byte[] manifestBytes = null;
            string manifestError = null;
            yield return ReadStreamingAsset(ManifestPath, bytes => manifestBytes = bytes, error => manifestError = error);
            if (manifestBytes == null)
            {
                Debug.LogWarning("內建 GGR manifest 無法讀取：" + manifestError);
                yield break;
            }

            var names = ParseManifest(Encoding.UTF8.GetString(manifestBytes));
            var existingIds = new HashSet<string>(LocalChartLibrary.Load()
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.Id))
                .Select(entry => entry.Id), StringComparer.OrdinalIgnoreCase);
            var importedCount = 0;
            var complete = true;

            foreach (var name in names)
            {
                report?.Invoke("正在加入 " + Path.GetFileNameWithoutExtension(name) + "…");
                byte[] bytes = null;
                string error = null;
                yield return ReadStreamingAsset("BundledCharts/" + name, value => bytes = value, value => error = value);
                if (bytes == null || bytes.Length == 0)
                {
                    complete = false;
                    Debug.LogWarning("內建 GGR 無法讀取：" + name + "，" + error);
                    continue;
                }

                var id = LocalChartLibrary.Sha256(bytes);
                if (existingIds.Contains(id)) continue;

                var result = new GgrChartImporter().Import(name, bytes, null);
                if (!result.Success)
                {
                    complete = false;
                    Debug.LogWarning("內建 GGR 無法匯入：" + name + "，" + result.Error);
                    continue;
                }

                var groupId = LocalChartLibrary.FindMatchingGroupId(result.Chart.Title, result.Chart.Artist);
                LocalChartLibrary.Save(name, bytes, result.Chart,
                    string.IsNullOrWhiteSpace(groupId) ? LocalChartLibrary.NewGroupId() : groupId);
                existingIds.Add(id);
                importedCount++;
                yield return null;
            }

            if (!complete) yield break;
            PlayerPrefs.SetString(PreferenceKey, Version);
            PlayerPrefs.Save();
            Debug.Log("GUGARYTHM_BUNDLED_CHARTS_IMPORTED count=" + importedCount);
        }

        static string[] ParseManifest(string text) => (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        static IEnumerator ReadStreamingAsset(string relativePath, Action<byte[]> onSuccess, Action<string> onError)
        {
            var root = Application.streamingAssetsPath.TrimEnd('/', '\\');
            var url = root.Contains("://", StringComparison.Ordinal)
                ? root + "/" + relativePath
                : "file://" + root + "/" + relativePath;
            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }
                onSuccess?.Invoke(request.downloadHandler.data);
            }
        }
    }
}
