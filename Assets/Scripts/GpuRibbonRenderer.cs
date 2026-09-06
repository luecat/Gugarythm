using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed class GpuRibbonRenderer : IDisposable
    {
        public const int MaximumGroupPositionCount = 256;

        static readonly int GroupPositionsProperty = Shader.PropertyToID("_GroupPositions");
        static readonly int ApproachDurationProperty = Shader.PropertyToID("_ApproachDuration");
        static readonly int CanvasHeightProperty = Shader.PropertyToID("_CanvasHeight");
        static readonly int NearTrackProgressProperty = Shader.PropertyToID("_NearTrackProgress");

        sealed class Entry
        {
            public GpuRibbonGraphic Graphic;
            public int GroupIndex;
            public double MinVisualPosition;
            public double MaxVisualPosition;
            public bool Culled;
        }

        // A chunk whose whole visual-position span cannot be on screen this
        // frame is skipped via CanvasRenderer.cull instead of vertex-level
        // clipping. Unlike SetActive, toggling cull does not re-invoke
        // OnPopulateMesh or force sibling Graphics to rebuild, and the
        // static per-chunk mesh never changes, so this is a pure submission
        // skip. The margin absorbs float rounding at the exact span edges.
        const double CullMarginVisualSeconds = .05d;

        readonly List<Entry> entries = new();
        readonly List<Material> materials = new();
        readonly Dictionary<int, int> rootStates;
        readonly Color32[] statePixels;
        readonly Texture2D stateTexture;
        readonly string[] groupNames;
        readonly float[] groupPositions;
        readonly RuntimeChart chart;
        float appliedApproachDuration = float.NaN;
        float appliedCanvasHeight = float.NaN;
        float appliedNearTrackProgress = float.NaN;
        bool disposed;

        public RuntimeChart Chart => chart;
        public int ChunkCount => entries.Count;
        public int VertexCount { get; }
        public int GuidePathCount { get; }
        public int HoldPathCount { get; }
        public bool RendersGuides { get; }
        public bool RendersHolds { get; }
        public bool CacheHit { get; }
        public int StaticBuildCount
        {
            get
            {
                var count = 0;
                foreach (var entry in entries) count += entry.Graphic.StaticBuildCount;
                return count;
            }
        }

        GpuRibbonRenderer(RuntimeChart chart, GpuRibbonBuildResult build, bool cacheHit, Shader shader,
            RectTransform guideLayer, RectTransform holdLayer, Canvas stageCanvas,
            Texture2D holdGreen, Texture2D holdYellow, bool renderGuides, bool renderHolds)
        {
            this.chart = chart;
            CacheHit = cacheHit;
            RendersGuides = renderGuides;
            RendersHolds = renderHolds;
            rootStates = renderHolds ? build.HoldRootStates : new Dictionary<int, int>();
            GuidePathCount = renderGuides ? build.GuidePathCount : 0;
            HoldPathCount = renderHolds ? build.HoldPathCount : 0;
            groupNames = build.GroupNames.ToArray();
            groupPositions = new float[Mathf.Max(1, groupNames.Length)];

            var textureWidth = Mathf.Max(1, rootStates.Count);
            statePixels = new Color32[textureWidth];
            for (var index = 0; index < statePixels.Length; index++) statePixels[index] = new Color32(0, 0, 0, 255);
            stateTexture = new Texture2D(textureWidth, 1, TextureFormat.RGBA32, false, true)
            {
                name = "GPU Hold State",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            stateTexture.SetPixels32(statePixels);
            stateTexture.Apply(false, false);

            var materialByKind = new Dictionary<GpuRibbonKind, Material>();
            var validChunkCount = 0;
            var visibleVertexCount = 0;

            foreach (var chunk in build.Chunks)
            {
                if (chunk == null)
                {
                    Debug.LogWarning("GPU ribbon chunk is missing.");
                    continue;
                }

                var isGuide = chunk.Kind == GpuRibbonKind.Guide;
                if ((isGuide && !renderGuides) || (!isGuide && !renderHolds)) continue;

                if (!TryNormalizeChunk(chunk, out var vertices, out var indices, out var warning))
                {
                    Debug.LogWarning($"GPU ribbon chunk was skipped: {warning}");
                    continue;
                }

                var parent = chunk.Kind == GpuRibbonKind.Guide ? guideLayer : holdLayer;
                var go = new GameObject($"GPU Ribbon {chunk.Kind}", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(GpuRibbonGraphic));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                var graphic = go.GetComponent<GpuRibbonGraphic>();
                graphic.raycastTarget = false;
                graphic.color = Color.white;
                if (!materialByKind.TryGetValue(chunk.Kind, out var material))
                {
                    material = new Material(shader) { name = $"GPU Ribbon {chunk.Kind}" };
                    var isHold = chunk.Kind != GpuRibbonKind.Guide;
                    material.SetFloat("_IsHold", isHold ? 1 : 0);
                    material.SetFloat("_RibbonOpacity", isHold ? .62f : 1);
                    material.SetFloat("_UvInset", isHold ? (306f - 240f) / 306f * .5f : 0);
                    material.SetFloat("_GroupCount", Mathf.Max(1, groupNames.Length));
                    material.SetFloat("_HoldStateCount", Mathf.Max(1, rootStates.Count));
                    material.SetFloatArray(GroupPositionsProperty, groupPositions);
                    material.SetTexture("_HoldStateTex", stateTexture);
                    materialByKind.Add(chunk.Kind, material);
                    materials.Add(material);
                }
                graphic.material = material;
                var texture = chunk.Kind switch
                {
                    GpuRibbonKind.HoldCritical => holdYellow,
                    GpuRibbonKind.HoldNormal => holdGreen,
                    _ => Texture2D.whiteTexture,
                };
                graphic.SetStaticGeometry(new GpuRibbonChunkData(chunk.Kind, vertices, indices), texture);
                entries.Add(new Entry
                {
                    Graphic = graphic,
                    GroupIndex = chunk.GroupIndex,
                    MinVisualPosition = chunk.MinVisualPosition,
                    MaxVisualPosition = chunk.MaxVisualPosition,
                });
                validChunkCount++;
                visibleVertexCount += vertices.Length;
            }

            if (validChunkCount == 0)
            {
                throw new InvalidOperationException("GPU ribbon data has no usable chunks.");
            }
            VertexCount = visibleVertexCount;
        }

        public static bool TryCreate(RuntimeChart chart,
            IReadOnlyDictionary<RuntimeGuide, GuideRenderCache> guideCaches,
            RectTransform guideLayer, RectTransform holdLayer, Canvas stageCanvas,
            Texture2D holdGreen, Texture2D holdYellow,
            out GpuRibbonRenderer renderer, out string fallbackReason)
        {
            renderer = null;
            fallbackReason = string.Empty;
            if (chart == null || guideLayer == null || holdLayer == null || stageCanvas == null)
            {
                fallbackReason = "GPU ribbon initialization is missing chart or Canvas state.";
                return false;
            }
            // Guide geometry is immutable after chart load. Hold runs that
            // fail GpuRibbonHoldRouting (TimeScale reversal/discontinuity) and
            // fallback connectors still require CPU runtime clipping —
            // GugarhythmLandscapePrototype mixes GPU and CPU rendering per
            // run at draw time (see exactCpuHoldRuns), matching how Guides
            // already mix per guide via exactCpuGuides.
            var renderGuides = false;
            foreach (var guide in chart.Guides)
            {
                if (guide == null || !guideCaches.TryGetValue(guide, out var cache) ||
                    GpuRibbonGuideRouting.RequiresCpu(chart, cache)) continue;
                renderGuides = true;
                break;
            }
            var renderHolds = false;
            foreach (var path in chart.HoldPaths)
            {
                if (path == null) continue;
                foreach (var run in path.RenderRuns)
                {
                    if (GpuRibbonHoldRouting.RequiresCpu(chart, path, run)) continue;
                    renderHolds = true;
                    break;
                }
                if (renderHolds) break;
            }
            if (!renderGuides && !renderHolds)
            {
                fallbackReason = "GPU ribbon renderer has no eligible decoration or Hold paths; using the CPU renderer.";
                return false;
            }
            var shader = Resources.Load<Shader>("Shaders/GpuRibbonUI");
            if (shader == null || !shader.isSupported)
            {
                fallbackReason = "Gugarhythm/GPU Ribbon UI shader is missing or unsupported.";
                return false;
            }
            try
            {
                var build = GpuRibbonCache.LoadOrBuild(chart, guideCaches, out var cacheHit);
                if (!SupportsGroupPositionCount(build.GroupNames.Count))
                {
                    fallbackReason = $"GPU ribbon renderer supports 1-{MaximumGroupPositionCount} time-scale groups; " +
                                     $"chart contains {build.GroupNames.Count}.";
                    return false;
                }
                renderer = new GpuRibbonRenderer(chart, build, cacheHit, shader, guideLayer, holdLayer, stageCanvas,
                    holdGreen, holdYellow, renderGuides, renderHolds);
                return true;
            }
            catch (Exception exception)
            {
                fallbackReason = "GPU ribbon mesh build failed: " + exception.Message;
                renderer?.Dispose();
                renderer = null;
                return false;
            }
        }

        public void UpdateFrame(VisualFrameContext frame, float approachDuration, float canvasHeight,
            float nearTrackProgress)
        {
            if (disposed || frame == null) return;
            for (var index = 0; index < groupNames.Length; index++)
                groupPositions[index] = (float)frame.CurrentPosition(groupNames[index]);

            var nextApproachDuration = Mathf.Max(.0001f, approachDuration);
            var nextCanvasHeight = Mathf.Max(1, canvasHeight);
            var nextNearTrackProgress = Mathf.Max(1, nearTrackProgress);
            foreach (var material in materials)
            {
                material.SetFloatArray(GroupPositionsProperty, groupPositions);
                if (!appliedApproachDuration.Equals(nextApproachDuration))
                    material.SetFloat(ApproachDurationProperty, nextApproachDuration);
                if (!appliedCanvasHeight.Equals(nextCanvasHeight))
                    material.SetFloat(CanvasHeightProperty, nextCanvasHeight);
                if (!appliedNearTrackProgress.Equals(nextNearTrackProgress))
                    material.SetFloat(NearTrackProgressProperty, nextNearTrackProgress);
            }
            appliedApproachDuration = nextApproachDuration;
            appliedCanvasHeight = nextCanvasHeight;
            appliedNearTrackProgress = nextNearTrackProgress;

            // A vertex is only visible when its approach lands in [0, 1] (see
            // the shader's frag clips), which is exactly when its authored
            // visual position lands in [currentPosition, currentPosition +
            // approachDuration]. A chunk whose whole span misses that window
            // for its own time-scale group can never draw a visible pixel
            // this frame, so skip submitting its (otherwise unchanged)
            // static mesh entirely instead of relying on per-fragment clip.
            var approachWindow = (double)nextApproachDuration;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var currentPosition = entry.GroupIndex >= 0 && entry.GroupIndex < groupPositions.Length
                    ? groupPositions[entry.GroupIndex] : (float)frame.CurrentPosition(null);
                var visible = entry.MaxVisualPosition >= currentPosition - CullMarginVisualSeconds &&
                    entry.MinVisualPosition <= currentPosition + approachWindow + CullMarginVisualSeconds;
                var shouldCull = !visible;
                if (entry.Culled == shouldCull) continue;
                entry.Culled = shouldCull;
                // Never writes canvasRenderer.cull directly: RectMask2D owns
                // that flag (see GpuRibbonGraphic.SetWindowCulled).
                entry.Graphic.SetWindowCulled(shouldCull);
            }
        }

        public static bool SupportsGroupPositionCount(int count) =>
            count > 0 && count <= MaximumGroupPositionCount;

        public void SetHoldMissed(int rootIndex, bool missed)
        {
            if (disposed || !rootStates.TryGetValue(rootIndex, out var index)) return;
            var value = missed ? (byte)255 : (byte)0;
            if (statePixels[index].r == value) return;
            statePixels[index] = new Color32(value, value, value, 255);
            stateTexture.SetPixels32(statePixels);
            stateTexture.Apply(false, false);
        }

        public void ClearHoldStates()
        {
            if (disposed) return;
            var changed = false;
            for (var index = 0; index < statePixels.Length; index++)
            {
                if (statePixels[index].r == 0) continue;
                statePixels[index] = new Color32(0, 0, 0, 255);
                changed = true;
            }
            if (!changed) return;
            stateTexture.SetPixels32(statePixels);
            stateTexture.Apply(false, false);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var entry in entries)
            {
                if (entry.Graphic != null) UnityEngine.Object.Destroy(entry.Graphic.gameObject);
            }
            entries.Clear();
            foreach (var material in materials)
                if (material != null) UnityEngine.Object.Destroy(material);
            materials.Clear();
            if (stateTexture != null) UnityEngine.Object.Destroy(stateTexture);
        }

        static bool TryNormalizeChunk(GpuRibbonChunkData chunk, out UIVertex[] vertices, out int[] indices, out string reason)
        {
            vertices = chunk.Vertices ?? Array.Empty<UIVertex>();
            indices = chunk.Indices ?? Array.Empty<int>();
            if (vertices.Length == 0 || indices.Length == 0)
            {
                reason = "Chunk has no vertices or no indices.";
                return false;
            }

            if (vertices.Length % 2 != 0)
            {
                reason = $"Chunk has odd vertex count: {vertices.Length}.";
                return false;
            }

            if (indices.Length % 3 != 0)
            {
                reason = $"Chunk has invalid index count: {indices.Length}.";
                return false;
            }

            var safeIndices = new List<int>(indices.Length);
            for (var index = 0; index < indices.Length; index += 3)
            {
                var i0 = indices[index];
                var i1 = indices[index + 1];
                var i2 = indices[index + 2];
                if (i0 < 0 || i0 >= vertices.Length ||
                    i1 < 0 || i1 >= vertices.Length ||
                    i2 < 0 || i2 >= vertices.Length)
                {
                    continue;
                }

                safeIndices.Add(i0);
                safeIndices.Add(i1);
                safeIndices.Add(i2);
            }

            if (safeIndices.Count == 0)
            {
                reason = "All indices are out of range.";
                return false;
            }

            if (safeIndices.Count < indices.Length)
            {
                reason = $"Chunk had out-of-range triangles and was sanitized: {indices.Length} -> {safeIndices.Count}.";
                indices = safeIndices.ToArray();
                return true;
            }

            reason = null;
            return true;
        }
    }
}
