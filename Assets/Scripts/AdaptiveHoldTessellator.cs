using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gugarhythm
{
    public readonly struct HoldTessellationPoint
    {
        public readonly double Time;
        public readonly HoldPathSample Sample;

        public HoldTessellationPoint(double time, HoldPathSample sample)
        {
            Time = time;
            Sample = sample;
        }
    }

    public sealed class AdaptiveHoldTessellator
    {
        public const int MaxPointsPerSegment = 64;
        public const int MaxPointsPerRun = 256;
        public const float DefaultScreenErrorPixels = .75f;

        const int MaxSubdivisionDepth = 5;

        public void BuildVisibleRun(HoldRenderRun run, double firstVisibleTime, double lastVisibleTime,
            Func<HoldTessellationPoint, Vector2> project, List<HoldTessellationPoint> output)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (lastVisibleTime < firstVisibleTime) (firstVisibleTime, lastVisibleTime) = (lastVisibleTime, firstVisibleTime);

            var tolerance = DefaultScreenErrorPixels;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                output.Clear();
                if (BuildWithTolerance(run, firstVisibleTime, lastVisibleTime, tolerance, project, output)) return;
                tolerance *= 2;
            }

            BuildCappedFallback(run, firstVisibleTime, lastVisibleTime, output);
        }

        bool BuildWithTolerance(HoldRenderRun run, double firstVisibleTime, double lastVisibleTime, float tolerance,
            Func<HoldTessellationPoint, Vector2> project, List<HoldTessellationPoint> output)
        {
            var path = run.Path;
            for (var segmentIndex = run.FirstSegmentIndex; segmentIndex <= run.LastSegmentIndex; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                if (segment.End.Time < firstVisibleTime - 1e-9 || segment.Start.Time > lastVisibleTime + 1e-9) continue;
                var duration = segment.End.Time - segment.Start.Time;
                var startProgress = duration <= 1e-9 ? 0f :
                    (float)Math.Clamp((firstVisibleTime - segment.Start.Time) / duration, 0, 1);
                var endProgress = duration <= 1e-9 ? 1f :
                    (float)Math.Clamp((lastVisibleTime - segment.Start.Time) / duration, 0, 1);
                if (endProgress < startProgress) (startProgress, endProgress) = (endProgress, startProgress);
                var start = PointAt(path, segmentIndex, startProgress);
                var end = PointAt(path, segmentIndex, endProgress);
                AppendDistinct(output, start);
                Subdivide(path, segmentIndex, startProgress, endProgress, start, end, 0, tolerance, project, output);
                if (output.Count > MaxPointsPerRun) return false;
            }
            return output.Count <= MaxPointsPerRun;
        }

        static void BuildCappedFallback(HoldRenderRun run, double firstVisibleTime, double lastVisibleTime,
            List<HoldTessellationPoint> output)
        {
            output.Clear();
            var startTime = Math.Max(firstVisibleTime, run.Start.Time);
            var endTime = Math.Min(lastVisibleTime, run.End.Time);
            if (endTime < startTime) return;
            if (endTime - startTime <= 1e-9)
            {
                output.Add(new HoldTessellationPoint(startTime, run.Path.Evaluator.Evaluate(startTime)));
                return;
            }

            for (var index = 0; index < MaxPointsPerRun; index++)
            {
                var progress = index / (double)(MaxPointsPerRun - 1);
                var time = index == MaxPointsPerRun - 1 ? endTime : startTime + (endTime - startTime) * progress;
                output.Add(new HoldTessellationPoint(time, run.Path.Evaluator.Evaluate(time)));
            }
        }

        static void Subdivide(RuntimeHoldPath path, int segmentIndex, float startProgress, float endProgress,
            HoldTessellationPoint start, HoldTessellationPoint end, int depth, float tolerance,
            Func<HoldTessellationPoint, Vector2> project, List<HoldTessellationPoint> output)
        {
            if (depth >= MaxSubdivisionDepth || endProgress - startProgress <= 1e-6f)
            {
                AppendDistinct(output, end);
                return;
            }
            var middleProgress = (startProgress + endProgress) * .5f;
            var middle = PointAt(path, segmentIndex, middleProgress);
            var projectedStart = project(start);
            var projectedEnd = project(end);
            var projectedMiddle = project(middle);
            var linearMiddle = (projectedStart + projectedEnd) * .5f;
            if ((projectedMiddle - linearMiddle).sqrMagnitude <= tolerance * tolerance)
            {
                AppendDistinct(output, end);
                return;
            }
            Subdivide(path, segmentIndex, startProgress, middleProgress, start, middle, depth + 1, tolerance, project, output);
            Subdivide(path, segmentIndex, middleProgress, endProgress, middle, end, depth + 1, tolerance, project, output);
        }

        static HoldTessellationPoint PointAt(RuntimeHoldPath path, int segmentIndex, float progress)
        {
            var segment = path.Segments[segmentIndex];
            var time = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
            return new HoldTessellationPoint(time, path.Evaluator.EvaluateSegment(segmentIndex, progress));
        }

        static void AppendDistinct(List<HoldTessellationPoint> output, HoldTessellationPoint point)
        {
            if (output.Count > 0)
            {
                var previous = output[^1];
                if (Math.Abs(previous.Time - point.Time) < 1e-9 &&
                    Math.Abs(previous.Sample.Lane - point.Sample.Lane) < 1e-6f &&
                    Math.Abs(previous.Sample.Size - point.Sample.Size) < 1e-6f) return;
            }
            output.Add(point);
        }
    }
}
