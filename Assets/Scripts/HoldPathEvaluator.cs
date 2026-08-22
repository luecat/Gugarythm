using System;

namespace Gugarythm
{
    public sealed class HoldPathEvaluator
    {
        const double TimeEpsilon = 1e-9;

        readonly RuntimeHoldPath path;
        readonly float[] laneTangents;
        readonly float[] sizeTangents;

        internal HoldPathEvaluator(RuntimeHoldPath path)
        {
            this.path = path;
            laneTangents = BuildTangents(path, note => note.Lane);
            sizeTangents = BuildTangents(path, note => Math.Max(.25f, note.Size));
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

            var duration = (float)(segment.End.Time - segment.Start.Time);
            var lane = Hermite(segment.Start.Lane, segment.End.Lane,
                laneTangents[segmentIndex], laneTangents[segmentIndex + 1], duration, progress);
            var size = Hermite(Math.Max(.25f, segment.Start.Size), Math.Max(.25f, segment.End.Size),
                sizeTangents[segmentIndex], sizeTangents[segmentIndex + 1], duration, progress);
            lane = Math.Clamp(lane, Math.Min(segment.Start.Lane, segment.End.Lane), Math.Max(segment.Start.Lane, segment.End.Lane));
            size = Math.Max(.25f, Math.Clamp(size, Math.Min(segment.Start.Size, segment.End.Size),
                Math.Max(segment.Start.Size, segment.End.Size)));
            return new HoldPathSample(lane, size, segmentIndex, progress);
        }

        static float[] BuildTangents(RuntimeHoldPath path, Func<RuntimeNote, float> value)
        {
            var nodes = path.Nodes;
            var count = nodes.Count;
            var tangents = new float[count];
            if (count < 2) return tangents;
            var slopes = new float[count - 1];
            var durations = new double[count - 1];
            for (var index = 0; index < count - 1; index++)
            {
                durations[index] = nodes[index + 1].Time - nodes[index].Time;
                slopes[index] = durations[index] <= TimeEpsilon ? 0 :
                    (float)((value(nodes[index + 1]) - value(nodes[index])) / durations[index]);
            }

            tangents[0] = slopes[0];
            tangents[^1] = slopes[^1];
            for (var index = 1; index < count - 1; index++)
            {
                var previous = slopes[index - 1];
                var next = slopes[index];
                if (durations[index - 1] <= TimeEpsilon || durations[index] <= TimeEpsilon ||
                    previous == 0 || next == 0 || Math.Sign(previous) != Math.Sign(next))
                {
                    tangents[index] = 0;
                    continue;
                }
                var previousWeight = 2 * durations[index] + durations[index - 1];
                var nextWeight = durations[index] + 2 * durations[index - 1];
                tangents[index] = (float)((previousWeight + nextWeight) /
                    (previousWeight / previous + nextWeight / next));
            }

            for (var nodeIndex = 0; nodeIndex < count; nodeIndex++)
            {
                var incomingStops = nodeIndex > 0 && EndsAtRest(path.Segments[nodeIndex - 1].Ease);
                var outgoingStops = nodeIndex < path.Segments.Count && StartsAtRest(path.Segments[nodeIndex].Ease);
                if (incomingStops || outgoingStops) tangents[nodeIndex] = 0;
            }
            return tangents;
        }

        static bool StartsAtRest(int ease) => ease is 1 or 3;
        static bool EndsAtRest(int ease) => ease is 2 or 3;

        static float Hermite(float start, float end, float startTangent, float endTangent, float duration, float progress)
        {
            var t2 = progress * progress;
            var t3 = t2 * progress;
            return (2 * t3 - 3 * t2 + 1) * start +
                   (t3 - 2 * t2 + progress) * duration * startTangent +
                   (-2 * t3 + 3 * t2) * end +
                   (t3 - t2) * duration * endTangent;
        }
    }
}
