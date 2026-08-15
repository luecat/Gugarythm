using UnityEngine;
using UnityEngine.UI;

namespace Gugarythm
{
    // A three-slice UI image: the source's left and right caps keep their shape,
    // while only the center section stretches horizontally.
    [AddComponentMenu("UI/Horizontal Sliced Raw Image")]
    public sealed class HorizontalSlicedRawImage : MaskableGraphic
    {
        [SerializeField] Texture sourceTexture;
        [SerializeField, Range(0, .49f)] float sourceCapRatio = .3f;

        public Texture texture
        {
            get => sourceTexture;
            set
            {
                if (sourceTexture == value) return;
                sourceTexture = value;
                SetMaterialDirty();
                SetVerticesDirty();
            }
        }

        public float capRatio
        {
            get => sourceCapRatio;
            set
            {
                value = Mathf.Clamp(value, 0, .49f);
                if (Mathf.Approximately(sourceCapRatio, value)) return;
                sourceCapRatio = value;
                SetVerticesDirty();
            }
        }

        public override Texture mainTexture => sourceTexture != null ? sourceTexture : Texture2D.whiteTexture;

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;

            var ratio = Mathf.Clamp(sourceCapRatio, 0, .49f);
            if (ratio <= .001f || sourceTexture == null)
            {
                AddQuad(helper, rect.xMin, rect.xMax, rect.yMin, rect.yMax, 0, 1);
                return;
            }

            // Preserve the source cap's aspect ratio. Width is limited only when
            // the destination is too narrow to fit both caps without overlap.
            var sourceHeight = Mathf.Max(1, sourceTexture.height);
            var sourceCapPixels = sourceTexture.width * ratio;
            var capWidth = Mathf.Min(rect.width * .5f, rect.height * sourceCapPixels / sourceHeight);
            var leftEnd = rect.xMin + capWidth;
            var rightStart = rect.xMax - capWidth;

            AddQuad(helper, rect.xMin, leftEnd, rect.yMin, rect.yMax, 0, ratio);
            AddQuad(helper, leftEnd, rightStart, rect.yMin, rect.yMax, ratio, 1 - ratio);
            AddQuad(helper, rightStart, rect.xMax, rect.yMin, rect.yMax, 1 - ratio, 1);
        }

        void AddQuad(VertexHelper helper, float x0, float x1, float y0, float y1, float u0, float u1)
        {
            if (x1 <= x0) return;
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = new Vector2(x0, y0); vertex.uv0 = new Vector2(u0, 0); helper.AddVert(vertex);
            vertex.position = new Vector2(x0, y1); vertex.uv0 = new Vector2(u0, 1); helper.AddVert(vertex);
            vertex.position = new Vector2(x1, y1); vertex.uv0 = new Vector2(u1, 1); helper.AddVert(vertex);
            vertex.position = new Vector2(x1, y0); vertex.uv0 = new Vector2(u1, 0); helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
