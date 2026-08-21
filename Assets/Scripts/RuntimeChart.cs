using System;
using System.Collections.Generic;
using System.Linq;

namespace Gugarythm
{
    public enum RuntimeNoteKind { Tap, Flick, Sustain, Release }
    public enum JudgmentGrade { Pending, Perfect, Great, Good, Miss }
    public enum HoldCheckpointSource { None, Mid, Auto, Tail }

    [Serializable]
    public sealed class RuntimeNote
    {
        public int Index;
        public string SourceId;
        public string Archetype;
        public double Time;
        public double Beat;
        public float Lane;
        public float Size = 1;
        public int Direction;
        public RuntimeNoteKind Kind;
        public bool Critical;
        public string TimeScaleGroup;
        public JudgmentGrade Grade;
        public bool Visible = true;
        public bool Judged = true;
        public HoldCheckpointSource HoldCheckpointSource;
        public int HoldRootIndex = -1;
        public bool IsHoldTerminal;
    }

    public sealed class RuntimeTimeScaleGroup
    {
        readonly List<Point> points = new();

        public string SourceId { get; }

        public RuntimeTimeScaleGroup(string sourceId, IEnumerable<(double time, double scale)> source)
        {
            SourceId = sourceId ?? string.Empty;
            var ordered = new List<(double time, double scale)>();
            foreach (var value in source)
                if (double.IsFinite(value.time) && double.IsFinite(value.scale)) ordered.Add(value);
            ordered.Sort((a, b) => a.time.CompareTo(b.time));

            var unique = new List<(double time, double scale)>();
            foreach (var value in ordered)
            {
                if (unique.Count > 0 && Math.Abs(unique[^1].time - value.time) < 1e-9) unique[^1] = value;
                else unique.Add(value);
            }
            if (unique.Count == 0) unique.Add((0, 1));
            if (!unique.Exists(value => Math.Abs(value.time) < 1e-9))
            {
                var scaleAtZero = unique[0].scale;
                foreach (var value in unique)
                {
                    if (value.time > 0) break;
                    scaleAtZero = value.scale;
                }
                unique.Add((0, scaleAtZero));
                unique.Sort((a, b) => a.time.CompareTo(b.time));
            }

            var zero = unique.FindIndex(value => Math.Abs(value.time) < 1e-9);
            var positions = new double[unique.Count];
            for (var i = zero + 1; i < unique.Count; i++)
                positions[i] = positions[i - 1] + (unique[i].time - unique[i - 1].time) * unique[i - 1].scale;
            for (var i = zero - 1; i >= 0; i--)
                positions[i] = positions[i + 1] - (unique[i + 1].time - unique[i].time) * unique[i].scale;
            for (var i = 0; i < unique.Count; i++) points.Add(new Point(unique[i].time, unique[i].scale, positions[i]));
        }

        public void ShiftTime(double timeDelta)
        {
            if (!double.IsFinite(timeDelta) || Math.Abs(timeDelta) < 1e-12) return;
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                points[index] = new Point(point.Time + timeDelta, point.Scale, point.Position);
            }
        }

        public double PositionAt(double time)
        {
            var point = points[0];
            foreach (var candidate in points)
            {
                if (candidate.Time > time) break;
                point = candidate;
            }
            return point.Position + (time - point.Time) * point.Scale;
        }

        readonly struct Point
        {
            public readonly double Time;
            public readonly double Scale;
            public readonly double Position;
            public Point(double time, double scale, double position) { Time = time; Scale = scale; Position = position; }
        }
    }

    [Serializable]
    public sealed class RuntimeConnector
    {
        public RuntimeNote Start;
        public RuntimeNote End;
        public bool Critical;
        public int Ease;
    }

    [Serializable]
    public struct RuntimeGuidePoint
    {
        public double Time;
        public double Beat;
        public float Lane;
        public float Size;
        public string TimeScaleGroup;
    }

    [Serializable]
    public sealed class RuntimeGuide
    {
        public RuntimeGuidePoint Start;
        public RuntimeGuidePoint Head;
        public RuntimeGuidePoint Tail;
        public RuntimeGuidePoint End;
        public int Color;
        public int Fade;
        public int Ease;
        public bool FadeOut;
        public float HeadOpacity = 1;
        public float TailOpacity = 1;
    }

    [Serializable]
    public sealed class RuntimeSimLine
    {
        public RuntimeNote A;
        public RuntimeNote B;
    }

    [Serializable]
    public sealed class RuntimeChart
    {
        public string SourceFormat;
        public string Title = "Untitled";
        public string Artist = "";
        public string Author = "";
        public string DifficultyName = "";
        public string DifficultyLevel = "";
        public string Engine = "";
        public double BgmOffset;
        public double BgmStartDelaySeconds;
        public byte[] BgmBytes;
        public string BgmExtension = ".mp3";
        public string ReferencedBgm;
        public byte[] CoverBytes;
        public readonly List<RuntimeNote> Notes = new();
        public readonly List<RuntimeConnector> Connectors = new();
        // SimLine is a visual-only synchronization link between notes. It is
        // neither a playable hold nor an engine decoration guide.
        public readonly List<RuntimeSimLine> SimLines = new();
        // Engine guides are visual-only ribbons. They may extend beyond the
        // playable lane range and must never be judged as slide connectors.
        public readonly List<RuntimeGuide> Guides = new();
        public readonly Dictionary<string, RuntimeTimeScaleGroup> TimeScaleGroups = new(StringComparer.Ordinal);
        public string DefaultTimeScaleGroup;
        public readonly List<string> Warnings = new();

        public void ShiftTiming(double beatDelta, double timeDelta)
        {
            if (!double.IsFinite(beatDelta) || !double.IsFinite(timeDelta)) return;
            var shiftedNotes = new HashSet<RuntimeNote>();
            foreach (var note in Notes) shiftedNotes.Add(note);
            foreach (var connector in Connectors)
            {
                if (connector.Start != null) shiftedNotes.Add(connector.Start);
                if (connector.End != null) shiftedNotes.Add(connector.End);
            }
            foreach (var note in shiftedNotes)
            {
                note.Beat += beatDelta;
                note.Time += timeDelta;
            }
            foreach (var guide in Guides)
            {
                guide.Start.Beat += beatDelta;
                guide.Start.Time += timeDelta;
                guide.Head.Beat += beatDelta;
                guide.Head.Time += timeDelta;
                guide.Tail.Beat += beatDelta;
                guide.Tail.Time += timeDelta;
                guide.End.Beat += beatDelta;
                guide.End.Time += timeDelta;
            }
            foreach (var group in TimeScaleGroups.Values) group.ShiftTime(timeDelta);
            BgmStartDelaySeconds += timeDelta;
        }

        public int PlayableCount
        {
            get
            {
                var count = 0;
                foreach (var note in Notes) if (note.Judged) count++;
                return count;
            }
        }

        public double LastNoteTime
        {
            get
            {
                var last = 0d;
                foreach (var note in Notes) if (note.Judged && note.Time > last) last = note.Time;
                return last;
            }
        }

        public double VisualPosition(double time, string group)
        {
            var key = string.IsNullOrEmpty(group) ? DefaultTimeScaleGroup : group;
            return key != null && TimeScaleGroups.TryGetValue(key, out var map) ? map.PositionAt(time) : time;
        }
    }

    /// <summary>
    /// Adds runtime-only eighth-note checkpoints to connected Hold paths. Authored
    /// Sustain mids remain separate checkpoints; judged terminals remain
    /// contact/flick checkpoints while unjudged terminals stay visual only.
    /// </summary>
    public static class HoldCheckpointBuilder
    {
        public const double EighthNoteBeats = .5;

        public static void Apply(RuntimeChart chart, Func<double, double> timeAtBeat)
        {
            if (chart == null || timeAtBeat == null) return;

            var outgoing = chart.Connectors
                .Where(connector => connector?.Start != null && connector.End != null)
                .GroupBy(connector => connector.Start)
                .ToDictionary(group => group.Key, group => group.Select(connector => connector.End).Distinct().ToList());
            var incoming = new HashSet<RuntimeNote>(chart.Connectors
                .Where(connector => connector?.Start != null && connector.End != null)
                .Select(connector => connector.End));
            var nextIndex = chart.Notes.Count == 0 ? 0 : chart.Notes.Max(note => note.Index) + 1;

            foreach (var head in outgoing.Keys.Where(note => !incoming.Contains(note)).ToArray())
            {
                var path = CollectPath(head, outgoing);
                if (path.Count < 2) continue;
                var tail = path[^1];
                if (tail.Beat <= head.Beat + 1e-9) continue;

                foreach (var point in path) point.HoldRootIndex = head.Index;
                tail.IsHoldTerminal = true;
                foreach (var mid in path.Skip(1).Take(path.Count - 2).Where(IsAuthoredMid))
                    mid.HoldCheckpointSource = HoldCheckpointSource.Mid;
                if (tail.Judged)
                {
                    if (tail.Kind == RuntimeNoteKind.Release)
                        tail.Kind = RuntimeNoteKind.Sustain;
                    tail.HoldCheckpointSource = HoldCheckpointSource.Tail;
                }

                for (var beat = head.Beat + EighthNoteBeats; beat < tail.Beat - 1e-9; beat += EighthNoteBeats)
                {
                    var segment = SegmentAt(path, beat);
                    if (segment == null) continue;
                    var start = segment.Value.start;
                    var end = segment.Value.end;
                    var progress = (float)((beat - start.Beat) / (end.Beat - start.Beat));
                    chart.Notes.Add(new RuntimeNote
                    {
                        Index = nextIndex++,
                        SourceId = $"hold:auto:{head.SourceId}:{beat:R}",
                        Archetype = "RuntimeHoldAutoCheckpoint",
                        Beat = beat,
                        Time = timeAtBeat(beat),
                        Lane = start.Lane + (end.Lane - start.Lane) * progress,
                        Size = start.Size + (end.Size - start.Size) * progress,
                        Kind = RuntimeNoteKind.Sustain,
                        Critical = head.Critical,
                        TimeScaleGroup = head.TimeScaleGroup,
                        Visible = false,
                        Judged = true,
                        HoldCheckpointSource = HoldCheckpointSource.Auto,
                        HoldRootIndex = head.Index,
                    });
                }
            }
        }

        static List<RuntimeNote> CollectPath(RuntimeNote head, IReadOnlyDictionary<RuntimeNote, List<RuntimeNote>> outgoing)
        {
            var path = new List<RuntimeNote> { head };
            var seen = new HashSet<RuntimeNote> { head };
            var current = head;
            while (outgoing.TryGetValue(current, out var next) && next.Count > 0)
            {
                var candidate = next.OrderBy(note => note.Beat).ThenBy(note => note.Index).First();
                if (!seen.Add(candidate)) break;
                path.Add(candidate);
                current = candidate;
            }
            return path;
        }

        static (RuntimeNote start, RuntimeNote end)? SegmentAt(IReadOnlyList<RuntimeNote> path, double beat)
        {
            for (var index = 0; index < path.Count - 1; index++)
                if (beat >= path[index].Beat - 1e-9 && beat <= path[index + 1].Beat + 1e-9 &&
                    path[index + 1].Beat > path[index].Beat + 1e-9)
                    return (path[index], path[index + 1]);
            return null;
        }

        static bool IsAuthoredMid(RuntimeNote note) => note.Judged && note.Kind == RuntimeNoteKind.Sustain &&
            (note.Archetype ?? string.Empty).Contains("SlideTick", StringComparison.OrdinalIgnoreCase);
    }

    public sealed class ImportResult
    {
        public RuntimeChart Chart;
        public string Error;
        public bool Success => Chart != null && string.IsNullOrEmpty(Error);

        public static ImportResult Ok(RuntimeChart chart) => new() { Chart = chart };
        public static ImportResult Fail(string error) => new() { Error = error };
    }

    public interface IChartImporter
    {
        bool CanImport(string fileName, byte[] header);
        ImportResult Import(string fileName, byte[] data, IReadOnlyDictionary<string, byte[]> companionFiles = null);
    }
}
