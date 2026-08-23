using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Gugarhythm
{
    // Presentation-only cache for one frame's visual-time anchors. It never owns
    // chart or gameplay state, so judgment and audio continue using RuntimeChart.
    public sealed class VisualFrameContext
    {
        struct GroupEntry
        {
            public RuntimeTimeScaleGroup Map;
            public double CurrentPosition;
            public bool HasCurrentPosition;

            public GroupEntry(RuntimeTimeScaleGroup map)
            {
                Map = map;
                CurrentPosition = 0;
                HasCurrentPosition = false;
            }
        }

        readonly Dictionary<string, int> groupIndices = new(StringComparer.Ordinal);
        readonly List<GroupEntry> groups = new();
        RuntimeChart chart;
        double visualTime;
        long positionAtTicks;
        int positionAtCalls;
        int positionAtSearchSteps;

        public int PositionAtCallCount => positionAtCalls;
        public int PositionAtSearchStepCount => positionAtSearchSteps;
        public float PositionAtMilliseconds =>
            (float)(positionAtTicks * 1000d / Stopwatch.Frequency);

        public void BeginFrame(RuntimeChart runtimeChart, double currentVisualTime)
        {
            if (!ReferenceEquals(chart, runtimeChart)) Rebuild(runtimeChart);
            visualTime = currentVisualTime;
            positionAtTicks = 0;
            positionAtCalls = 0;
            positionAtSearchSteps = 0;
            for (var index = 0; index < groups.Count; index++)
            {
                var entry = groups[index];
                entry.HasCurrentPosition = false;
                groups[index] = entry;
            }
        }

        public double PositionAt(double time, string group)
        {
            if (!TryGetGroup(group, out var index)) return time;
            return Evaluate(groups[index].Map, time);
        }

        public double CurrentPosition(string group)
        {
            if (!TryGetGroup(group, out var index)) return visualTime;
            var entry = groups[index];
            if (entry.HasCurrentPosition) return entry.CurrentPosition;
            entry.CurrentPosition = Evaluate(entry.Map, visualTime);
            entry.HasCurrentPosition = true;
            groups[index] = entry;
            return entry.CurrentPosition;
        }

        public float Approach(double targetVisualPosition, string group, double approachDuration)
        {
            if (Math.Abs(approachDuration) < 1e-12) return 0;
            return 1f - (float)((targetVisualPosition - CurrentPosition(group)) / approachDuration);
        }

        void Rebuild(RuntimeChart runtimeChart)
        {
            chart = runtimeChart;
            groupIndices.Clear();
            groups.Clear();
            if (chart == null) return;
            foreach (var pair in chart.TimeScaleGroups)
            {
                groupIndices.Add(pair.Key, groups.Count);
                groups.Add(new GroupEntry(pair.Value));
            }
        }

        bool TryGetGroup(string group, out int index)
        {
            index = -1;
            if (chart == null) return false;
            var key = string.IsNullOrEmpty(group) ? chart.DefaultTimeScaleGroup : group;
            return key != null && groupIndices.TryGetValue(key, out index);
        }

        double Evaluate(RuntimeTimeScaleGroup map, double time)
        {
            var start = Stopwatch.GetTimestamp();
            var position = map.PositionAt(time, out var searchSteps);
            positionAtTicks += Stopwatch.GetTimestamp() - start;
            positionAtCalls++;
            positionAtSearchSteps += searchSteps;
            return position;
        }
    }
}
