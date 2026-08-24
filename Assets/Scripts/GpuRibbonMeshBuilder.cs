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

    // Converts chart-space ribbons into immutable UI metadata meshes.  All
    // sampling and time-scale evaluation happens here, never in the frame loop.
    public static class GpuRibbonMeshBuilder
    {
        const int GuideSubdivisionCount = 128;
        const int HoldSubdivisionCount = 32;
        const int LegacySubdivisionCount = 128;
        const int MaximumVerticesPerChunk = 32000;

        struct RibbonPoint
        {
            public float Lane;
            public float Size;
            public double VisualPosition;
            public float Alpha;

            public RibbonPoint(float lane, float size, double visualPosition, float alpha)
            {
                Lane = lane;
                Size = Mathf.Max(.01f, size);
                VisualPosition = visualPosition;
                Alpha = Mathf.Clamp01(alpha);
            }
        }

        sealed class ChunkAccumulator
        {
            readonly GpuRibbonBuildResult result;
            readonly List<UIVertex> vertices = new(MaximumVerticesPerChunk);
            readonly List<int> indices = new(MaximumVerticesPerChunk * 3);
            GpuRibbonKind kind;
            bool active;

            public ChunkAccumulator(GpuRibbonBuildResult result) => this.result = result;

            public void AddPath(GpuRibbonKind nextKind, int groupIndex, int auxiliaryIndex,
                List<RibbonPoint> points)
            {
                if (points.Count < 2) return;
                var sourceIndex = 0;
                while (sourceIndex < points.Count - 1)
                {
                    if (active && kind != nextKind) Flush();
                    if (!active)
                    {
                        active = true;
                        kind = nextKind;
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
                    var textureV = sourceIndex / (float)denominator;
                    var guide = nextKind == GpuRibbonKind.Guide;
                    vertices.Add(GpuRibbonProjection.Vertex(point.Lane, point.Size, point.VisualPosition, -1,
                        textureV, groupIndex, auxiliaryIndex, point.Alpha, guide));
                    vertices.Add(GpuRibbonProjection.Vertex(point.Lane, point.Size, point.VisualPosition, 1,
                        textureV, groupIndex, auxiliaryIndex, point.Alpha, guide));
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
                var chunk = new GpuRibbonChunkData(kind, vertices.ToArray(), indices.ToArray());
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
                var group = ResolveGroup(chart, cache.TimeScaleGroup);
                var groupIndex = GroupIndex(result, groupIndices, group);
                progressValues.Clear();
                for (var index = 0; index <= GuideSubdivisionCount; index++)
                    progressValues.Add(index / (float)GuideSubdivisionCount);
                AppendGuideTimeScaleBoundaries(chart, cache, group, progressValues);
                points.Clear();
                foreach (var progress in progressValues)
                {
                    var sample = cache.Evaluate(progress);
                    points.Add(new RibbonPoint(sample.Lane, sample.Size,
                        chart.VisualPosition(sample.Time, group),
                        GuideStackOptimizer.CompositeAlpha(sample.Alpha, cache.StackCount)));
                }
                guideAccumulator.AddPath(GpuRibbonKind.Guide, groupIndex, Mathf.Clamp(cache.Color, 0, 255), points);
                result.GuidePathCount++;
            }

            foreach (var path in chart.HoldPaths)
            {
                if (path == null) continue;
                var stateIndex = StateIndex(result.HoldRootStates, path.RootIndex);
                foreach (var run in path.RenderRuns)
                {
                    points.Clear();
                    var group = ResolveGroup(chart, run.Start.TimeScaleGroup);
                    var groupIndex = GroupIndex(result, groupIndices, group);
                    for (var segmentIndex = run.FirstSegmentIndex; segmentIndex <= run.LastSegmentIndex; segmentIndex++)
                    {
                        var segment = path.Segments[segmentIndex];
                        for (var index = 0; index <= HoldSubdivisionCount; index++)
                        {
                            if (segmentIndex > run.FirstSegmentIndex && index == 0) continue;
                            var progress = index / (float)HoldSubdivisionCount;
                            var time = segment.Start.Time + (segment.End.Time - segment.Start.Time) * progress;
                            var sample = path.Evaluator.EvaluateSegment(segmentIndex, progress);
                            points.Add(new RibbonPoint(sample.Lane, sample.Size,
                                chart.VisualPosition(time, group), 1));
                        }
                    }
                    var kind = run.Critical ? GpuRibbonKind.HoldCritical : GpuRibbonKind.HoldNormal;
                    AccumulatorFor(kind).AddPath(kind, groupIndex, stateIndex, points);
                    result.HoldPathCount++;
                }
            }

            foreach (var connector in chart.FallbackConnectors)
            {
                if (connector?.Start == null || connector.End == null) continue;
                points.Clear();
                var group = ResolveGroup(chart, string.IsNullOrEmpty(connector.Start.TimeScaleGroup)
                    ? connector.End.TimeScaleGroup : connector.Start.TimeScaleGroup);
                var root = connector.Start.HoldRootIndex;
                var stateIndex = StateIndex(result.HoldRootStates, root);
                var groupIndex = GroupIndex(result, groupIndices, group);
                for (var index = 0; index <= LegacySubdivisionCount; index++)
                {
                    var progress = index / (float)LegacySubdivisionCount;
                    var eased = HoldPathMath.EaseProgress(progress, connector.Ease);
                    var time = connector.Start.Time + (connector.End.Time - connector.Start.Time) * progress;
                    points.Add(new RibbonPoint(
                        Mathf.Lerp(connector.Start.Lane, connector.End.Lane, eased),
                        Mathf.Lerp(connector.Start.Size, connector.End.Size, eased),
                        chart.VisualPosition(time, group), 1));
                }
                var kind = connector.Critical ? GpuRibbonKind.HoldCritical : GpuRibbonKind.HoldNormal;
                AccumulatorFor(kind).AddPath(kind, groupIndex, stateIndex, points);
                result.HoldPathCount++;
            }
            guideAccumulator.Flush();
            normalHoldAccumulator.Flush();
            criticalHoldAccumulator.Flush();
            return result;
        }

        static void AppendGuideTimeScaleBoundaries(RuntimeChart chart, GuideRenderCache cache, string group,
            SortedSet<float> output)
        {
            if (string.IsNullOrEmpty(group) || !chart.TimeScaleGroups.TryGetValue(group, out var map)) return;
            var boundaries = new List<double>();
            map.AppendBoundaryTimes(cache.HeadTime, cache.TailTime, boundaries);
            var duration = cache.TailTime - cache.HeadTime;
            if (Math.Abs(duration) < 1e-12) return;
            foreach (var time in boundaries) output.Add(Mathf.Clamp01((float)((time - cache.HeadTime) / duration)));
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

    }
}
