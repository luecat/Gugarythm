using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed class GpuRibbonBuildResult
    {
        public readonly List<GpuRibbonChunkData> Chunks = new();
        public readonly List<string> GroupNames = new();
        public readonly Dictionary<int, int> HoldRootStates = new();
        public int GuidePathCount;
        public int HoldPathCount;
        public int VertexCount;
    }

    public static class GpuRibbonGuideRouting
    {
        // At the fastest supported scroll setting the complete approach window
        // is 0.4 visual seconds. If one immutable GPU mesh edge crosses more
        // than that, an extreme TimeScale can interpolate an entire flash from
        // one coarse edge. Keep only those Guides on exact CPU clipping.
        public const double MaximumGpuVisualStep = .4d;

        public static bool RequiresCpu(RuntimeChart chart, GuideRenderCache cache)
        {
            if (chart == null || cache == null) return true;
            var group = string.IsNullOrEmpty(cache.TimeScaleGroup)
                ? chart.DefaultTimeScaleGroup ?? string.Empty : cache.TimeScaleGroup;
            var progressValues = new SortedSet<float>();
            GpuRibbonMeshBuilder.AppendAdaptiveGuideProgress(chart, cache, group, progressValues);

            var previous = double.NaN;
            foreach (var progress in progressValues)
            {
                var sample = cache.Evaluate(progress);
                var position = chart.VisualPosition(sample.Time, group);
                if (!double.IsFinite(position)) return true;
                if (double.IsFinite(previous) && Math.Abs(position - previous) > MaximumGpuVisualStep)
                    return true;
                previous = position;
            }
            return false;
        }
    }

    // Shared curve-sample tuple: the authored time a progress value maps to,
    // plus the (lane, size) an evaluator computes for it. GpuRibbonMeshBuilder's
    // adaptive subdivision is generic over Guide's cubic-spline curve and
    // Hold's eased-lerp curve through this single delegate shape.
    internal readonly struct GpuRibbonCurveSample
    {
        public readonly double Time;
        public readonly float Lane;
        public readonly float Size;

        public GpuRibbonCurveSample(double time, float lane, float size)
        {
            Time = time;
            Lane = lane;
            Size = size;
        }
    }

    // Mirrors GpuRibbonGuideRouting's reasoning for Hold bodies. Only the two
    // provably-safe conditions are checked here: a coarse GPU mesh edge must
    // not represent a TimeScale reversal (the mesh is ordered by authored
    // time, so a backwards jump draws the ribbon inside out) or a jump wider
    // than one fast-scroll approach window (an extreme TimeScale could
    // otherwise interpolate an entire visible span from one edge). A run
    // failing either check keeps rendering on the CPU adaptive path exactly
    // as it always has. Curved-lane "hard corner" faceting is not routed
    // here — verifying a screen-space tolerance safely needs the runtime
    // perspective projection, which isn't available at chart-load time —
    // and is instead bounded by matching Guide's adaptive subdivision
    // density (see AppendAdaptiveProgress).
    public static class GpuRibbonHoldRouting
    {
        public const double MaximumGpuVisualStep = GpuRibbonGuideRouting.MaximumGpuVisualStep;

        public static bool RequiresCpu(RuntimeChart chart, RuntimeHoldPath path, HoldRenderRun run)
        {
            if (chart == null || path == null || run == null) return true;
            if (run.FirstSegmentIndex < 0 || run.LastSegmentIndex < run.FirstSegmentIndex ||
                run.LastSegmentIndex >= path.Segments.Count) return true;

            var previous = double.NaN;
            for (var segmentIndex = run.FirstSegmentIndex; segmentIndex <= run.LastSegmentIndex; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                if (segment?.Start == null || segment.End == null) return true;
                var group = string.IsNullOrEmpty(segment.Start.TimeScaleGroup)
                    ? segment.End.TimeScaleGroup : segment.Start.TimeScaleGroup;
                group = string.IsNullOrEmpty(group) ? chart.DefaultTimeScaleGroup ?? string.Empty : group;

                var progressValues = new SortedSet<float>();
                var capturedSegmentIndex = segmentIndex;
                GpuRibbonMeshBuilder.AppendAdaptiveProgress(chart, group, segment.Start.Time, segment.End.Time,
                    progress =>
                    {
                        var sampleTime = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
                        var sample = path.Evaluator.EvaluateSegment(capturedSegmentIndex, progress);
                        return new GpuRibbonCurveSample(sampleTime, sample.Lane, sample.Size);
                    }, progressValues);

                foreach (var progress in progressValues)
                {
                    var time = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
                    var position = chart.VisualPosition(time, group);
                    if (!double.IsFinite(position)) return true;
                    if (double.IsFinite(previous))
                    {
                        // A hold that hasn't started moving forward yet
                        // (waiting offscreen) can hold position flat across a
                        // boundary; only a genuine backwards step disqualifies it.
                        if (position < previous - 1e-6) return true;
                        if (position - previous > MaximumGpuVisualStep) return true;
                    }
                    previous = position;
                }
            }
            return false;
        }
    }

    // Converts chart-space ribbons into immutable UI metadata meshes.  All
    // sampling and time-scale evaluation happens here, never in the frame loop.
    public static class GpuRibbonMeshBuilder
    {
        // Target spacing, in visual-position units, between adjacent
        // adaptive samples. Comfortably smaller (1/8th) than
        // GpuRibbonGuideRouting.MaximumGpuVisualStep, the step size at which
        // a chunk is judged unsafe to route to the GPU at all, so the
        // adaptive grid stays well inside proven-safe density while still
        // collapsing near-flat spans (the common case) to a couple of points.
        const double TargetVisualStep = GpuRibbonGuideRouting.MaximumGpuVisualStep / 8d;
        // Lowered from 32000: each chunk is culled as a whole by visual-position
        // span (see ChunkAccumulator), so a smaller cap trades a few more
        // draw calls for far finer per-frame culling granularity — a chart's
        // full-song ribbon no longer has to be one inseparable blob that is
        // either entirely submitted to the GPU or entirely skipped.
        const int MaximumVerticesPerChunk = 3000;

        struct RibbonPoint
        {
            public float Lane;
            public float Size;
            public double VisualPosition;
            public float Alpha;
            public float TextureV;

            public RibbonPoint(float lane, float size, double visualPosition, float alpha,
                float textureV = float.NaN)
            {
                Lane = lane;
                Size = Mathf.Max(.01f, size);
                VisualPosition = visualPosition;
                Alpha = Mathf.Clamp01(alpha);
                TextureV = textureV;
            }
        }

        sealed class ChunkAccumulator
        {
            readonly GpuRibbonBuildResult result;
            readonly List<UIVertex> vertices = new(MaximumVerticesPerChunk);
            readonly List<int> indices = new(MaximumVerticesPerChunk * 3);
            GpuRibbonKind kind;
            int activeGroupIndex;
            double chunkMinVisualPosition;
            double chunkMaxVisualPosition;
            bool active;

            public ChunkAccumulator(GpuRibbonBuildResult result) => this.result = result;

            public bool AddPath(GpuRibbonKind nextKind, int groupIndex, int auxiliaryIndex,
                List<RibbonPoint> points)
            {
                if (points.Count < 2) return false;
                var sourceIndex = 0;
                while (sourceIndex < points.Count - 1)
                {
                    // A chunk mixing groups or kinds cannot be culled by one
                    // visual-position span, so a change of either forces a
                    // fresh chunk instead of silently growing this one.
                    if (active && (kind != nextKind || activeGroupIndex != groupIndex)) Flush();
                    if (!active)
                    {
                        active = true;
                        kind = nextKind;
                        activeGroupIndex = groupIndex;
                        chunkMinVisualPosition = double.PositiveInfinity;
                        chunkMaxVisualPosition = double.NegativeInfinity;
                    }

                    var availablePoints = (MaximumVerticesPerChunk - vertices.Count) / 2;
                    if (availablePoints < 2)
                    {
                        Flush();
                        continue;
                    }

                    var chunkPointCount = Math.Min(points.Count - sourceIndex, availablePoints);
                    AppendPathPart(points, sourceIndex, chunkPointCount, nextKind, groupIndex, auxiliaryIndex);
                    sourceIndex += chunkPointCount - 1;
                }
                return true;
            }

            void AppendPathPart(List<RibbonPoint> points, int sourceStart, int sourceCount,
                GpuRibbonKind nextKind, int groupIndex, int auxiliaryIndex)
            {
                var firstVertex = vertices.Count;
                var denominator = points.Count - 1;
                for (var localIndex = 0; localIndex < sourceCount; localIndex++)
                {
                    var sourceIndex = sourceStart + localIndex;
                    var point = points[sourceIndex];
                    if (point.VisualPosition < chunkMinVisualPosition) chunkMinVisualPosition = point.VisualPosition;
                    if (point.VisualPosition > chunkMaxVisualPosition) chunkMaxVisualPosition = point.VisualPosition;
                    var textureV = float.IsFinite(point.TextureV)
                        ? point.TextureV
                        : sourceIndex / (float)denominator;
                    vertices.Add(GpuRibbonProjection.Vertex(point.Lane, point.Size, point.VisualPosition, -1,
                        textureV, groupIndex, auxiliaryIndex, point.Alpha));
                    vertices.Add(GpuRibbonProjection.Vertex(point.Lane, point.Size, point.VisualPosition, 1,
                        textureV, groupIndex, auxiliaryIndex, point.Alpha));
                    if (localIndex == 0) continue;
                    var previous = firstVertex + (localIndex - 1) * 2;
                    var current = firstVertex + localIndex * 2;
                    indices.Add(previous); indices.Add(previous + 1); indices.Add(current + 1);
                    indices.Add(previous); indices.Add(current + 1); indices.Add(current);
                }
            }

            public void Flush()
            {
                if (!active) return;
                var chunk = new GpuRibbonChunkData(kind, vertices.ToArray(), indices.ToArray(),
                    activeGroupIndex, chunkMinVisualPosition, chunkMaxVisualPosition);
                result.Chunks.Add(chunk);
                result.VertexCount += chunk.Vertices.Length;
                vertices.Clear();
                indices.Clear();
                active = false;
            }
        }

        public static GpuRibbonBuildResult Build(RuntimeChart chart,
            IReadOnlyDictionary<RuntimeGuide, GuideRenderCache> guideCaches)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            var result = new GpuRibbonBuildResult();
            BuildRootStateMap(chart, result.HoldRootStates);
            var groupIndices = new Dictionary<string, int>(StringComparer.Ordinal);
            var guideAccumulator = new ChunkAccumulator(result);
            var normalHoldAccumulator = new ChunkAccumulator(result);
            var criticalHoldAccumulator = new ChunkAccumulator(result);
            var points = new List<RibbonPoint>(AdaptiveGuideTessellator.MaxPoints);
            var progressValues = new SortedSet<float>();

            ChunkAccumulator AccumulatorFor(GpuRibbonKind kind) => kind switch
            {
                GpuRibbonKind.Guide => guideAccumulator,
                GpuRibbonKind.HoldCritical => criticalHoldAccumulator,
                _ => normalHoldAccumulator,
            };

            foreach (var guide in chart.Guides)
            {
                if (guide == null || !guideCaches.TryGetValue(guide, out var cache)) continue;
                if (GpuRibbonGuideRouting.RequiresCpu(chart, cache)) continue;
                var group = ResolveGroup(chart, cache.TimeScaleGroup);
                var groupIndex = GroupIndex(result, groupIndices, group);
                progressValues.Clear();
                AppendAdaptiveGuideProgress(chart, cache, group, progressValues);
                points.Clear();
                foreach (var progress in progressValues)
                {
                    var sample = cache.Evaluate(progress);
                    points.Add(new RibbonPoint(sample.Lane, sample.Size,
                        chart.VisualPosition(sample.Time, group),
                        GuideStackOptimizer.CompositeAlpha(sample.Alpha, cache.StackCount)));
                }
                if (guideAccumulator.AddPath(GpuRibbonKind.Guide, groupIndex,
                        Mathf.Clamp(cache.Color, 0, 255), points))
                    result.GuidePathCount++;
            }

            foreach (var path in chart.HoldPaths)
            {
                if (path == null) continue;
                var stateIndex = StateIndex(result.HoldRootStates, path.RootIndex);
                foreach (var run in path.RenderRuns)
                {
                    if (GpuRibbonHoldRouting.RequiresCpu(chart, path, run)) continue;
                    var kind = run.Critical ? GpuRibbonKind.HoldCritical : GpuRibbonKind.HoldNormal;
                    if (TryAddHoldRun(chart, path, run, kind, stateIndex, result, groupIndices,
                            AccumulatorFor(kind), points, progressValues))
                        result.HoldPathCount++;
                }
            }

            // Fallback connectors are legacy/edge-case geometry that never
            // goes through the routing check above; keep them on the CPU
            // path unconditionally rather than risk misclassifying them.
            guideAccumulator.Flush();
            normalHoldAccumulator.Flush();
            criticalHoldAccumulator.Flush();
            return result;
        }

        static bool TryAddHoldRun(RuntimeChart chart, RuntimeHoldPath path, HoldRenderRun run,
            GpuRibbonKind kind, int stateIndex, GpuRibbonBuildResult result,
            Dictionary<string, int> groupIndices, ChunkAccumulator accumulator,
            List<RibbonPoint> points, SortedSet<float> progressValues)
        {
            if (run == null || run.FirstSegmentIndex < 0 || run.LastSegmentIndex < run.FirstSegmentIndex ||
                run.LastSegmentIndex >= path.Segments.Count)
                return false;

            points.Clear();
            string activeGroup = null;
            var runSegmentCount = run.LastSegmentIndex - run.FirstSegmentIndex + 1;
            for (var segmentIndex = run.FirstSegmentIndex; segmentIndex <= run.LastSegmentIndex; segmentIndex++)
            {
                var segment = path.Segments[segmentIndex];
                if (segment?.Start == null || segment.End == null) return false;
                var group = ResolveSegmentGroup(chart, segment);
                if (activeGroup != null && !string.Equals(activeGroup, group, StringComparison.Ordinal))
                {
                    var groupIndex = GroupIndex(result, groupIndices, activeGroup);
                    if (!accumulator.AddPath(kind, groupIndex, stateIndex, points)) return false;
                    points.Clear();
                }
                activeGroup = group;

                progressValues.Clear();
                var capturedSegmentIndex = segmentIndex;
                AppendAdaptiveProgress(chart, group, segment.Start.Time, segment.End.Time,
                    progress =>
                    {
                        var sampleTime = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
                        var sample = path.Evaluator.EvaluateSegment(capturedSegmentIndex, progress);
                        return new GpuRibbonCurveSample(sampleTime, sample.Lane, sample.Size);
                    }, progressValues);
                foreach (var progress in progressValues)
                {
                    if (points.Count > 0 && progress <= 0) continue;
                    var time = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
                    var sample = path.Evaluator.EvaluateSegment(segmentIndex, progress);
                    var visualPosition = chart.VisualPosition(time, group);
                    if (!IsRepresentable(time, sample.Lane, sample.Size, visualPosition)) return false;
                    var textureV = (segmentIndex - run.FirstSegmentIndex + progress) / runSegmentCount;
                    points.Add(new RibbonPoint(sample.Lane, sample.Size, visualPosition, 1, textureV));
                }
            }

            if (activeGroup == null) return false;
            return accumulator.AddPath(kind, GroupIndex(result, groupIndices, activeGroup), stateIndex, points);
        }

        internal static void AppendAdaptiveGuideProgress(RuntimeChart chart, GuideRenderCache cache, string group,
            SortedSet<float> output)
        {
            AppendAdaptiveProgress(chart, group, cache.HeadTime, cache.TailTime,
                progress =>
                {
                    var sample = cache.Evaluate(progress);
                    return new GpuRibbonCurveSample(sample.Time, sample.Lane, sample.Size);
                }, output);
        }

        // A recursion depth of 7 matches the runtime CPU tessellators'
        // MaxSubdivisionDepth (AdaptiveGuideTessellator/AdaptiveHoldTessellator),
        // so a pathological curve gets the same worst-case density here as it
        // always got there.
        const int MaxSubdivisionDepth = 7;
        // Tolerances are in authored lane/size units, not pixels: at bake
        // time there is no canvas scale to test a screen-space error against
        // (that is exactly what forced the old fixed 128-point grid). A lane
        // is roughly one key's width, so 0.02 is a couple of percent of a key
        // — comfortably below anything perceptible at any canvas size.
        const float LaneErrorTolerance = .02f;
        const float SizeErrorTolerance = .02f;

        // Fills [0,1] progress with: every TimeScale boundary inside the
        // range (each split is exact, never approximated), plus — for each
        // resulting boundary-free piece — recursive midpoint subdivision
        // exactly like the runtime CPU tessellators use, refining wherever
        // the curve's own (lane, size) shape departs from a straight chord,
        // or wherever a TimeScale within that piece still steps too far in
        // one edge. Sampling by curvature (not by how much time or visual
        // position the piece spans) is required because a Guide's authored
        // curve is a cubic spline through Start/Head/Tail/End control points
        // (GuideRenderCache.EvaluateCurve): a short, visually-flat piece can
        // still swing across many lanes, and subdividing only by span used
        // to under-sample exactly that case.
        internal static void AppendAdaptiveProgress(RuntimeChart chart, string group, double startTime, double endTime,
            Func<float, GpuRibbonCurveSample> sampleAt, SortedSet<float> output)
        {
            output.Add(0f);
            output.Add(1f);
            var duration = endTime - startTime;
            if (Math.Abs(duration) < 1e-12) return;

            var breakpoints = new List<double> { startTime };
            if (!string.IsNullOrEmpty(group) && chart.TimeScaleGroups.TryGetValue(group, out var map))
                map.AppendBoundaryTimes(startTime, endTime, breakpoints);
            breakpoints.Add(endTime);
            breakpoints.Sort();
            if (endTime < startTime) breakpoints.Reverse();

            for (var index = 1; index < breakpoints.Count; index++)
            {
                var rangeStart = breakpoints[index - 1];
                var rangeEnd = breakpoints[index];
                var startProgress = Mathf.Clamp01((float)((rangeStart - startTime) / duration));
                var endProgress = Mathf.Clamp01((float)((rangeEnd - startTime) / duration));
                output.Add(startProgress);
                output.Add(endProgress);
                SubdivideByCurvature(chart, group, startProgress, sampleAt(startProgress),
                    endProgress, sampleAt(endProgress), sampleAt, 0, output);
            }
        }

        static void SubdivideByCurvature(RuntimeChart chart, string group,
            float startProgress, GpuRibbonCurveSample startSample,
            float endProgress, GpuRibbonCurveSample endSample,
            Func<float, GpuRibbonCurveSample> sampleAt, int depth, SortedSet<float> output)
        {
            if (depth >= MaxSubdivisionDepth || endProgress - startProgress <= 1e-6f) return;

            var middleProgress = (startProgress + endProgress) * .5f;
            var middleSample = sampleAt(middleProgress);

            var laneError = Math.Abs(middleSample.Lane - (startSample.Lane + endSample.Lane) * .5f);
            var sizeError = Math.Abs(middleSample.Size - (startSample.Size + endSample.Size) * .5f);
            var startPosition = chart.VisualPosition(startSample.Time, group);
            var endPosition = chart.VisualPosition(endSample.Time, group);
            var stepOk = double.IsFinite(startPosition) && double.IsFinite(endPosition) &&
                Math.Abs(endPosition - startPosition) <= TargetVisualStep;

            if (laneError <= LaneErrorTolerance && sizeError <= SizeErrorTolerance && stepOk) return;

            output.Add(middleProgress);
            SubdivideByCurvature(chart, group, startProgress, startSample, middleProgress, middleSample,
                sampleAt, depth + 1, output);
            SubdivideByCurvature(chart, group, middleProgress, middleSample, endProgress, endSample,
                sampleAt, depth + 1, output);
        }

        static void BuildRootStateMap(RuntimeChart chart, Dictionary<int, int> output)
        {
            foreach (var path in chart.HoldPaths)
                AddRoot(output, path?.RootIndex ?? -1);
            foreach (var connector in chart.FallbackConnectors)
                AddRoot(output, connector?.Start?.HoldRootIndex ?? -1);
        }

        static void AddRoot(Dictionary<int, int> output, int root)
        {
            if (root < 0 || output.ContainsKey(root)) return;
            output.Add(root, output.Count);
        }

        static int StateIndex(Dictionary<int, int> map, int root)
        {
            return root >= 0 && map.TryGetValue(root, out var index) ? index : 0xffff;
        }

        static int GroupIndex(GpuRibbonBuildResult result, Dictionary<string, int> indices, string group)
        {
            group ??= string.Empty;
            if (indices.TryGetValue(group, out var index)) return index;
            if (indices.Count >= 0xffff) throw new InvalidOperationException("GPU ribbon supports at most 65535 time-scale groups.");
            index = indices.Count;
            indices.Add(group, index);
            result.GroupNames.Add(group);
            return index;
        }

        static string ResolveGroup(RuntimeChart chart, string group) =>
            string.IsNullOrEmpty(group) ? chart.DefaultTimeScaleGroup ?? string.Empty : group;

        static string ResolveSegmentGroup(RuntimeChart chart, RuntimeHoldPathSegment segment) =>
            ResolveGroup(chart, string.IsNullOrEmpty(segment.Start.TimeScaleGroup)
                ? segment.End.TimeScaleGroup : segment.Start.TimeScaleGroup);

        static bool IsRepresentable(double time, float lane, float size, double visualPosition) =>
            double.IsFinite(time) && float.IsFinite(lane) && float.IsFinite(size) && double.IsFinite(visualPosition);

    }
}
