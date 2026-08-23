using System;

namespace Gugarhythm
{
    public sealed class HoldPathEvaluator
    {
        const double TimeEpsilon = 1e-9;

        readonly RuntimeHoldPath path;

        internal HoldPathEvaluator(RuntimeHoldPath path)
        {
            this.path = path;
        }

        public HoldPathSample Evaluate(double time)
        {
            var nodes = path.Nodes;
            if (nodes.Count < 2) return new HoldPathSample(nodes[0].Lane, nodes[0].Size, 0, 0);
            if (time < nodes[0].Time) return EvaluateSegment(0, 0);
            if (time >= nodes[^1].Time) return EvaluateSegment(path.Segments.Count - 1, 1);

            var segmentIndex = 0;
            while (segmentIndex < path.Segments.Count - 1 && nodes[segmentIndex + 1].Time <= time + TimeEpsilon)
                segmentIndex++;
            var segment = path.Segments[segmentIndex];
            var duration = segment.End.Time - segment.Start.Time;
            var progress = duration <= TimeEpsilon ? 1f :
                (float)Math.Clamp((time - segment.Start.Time) / duration, 0, 1);
            return EvaluateSegment(segmentIndex, progress);
        }

        public HoldPathSample EvaluateSegment(int segmentIndex, float progress)
        {
            segmentIndex = Math.Clamp(segmentIndex, 0, path.Segments.Count - 1);
            progress = Math.Clamp(progress, 0, 1);
            var segment = path.Segments[segmentIndex];
            if (segment.HardCorner)
            {
                var point = progress < 1 ? segment.Start : segment.End;
                return new HoldPathSample(point.Lane, point.Size, segmentIndex, progress);
            }

            var easedProgress = HoldPathMath.EaseProgress(progress, segment.Ease);
            var lane = segment.Start.Lane + (segment.End.Lane - segment.Start.Lane) * easedProgress;
            var startSize = Math.Max(.25f, segment.Start.Size);
            var endSize = Math.Max(.25f, segment.End.Size);
            var size = Math.Max(.25f, startSize + (endSize - startSize) * easedProgress);
            return new HoldPathSample(lane, size, segmentIndex, progress);
        }
    }
}
