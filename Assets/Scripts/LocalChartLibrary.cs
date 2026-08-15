using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEngine;

namespace Gugarythm
{
    [Serializable]
    public sealed class LocalChartEntry
    {
        public string Id;
        public string Title;
        public string Artist;
        public string Author;
        public string Format;
        public string SourceFile;
        public int NoteCount;
        public long ImportedAtUnixMilliseconds;
    }

    public static class LocalChartLibrary
    {
        const string ManifestFile = "library.json";

        static string Root => Path.Combine(Application.persistentDataPath, "ChartLibrary");
        static string ManifestPath => Path.Combine(Root, ManifestFile);

        public static string Sha256(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        public static LocalChartEntry Save(string fileName, byte[] bytes, RuntimeChart chart)
        {
            Directory.CreateDirectory(Root);
            var id = Sha256(bytes);
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".chart";
            var sourceName = id + extension.ToLowerInvariant();
            var sourcePath = Path.Combine(Root, sourceName);
            if (!File.Exists(sourcePath)) File.WriteAllBytes(sourcePath, bytes);

            var entries = Load().ToList();
            var entry = entries.FirstOrDefault(value => value.Id == id) ?? new LocalChartEntry { Id = id };
            entry.Title = chart.Title;
            entry.Artist = chart.Artist;
            entry.Author = chart.Author;
            entry.Format = chart.SourceFormat;
            entry.SourceFile = sourceName;
            entry.NoteCount = chart.PlayableCount;
            entry.ImportedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            entries.RemoveAll(value => value.Id == id);
            entries.Insert(0, entry);
            File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
            return entry;
        }

        public static IReadOnlyList<LocalChartEntry> Load()
        {
            try
            {
                if (!File.Exists(ManifestPath)) return Array.Empty<LocalChartEntry>();
                return JsonConvert.DeserializeObject<List<LocalChartEntry>>(File.ReadAllText(ManifestPath)) ?? new List<LocalChartEntry>();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("本機曲庫索引損壞，將以空索引啟動：" + exception.Message);
                return Array.Empty<LocalChartEntry>();
            }
        }
    }
}
