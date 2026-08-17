using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Gugarythm
{
    public sealed class LevelDataImporter : IChartImporter
    {
        public bool CanImport(string fileName, byte[] header) =>
            fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

        public ImportResult Import(string fileName, byte[] data, IReadOnlyDictionary<string, byte[]> companionFiles = null)
        {
            try
            {
                string json;
                if (data.Length >= 2 && data[0] == 0x1f && data[1] == 0x8b)
                {
                    using var source = new MemoryStream(data, false);
                    using var gzip = new GZipStream(source, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip, new UTF8Encoding(false, true));
                    json = reader.ReadToEnd();
                }
                else json = new UTF8Encoding(false, true).GetString(data);
                var levelData = JObject.Parse(json);
                if (levelData["entities"] is not JArray) return ImportResult.Fail("JSON 不是 Sonolus LevelData。");
                var chart = ScpChartImporter.BuildRuntimeChart(levelData, null, "LevelData");
                chart.Title = Path.GetFileNameWithoutExtension(fileName);
                chart.BgmOffset = (double?)levelData["bgmOffset"] ?? 0;
                AttachCompanionAudio(chart, companionFiles);
                return ImportResult.Ok(chart);
            }
            catch (Exception exception) { return ImportResult.Fail("LevelData 解析失敗：" + exception.Message); }
        }

        internal static void AttachCompanionAudio(RuntimeChart chart, IReadOnlyDictionary<string, byte[]> companionFiles)
        {
            if (companionFiles == null) return;
            var preferred = chart.ReferencedBgm?.Replace('\\', '/').Split('/').LastOrDefault();
            KeyValuePair<string, byte[]>? match = null;
            if (!string.IsNullOrEmpty(preferred))
                match = companionFiles.FirstOrDefault(pair => string.Equals(Path.GetFileName(pair.Key), preferred, StringComparison.OrdinalIgnoreCase));
            if (!match.HasValue || match.Value.Value == null)
                match = companionFiles.FirstOrDefault(pair => IsAudio(pair.Key));
            if (match.HasValue && match.Value.Value != null)
            {
                chart.BgmBytes = match.Value.Value;
                chart.BgmExtension = Path.GetExtension(match.Value.Key).ToLowerInvariant();
            }
        }

        static bool IsAudio(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension is ".mp3" or ".ogg" or ".wav" or ".m4a" or ".aac";
        }
    }

    public sealed class UscChartImporter : IChartImporter
    {
        public bool CanImport(string fileName, byte[] header) => fileName.EndsWith(".usc", StringComparison.OrdinalIgnoreCase);

        public ImportResult Import(string fileName, byte[] data, IReadOnlyDictionary<string, byte[]> companionFiles = null)
        {
            try
            {
                var root = JObject.Parse(new UTF8Encoding(false, true).GetString(data));
                var usc = root["usc"] as JObject;
                if (usc?["objects"] is not JArray objects) return ImportResult.Fail("USC 缺少 usc.objects。");
                var bpm = objects.OfType<JObject>().Where(item => (string)item["type"] == "bpm")
                    .Select(item => new BeatBpm((double?)item["beat"] ?? 0, (double?)item["bpm"] ?? 120)).ToList();
                var tempo = new BeatTimeMap(bpm);
                var chart = new RuntimeChart
                {
                    SourceFormat = "USC",
                    Title = Path.GetFileNameWithoutExtension(fileName),
                    BgmOffset = (double?)usc["offset"] ?? 0,
                };
                BuildTimeScaleGroups(chart, objects, tempo);
                if ((int?)root["version"] is int version && version != 2)
                    chart.Warnings.Add($"USC version {version} 不是目前完整支援的 version 2。");
                var index = 0;
                foreach (var item in objects.OfType<JObject>())
                {
                    var type = (string)item["type"];
                    if (type is "single" or "damage") AddSingle(chart, item, tempo, ref index);
                    else if (type == "slide" && item["connections"] is JArray connections) AddSlide(chart, item, connections, tempo, ref index);
                    else if (type == "guide" && item["midpoints"] is JArray midpoints) AddGuide(chart, item, midpoints, tempo);
                }
                HoldCheckpointBuilder.Apply(chart, tempo.SecondsAt);
                chart.Notes.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Index.CompareTo(b.Index));
                LevelDataImporter.AttachCompanionAudio(chart, companionFiles);
                return ImportResult.Ok(chart);
            }
            catch (Exception exception) { return ImportResult.Fail("USC 解析失敗：" + exception.Message); }
        }

        static void AddSingle(RuntimeChart chart, JObject item, BeatTimeMap tempo, ref int index)
        {
            var trace = (bool?)item["trace"] == true;
            var flick = item["direction"] != null;
            var damage = string.Equals((string)item["type"], "damage", StringComparison.Ordinal);
            var beat = (double?)item["beat"] ?? 0;
            chart.Notes.Add(new RuntimeNote
            {
                Index = index++, SourceId = "usc:" + index,
                Archetype = damage ? "USC Damage" : trace && flick ? "USC TraceFlick" : trace ? "USC Trace" : flick ? "USC Flick" : "USC Tap",
                Beat = beat, Time = tempo.SecondsAt(beat), Lane = (float?)item["lane"] ?? 0, Size = Math.Max(.25f, (float?)item["size"] ?? 1),
                Direction = FlickDirection(item["direction"]),
                Critical = (bool?)item["critical"] == true, Kind = flick ? RuntimeNoteKind.Flick : trace ? RuntimeNoteKind.Sustain : RuntimeNoteKind.Tap,
                TimeScaleGroup = TimeScaleGroupKey(chart, item["timeScaleGroup"]),
            });
        }

        static void AddSlide(RuntimeChart chart, JObject slide, JArray connections, BeatTimeMap tempo, ref int index)
        {
            RuntimeNote previousPoint = null;
            RuntimeNote holdRoot = null;
            var previousEase = 0;
            var sourceConnections = connections.OfType<JObject>().ToArray();
            var firstJudgedConnection = true;
            for (var connectionIndex = 0; connectionIndex < sourceConnections.Length; connectionIndex++)
            {
                var connection = sourceConnections[connectionIndex];
                var beat = (double?)connection["beat"] ?? 0;
                var judgeType = (string)connection["judgeType"] ?? "none";
                var connectionType = (string)connection["type"] ?? "tick";
                var flick = connection["direction"] != null;
                var trace = judgeType.Equals("trace", StringComparison.OrdinalIgnoreCase);
                var judged = !judgeType.Equals("none", StringComparison.OrdinalIgnoreCase);
                var terminal = connectionIndex == sourceConnections.Length - 1;
                // USC middle connections encode independent path and particle
                // roles: tick changes the path, attach is particle-only, and
                // a tick carrying critical does both.
                var isAttach = connectionType == "attach";
                var isPathPoint = !isAttach;
                var hasParticle = isAttach || connection["critical"] != null;
                var lane = (float?)connection["lane"] ?? 0;
                var size = Math.Max(.25f, (float?)connection["size"] ?? 1);
                if (isAttach && previousPoint != null && TryFindNextPathConnection(sourceConnections, connectionIndex + 1, out var nextPath))
                {
                    var nextBeat = (double?)nextPath["beat"] ?? beat;
                    var span = nextBeat - previousPoint.Beat;
                    var progress = span <= 1e-7 ? 0f : (float)Math.Clamp((beat - previousPoint.Beat) / span, 0, 1);
                    progress = EaseProgress(progress, previousEase);
                    lane = previousPoint.Lane + (((float?)nextPath["lane"] ?? previousPoint.Lane) - previousPoint.Lane) * progress;
                    size = previousPoint.Size + (Math.Max(.25f, (float?)nextPath["size"] ?? previousPoint.Size) - previousPoint.Size) * progress;
                }
                var kind = !judged ? RuntimeNoteKind.Sustain :
                    firstJudgedConnection ? RuntimeNoteKind.Tap :
                    terminal && flick ? RuntimeNoteKind.Flick : RuntimeNoteKind.Sustain;
                if (judged) firstJudgedConnection = false;
                var archetype = trace ? "USC Trace Slide " + connectionType :
                    (connectionType is "tick" or "attach") ? "USC SlideTickNote" : "USC Slide " + connectionType;
                if (flick) archetype += " Flick";
                var point = new RuntimeNote
                {
                    Index = index++, SourceId = "usc-slide:" + index, Archetype = archetype,
                    Beat = beat, Time = tempo.SecondsAt(beat), Lane = lane,
                    Size = size, Critical = (bool?)connection["critical"] ?? (bool?)slide["critical"] ?? false,
                    Direction = FlickDirection(connection["direction"]),
                    Kind = kind,
                    Visible = judged || hasParticle,
                    Judged = judged,
                    HoldCheckpointSource = judged && !terminal && kind == RuntimeNoteKind.Sustain
                        ? HoldCheckpointSource.Mid : HoldCheckpointSource.None,
                    TimeScaleGroup = TimeScaleGroupKey(chart, connection["timeScaleGroup"]),
                };
                if (isAttach && holdRoot != null) point.HoldRootIndex = holdRoot.Index;
                if (point.Visible) chart.Notes.Add(point);
                if (isPathPoint && previousPoint != null) chart.Connectors.Add(new RuntimeConnector
                {
                    Start = previousPoint,
                    End = point,
                    Critical = point.Critical,
                    Ease = previousEase,
                });
                if (isPathPoint)
                {
                    previousPoint = point;
                    holdRoot ??= point;
                    previousEase = EaseType(connection["ease"]);
                }
            }
        }

        static bool TryFindNextPathConnection(JObject[] connections, int startIndex, out JObject pathConnection)
        {
            for (var index = startIndex; index < connections.Length; index++)
                if (!string.Equals((string)connections[index]["type"], "attach", StringComparison.OrdinalIgnoreCase))
                {
                    pathConnection = connections[index];
                    return true;
                }
            pathConnection = null;
            return false;
        }

        static void BuildTimeScaleGroups(RuntimeChart chart, JArray objects, BeatTimeMap tempo)
        {
            var groups = objects.OfType<JObject>().Where(item => (string)item["type"] == "timeScaleGroup").ToArray();
            if (groups.Length == 0)
            {
                const string fallback = "usc:tsg:0";
                chart.TimeScaleGroups[fallback] = new RuntimeTimeScaleGroup(fallback, new[] { (0d, 1d) });
                chart.DefaultTimeScaleGroup = fallback;
                chart.Warnings.Add("USC 未提供 timeScaleGroup，已使用 1.0 倍速。");
                return;
            }

            for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                var key = "usc:tsg:" + groupIndex.ToString(CultureInfo.InvariantCulture);
                var changes = groups[groupIndex]["changes"] is JArray source
                    ? source.OfType<JObject>()
                        .Select((change, order) => new
                        {
                            Beat = (double?)change["beat"] ?? 0,
                            Scale = (double?)change["timeScale"] ?? 1,
                            Order = order,
                        })
                        .GroupBy(change => change.Beat)
                        .Select(group => group.OrderBy(change => change.Order).Last())
                        .OrderBy(change => change.Beat)
                        .Select(change => (tempo.SecondsAt(change.Beat), change.Scale))
                        .ToArray()
                    : Array.Empty<(double, double)>();
                chart.TimeScaleGroups[key] = new RuntimeTimeScaleGroup(key, changes);
                chart.DefaultTimeScaleGroup ??= key;
            }
        }

        static void AddGuide(RuntimeChart chart, JObject guide, JArray midpoints, BeatTimeMap tempo)
        {
            var source = midpoints.OfType<JObject>().ToArray();
            if (source.Length < 2)
            {
                chart.Warnings.Add("USC Guide 至少需要兩個 midpoint。");
                return;
            }

            var points = source.Select(point => new RuntimeGuidePoint
            {
                Beat = (double?)point["beat"] ?? 0,
                Time = tempo.SecondsAt((double?)point["beat"] ?? 0),
                Lane = (float?)point["lane"] ?? 0,
                Size = Math.Max(.01f, (float?)point["size"] ?? 1),
                TimeScaleGroup = TimeScaleGroupKey(chart, point["timeScaleGroup"]),
            }).ToArray();
            var color = GuideColor((string)guide["color"]);
            var fade = GuideFade((string)guide["fade"]);
            var firstBeat = points[0].Beat;
            var duration = Math.Max(1e-7, points[^1].Beat - firstBeat);
            for (var pointIndex = 0; pointIndex < points.Length - 1; pointIndex++)
            {
                var headProgress = (points[pointIndex].Beat - firstBeat) / duration;
                var tailProgress = (points[pointIndex + 1].Beat - firstBeat) / duration;
                chart.Guides.Add(new RuntimeGuide
                {
                    Start = points[Math.Max(0, pointIndex - 1)],
                    Head = points[pointIndex],
                    Tail = points[pointIndex + 1],
                    End = points[Math.Min(points.Length - 1, pointIndex + 2)],
                    Color = color,
                    Fade = fade,
                    Ease = EaseType(source[pointIndex]["ease"]),
                    FadeOut = fade == 2,
                    HeadOpacity = GuideOpacity(fade, headProgress),
                    TailOpacity = GuideOpacity(fade, tailProgress),
                });
            }
        }

        static string TimeScaleGroupKey(RuntimeChart chart, JToken token)
        {
            var index = (int?)token ?? 0;
            var key = "usc:tsg:" + index.ToString(CultureInfo.InvariantCulture);
            if (chart.TimeScaleGroups.ContainsKey(key)) return key;
            var warning = $"USC 引用了不存在的 timeScaleGroup {index}，已改用預設群組。";
            if (!chart.Warnings.Contains(warning)) chart.Warnings.Add(warning);
            return chart.DefaultTimeScaleGroup;
        }

        static int EaseType(JToken token)
        {
            var value = token?.ToString();
            if (string.Equals(value, "in", StringComparison.OrdinalIgnoreCase)) return 1;
            if (string.Equals(value, "out", StringComparison.OrdinalIgnoreCase)) return 2;
            if (string.Equals(value, "inout", StringComparison.OrdinalIgnoreCase)) return 3;
            return 0;
        }

        static float EaseProgress(float progress, int ease) => ease switch
        {
            1 => 1f - (float)Math.Cos(progress * Math.PI * .5),
            2 => (float)Math.Sin(progress * Math.PI * .5),
            3 => progress < .5f ? 2 * progress * progress : 1 - (float)Math.Pow(-2 * progress + 2, 2) * .5f,
            _ => progress,
        };

        static int GuideColor(string value) => value?.ToLowerInvariant() switch
        {
            "purple" => 1,
            "blue" => 2,
            "red" => 3,
            "yellow" => 4,
            "cyan" => 5,
            "black" => 6,
            _ => 0,
        };

        static int GuideFade(string value) => value?.ToLowerInvariant() switch
        {
            "in" => 1,
            "out" => 2,
            _ => 0,
        };

        static float GuideOpacity(int fade, double progress)
        {
            var clamped = (float)Math.Clamp(progress, 0, 1);
            return fade switch { 1 => clamped, 2 => 1 - clamped, _ => 1 };
        }

        static int FlickDirection(JToken token)
        {
            if (token == null) return 0;
            if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)) return Math.Clamp(numeric, -1, 1);
            var direction = token.ToString();
            if (direction.Equals("left", StringComparison.OrdinalIgnoreCase)) return -1;
            if (direction.Equals("right", StringComparison.OrdinalIgnoreCase)) return 1;
            if (direction.Equals("up", StringComparison.OrdinalIgnoreCase) || direction.Equals("none", StringComparison.OrdinalIgnoreCase)) return 0;
            return 0;
        }
    }

    public sealed class SusChartImporter : IChartImporter
    {
        static readonly Regex DataLine = new(@"^#(?<measure>\d{3})(?<type>[1-5])(?<lane>[0-9A-Fa-f])(?<stream>[0-9A-Za-z]?):(?<data>[0-9A-Za-z]+)$", RegexOptions.Compiled);
        static readonly Regex BpmDefinition = new(@"^#BPM(?<id>[0-9A-Fa-f]{2}):(?<bpm>[0-9.]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex BpmPlacement = new(@"^#(?<measure>\d{3})08:(?<data>[0-9A-Fa-f]+)$", RegexOptions.Compiled);

        public bool CanImport(string fileName, byte[] header) => fileName.EndsWith(".sus", StringComparison.OrdinalIgnoreCase);

        public ImportResult Import(string fileName, byte[] data, IReadOnlyDictionary<string, byte[]> companionFiles = null)
        {
            try
            {
                var text = new UTF8Encoding(false, true).GetString(data).Replace("\r", "");
                var lines = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith("//")).ToArray();
                var bpmDefinitions = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var bpmChanges = new List<BeatBpm>();
                var defaultBpm = 120d;
                foreach (var line in lines)
                {
                    if (line.StartsWith("#BPM ", StringComparison.OrdinalIgnoreCase) && double.TryParse(line[5..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) defaultBpm = parsed;
                    var definition = BpmDefinition.Match(line);
                    if (definition.Success) bpmDefinitions[definition.Groups["id"].Value] = double.Parse(definition.Groups["bpm"].Value, CultureInfo.InvariantCulture);
                }
                foreach (var line in lines)
                {
                    var placement = BpmPlacement.Match(line);
                    if (!placement.Success) continue;
                    var raw = placement.Groups["data"].Value;
                    var count = raw.Length / 2;
                    for (var i = 0; i < count; i++)
                    {
                        var id = raw.Substring(i * 2, 2);
                        if (id != "00" && bpmDefinitions.TryGetValue(id, out var value))
                            bpmChanges.Add(new BeatBpm(int.Parse(placement.Groups["measure"].Value) * 4 + i * 4d / count, value));
                    }
                }
                if (bpmChanges.Count == 0 || bpmChanges.All(change => Math.Abs(change.Beat) > 1e-9)) bpmChanges.Add(new BeatBpm(0, defaultBpm));
                var tempo = new BeatTimeMap(bpmChanges);
                var chart = new RuntimeChart { SourceFormat = "SUS", Title = Header(lines, "TITLE") ?? Path.GetFileNameWithoutExtension(fileName), Artist = Header(lines, "ARTIST") ?? "", Author = Header(lines, "DESIGNER") ?? "" };
                chart.ReferencedBgm = Header(lines, "WAVE");
                if (double.TryParse(Header(lines, "WAVEOFFSET"), NumberStyles.Float, CultureInfo.InvariantCulture, out var offset)) chart.BgmOffset = offset;
                var index = 0;
                var streamPoints = new Dictionary<string, List<RuntimeNote>>(StringComparer.Ordinal);
                foreach (var line in lines)
                {
                    var match = DataLine.Match(line);
                    if (!match.Success) continue;
                    var type = match.Groups["type"].Value[0];
                    var laneStart = Convert.ToInt32(match.Groups["lane"].Value, 16);
                    var stream = match.Groups["stream"].Value;
                    var raw = match.Groups["data"].Value;
                    var tokenCount = raw.Length / 2;
                    var measure = int.Parse(match.Groups["measure"].Value);
                    for (var tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
                    {
                        var token = raw.Substring(tokenIndex * 2, 2);
                        if (token == "00") continue;
                        var width = Math.Max(1, Base36(token[1]));
                        var beat = measure * 4 + tokenIndex * 4d / tokenCount;
                        var tokenType = Base36(token[0]);
                        var kind = type is '4' or '5' ? RuntimeNoteKind.Flick : type is '2' or '3' ? RuntimeNoteKind.Sustain : RuntimeNoteKind.Tap;
                        if ((type is '2' or '3') && tokenType == 1) kind = RuntimeNoteKind.Tap;
                        if ((type is '2' or '3') && tokenType == 3) kind = RuntimeNoteKind.Sustain;
                        var note = new RuntimeNote
                        {
                            Index = index++, SourceId = $"sus:{measure}:{type}:{laneStart}:{tokenIndex}", Archetype = type is '4' or '5' ? "SUS Air/Flick" : type is '2' or '3' ? "SUS Slide" : "SUS Tap",
                            Beat = beat, Time = tempo.SecondsAt(beat), Lane = laneStart + width / 2f - 8f, Size = width / 2f,
                            Kind = kind, Critical = tokenType == 2,
                        };
                        chart.Notes.Add(note);
                        if (type is '2' or '3')
                        {
                            var key = type + ":" + laneStart + ":" + stream;
                            if (!streamPoints.TryGetValue(key, out var points)) streamPoints[key] = points = new List<RuntimeNote>();
                            points.Add(note);
                        }
                    }
                }
                foreach (var points in streamPoints.Values)
                {
                    points.Sort((a, b) => a.Time.CompareTo(b.Time));
                    for (var i = 1; i < points.Count; i++) chart.Connectors.Add(new RuntimeConnector { Start = points[i - 1], End = points[i], Critical = points[i].Critical });
                }
                chart.Notes.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Index.CompareTo(b.Index));
                LevelDataImporter.AttachCompanionAudio(chart, companionFiles);
                return ImportResult.Ok(chart);
            }
            catch (Exception exception) { return ImportResult.Fail("SUS 解析失敗：" + exception.Message); }
        }

        static string Header(IEnumerable<string> lines, string key)
        {
            var prefix = "#" + key;
            var line = lines.FirstOrDefault(candidate => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (line == null) return null;
            var value = line[prefix.Length..].TrimStart(' ', ':', '\t').Trim();
            return value.Trim('"');
        }

        static int Base36(char value) => value >= '0' && value <= '9' ? value - '0' : char.ToUpperInvariant(value) - 'A' + 10;
    }

    public readonly struct BeatBpm
    {
        public readonly double Beat;
        public readonly double Bpm;
        public BeatBpm(double beat, double bpm) { Beat = beat; Bpm = bpm; }
    }

    public sealed class BeatTimeMap
    {
        readonly List<(double beat, double bpm, double seconds)> changes = new();

        public BeatTimeMap(IEnumerable<BeatBpm> source)
        {
            var ordered = source.Where(value => value.Bpm > 0).OrderBy(value => value.Beat).GroupBy(value => value.Beat).Select(group => group.Last()).ToList();
            if (ordered.Count == 0) ordered.Add(new BeatBpm(0, 120));
            if (ordered.All(value => Math.Abs(value.Beat) > 1e-9)) ordered.Insert(0, new BeatBpm(0, ordered[0].Bpm));
            double seconds = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (i > 0) seconds += (ordered[i].Beat - ordered[i - 1].Beat) * 60 / ordered[i - 1].Bpm;
                changes.Add((ordered[i].Beat, ordered[i].Bpm, seconds));
            }
        }

        public double SecondsAt(double beat)
        {
            var change = changes[0];
            foreach (var candidate in changes) { if (candidate.beat > beat) break; change = candidate; }
            return change.seconds + (beat - change.beat) * 60 / change.bpm;
        }
    }
}
