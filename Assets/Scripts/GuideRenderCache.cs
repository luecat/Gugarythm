using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gugarhythm
{
    public readonly struct GuideStackSummary
    {
        public readonly int SourcePathCount;
        public readonly int RenderPathCount;

        public GuideStackSummary(int sourcePathCount, int renderPathCount)
        {
            SourcePathCount = Math.Max(0, sourcePathCount);
            RenderPathCount = Math.Max(0, renderPathCount);
        }
    }

    // Presentation-only normalization for authored brightness stacks. Exact
    // duplicate Guides are equivalent to one Guide with source-over alpha
    // compositing, provided no differently styled Guide overlaps between them
    // in source order. The chart's judged notes and Hold paths are untouched.
    public static class GuideStackOptimizer
    {
        public const float BaseLayerAlpha = .32f;

        sealed class Representative
        {
            public RuntimeGuide Guide;
            public int SourceIndex;
        }

        readonly struct GuideKey : IEquatable<GuideKey>
        {
            readonly RuntimeGuide guide;

            public GuideKey(RuntimeGuide guide) => this.guide = guide;

            public bool Equals(GuideKey other) => SameGuide(guide, other.guide);
            public override bool Equals(object obj) => obj is GuideKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                AddPoint(ref hash, guide.Start);
                AddPoint(ref hash, guide.Head);
                AddPoint(ref hash, guide.Tail);
                AddPoint(ref hash, guide.End);
                hash.Add(guide.Color);
                hash.Add(guide.Fade);
                hash.Add(guide.Ease);
                hash.Add(guide.FadeOut);
                hash.Add(guide.HeadOpacity);
                hash.Add(guide.TailOpacity);
                return hash.ToHashCode();
            }

            static void AddPoint(ref HashCode hash, RuntimeGuidePoint point)
            {
                hash.Add(point.Time);
                hash.Add(point.Beat);
                hash.Add(point.Lane);
                hash.Add(point.Size);
                hash.Add(point.TimeScaleGroup, StringComparer.Ordinal);
            }
        }

        public static GuideStackSummary CollapseExactDuplicates(List<RuntimeGuide> guides)
        {
            if (guides == null || guides.Count == 0) return default;

            var sourcePathCount = 0;
            foreach (var guide in guides)
                if (guide != null) sourcePathCount += Math.Max(1, guide.StackCount);

            var output = new List<RuntimeGuide>(guides.Count);
            var latestByKey = new Dictionary<GuideKey, Representative>();
            for (var sourceIndex = 0; sourceIndex < guides.Count; sourceIndex++)
            {
                var guide = guides[sourceIndex];
                if (guide == null) continue;
                guide.StackCount = Math.Max(1, guide.StackCount);
                var key = new GuideKey(guide);
                if (latestByKey.TryGetValue(key, out var representative) &&
                    !HasOrderSensitiveOverlap(guides, representative.SourceIndex, sourceIndex, guide))
                {
                    representative.Guide.StackCount += guide.StackCount;
                    continue;
                }

                output.Add(guide);
                latestByKey[key] = new Representative { Guide = guide, SourceIndex = sourceIndex };
            }

            if (output.Count != guides.Count)
            {
                guides.Clear();
                guides.AddRange(output);
            }
            return new GuideStackSummary(sourcePathCount, output.Count);
        }

        public static float CompositeAlpha(float authoredOpacity, int stackCount)
        {
            var layerAlpha = BaseLayerAlpha * Mathf.Clamp01(authoredOpacity);
            return 1f - Mathf.Pow(1f - layerAlpha, Math.Max(1, stackCount));
        }

        static bool HasOrderSensitiveOverlap(IReadOnlyList<RuntimeGuide> guides, int firstIndex, int nextIndex,
            RuntimeGuide target)
        {
            var targetMinimum = Math.Min(target.Head.Time, target.Tail.Time);
            var targetMaximum = Math.Max(target.Head.Time, target.Tail.Time);
            for (var index = firstIndex + 1; index < nextIndex; index++)
            {
                var candidate = guides[index];
                if (candidate == null || SameGuide(target, candidate)) continue;
                var candidateMinimum = Math.Min(candidate.Head.Time, candidate.Tail.Time);
                var candidateMaximum = Math.Max(candidate.Head.Time, candidate.Tail.Time);
                if (Math.Min(targetMaximum, candidateMaximum) >
                    Math.Max(targetMinimum, candidateMinimum) + 1e-9)
                    return true;
            }
            return false;
        }

        static bool SameGuide(RuntimeGuide left, RuntimeGuide right) =>
            left != null && right != null &&
            SamePoint(left.Start, right.Start) && SamePoint(left.Head, right.Head) &&
            SamePoint(left.Tail, right.Tail) && SamePoint(left.End, right.End) &&
            left.Color == right.Color && left.Fade == right.Fade && left.Ease == right.Ease &&
            left.FadeOut == right.FadeOut && left.HeadOpacity.Equals(right.HeadOpacity) &&
            left.TailOpacity.Equals(right.TailOpacity);

        static bool SamePoint(RuntimeGuidePoint left, RuntimeGuidePoint right) =>
            left.Time.Equals(right.Time) && left.Beat.Equals(right.Beat) &&
            left.Lane.Equals(right.Lane) && left.Size.Equals(right.Size) &&
            string.Equals(left.TimeScaleGroup, right.TimeScaleGroup, StringComparison.Ordinal);
    }

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
        public int StackCount { get; }
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
            StackCount = Math.Max(1, guide.StackCount);
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
                return Mathf.Lerp(p1, p2, HoldPathMath.EaseProgress(progress, guide.Ease));

            var t2 = progress * progress;
            var t3 = t2 * progress;
            return .5f * ((2 * p1) + (-p0 + p2) * progress +
                (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 +
                (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

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
