using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed class GpuRibbonGraphic : MaskableGraphic
    {
        UIVertex[] vertices = Array.Empty<UIVertex>();
        int[] indices = Array.Empty<int>();
        Texture sourceTexture;

        public int StaticBuildCount { get; private set; }
        public int VertexCount => vertices.Length;
        public int TriangleCount => indices.Length / 3;
        public override Texture mainTexture => sourceTexture != null ? sourceTexture : Texture2D.whiteTexture;

        public void SetStaticGeometry(GpuRibbonChunkData data, Texture texture)
        {
            useLegacyMeshGeneration = false;
            vertices = data?.Vertices ?? Array.Empty<UIVertex>();
            var sourceIndices = data?.Indices ?? Array.Empty<int>();
            if (sourceIndices.Length == 0 || vertices.Length < 2)
            {
                indices = Array.Empty<int>();
                sourceTexture = texture;
                StaticBuildCount++;
                SetVerticesDirty();
                SetMaterialDirty();
                return;
            }

            var usefulIndexCount = sourceIndices.Length / 3 * 3;
            if (usefulIndexCount < sourceIndices.Length)
            {
                if (usefulIndexCount == 0)
                {
                    sourceIndices = Array.Empty<int>();
                }
                else
                {
                    var trimmed = new int[usefulIndexCount];
                    Array.Copy(sourceIndices, trimmed, usefulIndexCount);
                    sourceIndices = trimmed;
                }
            }

            var safeIndices = new List<int>(sourceIndices.Length);
            for (var index = 0; index + 2 < sourceIndices.Length; index += 3)
            {
                var i0 = sourceIndices[index];
                var i1 = sourceIndices[index + 1];
                var i2 = sourceIndices[index + 2];
                if (i0 >= 0 && i0 < vertices.Length &&
                    i1 >= 0 && i1 < vertices.Length &&
                    i2 >= 0 && i2 < vertices.Length)
                {
                    safeIndices.Add(i0);
                    safeIndices.Add(i1);
                    safeIndices.Add(i2);
                }
            }

            if (safeIndices.Count < sourceIndices.Length)
                Debug.LogWarning("GPU ribbon chunk has out-of-range indices; filtering invalid triangles.");

            indices = safeIndices.Count > 0 ? safeIndices.ToArray() : Array.Empty<int>();
            sourceTexture = texture;
            StaticBuildCount++;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            useLegacyMeshGeneration = false;
            canvasRenderer.cullTransparentMesh = false;
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (vertices.Length == 0 || indices.Length == 0) return;
            Populate(helper);
        }

        void Populate(VertexHelper helper)
        {
            for (var index = 0; index < vertices.Length; index++) helper.AddVert(vertices[index]);
            for (var index = 0; index + 2 < indices.Length; index += 3)
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
                helper.AddTriangle(i0, i1, i2);
            }
        }
    }
}
