using System;
using System.Collections.Generic;

namespace Gugarythm
{
    public sealed class ChartRenderIndex
    {
        readonly RuntimeChart chart;
        readonly Dictionary<string, Bucket<RuntimeNote>> notes = new(StringComparer.Ordinal);
        readonly Dictionary<string, Bucket<HoldRenderRun>> holdRuns = new(StringComparer.Ordinal);
        readonly Dictionary<string, Bucket<RuntimeSimLine>> simLines = new(StringComparer.Ordinal);
        readonly Dictionary<string, Bucket<RuntimeGuide>> guides = new(StringComparer.Ordinal);

        static readonly Comparison<RuntimeNote> NoteOrder = (left, right) =>
            left.Time != right.Time ? left.Time.CompareTo(right.Time) : left.Index.CompareTo(right.Index);
        static readonly Comparison<HoldRenderRun> HoldOrder = (left, right) =>
        {
            var time = left.Start.Time.CompareTo(right.Start.Time);
            if (time != 0) return time;
            var root = left.Path.RootIndex.CompareTo(right.Path.RootIndex);
            return root != 0 ? root : left.FirstSegmentIndex.CompareTo(right.FirstSegmentIndex);
        };

        public ChartRenderIndex(RuntimeChart chart)
        {
            this.chart = chart ?? throw new ArgumentNullException(nameof(chart));
            foreach (var note in chart.Notes)
            {
                if (!note.Visible) continue;
                var group = Group(note.TimeScaleGroup);
                var position = chart.VisualPosition(note.Time, group);
                Add(notes, group, new Entry<RuntimeNote>(position, position, note));
            }
            foreach (var path in chart.HoldPaths)
            foreach (var run in path.RenderRuns)
            {
                var group = Group(run.Start.TimeScaleGroup);
                var first = chart.VisualPosition(run.Start.Time, group);
                var last = chart.VisualPosition(run.End.Time, group);
                Add(holdRuns, group, new Entry<HoldRenderRun>(Math.Min(first, last), Math.Max(first, last), run));
            }
            foreach (var simLine in chart.SimLines)
            {
                var group = Group(simLine.A?.TimeScaleGroup);
                var first = chart.VisualPosition(simLine.A?.Time ?? 0, group);
                var last = chart.VisualPosition(simLine.B?.Time ?? 0, group);
                Add(simLines, group, new Entry<RuntimeSimLine>(Math.Min(first, last), Math.Max(first, last), simLine));
            }
            foreach (var guide in chart.Guides)
            {
                var group = Group(guide.Head.TimeScaleGroup);
                var first = chart.VisualPosition(guide.Head.Time, group);
                var last = chart.VisualPosition(guide.Tail.Time, group);
                Add(guides, group, new Entry<RuntimeGuide>(Math.Min(first, last), Math.Max(first, last), guide));
            }
            Seal(notes);
            Seal(holdRuns);
            Seal(simLines);
            Seal(guides);
        }

        public void QueryNotes(double visualTime, double behind, double ahead, List<RuntimeNote> output)
        {
            Query(notes, visualTime, behind, ahead, false, output);
            output.Sort(NoteOrder);
        }

        public void QueryHoldRuns(double visualTime, double behind, double ahead, List<HoldRenderRun> output)
        {
            Query(holdRuns, visualTime, behind, ahead, true, output);
            output.Sort(HoldOrder);
        }

        public void QuerySimLines(double visualTime, double behind, double ahead, List<RuntimeSimLine> output) =>
            Query(simLines, visualTime, behind, ahead, true, output);

        public void QueryGuides(double visualTime, double behind, double ahead, List<RuntimeGuide> output) =>
            Query(guides, visualTime, behind, ahead, true, output);

        string Group(string group) => string.IsNullOrEmpty(group) ? chart.DefaultTimeScaleGroup ?? string.Empty : group;

        static void Add<T>(Dictionary<string, Bucket<T>> buckets, string group, Entry<T> entry)
        {
            if (!buckets.TryGetValue(group, out var bucket)) buckets[group] = bucket = new Bucket<T>();
            bucket.Entries.Add(entry);
        }

        static void Seal<T>(Dictionary<string, Bucket<T>> buckets)
        {
            foreach (var bucket in buckets.Values) bucket.Seal();
        }

        void Query<T>(Dictionary<string, Bucket<T>> buckets, double visualTime, double behind, double ahead,
            bool intervals, List<T> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            behind = Math.Max(0, behind);
            ahead = Math.Max(0, ahead);
            foreach (var pair in buckets)
            {
                var current = chart.VisualPosition(visualTime, pair.Key);
                pair.Value.Query(current - behind, current + ahead, intervals, output);
            }
        }

        readonly struct Entry<T>
        {
            public readonly double Min;
            public readonly double Max;
            public readonly T Item;

            public Entry(double min, double max, T item)
            {
                Min = min;
                Max = max;
                Item = item;
            }
        }

        sealed class Bucket<T>
        {
            public readonly List<Entry<T>> Entries = new();
            double[] prefixMax = Array.Empty<double>();

            public void Seal()
            {
                Entries.Sort((left, right) => left.Min.CompareTo(right.Min));
                prefixMax = new double[Entries.Count];
                var maximum = double.NegativeInfinity;
                for (var index = 0; index < Entries.Count; index++)
                {
                    maximum = Math.Max(maximum, Entries[index].Max);
                    prefixMax[index] = maximum;
                }
            }

            public void Query(double minimum, double maximum, bool intervals, List<T> output)
            {
                var first = intervals ? LowerBoundPrefix(minimum) : LowerBoundMin(minimum);
                for (var index = first; index < Entries.Count; index++)
                {
                    var entry = Entries[index];
                    if (entry.Min > maximum) break;
                    if (!intervals || entry.Max >= minimum) output.Add(entry.Item);
                }
            }

            int LowerBoundMin(double value)
            {
                var low = 0;
                var high = Entries.Count;
                while (low < high)
                {
                    var middle = (low + high) >> 1;
                    if (Entries[middle].Min < value) low = middle + 1;
                    else high = middle;
                }
                return low;
            }

            int LowerBoundPrefix(double value)
            {
                var low = 0;
                var high = prefixMax.Length;
                while (low < high)
                {
                    var middle = (low + high) >> 1;
                    if (prefixMax[middle] < value) low = middle + 1;
                    else high = middle;
                }
                return low;
            }
        }
    }
}
