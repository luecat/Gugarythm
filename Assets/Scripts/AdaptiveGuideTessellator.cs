using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gugarhythm
{
    public sealed class AdaptiveGuideTessellator
    {
        public const int MaxPoints = 129;
        public const float ScreenErrorPixels = .75f;
        public const float StableScreenErrorPixels = 1f;
        const int MaxSubdivisionDepth = 7;

        public void Build(GuideRenderCache cache, float firstProgress, float lastProgress,
            Func<GuideRenderSample, GuideProjectedSample> project, List<GuideRenderSample> output)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (output == null) throw new ArgumentNullException(nameof(output));
            firstProgress = Mathf.Clamp01(firstProgress);
            lastProgress = Mathf.Clamp01(lastProgress);
            if (lastProgress < firstProgress) (firstProgress, lastProgress) = (lastProgress, firstProgress);

            output.Clear();
            var first = cache.Evaluate(firstProgress);
            var last = cache.Evaluate(lastProgress);
            output.Add(first);
            Subdivide(cache, first, last, project, 0, output);
        }

        public void BuildProjected(GuideRenderCache cache, GuideVisualSpan span,
            Func<GuideRenderSample, GuideProjectedPoint> project, List<GuideProjectedPoint> output)
        {
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (output == null) throw new ArgumentNullException(nameof(output));
            var firstProgress = Mathf.Clamp01(span.FirstProgress);
            var lastProgress = Mathf.Clamp01(span.LastProgress);
            if (lastProgress < firstProgress) (firstProgress, lastProgress) = (lastProgress, firstProgress);
            output.Clear();
            var first = Project(cache, span, firstProgress, project);
            var last = Project(cache, span, lastProgress, project);
            output.Add(first);
            SubdivideProjected(cache, span, first, last, project, 0, output);
        }

        static void Subdivide(GuideRenderCache cache, GuideRenderSample first, GuideRenderSample last,
            Func<GuideRenderSample, GuideProjectedSample> project, int depth, List<GuideRenderSample> output)
        {
            if (depth >= MaxSubdivisionDepth || last.Progress - first.Progress <= 1e-6f || output.Count >= MaxPoints - 1)
            {
                output.Add(last);
                return;
            }

            var middle = cache.Evaluate((first.Progress + last.Progress) * .5f);
            var firstProjected = project(first);
            var lastProjected = project(last);
            var middleProjected = project(middle);
            if (MaximumMidpointError(firstProjected, lastProjected, middleProjected) <= ScreenErrorPixels)
            {
                output.Add(last);
                return;
            }

            Subdivide(cache, first, middle, project, depth + 1, output);
            if (output.Count < MaxPoints)
                Subdivide(cache, middle, last, project, depth + 1, output);
        }

        static float MaximumMidpointError(GuideProjectedSample first, GuideProjectedSample last, GuideProjectedSample middle)
        {
            var centerError = Vector2.Distance(middle.Center, (first.Center + last.Center) * .5f);
            var leftError = Vector2.Distance(middle.Left, (first.Left + last.Left) * .5f);
            var rightError = Vector2.Distance(middle.Right, (first.Right + last.Right) * .5f);
            return Mathf.Max(centerError, leftError, rightError);
        }

        static GuideProjectedPoint Project(GuideRenderCache cache, GuideVisualSpan span, float progress,
            Func<GuideRenderSample, GuideProjectedPoint> project) =>
            project(cache.Evaluate(progress, span.VisualPositionAt(progress)));

        static void SubdivideProjected(GuideRenderCache cache, GuideVisualSpan span, GuideProjectedPoint first,
            GuideProjectedPoint last, Func<GuideRenderSample, GuideProjectedPoint> project, int depth,
            List<GuideProjectedPoint> output)
        {
            if (depth >= MaxSubdivisionDepth || last.Progress - first.Progress <= 1e-6f || output.Count >= MaxPoints - 1)
            {
                output.Add(last);
                return;
            }
            var middle = Project(cache, span, (first.Progress + last.Progress) * .5f, project);
            if (MaximumMidpointError(first, last, middle) <= ScreenErrorPixels)
            {
                output.Add(last);
                return;
            }
            SubdivideProjected(cache, span, first, middle, project, depth + 1, output);
            if (output.Count < MaxPoints)
                SubdivideProjected(cache, span, middle, last, project, depth + 1, output);
        }

        static float MaximumMidpointError(GuideProjectedPoint first, GuideProjectedPoint last, GuideProjectedPoint middle)
        {
            var centerError = Vector2.Distance(middle.Center, (first.Center + last.Center) * .5f);
            var firstLeft = first.Center + Vector2.left * first.Width * .5f;
            var lastLeft = last.Center + Vector2.left * last.Width * .5f;
            var middleLeft = middle.Center + Vector2.left * middle.Width * .5f;
            var firstRight = first.Center + Vector2.right * first.Width * .5f;
            var lastRight = last.Center + Vector2.right * last.Width * .5f;
            var middleRight = middle.Center + Vector2.right * middle.Width * .5f;
            return Mathf.Max(centerError, Vector2.Distance(middleLeft, (firstLeft + lastLeft) * .5f),
                Vector2.Distance(middleRight, (firstRight + lastRight) * .5f));
        }
    }
}
