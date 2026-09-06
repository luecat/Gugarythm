using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Textured equivalent of the fill-only Hold connector.  Batching preserves
    // the existing ribbon contour and atlas UVs while replacing one Canvas
    // rebuild per Hold run or legacy connector with one rebuild per visual style.
    public sealed class HoldBatchGraphic : MaskableGraphic
    {
        sealed class PathBuffer
        {
            public Vector2[] Centers = Array.Empty<Vector2>();
            public float[] Widths = Array.Empty<float>();
            public float[] Lengths = Array.Empty<float>();
            public int Count;

            public void Prepare(int capacity)
            {
                if (Centers.Length >= capacity) return;
                Centers = new Vector2[capacity];
                Widths = new float[capacity];
                Lengths = new float[capacity];
            }
        }

        public Texture texture;
        public override Texture mainTexture => texture != null ? texture : Texture2D.whiteTexture;
        [Range(0, .49f)] public float sourceUvInset;

        PathBuffer[] paths = Array.Empty<PathBuffer>();
        int activePathCount;
        int activePath = -1;
        int expectedPointCount;
        ulong frameHash;
        ulong renderedHash;
        int renderedPathCount = -1;

        public void Prepare(int pathCapacity, int pointCapacity)
        {
            pathCapacity = Mathf.Max(0, pathCapacity);
            pointCapacity = Mathf.Max(2, pointCapacity);
            if (paths.Length < pathCapacity)
            {
                var oldLength = paths.Length;
                Array.Resize(ref paths, pathCapacity);
                for (var index = oldLength; index < paths.Length; index++) paths[index] = new PathBuffer();
            }
            foreach (var path in paths) path.Prepare(pointCapacity);
        }

        public void BeginFrame()
        {
            activePathCount = 0;
            activePath = -1;
            expectedPointCount = 0;
            frameHash = 1469598103934665603UL;
        }

        public void BeginPath(int pointCount)
        {
            if (activePathCount >= paths.Length) return;
            activePath = activePathCount++;
            var path = paths[activePath];
            path.Count = expectedPointCount = Mathf.Min(Mathf.Max(0, pointCount), path.Centers.Length);
            AddHash(expectedPointCount);
        }

        public void SetPathPoint(int index, Vector2 center, float width)
        {
            if (activePath < 0 || index < 0 || index >= expectedPointCount) return;
            var path = paths[activePath];
            path.Centers[index] = center;
            path.Widths[index] = Mathf.Max(.001f, width);
            // Callers submit points in path order. Cache cumulative length here
            // so the deferred Canvas rebuild does not traverse every Hold twice
            // and repeat the same square-root work for UV generation.
            path.Lengths[index] = index == 0
                ? 0
                : path.Lengths[index - 1] + Vector2.Distance(path.Centers[index - 1], center);
            AddHash(center.x); AddHash(center.y); AddHash(path.Widths[index]);
        }

        public void EndPath() => activePath = -1;

        public void EndFrame()
        {
            AddHash(activePathCount);
            var skippedRebuild = renderedPathCount == activePathCount && renderedHash == frameHash;
            if (skippedRebuild) return;
            renderedPathCount = activePathCount;
            renderedHash = frameHash;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var uvMin = Mathf.Clamp(sourceUvInset, 0, .49f);
            var uvMax = 1 - uvMin;
            for (var pathIndex = 0; pathIndex < activePathCount; pathIndex++)
            {
                var path = paths[pathIndex];
                if (path.Count < 2) continue;
                var totalLength = path.Lengths[path.Count - 1];
                var first = helper.currentVertCount;
                for (var index = 0; index < path.Count; index++)
                {
                    var half = path.Widths[index] * .5f;
                    var vertex = UIVertex.simpleVert;
                    vertex.color = color;
                    vertex.position = path.Centers[index] + Vector2.left * half;
                    vertex.uv0 = new Vector2(uvMin, totalLength > 1e-5f
                        ? path.Lengths[index] / totalLength
                        : index / (float)(path.Count - 1));
                    helper.AddVert(vertex);
                    vertex.position = path.Centers[index] + Vector2.right * half;
                    vertex.uv0.x = uvMax;
                    helper.AddVert(vertex);
                    if (index == 0) continue;
                    var previous = first + (index - 1) * 2;
                    var current = first + index * 2;
                    helper.AddTriangle(previous, previous + 1, current + 1);
                    helper.AddTriangle(previous, current + 1, current);
                }
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
