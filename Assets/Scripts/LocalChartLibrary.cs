using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public string DifficultyName;
        public string DifficultyLevel;
        public string GroupId;
        public float BestAccuracy = -1f;
        public string Format;
        public string SourceFile;
        public int NoteCount;
        public long ImportedAtUnixMilliseconds;
    }

    public static class LocalChartLibrary
    {
        const string ManifestFile = "library.json";
        const string DifficultyTagsFile = "difficulty-tags.json";

        static string Root => Path.Combine(Application.persistentDataPath, "ChartLibrary");
        static string ManifestPath => Path.Combine(Root, ManifestFile);
        static string DifficultyTagsPath => Path.Combine(Root, DifficultyTagsFile);

        public static IReadOnlyList<string> LoadDifficultyTags()
        {
            var tags = ReadDifficultyTags();
            var imported = Load().Select(entry => NormalizeTag(entry.DifficultyName)).Where(tag => tag != "未標示");
            foreach (var tag in imported)
                if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) tags.Add(tag);
            SaveDifficultyTags(tags);
            return tags;
        }

        public static bool TryCreateDifficultyTag(string value, out string error)
        {
            var tag = NormalizeTag(value);
            if (tag == "未標示") { error = "標籤不可空白。"; return false; }
            var tags = ReadDifficultyTags();
            if (tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) { error = "此標籤已存在。"; return false; }
            tags.Add(tag); SaveDifficultyTags(tags); error = string.Empty; return true;
        }

        public static void MoveDifficultyTag(int fromIndex, int toIndex)
        {
            var tags = ReadDifficultyTags();
            if (fromIndex < 0 || fromIndex >= tags.Count || toIndex < 0 || toIndex >= tags.Count || fromIndex == toIndex) return;
            var tag = tags[fromIndex]; tags.RemoveAt(fromIndex); tags.Insert(toIndex, tag); SaveDifficultyTags(tags);
        }

        public static void DeleteDifficultyTag(string value)
        {
            var tag = NormalizeTag(value); var tags = ReadDifficultyTags();
            if (!tags.RemoveAll(item => string.Equals(item, tag, StringComparison.OrdinalIgnoreCase)).Equals(0)) SaveDifficultyTags(tags);
            var entries = Load().ToList();
            foreach (var entry in entries.Where(entry => string.Equals(NormalizeTag(entry.DifficultyName), tag, StringComparison.OrdinalIgnoreCase))) entry.DifficultyName = "未標示";
            Directory.CreateDirectory(Root); File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
        }

        static List<string> ReadDifficultyTags()
        {
            try { return File.Exists(DifficultyTagsPath) ? JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(DifficultyTagsPath)) ?? new List<string>() : new List<string>(); }
            catch { return new List<string>(); }
        }

        static void SaveDifficultyTags(IEnumerable<string> tags)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(DifficultyTagsPath, JsonConvert.SerializeObject(tags.Select(NormalizeTag).Where(tag => tag != "未標示").Distinct(StringComparer.OrdinalIgnoreCase).ToList(), Formatting.Indented));
        }

        static string NormalizeTag(string value) => string.IsNullOrWhiteSpace(value) ? "未標示" : value.Trim();

        public static string Sha256(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return string.Concat(algorithm.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }

        public static LocalChartEntry Save(string fileName, byte[] bytes, RuntimeChart chart, string groupId = null)
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
            entry.DifficultyName = chart.DifficultyName;
            entry.DifficultyLevel = chart.DifficultyLevel;
            entry.GroupId = string.IsNullOrWhiteSpace(groupId)
                ? string.IsNullOrWhiteSpace(entry.GroupId) ? NewGroupId() : entry.GroupId
                : groupId;
            entry.Format = chart.SourceFormat;
            entry.SourceFile = sourceName;
            entry.NoteCount = chart.PlayableCount;
            entry.ImportedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            entries.RemoveAll(value => value.Id == id);
            entries.Insert(0, entry);
            File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
            return entry;
        }

        public static string NewGroupId() => Guid.NewGuid().ToString("N");

        public static string FindMatchingGroupId(string title, string artist)
        {
            var key = Normalize(title);
            return Load().FirstOrDefault(entry => Normalize(entry.Title) == key)?.GroupId;
        }

        public static bool TryReadSource(LocalChartEntry entry, out byte[] bytes)
        {
            bytes = null;
            if (entry == null || string.IsNullOrWhiteSpace(entry.SourceFile) || Path.IsPathRooted(entry.SourceFile)) return false;
            try
            {
                var path = Path.Combine(Root, entry.SourceFile);
                if (!File.Exists(path)) return false;
                bytes = File.ReadAllBytes(path);
                return bytes.Length > 0;
            }
            catch (Exception) { return false; }
        }

        public static void UpdateBestAccuracy(string id, float accuracy)
        {
            if (string.IsNullOrWhiteSpace(id) || float.IsNaN(accuracy) || float.IsInfinity(accuracy)) return;
            var entries = Load().ToList();
            var entry = entries.FirstOrDefault(value => value.Id == id);
            if (entry == null || accuracy <= entry.BestAccuracy) return;
            entry.BestAccuracy = accuracy;
            Directory.CreateDirectory(Root);
            File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
        }

        /// <summary>
        /// Updates the shared song metadata for a merged group and the selected
        /// difficulty's chart constant.  A title cannot be blank because it is
        /// the primary label used by the library and grouping UI.
        /// </summary>
        public static bool TryUpdateChartDetails(string id, string title, string artist, string difficultyName, string difficultyLevel, out LocalChartEntry updated)
        {
            updated = null;
            if (string.IsNullOrWhiteSpace(id)) return false;

            title = (title ?? string.Empty).Trim();
            if (title.Length == 0) return false;

            var entries = Load().ToList();
            var entry = entries.FirstOrDefault(value => value.Id == id);
            if (entry == null) return false;

            var groupId = entry.GroupId;
            foreach (var groupedEntry in entries.Where(value => value.GroupId == groupId))
            {
                groupedEntry.Title = title;
                groupedEntry.Artist = (artist ?? string.Empty).Trim();
            }

            entry.DifficultyName = (difficultyName ?? string.Empty).Trim();
            entry.DifficultyLevel = (difficultyLevel ?? string.Empty).Trim();
            Directory.CreateDirectory(Root);
            File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(entries, Formatting.Indented));
            updated = entry;
            return true;
        }

        /// <summary>Removes one stored chart entry and its private source package.</summary>
        public static bool TryDelete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            var entries = Load().ToList();
            var entry = entries.FirstOrDefault(value => value.Id == id);
            if (entry == null) return false;

            entries.Remove(entry);
            Directory.CreateDirectory(Root);
            File.WriteAllText(ManifestPath, JsonConvert.SerializeObject(entries, Formatting.Indented));

            // SourceFile is stored as a filename; reject rooted values so a
            // corrupt manifest can never delete something outside ChartLibrary.
            if (!string.IsNullOrWhiteSpace(entry.SourceFile) && !Path.IsPathRooted(entry.SourceFile))
            {
                try
                {
                    var sourcePath = Path.Combine(Root, entry.SourceFile);
                    if (File.Exists(sourcePath)) File.Delete(sourcePath);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("譜面索引已刪除，但無法移除原始 GGR：" + exception.Message);
                }
            }
            return true;
        }

        public static IReadOnlyList<LocalChartEntry> Load()
        {
            try
            {
                if (!File.Exists(ManifestPath)) return Array.Empty<LocalChartEntry>();
                var entries = JsonConvert.DeserializeObject<List<LocalChartEntry>>(File.ReadAllText(ManifestPath)) ?? new List<LocalChartEntry>();
                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.GroupId)) entry.GroupId = LegacyGroupId(entry.Title, entry.Artist);
                    if (float.IsNaN(entry.BestAccuracy) || float.IsInfinity(entry.BestAccuracy)) entry.BestAccuracy = -1f;
                    entry.DifficultyName ??= string.Empty;
                    entry.DifficultyLevel ??= string.Empty;
                }
                return entries;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("本機曲庫索引損壞，將以空索引啟動：" + exception.Message);
                return Array.Empty<LocalChartEntry>();
            }
        }

        static string GroupKey(string title, string artist) => Normalize(title) + "\u001f" + Normalize(artist);

        static string LegacyGroupId(string title, string artist) => "legacy-" + Sha256(Encoding.UTF8.GetBytes(GroupKey(title, artist)))[..16];

        static string Normalize(string value) => string.Join(" ", (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormKC).Split((char[])null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
    }
}
