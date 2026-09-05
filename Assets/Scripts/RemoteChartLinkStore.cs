using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Gugarhythm
{
    public sealed class RemoteChartLinkStore : IRemoteChartLinkStore
    {
        const int CurrentSchemaVersion = 1;

        readonly object sync = new();
        readonly string path;

        [Serializable]
        sealed class RemoteChartLinkFile
        {
            [JsonProperty("schemaVersion")] public int SchemaVersion;
            [JsonProperty("links")] public List<RemoteChartLink> Links;
        }

        public RemoteChartLinkStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Link-store path is required.", nameof(path));
            this.path = Path.GetFullPath(path);
        }

        public bool TryGet(string chartId, int version, out RemoteChartLink link)
        {
            link = null;
            if (string.IsNullOrWhiteSpace(chartId) || version < 1) return false;

            lock (sync)
            {
                var found = LoadFromDisk().FirstOrDefault(candidate =>
                    string.Equals(candidate.ChartId, chartId, StringComparison.Ordinal) && candidate.Version == version);
                if (found == null) return false;
                link = Copy(found);
                return true;
            }
        }

        public bool TryGetLatestForChart(string chartId, out RemoteChartLink link)
        {
            link = null;
            if (string.IsNullOrWhiteSpace(chartId)) return false;

            lock (sync)
            {
                var found = LoadFromDisk()
                    .Where(candidate => string.Equals(candidate.ChartId, chartId, StringComparison.Ordinal))
                    .OrderByDescending(candidate => candidate.Version)
                    .FirstOrDefault();
                if (found == null) return false;
                link = Copy(found);
                return true;
            }
        }

        public void Upsert(RemoteChartLink link)
        {
            if (!IsValid(link)) throw new ArgumentException("Remote chart link is invalid.", nameof(link));

            lock (sync)
            {
                var links = LoadFromDisk();
                links.RemoveAll(candidate =>
                    string.Equals(candidate.ChartId, link.ChartId, StringComparison.Ordinal) && candidate.Version == link.Version);
                links.Add(Copy(link));
                Save(links);
            }
        }

        public IReadOnlyList<RemoteChartLink> Load()
        {
            lock (sync)
            {
                return LoadFromDisk().Select(Copy).ToArray();
            }
        }

        List<RemoteChartLink> LoadFromDisk()
        {
            if (!File.Exists(path)) return new List<RemoteChartLink>();

            try
            {
                var file = JsonConvert.DeserializeObject<RemoteChartLinkFile>(File.ReadAllText(path));
                if (file == null || file.SchemaVersion != CurrentSchemaVersion || file.Links == null ||
                    file.Links.Any(link => !IsValid(link)))
                    throw new JsonSerializationException("Remote chart link sidecar is invalid.");
                return file.Links.Select(Copy).ToList();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("遠端譜面連結索引損壞，將以空索引啟動：" + exception.Message);
                return new List<RemoteChartLink>();
            }
        }

        void Save(List<RemoteChartLink> links)
        {
            var file = new RemoteChartLinkFile
            {
                SchemaVersion = CurrentSchemaVersion,
                Links = links.Select(Copy).ToList(),
            };
            ChartVaultAtomicJsonFile.Write(path, JsonConvert.SerializeObject(file, Formatting.Indented));
        }

        static bool IsValid(RemoteChartLink link)
        {
            if (link == null || string.IsNullOrWhiteSpace(link.ChartId) || link.Version < 1 ||
                string.IsNullOrWhiteSpace(link.LocalEntryId) || link.DownloadedAtUnixMilliseconds < 0 ||
                link.Sha256 == null || link.Sha256.Length != 64)
                return false;
            foreach (var character in link.Sha256)
                if (!(character >= '0' && character <= '9') && !(character >= 'a' && character <= 'f'))
                    return false;
            return true;
        }

        static RemoteChartLink Copy(RemoteChartLink link) => new()
        {
            ChartId = link.ChartId,
            Version = link.Version,
            Sha256 = link.Sha256,
            LocalEntryId = link.LocalEntryId,
            DownloadedAtUnixMilliseconds = link.DownloadedAtUnixMilliseconds,
        };
    }

    internal static class ChartVaultAtomicJsonFile
    {
        public static void Write(string destinationPath, string json)
        {
            var fullPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Destination directory is required.", nameof(destinationPath));

            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(directory,
                Path.GetFileName(fullPath) + ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
                if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
                else File.Move(temporaryPath, fullPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
    }
}
