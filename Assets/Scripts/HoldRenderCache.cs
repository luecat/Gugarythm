using System;
using UnityEngine;

namespace Gugarhythm
{
    // Immutable presentation data layered over RuntimeHoldPath.  It never
    // participates in checkpoint, judgment, audio, or persistent-head state.
    public sealed class HoldRenderCache
    {
        readonly Segment[] segments;

        readonly struct Segment
        {
            public readonly double StartTime;
            public readonly double EndTime;
            public readonly double StartPosition;
            public readonly double EndPosition;
            public readonly bool CanInterpolate;

            public Segment(RuntimeHoldPathSegment segment, RuntimeChart chart)
            {
                StartTime = segment.Start.Time;
                EndTime = segment.End.Time;
                var group = string.IsNullOrEmpty(segment.Start.TimeScaleGroup)
                    ? segment.End.TimeScaleGroup : segment.Start.TimeScaleGroup;
                var key = string.IsNullOrEmpty(group) ? chart.DefaultTimeScaleGroup : group;
                StartPosition = chart.VisualPosition(StartTime, key);
                EndPosition = chart.VisualPosition(EndTime, key);
                CanInterpolate = key == null || !chart.TimeScaleGroups.TryGetValue(key, out var map) ||
                    map.IsSingleScaleInterval(StartTime, EndTime);
            }

            public bool TryPosition(HoldTessellationPoint point, out double position)
            {
                if (!CanInterpolate)
                {
                    position = 0;
                    return false;
                }
                var duration = EndTime - StartTime;
                var progress = Math.Abs(duration) < 1e-12 ? 0 : (point.Time - StartTime) / duration;
                position = StartPosition + (EndPosition - StartPosition) * progress;
                return true;
            }
        }

        public HoldRenderCache(RuntimeHoldPath path, RuntimeChart chart)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            segments = new Segment[path.Segments.Count];
            for (var index = 0; index < segments.Length; index++) segments[index] = new Segment(path.Segments[index], chart);
        }

        public bool TryVisualPosition(HoldTessellationPoint point, out double position)
        {
            var index = point.Sample.SegmentIndex;
            if (index >= 0 && index < segments.Length) return segments[index].TryPosition(point, out position);
            position = 0;
            return false;
        }
    }

    public readonly struct HoldProjectedPoint
    {
        public readonly double Time;
        public readonly HoldPathSample Sample;
        public readonly Vector2 Position;
        public readonly float Width;

        public HoldProjectedPoint(HoldTessellationPoint point, Vector2 position, float width)
        {
            Time = point.Time;
            Sample = point.Sample;
            Position = position;
            Width = width;
        }
    }
}
