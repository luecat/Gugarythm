using System;
using System.Collections.Generic;
using System.Linq;

namespace Gugarhythm
{
    public enum RuntimeNoteKind { Tap, Flick, Sustain, Release }
    public enum JudgmentGrade { Pending, Perfect, Great, Good, Miss }
    public enum HoldCheckpointSource { None, Mid, Auto, Tail }
    public enum SlideNodeRole { Unspecified, Start, Tick, Attach, End }
    public enum SlideJudgeMode { Unspecified, None, Normal, Trace, Flick }

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
        public SlideNodeRole SlideNodeRole;
        public SlideJudgeMode SlideJudgeMode;
        public int HoldRootIndex = -1;
        public bool IsHoldTerminal;
    }

    public sealed class RuntimeTimeScaleGroup
    {
        readonly List<Point> points = new();

        public string SourceId { get; }
        public bool SupportsVisualTimeInversion { get; }

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
            SupportsVisualTimeInversion = unique.TrueForAll(value => value.scale > 1e-9);
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

        public double TimeAtPosition(double position)
        {
            var first = points[0];
            if (position <= first.Position)
                return first.Time + (position - first.Position) / Math.Max(1e-9, first.Scale);

            for (var index = 1; index < points.Count; index++)
            {
                var previous = points[index - 1];
                var current = points[index];
                if (position > current.Position) continue;
                return previous.Time + (position - previous.Position) / Math.Max(1e-9, previous.Scale);
            }

            var last = points[^1];
            return last.Time + (position - last.Position) / Math.Max(1e-9, last.Scale);
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
        public readonly List<RuntimeHoldPath> HoldPaths = new();
        public readonly List<RuntimeConnector> FallbackConnectors = new();
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
            foreach (var path in HoldPaths) path.RefreshTimingBounds();
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

        public double TimeAtVisualPosition(double position, string group)
        {
            var key = string.IsNullOrEmpty(group) ? DefaultTimeScaleGroup : group;
            return key != null && TimeScaleGroups.TryGetValue(key, out var map) ? map.TimeAtPosition(position) : position;
        }

        public bool CanInvertVisualTime(string group)
        {
            var key = string.IsNullOrEmpty(group) ? DefaultTimeScaleGroup : group;
            return key == null || !TimeScaleGroups.TryGetValue(key, out var map) || map.SupportsVisualTimeInversion;
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

            chart.Notes.RemoveAll(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto);

            var pathBuild = HoldPathBuilder.Build(chart);
            chart.HoldPaths.Clear();
            chart.HoldPaths.AddRange(pathBuild.Paths);
            chart.FallbackConnectors.Clear();
            chart.FallbackConnectors.AddRange(pathBuild.FallbackConnectors);
            foreach (var warning in pathBuild.Warnings)
                if (!chart.Warnings.Contains(warning)) chart.Warnings.Add(warning);

            var unsafeExplicitFallbackNodes = new HashSet<RuntimeNote>();
            var semanticFallbackPaths = new Dictionary<RuntimeNote,
                (List<RuntimeNote> Nodes, List<RuntimeConnector> Connectors, List<RuntimeNote> SemanticNodes)>();
            foreach (var component in CollectConnectorComponents(pathBuild.FallbackConnectors))
            {
                var geometryNodes = component.SelectMany(connector => new[] { connector.Start, connector.End })
                    .Where(note => note != null).Distinct().ToList();
                var semanticNodes = CollectSemanticNodes(chart, geometryNodes);
                if (semanticNodes.All(IsUnspecifiedSemanticNode)) continue;
                ConfigureAuthoredSemantics(semanticNodes);
                if (TryCollectLinearConnectorPath(component, out var orderedNodes, out var orderedConnectors))
                    semanticFallbackPaths[orderedNodes[0]] = (orderedNodes, orderedConnectors, semanticNodes);
                else
                    foreach (var node in geometryNodes) unsafeExplicitFallbackNodes.Add(node);
            }

            var outgoing = chart.Connectors
                .Where(connector => connector?.Start != null && connector.End != null)
                .GroupBy(connector => connector.Start)
                .ToDictionary(group => group.Key, group => group.Select(connector => connector.End).Distinct().ToList());
            var incoming = new HashSet<RuntimeNote>(chart.Connectors
                .Where(connector => connector?.Start != null && connector.End != null)
                .Select(connector => connector.End));
            var runtimePathsByHead = chart.HoldPaths
                .Where(path => path.Nodes.Count > 0)
                .ToDictionary(path => path.Nodes[0]);
            var nextIndex = chart.Notes.Count == 0 ? 0 : chart.Notes.Max(note => note.Index) + 1;

            foreach (var head in outgoing.Keys.Where(note => !incoming.Contains(note)).ToArray())
            {
                var hasRuntimePath = runtimePathsByHead.TryGetValue(head, out var runtimePath);
                (List<RuntimeNote> Nodes, List<RuntimeConnector> Connectors,
                    List<RuntimeNote> SemanticNodes) semanticFallback = default;
                var hasSemanticFallback = !hasRuntimePath &&
                    semanticFallbackPaths.TryGetValue(head, out semanticFallback);
                if (!hasRuntimePath && !hasSemanticFallback && unsafeExplicitFallbackNodes.Contains(head)) continue;
                var path = hasRuntimePath ? runtimePath.Nodes.ToList() :
                    hasSemanticFallback ? semanticFallback.Nodes : CollectPath(head, outgoing);
                if (path.Count < 2) continue;

                var rootIndex = hasRuntimePath ? runtimePath.RootIndex : head.Index;
                foreach (var point in path) point.HoldRootIndex = rootIndex;

                double playableStartBeat;
                double playableEndBeat;
                if (hasRuntimePath)
                {
                    if (runtimePath.PreservesLegacyCheckpointSemantics) ConfigureLegacySemantics(path);
                    else
                    {
                        ConfigureAuthoredSemantics(runtimePath.SemanticNodes);
                        foreach (var node in runtimePath.SemanticNodes)
                        {
                            if (!node.Judged || node.SlideNodeRole != SlideNodeRole.Attach) continue;
                            var evaluatedAttach = runtimePath.Evaluator.Evaluate(node.Time);
                            node.Lane = evaluatedAttach.Lane;
                            node.Size = evaluatedAttach.Size;
                        }
                    }
                    if (!runtimePath.HasPlayableRange) continue;
                    playableStartBeat = runtimePath.PlayableStartBeat.Value;
                    playableEndBeat = runtimePath.PlayableEndBeat.Value;
                }
                else if (hasSemanticFallback)
                {
                    if (!TryGetPlayableBeatBounds(semanticFallback.SemanticNodes,
                        out playableStartBeat, out playableEndBeat)) continue;
                }
                else
                {
                    ConfigureLegacySemantics(path);
                    playableStartBeat = head.Beat;
                    playableEndBeat = path[^1].Beat;
                }
                if (playableEndBeat <= playableStartBeat + 1e-9) continue;

                var authoredJudgedNodes = (hasRuntimePath ? runtimePath.SemanticNodes :
                    hasSemanticFallback ? semanticFallback.SemanticNodes : path)
                    .Where(note => note.Judged).ToArray();
                for (var beat = playableStartBeat + EighthNoteBeats;
                    beat < playableEndBeat - 1e-9;
                    beat += EighthNoteBeats)
                {
                    if (authoredJudgedNodes.Any(note => Math.Abs(note.Beat - beat) < 1e-9)) continue;
                    var segment = SegmentAt(path, beat);
                    if (segment == null) continue;
                    var start = segment.Value.start;
                    var end = segment.Value.end;
                    var progress = (float)((beat - start.Beat) / (end.Beat - start.Beat));
                    var checkpointTime = timeAtBeat(beat);
                    var evaluated = runtimePath?.Evaluator.Evaluate(checkpointTime);
                    var fallbackConnector = hasSemanticFallback
                        ? semanticFallback.Connectors.FirstOrDefault(connector =>
                            ReferenceEquals(connector.Start, start) && ReferenceEquals(connector.End, end))
                        : null;
                    var fallbackProgress = fallbackConnector == null
                        ? progress
                        : HoldPathMath.EaseProgress(progress, fallbackConnector.Ease);
                    chart.Notes.Add(new RuntimeNote
                    {
                        Index = nextIndex++,
                        SourceId = $"hold:auto:{head.SourceId}:{beat:R}",
                        Archetype = "RuntimeHoldAutoCheckpoint",
                        Beat = beat,
                        Time = checkpointTime,
                        Lane = evaluated?.Lane ?? start.Lane + (end.Lane - start.Lane) * fallbackProgress,
                        Size = evaluated?.Size ?? Math.Max(.25f,
                            start.Size + (end.Size - start.Size) * fallbackProgress),
                        Kind = RuntimeNoteKind.Sustain,
                        Critical = head.Critical,
                        TimeScaleGroup = head.TimeScaleGroup,
                        Visible = false,
                        Judged = true,
                        HoldCheckpointSource = HoldCheckpointSource.Auto,
                        HoldRootIndex = rootIndex,
                    });
                }
            }
        }

        static void ConfigureAuthoredSemantics(IReadOnlyList<RuntimeNote> path)
        {
            foreach (var node in path)
            {
                var isJudgedEnd = node.Judged && node.SlideNodeRole == SlideNodeRole.End;
                node.IsHoldTerminal = isJudgedEnd;
                if (isJudgedEnd)
                {
                    if (node.Kind == RuntimeNoteKind.Release) node.Kind = RuntimeNoteKind.Sustain;
                    // Geometry termination and judgment type are independent.
                    // A Trace at the final path point ends the Hold lifecycle,
                    // but remains a Trace checkpoint rather than a Tail cue.
                    node.HoldCheckpointSource = node.SlideJudgeMode == SlideJudgeMode.Trace
                        ? HoldCheckpointSource.Mid
                        : HoldCheckpointSource.Tail;
                }
                else if (node.Judged && node.Kind == RuntimeNoteKind.Sustain &&
                    node.SlideNodeRole is SlideNodeRole.Tick or SlideNodeRole.Attach)
                    node.HoldCheckpointSource = HoldCheckpointSource.Mid;
                else if (node.HoldCheckpointSource == HoldCheckpointSource.Tail)
                    node.HoldCheckpointSource = HoldCheckpointSource.None;
            }
        }

        static void ConfigureLegacySemantics(IReadOnlyList<RuntimeNote> path)
        {
            var tail = path[^1];
            tail.IsHoldTerminal = true;
            foreach (var mid in path.Skip(1).Take(path.Count - 2).Where(IsAuthoredMid))
                mid.HoldCheckpointSource = HoldCheckpointSource.Mid;
            if (!tail.Judged) return;
            if (tail.Kind == RuntimeNoteKind.Release) tail.Kind = RuntimeNoteKind.Sustain;
            tail.HoldCheckpointSource = HoldCheckpointSource.Tail;
        }

        static List<List<RuntimeConnector>> CollectConnectorComponents(IReadOnlyList<RuntimeConnector> connectors)
        {
            var usable = connectors.Where(connector => connector?.Start != null && connector.End != null).ToList();
            var unseen = new HashSet<RuntimeConnector>(usable);
            var components = new List<List<RuntimeConnector>>();
            while (unseen.Count > 0)
            {
                RuntimeConnector seed = null;
                foreach (var connector in unseen) { seed = connector; break; }
                var component = new List<RuntimeConnector>();
                var nodes = new HashSet<RuntimeNote> { seed.Start, seed.End };
                var changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var connector in usable)
                    {
                        if (!unseen.Contains(connector) ||
                            (!nodes.Contains(connector.Start) && !nodes.Contains(connector.End))) continue;
                        unseen.Remove(connector);
                        component.Add(connector);
                        changed |= nodes.Add(connector.Start);
                        changed |= nodes.Add(connector.End);
                    }
                }
                components.Add(component);
            }
            return components;
        }

        static bool TryCollectLinearConnectorPath(IReadOnlyList<RuntimeConnector> component,
            out List<RuntimeNote> orderedNodes, out List<RuntimeConnector> orderedConnectors)
        {
            orderedNodes = null;
            orderedConnectors = null;
            if (component.Count == 0 || component.Any(connector => connector?.Start == null || connector.End == null))
                return false;

            var outgoing = new Dictionary<RuntimeNote, RuntimeConnector>();
            var incoming = new Dictionary<RuntimeNote, RuntimeConnector>();
            var nodes = new HashSet<RuntimeNote>();
            foreach (var connector in component)
            {
                if (outgoing.ContainsKey(connector.Start) || incoming.ContainsKey(connector.End)) return false;
                outgoing[connector.Start] = connector;
                incoming[connector.End] = connector;
                nodes.Add(connector.Start);
                nodes.Add(connector.End);
            }

            RuntimeNote head = null;
            var headCount = 0;
            foreach (var node in nodes)
                if (!incoming.ContainsKey(node)) { head = node; headCount++; }
            if (headCount != 1) return false;

            orderedNodes = new List<RuntimeNote> { head };
            orderedConnectors = new List<RuntimeConnector>();
            var visited = new HashSet<RuntimeConnector>();
            var current = head;
            while (outgoing.TryGetValue(current, out var connector))
            {
                if (!visited.Add(connector) || connector.End.Time < connector.Start.Time - 1e-9)
                {
                    orderedNodes = null;
                    orderedConnectors = null;
                    return false;
                }
                orderedConnectors.Add(connector);
                orderedNodes.Add(connector.End);
                current = connector.End;
            }
            if (visited.Count == component.Count) return true;
            orderedNodes = null;
            orderedConnectors = null;
            return false;
        }

        static bool TryGetPlayableBeatBounds(IReadOnlyList<RuntimeNote> semanticNodes,
            out double playableStartBeat, out double playableEndBeat)
        {
            playableStartBeat = double.PositiveInfinity;
            playableEndBeat = double.NegativeInfinity;
            foreach (var node in semanticNodes)
            {
                if (!node.Judged) continue;
                playableStartBeat = Math.Min(playableStartBeat, node.Beat);
                playableEndBeat = Math.Max(playableEndBeat, node.Beat);
            }
            return double.IsFinite(playableStartBeat) && double.IsFinite(playableEndBeat);
        }

        static List<RuntimeNote> CollectSemanticNodes(RuntimeChart chart, IReadOnlyList<RuntimeNote> geometryNodes)
        {
            var nodes = new List<RuntimeNote>(geometryNodes);
            var nodeSet = new HashSet<RuntimeNote>(geometryNodes);
            var rootIndices = new HashSet<int>(geometryNodes.Select(node => node.Index));
            foreach (var node in geometryNodes)
                if (node.HoldRootIndex >= 0) rootIndices.Add(node.HoldRootIndex);
            foreach (var note in chart.Notes)
            {
                if (note.HoldCheckpointSource == HoldCheckpointSource.Auto || nodeSet.Contains(note) ||
                    note.HoldRootIndex < 0 || !rootIndices.Contains(note.HoldRootIndex) || !nodeSet.Add(note)) continue;
                nodes.Add(note);
            }
            nodes.Sort((left, right) =>
            {
                var beat = left.Beat.CompareTo(right.Beat);
                return beat != 0 ? beat : left.Index.CompareTo(right.Index);
            });
            return nodes;
        }

        static bool IsUnspecifiedSemanticNode(RuntimeNote node) =>
            node.SlideNodeRole == SlideNodeRole.Unspecified && node.SlideJudgeMode == SlideJudgeMode.Unspecified;

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
