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
            return extension is ".mp3" or ".ogg" or ".wav";
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
                var index = 0;
                foreach (var item in objects.OfType<JObject>())
                {
                    var type = (string)item["type"];
                    if (type is "single" or "damage") AddSingle(chart, item, tempo, ref index);
                    else if (type == "slide" && item["connections"] is JArray connections) AddSlide(chart, item, connections, tempo, ref index);
                }
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
            var beat = (double?)item["beat"] ?? 0;
            chart.Notes.Add(new RuntimeNote
            {
                Index = index++, SourceId = "usc:" + index, Archetype = trace && flick ? "USC TraceFlick" : trace ? "USC Trace" : flick ? "USC Flick" : "USC Tap",
                Beat = beat, Time = tempo.SecondsAt(beat), Lane = (float?)item["lane"] ?? 0, Size = Math.Max(.25f, (float?)item["size"] ?? 1),
                Direction = FlickDirection(item["direction"]),
                Critical = (bool?)item["critical"] == true, Kind = flick ? RuntimeNoteKind.Flick : trace ? RuntimeNoteKind.Sustain : RuntimeNoteKind.Tap,
            });
        }

        static void AddSlide(RuntimeChart chart, JObject slide, JArray connections, BeatTimeMap tempo, ref int index)
        {
            RuntimeNote previousPoint = null;
            foreach (var connection in connections.OfType<JObject>())
            {
                var beat = (double?)connection["beat"] ?? 0;
                var judgeType = (string)connection["judgeType"] ?? "none";
                var connectionType = (string)connection["type"] ?? "tick";
                var flick = connection["direction"] != null;
                var point = new RuntimeNote
                {
                    Index = index++, SourceId = "usc-slide:" + index, Archetype = "USC Slide " + connectionType,
                    Beat = beat, Time = tempo.SecondsAt(beat), Lane = (float?)connection["lane"] ?? 0,
                    Size = Math.Max(.25f, (float?)connection["size"] ?? 1), Critical = (bool?)connection["critical"] ?? (bool?)slide["critical"] ?? false,
                    Direction = FlickDirection(connection["direction"]),
                    Kind = flick ? RuntimeNoteKind.Flick : connectionType == "start" && judgeType == "normal" ? RuntimeNoteKind.Tap : RuntimeNoteKind.Sustain,
                    Visible = judgeType != "none" || connectionType == "attach",
                };
                if (judgeType != "none" || flick) chart.Notes.Add(point);
                if (previousPoint != null) chart.Connectors.Add(new RuntimeConnector { Start = previousPoint, End = point, Critical = point.Critical });
                previousPoint = point;
            }
        }

        static int FlickDirection(JToken token)
        {
            if (token == null) return 0;
            if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)) return Math.Clamp(numeric, -1, 1);
            var direction = token.ToString();
            if (direction.Equals("left", StringComparison.OrdinalIgnoreCase)) return -1;
            if (direction.Equals("right", StringComparison.OrdinalIgnoreCase)) return 1;
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
