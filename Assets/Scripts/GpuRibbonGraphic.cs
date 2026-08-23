using System;
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
            vertices = data?.Vertices ?? Array.Empty<UIVertex>();
            indices = data?.Indices ?? Array.Empty<int>();
            sourceTexture = texture;
            StaticBuildCount++;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            for (var index = 0; index < vertices.Length; index++) helper.AddVert(vertices[index]);
            for (var index = 0; index + 2 < indices.Length; index += 3)
                helper.AddTriangle(indices[index], indices[index + 1], indices[index + 2]);
        }
    }
}
