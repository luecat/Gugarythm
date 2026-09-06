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
        // canvasRenderer.cull is owned by the masking system: RectMask2D
        // decides it through MaskableGraphic.Cull, and only re-asserts that
        // decision when its own clip rect changes. Writing the property
        // directly from the per-frame visual-window cull would therefore
        // silently clobber the upper-hidden-bar mask (connectorUpperHiddenClip)
        // and leave ribbon drawn across the area it is supposed to hide, so
        // both inputs are combined here instead.
        bool maskCulled;
        bool windowCulled;

        public int StaticBuildCount { get; private set; }
        public int VertexCount => vertices.Length;
        public int TriangleCount => indices.Length / 3;
        public override Texture mainTexture => sourceTexture != null ? sourceTexture : Texture2D.whiteTexture;

        /// <summary>
        /// Hides this chunk when its authored visual-position span cannot be
        /// on screen this frame, without taking ownership of the cull flag
        /// away from any RectMask2D above it.
        /// </summary>
        public void SetWindowCulled(bool culled)
        {
            if (windowCulled == culled) return;
            windowCulled = culled;
            ApplyCull();
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            base.Cull(clipRect, validRect);
            maskCulled = canvasRenderer.cull;
            ApplyCull();
        }

        void ApplyCull()
        {
            var cull = maskCulled || windowCulled;
            if (canvasRenderer.cull == cull) return;
            canvasRenderer.cull = cull;
            // Graphic.Rebuild returns immediately while canvasRenderer.cull is
            // set, and it returns *before* clearing the dirty flag, after the
            // registry has already dropped this element for the frame. A chunk
            // that is culled when its static geometry arrives -- which is every
            // chunk not already on screen at chart load -- therefore keeps a
            // permanently pending rebuild that nothing ever runs, and stays
            // blank for the whole song no matter how visible it later becomes.
            // OnCullingChanged is Unity's own hook for exactly this: it
            // re-registers a still-dirty graphic once it stops being culled.
            OnCullingChanged();
        }

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
