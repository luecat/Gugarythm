using UnityEngine;
using UnityEngine.UI;

namespace Gugarythm
{
    // Multi-layer judgment burst measured from the reference gameplay: expanding
    // diamond outlines plus small square fragments. It is geometry-only so the
    // effect stays crisp at every Android resolution.
    public sealed class HitBurstGraphic : MaskableGraphic
    {
        [Range(0, 1)] public float progress;

        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var fade = 1 - progress;
            for (var ring = 0; ring < 4; ring++)
            {
                var delayed = Mathf.Clamp01(progress * 1.35f - ring * .09f);
                var radius = Mathf.Lerp(28 + ring * 13, 118 + ring * 18, delayed);
                var tint = ring % 2 == 0 ? color : Color.Lerp(color, Color.white, .62f);
                tint.a *= fade * (.72f - ring * .11f);
                AddDiamondRing(helper, radius * 1.35f, radius * .55f, Mathf.Lerp(3.2f, 1.2f, delayed), tint);
            }

            for (var index = 0; index < 10; index++)
            {
                var phase = index / 9f;
                var direction = new Vector2(Mathf.Lerp(-1.15f, 1.15f, phase), .25f + Mathf.Abs(.5f - phase) * .85f).normalized;
                var distance = Mathf.Lerp(24, 150, progress) * (.72f + (index % 3) * .13f);
                var center = direction * distance + Vector2.up * 18 * progress;
                var size = Mathf.Lerp(9, 3, progress) * (index % 2 == 0 ? 1.2f : .8f);
                var tint = index % 3 == 0 ? Color.Lerp(color, Color.white, .72f) : color;
                tint.a *= fade * .7f;
                AddDiamondFill(helper, center, size, tint);
            }
        }

        static void AddDiamondRing(VertexHelper helper, float radiusX, float radiusY, float width, Color32 tint)
        {
            var top = new Vector2(0, radiusY);
            var right = new Vector2(radiusX, 0);
            var bottom = new Vector2(0, -radiusY);
            var left = new Vector2(-radiusX, 0);
            AddLine(helper, top, right, width, tint);
            AddLine(helper, right, bottom, width, tint);
            AddLine(helper, bottom, left, width, tint);
            AddLine(helper, left, top, width, tint);
        }

        static void AddLine(VertexHelper helper, Vector2 start, Vector2 end, float width, Color32 tint)
        {
            var direction = (end - start).normalized;
            var normal = new Vector2(-direction.y, direction.x) * width * .5f;
            AddQuad(helper, start - normal, start + normal, end + normal, end - normal, tint);
        }

        static void AddDiamondFill(VertexHelper helper, Vector2 center, float radius, Color32 tint) =>
            AddQuad(helper, center + Vector2.up * radius, center + Vector2.right * radius,
                center + Vector2.down * radius, center + Vector2.left * radius, tint);

        static void AddQuad(VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = a; helper.AddVert(vertex);
            vertex.position = b; helper.AddVert(vertex);
            vertex.position = c; helper.AddVert(vertex);
            vertex.position = d; helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
