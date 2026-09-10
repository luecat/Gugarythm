using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Batches Flick arrow overlays (one shared atlas texture per instance)
    // so dense Flick streams no longer pay one CanvasRenderer per note.
    // Animation progress is derived from unscaled time inside OnPopulateMesh,
    // matching the former per-arrow RectTransform bob.
    public sealed class FlickArrowBatchGraphic : MaskableGraphic
    {
        public Texture texture;
        public override Texture mainTexture => texture != null ? texture : Texture2D.whiteTexture;

        Vector2[] origins = Array.Empty<Vector2>();
        Vector2[] sizes = Array.Empty<Vector2>();
        float[] directions = Array.Empty<float>();
        float[] laneUnits = Array.Empty<float>();
        bool[] flipU = Array.Empty<bool>();
        int capacity;
        int activeCount;
        ulong frameHash;
        ulong renderedHash;
        int renderedCount = -1;

        public void Prepare(int arrowCapacity)
        {
            EnsureCapacity(Mathf.Max(0, arrowCapacity), quiet: true);
        }

        public void BeginFrame()
        {
            activeCount = 0;
            frameHash = 1469598103934665603UL;
        }

        public void AddArrow(Vector2 noteCenter, Vector2 arrowSize, float direction, float laneUnit, bool mirrorU)
        {
            EnsureCapacity(activeCount + 1, quiet: false);
            origins[activeCount] = noteCenter;
            sizes[activeCount] = arrowSize;
            directions[activeCount] = direction;
            laneUnits[activeCount] = laneUnit;
            flipU[activeCount] = mirrorU;
            AddHash(noteCenter.x); AddHash(noteCenter.y);
            AddHash(arrowSize.x); AddHash(arrowSize.y);
            AddHash(direction); AddHash(laneUnit);
            AddHash(mirrorU ? 1u : 0u);
            activeCount++;
        }

        public void EndFrame()
        {
            AddHash(activeCount);
            var geometryChanged = renderedCount != activeCount || renderedHash != frameHash;
            renderedCount = activeCount;
            renderedHash = frameHash;
            if (geometryChanged || activeCount > 0) SetVerticesDirty();
        }

        void LateUpdate()
        {
            if (activeCount > 0) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (activeCount == 0) return;
            var animationProgress = Mathf.Repeat(Time.unscaledTime, .5f) / .5f;
            var alpha = 1f - animationProgress * animationProgress * animationProgress;
            for (var index = 0; index < activeCount; index++)
            {
                var arrowSize = sizes[index];
                var laneUnit = laneUnits[index];
                var center = origins[index] + new Vector2(
                    directions[index] * laneUnit * animationProgress,
                    arrowSize.y * .5f + laneUnit * 2f * animationProgress);
                var half = arrowSize * .5f;
                var u0 = flipU[index] ? 1f : 0f;
                var u1 = flipU[index] ? 0f : 1f;
                var tint = color;
                tint.a *= alpha;
                var first = helper.currentVertCount;
                var vertex = UIVertex.simpleVert;
                vertex.color = tint;
                vertex.position = center + new Vector2(-half.x, -half.y); vertex.uv0 = new Vector2(u0, 0); helper.AddVert(vertex);
                vertex.position = center + new Vector2(-half.x, half.y); vertex.uv0 = new Vector2(u0, 1); helper.AddVert(vertex);
                vertex.position = center + new Vector2(half.x, half.y); vertex.uv0 = new Vector2(u1, 1); helper.AddVert(vertex);
                vertex.position = center + new Vector2(half.x, -half.y); vertex.uv0 = new Vector2(u1, 0); helper.AddVert(vertex);
                helper.AddTriangle(first, first + 1, first + 2);
                helper.AddTriangle(first, first + 2, first + 3);
            }
        }

        void EnsureCapacity(int needed, bool quiet)
        {
            if (capacity >= needed) return;
            var next = capacity <= 0 ? Mathf.Max(8, needed) : capacity;
            while (next < needed) next *= 2;
            if (!quiet && capacity > 0)
                Debug.LogWarning($"FlickArrowBatchGraphic grew from {capacity} to {next}; Prepare underestimated Flick arrows.");
            capacity = next;
            Array.Resize(ref origins, capacity);
            Array.Resize(ref sizes, capacity);
            Array.Resize(ref directions, capacity);
            Array.Resize(ref laneUnits, capacity);
            Array.Resize(ref flipU, capacity);
        }

        void AddHash(float value) => AddHash((uint)BitConverter.SingleToInt32Bits(value));
        void AddHash(uint value)
        {
            unchecked { frameHash = (frameHash ^ value) * 1099511628211UL; }
        }
    }
}
