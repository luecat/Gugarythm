using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed class GpuRibbonRenderer : IDisposable
    {
        sealed class Entry
        {
            public GpuRibbonGraphic Graphic;
        }

        readonly List<Entry> entries = new();
        readonly List<Material> materials = new();
        readonly Dictionary<int, int> rootStates;
        readonly Color32[] statePixels;
        readonly Texture2D stateTexture;
        readonly string[] groupNames;
        readonly Color[] groupPositionPixels;
        readonly Texture2D groupPositionTexture;
        readonly RuntimeChart chart;
        bool disposed;

        public RuntimeChart Chart => chart;
        public int ChunkCount => entries.Count;
        public int VertexCount { get; }
        public int GuidePathCount { get; }
        public int HoldPathCount { get; }
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
            Texture2D holdGreen, Texture2D holdYellow)
        {
            this.chart = chart;
            CacheHit = cacheHit;
            rootStates = build.HoldRootStates;
            VertexCount = build.VertexCount;
            GuidePathCount = build.GuidePathCount;
            HoldPathCount = build.HoldPathCount;
            groupNames = build.GroupNames.ToArray();
            groupPositionPixels = new Color[Mathf.Max(1, groupNames.Length)];
            groupPositionTexture = new Texture2D(groupPositionPixels.Length, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "GPU Ribbon Group Positions",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            groupPositionTexture.SetPixels(groupPositionPixels);
            groupPositionTexture.Apply(false, false);

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

            foreach (var chunk in build.Chunks)
            {
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
                    material.SetTexture("_GroupPositionTex", groupPositionTexture);
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
                graphic.SetStaticGeometry(chunk, texture);
                entries.Add(new Entry
                {
                    Graphic = graphic,
                });
            }
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
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat))
            {
                fallbackReason = "RGBAFloat group-position textures are unsupported.";
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
                renderer = new GpuRibbonRenderer(chart, build, cacheHit, shader, guideLayer, holdLayer, stageCanvas,
                    holdGreen, holdYellow);
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
            {
                var current = (float)frame.CurrentPosition(groupNames[index]);
                groupPositionPixels[index] = new Color(current, 0, 0, 0);
            }
            groupPositionTexture.SetPixels(groupPositionPixels);
            groupPositionTexture.Apply(false, false);
            foreach (var material in materials)
            {
                material.SetFloat("_ApproachDuration", Mathf.Max(.0001f, approachDuration));
                material.SetFloat("_CanvasHeight", Mathf.Max(1, canvasHeight));
                material.SetFloat("_NearTrackProgress", Mathf.Max(1, nearTrackProgress));
            }
        }

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
            if (groupPositionTexture != null) UnityEngine.Object.Destroy(groupPositionTexture);
            if (stateTexture != null) UnityEngine.Object.Destroy(stateTexture);
        }
    }
}
