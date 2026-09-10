using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Batches synchronization lines into one CanvasRenderer. Per-line
    // SimLineGraphic instances always dirtied the shared gameplay Canvas
    // every frame; frame-hash dedupe here skips rebuilds when geometry is
    // stable across a frame boundary.
    public sealed class SimLineBatchGraphic : MaskableGraphic
    {
        Vector2[] starts = Array.Empty<Vector2>();
        Vector2[] ends = Array.Empty<Vector2>();
        float[] thicknesses = Array.Empty<float>();
        int capacity;
        int activeCount;
        ulong frameHash;
        ulong renderedHash;
        int renderedCount = -1;

        public void Prepare(int lineCapacity)
        {
            EnsureCapacity(Mathf.Max(0, lineCapacity), quiet: true);
        }

        public void BeginFrame()
        {
            activeCount = 0;
            frameHash = 1469598103934665603UL;
        }

        public void AddLine(Vector2 start, Vector2 end, float thickness)
        {
            EnsureCapacity(activeCount + 1, quiet: false);
            starts[activeCount] = start;
            ends[activeCount] = end;
            thicknesses[activeCount] = Mathf.Max(.5f, thickness);
            AddHash(start.x); AddHash(start.y);
            AddHash(end.x); AddHash(end.y);
            AddHash(thicknesses[activeCount]);
            activeCount++;
        }

        public void EndFrame()
        {
            AddHash(activeCount);
            if (renderedCount == activeCount && renderedHash == frameHash) return;
            renderedCount = activeCount;
            renderedHash = frameHash;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            for (var index = 0; index < activeCount; index++)
            {
                var start = starts[index];
                var end = ends[index];
                var thickness = thicknesses[index];
                var delta = end - start;
                if (delta.sqrMagnitude < .0001f) continue;
                var normal = new Vector2(-delta.y, delta.x).normalized;
                var glow = color;
                glow.a *= .22f;
                AddBand(helper, start, end, normal * thickness * 2.6f, glow);
                AddBand(helper, start, end, normal * thickness * .5f, color);
            }
        }

        static void AddBand(VertexHelper helper, Vector2 start, Vector2 end, Vector2 offset, Color32 tint)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = start - offset; helper.AddVert(vertex);
            vertex.position = start + offset; helper.AddVert(vertex);
            vertex.position = end + offset; helper.AddVert(vertex);
            vertex.position = end - offset; helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }

        void EnsureCapacity(int needed, bool quiet)
        {
            if (capacity >= needed) return;
            var next = capacity <= 0 ? Mathf.Max(8, needed) : capacity;
            while (next < needed) next *= 2;
            if (!quiet && capacity > 0)
                Debug.LogWarning($"SimLineBatchGraphic grew from {capacity} to {next}; Prepare underestimated visible sim lines.");
            capacity = next;
            Array.Resize(ref starts, capacity);
            Array.Resize(ref ends, capacity);
            Array.Resize(ref thicknesses, capacity);
        }

        void AddHash(float value) => AddHash((uint)BitConverter.SingleToInt32Bits(value));
        void AddHash(int value) => AddHash((uint)value);
        void AddHash(uint value)
        {
            unchecked { frameHash = (frameHash ^ value) * 1099511628211UL; }
        }
    }
}
