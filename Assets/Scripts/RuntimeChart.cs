using System;
using System.Collections.Generic;

namespace Gugarythm
{
    public enum RuntimeNoteKind { Tap, Flick, Sustain, Release }
    public enum JudgmentGrade { Pending, Perfect, Great, Good, Miss }

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
        public string Engine = "";
        public double BgmOffset;
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
