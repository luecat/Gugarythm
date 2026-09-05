using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Batches many HorizontalSlicedRawImage-style three-slice quads (one
    // shared texture per instance) into a single CanvasRenderer. A dense
    // chart otherwise puts one CanvasRenderer per visible Tap/Trace/Hold-head
    // note, each dirtying the shared gameplay Canvas and paying RectMask2D's
    // per-target clip cost every frame the note moves — which is every
    // frame. Geometry here is submitted directly in the batch's own local
    // space (the batch RectTransform is Filled to match its layer, same as
    // NoteParticleBatchGraphic), so quads need no per-note RectTransform to
    // anchor to; only Flick's arrow overlay still needs one and keeps using
    // the pooled HorizontalSlicedRawImage path instead of this batch.
    public sealed class NoteBodyBatchGraphic : MaskableGraphic
    {
        public Texture texture;
        [Range(0, .49f)] public float capRatio = 93f / 354f;
        public override Texture mainTexture => texture != null ? texture : Texture2D.whiteTexture;

        Vector2[] upperLefts = Array.Empty<Vector2>();
        Vector2[] upperRights = Array.Empty<Vector2>();
        Vector2[] lowerRights = Array.Empty<Vector2>();
        Vector2[] lowerLefts = Array.Empty<Vector2>();
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
            Array.Resize(ref upperLefts, capacity);
            Array.Resize(ref upperRights, capacity);
            Array.Resize(ref lowerRights, capacity);
            Array.Resize(ref lowerLefts, capacity);
        }

        public void BeginFrame()
        {
            activeCount = 0;
            frameHash = 1469598103934665603UL;
        }

        public void AddQuad(Vector2 upperLeft, Vector2 upperRight, Vector2 lowerRight, Vector2 lowerLeft)
        {
            if (activeCount >= capacity) return;
            upperLefts[activeCount] = upperLeft;
            upperRights[activeCount] = upperRight;
            lowerRights[activeCount] = lowerRight;
            lowerLefts[activeCount] = lowerLeft;
            activeCount++;
            AddHash(upperLeft.x); AddHash(upperLeft.y);
            AddHash(upperRight.x); AddHash(upperRight.y);
            AddHash(lowerRight.x); AddHash(lowerRight.y);
            AddHash(lowerLeft.x); AddHash(lowerLeft.y);
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
            var ratio = Mathf.Clamp(capRatio, 0, .49f);
            for (var index = 0; index < activeCount; index++)
                AddSlicedQuad(helper, upperLefts[index], upperRights[index], lowerRights[index], lowerLefts[index], ratio);
        }

        void AddSlicedQuad(VertexHelper helper, Vector2 upperLeft, Vector2 upperRight, Vector2 lowerRight, Vector2 lowerLeft, float ratio)
        {
            if (ratio <= .001f || texture == null)
            {
                AddSurfaceQuad(helper, upperLeft, upperRight, lowerRight, lowerLeft, 0, 1);
                return;
            }
            // Mirrors HorizontalSlicedRawImage.AddSurfaceSlices exactly, minus
            // the horizontalStart/End clipping range: every note here always
            // submits the full 0..1 span, so that parameter is dropped.
            var averageHeight = ((upperLeft - lowerLeft).magnitude + (upperRight - lowerRight).magnitude) * .5f;
            var widestEdge = Mathf.Max((upperRight - upperLeft).magnitude, (lowerRight - lowerLeft).magnitude);
            if (widestEdge <= .0001f) return;
            var sourceCapPixels = texture.width * ratio;
            var capWidth = Mathf.Min(widestEdge * .5f, averageHeight * sourceCapPixels / Mathf.Max(1, texture.height));
            var capFraction = Mathf.Clamp(capWidth / widestEdge, 0, .5f);

            AddMappedSegment(helper, upperLeft, upperRight, lowerLeft, lowerRight, 0f, capFraction, capFraction, ratio);
            AddMappedSegment(helper, upperLeft, upperRight, lowerLeft, lowerRight, capFraction, 1f - capFraction, capFraction, ratio);
            AddMappedSegment(helper, upperLeft, upperRight, lowerLeft, lowerRight, 1f - capFraction, 1f, capFraction, ratio);
        }

        void AddMappedSegment(VertexHelper helper, Vector2 upperLeft, Vector2 upperRight, Vector2 lowerLeft, Vector2 lowerRight,
            float start, float end, float capFraction, float sourceRatio)
        {
            if (end <= start + .0001f) return;
            AddSurfaceQuad(helper,
                Vector2.LerpUnclamped(upperLeft, upperRight, start),
                Vector2.LerpUnclamped(upperLeft, upperRight, end),
                Vector2.LerpUnclamped(lowerLeft, lowerRight, end),
                Vector2.LerpUnclamped(lowerLeft, lowerRight, start),
                SourceUAt(start, capFraction, sourceRatio), SourceUAt(end, capFraction, sourceRatio));
        }

        static float SourceUAt(float horizontalPosition, float capFraction, float sourceRatio)
        {
            if (capFraction <= .0001f) return horizontalPosition;
            if (horizontalPosition <= capFraction)
                return horizontalPosition / capFraction * sourceRatio;
            if (horizontalPosition >= 1 - capFraction)
                return 1 - sourceRatio + (horizontalPosition - (1 - capFraction)) / capFraction * sourceRatio;
            var centerSpan = 1 - capFraction * 2;
            return sourceRatio + (horizontalPosition - capFraction) / centerSpan * (1 - sourceRatio * 2);
        }

        void AddSurfaceQuad(VertexHelper helper, Vector2 upperLeft, Vector2 upperRight, Vector2 lowerRight, Vector2 lowerLeft, float u0, float u1)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = lowerLeft; vertex.uv0 = new Vector2(u0, 0); helper.AddVert(vertex);
            vertex.position = upperLeft; vertex.uv0 = new Vector2(u0, 1); helper.AddVert(vertex);
            vertex.position = upperRight; vertex.uv0 = new Vector2(u1, 1); helper.AddVert(vertex);
            vertex.position = lowerRight; vertex.uv0 = new Vector2(u1, 0); helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }

        void AddHash(int value) => AddHash((uint)value);
        void AddHash(float value) => AddHash((uint)BitConverter.SingleToInt32Bits(value));
        void AddHash(uint value)
        {
            unchecked { frameHash = (frameHash ^ value) * 1099511628211UL; }
        }
    }
}
