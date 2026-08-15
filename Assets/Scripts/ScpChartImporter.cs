using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Gugarythm
{
    public sealed class ScpChartImporter : IChartImporter
    {
        const long MaxArchiveBytes = 128L * 1024 * 1024;
        const long MaxEntryBytes = 64L * 1024 * 1024;
        const long MaxInflatedBytes = 256L * 1024 * 1024;

        public bool CanImport(string fileName, byte[] header) =>
            fileName.EndsWith(".scp", StringComparison.OrdinalIgnoreCase) ||
            (header.Length >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K');

        public ImportResult Import(string fileName, byte[] data, IReadOnlyDictionary<string, byte[]> companionFiles = null)
        {
            try
            {
                if (data == null || data.LongLength == 0) return ImportResult.Fail("SCP 是空檔案。");
                if (data.LongLength > MaxArchiveBytes) return ImportResult.Fail("SCP 超過 128 MB 上限。");
                var files = ReadArchive(data);
                if (!files.ContainsKey("sonolus/package")) return ImportResult.Fail("ZIP 不是 Sonolus Collection Package。");
                var detailPath = files.Keys.FirstOrDefault(path => path.StartsWith("sonolus/levels/", StringComparison.Ordinal) &&
                    path != "sonolus/levels/info" && path != "sonolus/levels/list");
                if (detailPath == null) return ImportResult.Fail("SCP 內沒有 level detail。");

                var detail = ParseJson(files[detailPath]);
                var item = (detail["item"] as JObject) ?? detail;
                var levelHash = (string)item["data"]?["hash"];
                if (string.IsNullOrEmpty(levelHash)) return ImportResult.Fail("Level 缺少 data hash。");
                var levelData = ParseGzipJson(GetRepository(files, levelHash));

                JObject playData = null;
                var playHash = (string)item["engine"]?["playData"]?["hash"];
                if (!string.IsNullOrEmpty(playHash) && files.TryGetValue("sonolus/repository/" + playHash.ToLowerInvariant(), out var playBytes))
                    playData = ParseGzipJson(playBytes);

                var chart = BuildRuntimeChart(levelData, playData, "SCP");
                chart.Title = (string)item["title"] ?? chart.Title;
                chart.Artist = (string)item["artists"] ?? "";
                chart.Author = (string)item["author"] ?? "";
                chart.Engine = (string)item["engine"]?["name"] ?? "";
                chart.BgmOffset = (double?)levelData["bgmOffset"] ?? 0;

                var bgmHash = (string)item["bgm"]?["hash"];
                if (!string.IsNullOrEmpty(bgmHash))
                {
                    chart.BgmBytes = GetRepository(files, bgmHash);
                    chart.BgmExtension = DetectAudioExtension(chart.BgmBytes);
                }
                var coverHash = (string)item["cover"]?["hash"];
                if (!string.IsNullOrEmpty(coverHash) && files.TryGetValue("sonolus/repository/" + coverHash.ToLowerInvariant(), out var cover)) chart.CoverBytes = cover;
                return ImportResult.Ok(chart);
            }
            catch (Exception exception)
            {
                return ImportResult.Fail("SCP 解析失敗：" + exception.Message);
            }
        }

        static Dictionary<string, byte[]> ReadArchive(byte[] data)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            using var stream = new MemoryStream(data, false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, Encoding.UTF8);
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                var path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith("/", StringComparison.Ordinal) || path.Split('/').Contains("..")) throw new InvalidDataException("SCP 含不安全路徑：" + path);
                if (entry.Length > MaxEntryBytes) throw new InvalidDataException(path + " 超過單檔大小限制。");
                total += entry.Length;
                if (total > MaxInflatedBytes) throw new InvalidDataException("SCP 解壓後超過 256 MB。");
                using var source = entry.Open();
                using var destination = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
                source.CopyTo(destination);
                files[path] = destination.ToArray();
            }
            return files;
        }

        static byte[] GetRepository(IReadOnlyDictionary<string, byte[]> files, string hash)
        {
            var path = "sonolus/repository/" + hash.ToLowerInvariant();
            if (!files.TryGetValue(path, out var bytes)) throw new InvalidDataException("缺少 repository resource：" + hash);
            return bytes;
        }

        static JObject ParseJson(byte[] bytes) => JObject.Parse(new UTF8Encoding(false, true).GetString(bytes));

        public static JObject ParseGzipJson(byte[] bytes)
        {
            using var source = new MemoryStream(bytes, false);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
            return JObject.Parse(reader.ReadToEnd());
        }

        public static RuntimeChart BuildRuntimeChart(JObject levelData, JObject playData, string sourceFormat)
        {
            var chart = new RuntimeChart { SourceFormat = sourceFormat };
            var playableArchetypes = new HashSet<string>(StringComparer.Ordinal);
            if (playData?["archetypes"] is JArray schemas)
            {
                foreach (var schema in schemas.OfType<JObject>())
                    if ((bool?)schema["hasInput"] == true && schema["name"] != null) playableArchetypes.Add((string)schema["name"]);
            }

            var entities = new List<Entity>();
            if (levelData["entities"] is not JArray sourceEntities) throw new InvalidDataException("LevelData 缺少 entities。");
            for (var index = 0; index < sourceEntities.Count; index++)
            {
                if (sourceEntities[index] is not JObject source) continue;
                var archetype = (string)source["archetype"];
                if (string.IsNullOrEmpty(archetype)) continue;
                var entity = new Entity { Index = index, Name = (string)source["name"], Archetype = archetype };
                if (source["data"] is JArray data)
                {
                    foreach (var field in data.OfType<JObject>())
                    {
                        var name = (string)field["name"];
                        if (string.IsNullOrEmpty(name)) continue;
                        if (field.TryGetValue("value", out var value) && !entity.Values.ContainsKey(name)) entity.Values[name] = value;
                        if (field.TryGetValue("ref", out var reference) && !entity.Refs.ContainsKey(name)) entity.Refs[name] = (string)reference;
                    }
                }
                entity.Beat = Number(entity.Values, "#BEAT");
                entity.Lane = Number(entity.Values, "lane");
                entity.Size = Number(entity.Values, "size");
                entity.Direction = Number(entity.Values, "direction", 0);
                entities.Add(entity);
            }

            var tempo = TempoMap.FromEntities(entities);
            foreach (var entity in entities) entity.Time = tempo.SecondsAt(entity.Beat);
            var byName = entities.Where(entity => !string.IsNullOrEmpty(entity.Name)).GroupBy(entity => entity.Name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var runtimeByEntity = new Dictionary<int, RuntimeNote>();
            foreach (var entity in entities)
            {
                var isNote = entity.Archetype.EndsWith("Note", StringComparison.Ordinal);
                // Sonolus hasInput does not mean "draw and judge this entity".
                // Hold control points such as Ignored/AttachedSlideTick also have
                // input access inside the engine, but must remain connector-only.
                var playable = (playableArchetypes.Count > 0 ? playableArchetypes.Contains(entity.Archetype) : IsFallbackPlayable(entity.Archetype)) &&
                    IsVisiblePlayable(entity.Archetype);
                if (!isNote || !double.IsFinite(entity.Time)) continue;
                var geometry = ResolveGeometry(entity, byName, new HashSet<int>());
                if (!geometry.HasValue)
                {
                    chart.Warnings.Add("無法解析音符位置：" + (entity.Name ?? entity.Index.ToString(CultureInfo.InvariantCulture)));
                    continue;
                }
                var note = new RuntimeNote
                {
                    Index = entity.Index,
                    SourceId = entity.Name ?? "@" + entity.Index,
                    Archetype = entity.Archetype,
                    Beat = entity.Beat,
                    Time = entity.Time,
                    Lane = geometry.Value.lane,
                    Size = Math.Max(.25f, geometry.Value.size),
                    Direction = Math.Clamp((int)Math.Round(entity.Direction), -1, 1),
                    Kind = Classify(entity.Archetype),
                    Critical = entity.Archetype.StartsWith("Critical", StringComparison.Ordinal),
                };
                runtimeByEntity[entity.Index] = note;
                if (playable) chart.Notes.Add(note);
            }

            foreach (var connector in entities.Where(entity => entity.Archetype.EndsWith("Connector", StringComparison.Ordinal)))
            {
                if (!connector.Refs.TryGetValue("start", out var startName) || !connector.Refs.TryGetValue("end", out var endName)) continue;
                if (!byName.TryGetValue(startName, out var startEntity) || !byName.TryGetValue(endName, out var endEntity)) continue;
                if (!runtimeByEntity.TryGetValue(startEntity.Index, out var start) || !runtimeByEntity.TryGetValue(endEntity.Index, out var end)) continue;
                chart.Connectors.Add(new RuntimeConnector
                {
                    Start = start,
                    End = end,
                    Critical = connector.Archetype.StartsWith("Critical", StringComparison.Ordinal),
                    Ease = (int)(Number(connector.Values, "ease", 0)),
                });
            }

            foreach (var entity in entities.Where(entity => entity.Archetype == "SimLine"))
            {
                if (!entity.Refs.TryGetValue("a", out var aName) || !entity.Refs.TryGetValue("b", out var bName) ||
                    !byName.TryGetValue(aName, out var aEntity) || !byName.TryGetValue(bName, out var bEntity) ||
                    !runtimeByEntity.TryGetValue(aEntity.Index, out var a) || !runtimeByEntity.TryGetValue(bEntity.Index, out var b))
                {
                    chart.Warnings.Add("無法解析同步線：" + entity.Index.ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                chart.SimLines.Add(new RuntimeSimLine { A = a, B = b });
            }

            foreach (var entity in entities.Where(entity => entity.Archetype == "Guide"))
            {
                if (!TryGuidePoint(entity, "start", tempo, out var start) ||
                    !TryGuidePoint(entity, "head", tempo, out var head) ||
                    !TryGuidePoint(entity, "tail", tempo, out var tail) ||
                    !TryGuidePoint(entity, "end", tempo, out var end))
                {
                    chart.Warnings.Add("無法解析裝飾 Guide：" + entity.Index.ToString(CultureInfo.InvariantCulture));
                    continue;
                }
                chart.Guides.Add(new RuntimeGuide
                {
                    Start = start,
                    Head = head,
                    Tail = tail,
                    End = end,
                    Color = (int)Number(entity.Values, "color", 0),
                    Fade = (int)Number(entity.Values, "fade", 0),
                    Ease = (int)Number(entity.Values, "ease", 0),
                });
            }

            foreach (var guide in chart.Guides)
                guide.FadeOut = !chart.Guides.Any(candidate => !ReferenceEquals(candidate, guide) &&
                    candidate.Color == guide.Color && SameGuidePoint(guide.Tail, candidate.Head));
            AssignGuideOpacities(chart.Guides);

            chart.Notes.Sort((a, b) =>
            {
                var time = a.Time.CompareTo(b.Time);
                return time != 0 ? time : a.Index.CompareTo(b.Index);
            });
            return chart;
        }

        static bool TryGuidePoint(Entity entity, string prefix, TempoMap tempo, out RuntimeGuidePoint point)
        {
            var beat = Number(entity.Values, prefix + "Beat");
            var lane = Number(entity.Values, prefix + "Lane");
            var size = Number(entity.Values, prefix + "Size");
            point = default;
            if (!double.IsFinite(beat) || !double.IsFinite(lane) || !double.IsFinite(size)) return false;
            point = new RuntimeGuidePoint
            {
                Beat = beat,
                Time = tempo.SecondsAt(beat),
                Lane = (float)lane,
                Size = Math.Max(.01f, (float)size),
            };
            return double.IsFinite(point.Time);
        }

        static bool SameGuidePoint(RuntimeGuidePoint a, RuntimeGuidePoint b) =>
            Math.Abs(a.Beat - b.Beat) < 1e-7 &&
            Math.Abs(a.Lane - b.Lane) < 1e-5f &&
            Math.Abs(a.Size - b.Size) < 1e-5f;

        static void AssignGuideOpacities(IReadOnlyList<RuntimeGuide> guides)
        {
            var remaining = new HashSet<RuntimeGuide>(guides);
            while (remaining.Count > 0)
            {
                var first = remaining.First();
                var component = new List<RuntimeGuide>();
                var queue = new Queue<RuntimeGuide>();
                remaining.Remove(first);
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    component.Add(current);
                    foreach (var candidate in remaining.ToArray())
                    {
                        if (candidate.Color != current.Color ||
                            !(SameGuidePoint(current.Head, candidate.Tail) || SameGuidePoint(current.Tail, candidate.Head))) continue;
                        remaining.Remove(candidate);
                        queue.Enqueue(candidate);
                    }
                }

                var startBeat = component.Min(guide => guide.Head.Beat);
                var endBeat = component.Max(guide => guide.Tail.Beat);
                var duration = Math.Max(1e-7, endBeat - startBeat);
                foreach (var guide in component)
                {
                    guide.HeadOpacity = GuideOpacity((guide.Head.Beat - startBeat) / duration);
                    guide.TailOpacity = GuideOpacity((guide.Tail.Beat - startBeat) / duration);
                }
            }
        }

        static float GuideOpacity(double progress)
        {
            progress = Math.Clamp(progress, 0, 1);
            var smooth = progress * progress * (3 - 2 * progress);
            return (float)(1 - .92 * smooth);
        }

        static bool IsFallbackPlayable(string archetype) => archetype.EndsWith("Note", StringComparison.Ordinal) &&
            !archetype.StartsWith("Ignored", StringComparison.Ordinal) && !archetype.Contains("Attached");

        static bool IsVisiblePlayable(string archetype) =>
            !archetype.StartsWith("Ignored", StringComparison.Ordinal) &&
            !archetype.StartsWith("Hidden", StringComparison.Ordinal) &&
            archetype.IndexOf("Attached", StringComparison.Ordinal) < 0;

        static RuntimeNoteKind Classify(string archetype)
        {
            if (archetype.Contains("Flick")) return RuntimeNoteKind.Flick;
            // A regular slide tail is released. Trace tails and visible slide
            // ticks are passive hold checkpoints: they complete while covered
            // and must not demand an extra lift/tap at the end of every Hold.
            if (archetype.EndsWith("SlideEndNote", StringComparison.Ordinal) &&
                archetype.IndexOf("TraceSlideEnd", StringComparison.Ordinal) < 0)
                return RuntimeNoteKind.Release;
            if (archetype.Contains("Trace") || archetype.Contains("SlideTick") || archetype.Contains("Attached") ||
                archetype.Contains("Ignored")) return RuntimeNoteKind.Sustain;
            return RuntimeNoteKind.Tap;
        }

        static (float lane, float size)? ResolveGeometry(Entity entity, IReadOnlyDictionary<string, Entity> byName, HashSet<int> seen)
        {
            if (entity == null || !seen.Add(entity.Index)) return null;
            if (double.IsFinite(entity.Lane) && double.IsFinite(entity.Size)) return ((float)entity.Lane, (float)entity.Size);
            if (!entity.Refs.TryGetValue("attach", out var attachName) || !byName.TryGetValue(attachName, out var attached)) return null;
            if (!attached.Archetype.EndsWith("Connector", StringComparison.Ordinal)) return ResolveGeometry(attached, byName, seen);
            if (!attached.Refs.TryGetValue("start", out var startName) || !attached.Refs.TryGetValue("end", out var endName) ||
                !byName.TryGetValue(startName, out var start) || !byName.TryGetValue(endName, out var end)) return null;
            var startGeometry = ResolveGeometry(start, byName, new HashSet<int>(seen));
            var endGeometry = ResolveGeometry(end, byName, new HashSet<int>(seen));
            if (!startGeometry.HasValue || !endGeometry.HasValue || !double.IsFinite(start.Beat) || !double.IsFinite(end.Beat)) return null;
            var duration = end.Beat - start.Beat;
            var progress = Math.Abs(duration) < 1e-9 ? 0 : (entity.Beat - start.Beat) / duration;
            progress = Math.Clamp(progress, 0, 1);
            var ease = (int)Number(attached.Values, "ease", 0);
            if (ease == 1) progress = 1 - Math.Cos(progress * Math.PI / 2);
            else if (ease == 2) progress = Math.Sin(progress * Math.PI / 2);
            return (
                (float)(startGeometry.Value.lane + (endGeometry.Value.lane - startGeometry.Value.lane) * progress),
                (float)(startGeometry.Value.size + (endGeometry.Value.size - startGeometry.Value.size) * progress));
        }

        static double Number(IReadOnlyDictionary<string, JToken> values, string key, double fallback = double.NaN) =>
            values.TryGetValue(key, out var token) && double.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        static string DetectAudioExtension(byte[] bytes)
        {
            if (bytes?.Length >= 4 && bytes[0] == (byte)'O' && bytes[1] == (byte)'g') return ".ogg";
            if (bytes?.Length >= 4 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I') return ".wav";
            return ".mp3";
        }

        sealed class Entity
        {
            public int Index;
            public string Name;
            public string Archetype;
            public double Beat = double.NaN;
            public double Time = double.NaN;
            public double Lane = double.NaN;
            public double Size = double.NaN;
            public double Direction;
            public readonly Dictionary<string, JToken> Values = new(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Refs = new(StringComparer.Ordinal);
        }

        sealed class TempoMap
        {
            readonly List<Change> changes;
            TempoMap(List<Change> changes) => this.changes = changes;

            public static TempoMap FromEntities(IEnumerable<Entity> entities)
            {
                var source = entities.Where(entity => entity.Archetype == "#BPM_CHANGE")
                    .Select(entity => new Change { Beat = entity.Beat, Bpm = Number(entity.Values, "#BPM"), Index = entity.Index })
                    .Where(change => double.IsFinite(change.Beat) && double.IsFinite(change.Bpm) && change.Bpm > 0)
                    .OrderBy(change => change.Beat).ThenBy(change => change.Index).ToList();
                if (source.Count == 0) source.Add(new Change { Beat = 0, Bpm = 120 });
                source = source.GroupBy(change => change.Beat).Select(group => group.Last()).OrderBy(change => change.Beat).ToList();
                if (source.All(change => Math.Abs(change.Beat) > 1e-9)) source.Add(new Change { Beat = 0, Bpm = source[0].Bpm, Index = -1 });
                source = source.OrderBy(change => change.Beat).ToList();
                var zero = source.FindIndex(change => Math.Abs(change.Beat) < 1e-9);
                source[zero].Seconds = 0;
                for (var i = zero + 1; i < source.Count; i++) source[i].Seconds = source[i - 1].Seconds + (source[i].Beat - source[i - 1].Beat) * 60 / source[i - 1].Bpm;
                for (var i = zero - 1; i >= 0; i--) source[i].Seconds = source[i + 1].Seconds - (source[i + 1].Beat - source[i].Beat) * 60 / source[i].Bpm;
                return new TempoMap(source);
            }

            public double SecondsAt(double beat)
            {
                if (!double.IsFinite(beat)) return double.NaN;
                var change = changes[0];
                foreach (var candidate in changes)
                {
                    if (candidate.Beat > beat) break;
                    change = candidate;
                }
                return change.Seconds + (beat - change.Beat) * 60 / change.Bpm;
            }

            sealed class Change { public int Index; public double Beat; public double Bpm; public double Seconds; }
        }
    }
}
