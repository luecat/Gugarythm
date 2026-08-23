using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed class GuideBatchGraphic : MaskableGraphic
    {
        sealed class PathBuffer
        {
            public Vector2[] Centers = Array.Empty<Vector2>();
            public float[] Widths = Array.Empty<float>();
            public float[] Alphas = Array.Empty<float>();
            public Color Color;
            public int Count;

            public void Prepare(int pointCapacity)
            {
                if (Centers.Length >= pointCapacity) return;
                Centers = new Vector2[pointCapacity];
                Widths = new float[pointCapacity];
                Alphas = new float[pointCapacity];
            }
        }

        PathBuffer[] paths = Array.Empty<PathBuffer>();
        int activePathCount;
        int activePath;
        int expectedPointCount;
        ulong frameHash;
        ulong renderedHash;
        int renderedPathCount = -1;

        public int SampleCount { get; private set; }
        public int VertexCount { get; private set; }
        public int TriangleCount { get; private set; }
        public bool LastFrameDirtied { get; private set; }
        public float MeshBuildMilliseconds { get; private set; }

        public void Prepare(int pathCapacity, int pointCapacity)
        {
            pathCapacity = Mathf.Max(0, pathCapacity);
            pointCapacity = Mathf.Max(2, pointCapacity);
            if (paths.Length < pathCapacity)
            {
                var previousLength = paths.Length;
                Array.Resize(ref paths, pathCapacity);
                for (var index = previousLength; index < paths.Length; index++) paths[index] = new PathBuffer();
            }
            for (var index = 0; index < paths.Length; index++) paths[index].Prepare(pointCapacity);
        }

        public void BeginFrame()
        {
            activePathCount = 0;
            activePath = -1;
            expectedPointCount = 0;
            SampleCount = 0;
            VertexCount = 0;
            TriangleCount = 0;
            frameHash = 1469598103934665603UL;
            LastFrameDirtied = false;
        }

        public void BeginPath(Color color, int pointCount)
        {
            if (activePathCount >= paths.Length) return;
            activePath = activePathCount++;
            var path = paths[activePath];
            expectedPointCount = Mathf.Min(Mathf.Max(0, pointCount), path.Centers.Length);
            path.Count = expectedPointCount;
            path.Color = color;
            AddHash(color);
            AddHash(expectedPointCount);
        }

        public void SetPathPoint(int index, Vector2 center, float width, float alpha)
        {
            if (activePath < 0 || index < 0 || index >= expectedPointCount) return;
            var path = paths[activePath];
            path.Centers[index] = center;
            path.Widths[index] = Mathf.Max(.001f, width);
            path.Alphas[index] = Mathf.Clamp01(alpha);
            AddHash(center.x); AddHash(center.y); AddHash(path.Widths[index]); AddHash(path.Alphas[index]);
        }

        public void EndPath()
        {
            if (activePath < 0) return;
            var path = paths[activePath];
            SampleCount += path.Count;
            VertexCount += path.Count * 2;
            TriangleCount += Mathf.Max(0, path.Count - 1) * 2;
            activePath = -1;
        }

        public void EndFrame()
        {
            AddHash(activePathCount);
            LastFrameDirtied = renderedPathCount != activePathCount || renderedHash != frameHash;
            if (LastFrameDirtied)
            {
                renderedPathCount = activePathCount;
                renderedHash = frameHash;
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            helper.Clear();
            for (var pathIndex = 0; pathIndex < activePathCount; pathIndex++)
            {
                var path = paths[pathIndex];
                if (path.Count < 2) continue;
                var first = helper.currentVertCount;
                for (var index = 0; index < path.Count; index++)
                {
                    var halfWidth = path.Widths[index] * .5f;
                    var color = path.Color;
                    color.a *= path.Alphas[index];
                    var vertex = UIVertex.simpleVert;
                    vertex.color = color;
                    vertex.position = path.Centers[index] + Vector2.left * halfWidth;
                    vertex.uv0 = new Vector2(0, path.Count > 1 ? index / (float)(path.Count - 1) : 0);
                    helper.AddVert(vertex);
                    vertex.position = path.Centers[index] + Vector2.right * halfWidth;
                    vertex.uv0 = new Vector2(1, path.Count > 1 ? index / (float)(path.Count - 1) : 0);
                    helper.AddVert(vertex);
                    if (index == 0) continue;
                    var previous = first + (index - 1) * 2;
                    var current = first + index * 2;
                    helper.AddTriangle(previous, previous + 1, current + 1);
                    helper.AddTriangle(previous, current + 1, current);
                }
            }
            MeshBuildMilliseconds = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000d /
                System.Diagnostics.Stopwatch.Frequency);
        }

        void AddHash(Color value)
        {
            AddHash(value.r); AddHash(value.g); AddHash(value.b); AddHash(value.a);
        }

        void AddHash(int value) => AddHash((uint)value);
        void AddHash(float value) => AddHash((uint)BitConverter.SingleToInt32Bits(value));
        void AddHash(uint value)
        {
            unchecked { frameHash = (frameHash ^ value) * 1099511628211UL; }
        }
    }
}
