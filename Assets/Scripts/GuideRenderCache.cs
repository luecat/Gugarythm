using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gugarhythm
{
    public readonly struct GuideRenderSample
    {
        public readonly float Progress;
        public readonly double Time;
        public readonly float Lane;
        public readonly float Size;
        public readonly float Alpha;
        public readonly double VisualPosition;
        public readonly bool HasVisualPosition;

        public GuideRenderSample(float progress, double time, float lane, float size, float alpha,
            double visualPosition = double.NaN)
        {
            Progress = progress;
            Time = time;
            Lane = lane;
            Size = size;
            Alpha = alpha;
            VisualPosition = visualPosition;
            HasVisualPosition = double.IsFinite(visualPosition);
        }
    }

    public readonly struct GuideVisualSpan
    {
        public readonly float FirstProgress;
        public readonly float LastProgress;
        public readonly double FirstVisualPosition;
        public readonly double LastVisualPosition;
        public readonly bool UsesDirectEvaluator;

        public GuideVisualSpan(float firstProgress, float lastProgress, double firstVisualPosition,
            double lastVisualPosition, bool usesDirectEvaluator)
        {
            FirstProgress = firstProgress;
            LastProgress = lastProgress;
            FirstVisualPosition = firstVisualPosition;
            LastVisualPosition = lastVisualPosition;
            UsesDirectEvaluator = usesDirectEvaluator;
        }

        public double VisualPositionAt(float progress)
        {
            var length = LastProgress - FirstProgress;
            if (Math.Abs(length) < 1e-7f) return FirstVisualPosition;
            return FirstVisualPosition + (LastVisualPosition - FirstVisualPosition) *
                ((progress - FirstProgress) / length);
        }

        public bool Intersects(double minimum, double maximum) =>
            Math.Max(Math.Min(FirstVisualPosition, LastVisualPosition), minimum) <=
            Math.Min(Math.Max(FirstVisualPosition, LastVisualPosition), maximum) + 1e-9;

        public GuideVisualSpan Clip(double minimum, double maximum)
        {
            if (Math.Abs(LastVisualPosition - FirstVisualPosition) < 1e-12) return this;
            var first = Mathf.Clamp01(FirstProgress + (float)((minimum - FirstVisualPosition) /
                (LastVisualPosition - FirstVisualPosition)) * (LastProgress - FirstProgress));
            var last = Mathf.Clamp01(FirstProgress + (float)((maximum - FirstVisualPosition) /
                (LastVisualPosition - FirstVisualPosition)) * (LastProgress - FirstProgress));
            if (last < first) (first, last) = (last, first);
            return new GuideVisualSpan(first, last, VisualPositionAt(first), VisualPositionAt(last), UsesDirectEvaluator);
        }
    }

    // Immutable authoring-data projection for one Guide.  It is rebuilt only
    // when a RuntimeChart is replaced, never while the song clock advances.
    public sealed class GuideRenderCache
    {
        readonly RuntimeGuide guide;
        readonly float laneP0;
        readonly float laneP1;
        readonly float laneP2;
        readonly float laneP3;
        readonly float sizeP0;
        readonly float sizeP1;
        readonly float sizeP2;
        readonly float sizeP3;
        readonly List<double> boundaryTimes = new();
        readonly List<GuideVisualSpan> visualSpans = new();

        public RuntimeGuide Guide => guide;
        public string TimeScaleGroup { get; }
        public double HeadTime { get; }
        public double TailTime { get; }
        public int Color { get; }
        public int VisualSpanCount => visualSpans.Count;

        public GuideRenderCache(RuntimeGuide guide)
        {
            this.guide = guide ?? throw new ArgumentNullException(nameof(guide));
            laneP0 = guide.Start.Lane;
            laneP1 = guide.Head.Lane;
            laneP2 = guide.Tail.Lane;
            laneP3 = guide.End.Lane;
            sizeP0 = guide.Start.Size;
            sizeP1 = guide.Head.Size;
            sizeP2 = guide.Tail.Size;
            sizeP3 = guide.End.Size;
            HeadTime = guide.Head.Time;
            TailTime = guide.Tail.Time;
            TimeScaleGroup = string.IsNullOrEmpty(guide.Head.TimeScaleGroup)
                ? guide.Tail.TimeScaleGroup : guide.Head.TimeScaleGroup;
            Color = guide.Color;
        }

        public GuideRenderSample Evaluate(float progress, double visualPosition = double.NaN)
        {
            progress = Mathf.Clamp01(progress);
            var lane = EvaluateCurve(laneP0, laneP1, laneP2, laneP3, progress);
            var size = Mathf.Max(.01f, EvaluateCurve(sizeP0, sizeP1, sizeP2, sizeP3, progress));
            return new GuideRenderSample(progress, HeadTime + (TailTime - HeadTime) * progress, lane, size,
                Mathf.Lerp(guide.HeadOpacity, guide.TailOpacity, progress), visualPosition);
        }

        public void BuildVisualSpans(RuntimeChart chart)
        {
            visualSpans.Clear();
            boundaryTimes.Clear();
            boundaryTimes.Add(HeadTime);
            var key = chart != null && string.IsNullOrEmpty(TimeScaleGroup) ? chart.DefaultTimeScaleGroup : TimeScaleGroup;
            if (chart != null && !string.IsNullOrEmpty(key) &&
                chart.TimeScaleGroups.TryGetValue(key, out var map))
                map.AppendBoundaryTimes(HeadTime, TailTime, boundaryTimes);
            boundaryTimes.Add(TailTime);
            boundaryTimes.Sort();
            if (TailTime < HeadTime) boundaryTimes.Reverse();

            var duration = TailTime - HeadTime;
            for (var index = 1; index < boundaryTimes.Count; index++)
            {
                var firstTime = boundaryTimes[index - 1];
                var lastTime = boundaryTimes[index];
                var firstProgress = Math.Abs(duration) < 1e-12 ? 0 : (float)((firstTime - HeadTime) / duration);
                var lastProgress = Math.Abs(duration) < 1e-12 ? 1 : (float)((lastTime - HeadTime) / duration);
                var firstPosition = chart?.VisualPosition(firstTime, key) ?? firstTime;
                var lastPosition = chart?.VisualPosition(lastTime, key) ?? lastTime;
                visualSpans.Add(new GuideVisualSpan(firstProgress, lastProgress, firstPosition, lastPosition,
                    !double.IsFinite(firstPosition) || !double.IsFinite(lastPosition)));
            }
            if (visualSpans.Count == 0)
            {
                var firstPosition = chart?.VisualPosition(HeadTime, key) ?? HeadTime;
                var lastPosition = chart?.VisualPosition(TailTime, key) ?? TailTime;
                visualSpans.Add(new GuideVisualSpan(0, 1, firstPosition, lastPosition, false));
            }
        }

        public void QueryVisibleSpans(VisualFrameContext frame, double approachDuration, List<GuideVisualSpan> output)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            var current = frame.CurrentPosition(TimeScaleGroup);
            var minimum = Math.Min(current, current + approachDuration);
            var maximum = Math.Max(current, current + approachDuration);
            foreach (var span in visualSpans)
            {
                if (!span.Intersects(minimum, maximum)) continue;
                output.Add(span.Clip(Math.Max(minimum, Math.Min(span.FirstVisualPosition, span.LastVisualPosition)),
                    Math.Min(maximum, Math.Max(span.FirstVisualPosition, span.LastVisualPosition))));
            }
        }

        float EvaluateCurve(float p0, float p1, float p2, float p3, float progress)
        {
            if (guide.Ease != -1)
                return Mathf.Lerp(p1, p2, Ease(progress, guide.Ease));

            var t2 = progress * progress;
            var t3 = t2 * progress;
            return .5f * ((2 * p1) + (-p0 + p2) * progress +
                (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        static float Ease(float progress, int ease) => ease switch
        {
            1 => progress * progress,
            2 => 1 - (1 - progress) * (1 - progress),
            3 => progress < .5f ? 2 * progress * progress : 1 - Mathf.Pow(-2 * progress + 2, 2) * .5f,
            _ => progress,
        };
    }

    public readonly struct GuideProjectedSample
    {
        public readonly Vector2 Center;
        public readonly Vector2 Left;
        public readonly Vector2 Right;

        public GuideProjectedSample(Vector2 center, Vector2 left, Vector2 right)
        {
            Center = center;
            Left = left;
            Right = right;
        }
    }
}
