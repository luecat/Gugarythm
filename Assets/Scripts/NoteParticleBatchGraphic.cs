using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Batches Hold-mid particle quads (small axis-aligned billboards, one
    // shared texture per instance) into a single CanvasRenderer. A dense Hold
    // otherwise puts two CanvasRenderers (an invisible parent plus its
    // particle child) on every mid, each dirtying the shared gameplay Canvas
    // every frame even while fully transparent. Mirrors HoldBatchGraphic's
    // frame-hash dedupe so a stable frame of ticks skips the mesh rebuild.
    public sealed class NoteParticleBatchGraphic : MaskableGraphic
    {
        public Texture texture;
        public override Texture mainTexture => texture != null ? texture : Texture2D.whiteTexture;

        Vector2[] centers = Array.Empty<Vector2>();
        Vector2[] sizes = Array.Empty<Vector2>();
        int capacity;
        int activeCount;
        ulong frameHash;
        ulong renderedHash;
        int renderedCount = -1;

        public void Prepare(int quadCapacity)
        {
            quadCapacity = Mathf.Max(0, quadCapacity);
            if (capacity >= quadCapacity) return;
            capacity = quadCapacity;
            Array.Resize(ref centers, capacity);
            Array.Resize(ref sizes, capacity);
        }

        public void BeginFrame()
        {
            activeCount = 0;
            frameHash = 1469598103934665603UL;
        }

        public void AddQuad(Vector2 center, Vector2 size)
        {
            if (activeCount >= capacity) return;
            centers[activeCount] = center;
            sizes[activeCount] = size;
            activeCount++;
            AddHash(center.x); AddHash(center.y); AddHash(size.x); AddHash(size.y);
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
                var center = centers[index];
                var half = sizes[index] * .5f;
                var first = helper.currentVertCount;
                var vertex = UIVertex.simpleVert;
                vertex.color = color;
                vertex.position = center + new Vector2(-half.x, -half.y); vertex.uv0 = new Vector2(0, 0); helper.AddVert(vertex);
                vertex.position = center + new Vector2(-half.x, half.y); vertex.uv0 = new Vector2(0, 1); helper.AddVert(vertex);
                vertex.position = center + new Vector2(half.x, half.y); vertex.uv0 = new Vector2(1, 1); helper.AddVert(vertex);
                vertex.position = center + new Vector2(half.x, -half.y); vertex.uv0 = new Vector2(1, 0); helper.AddVert(vertex);
                helper.AddTriangle(first, first + 1, first + 2);
                helper.AddTriangle(first, first + 2, first + 3);
            }
        }

        void AddHash(int value) => AddHash((uint)value);
        void AddHash(float value) => AddHash((uint)BitConverter.SingleToInt32Bits(value));
        void AddHash(uint value)
        {
            unchecked { frameHash = (frameHash ^ value) * 1099511628211UL; }
        }
    }
}
