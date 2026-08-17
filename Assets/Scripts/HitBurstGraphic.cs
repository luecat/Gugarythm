using UnityEngine;
using UnityEngine.UI;

namespace Gugarythm
{
    // A 15-frame judgment pulse: fixed-size perspective plates establish the
    // impact while the upper fill follows note width and low shards move linearly.
    public sealed class HitBurstGraphic : MaskableGraphic
    {
        [Range(0, 1)] public float progress;
        public float upperWidth = 96;

        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var outlineFade = 1 - progress;
            var fillFade = 1 - Mathf.Clamp01(progress / .4f);
            var shardFade = 1 - Mathf.Clamp01((progress - .56f) / .44f);
            for (var ring = 0; ring < 3; ring++)
            {
                var startWidth = 58 + ring * 11;
                var endWidth = 112 + ring * 15;
                var width = Mathf.Lerp(startWidth, endWidth, progress);
                var height = Mathf.Lerp(12 + ring * 3, 22 + ring * 4, progress);
                var tint = color;
                tint.a *= outlineFade * (.78f - ring * .14f);
                AddDiamondRing(helper, width, height, Mathf.Lerp(2.4f, 1.1f, progress), tint);
            }

            var fillTint = color;
            fillTint.a *= fillFade * .68f;
            AddDiamondFill(helper, Vector2.zero,
                Mathf.Lerp(upperWidth * .34f, upperWidth * .52f, progress),
                Mathf.Lerp(7, 11, progress), fillTint);
            var innerTint = color;
            innerTint.a *= fillFade * .32f;
            AddDiamondFill(helper, Vector2.up * 3,
                Mathf.Lerp(upperWidth * .22f, upperWidth * .39f, progress),
                Mathf.Lerp(5, 8, progress), innerTint);

            // Reference footage keeps its fragments opaque through the impact
            // peak, then removes them quickly. Their positions remain linear so
            // they read as a low splash rather than floating droplets.
            for (var index = 0; index < 10; index++)
            {
                var direction = Mathf.Lerp(-1f, 1f, index / 9f);
                var rank = index % 3;
                var arch = 1 - Mathf.Abs(direction);
                var center = new Vector2(
                    Mathf.Lerp(direction * 8, direction * (54 + rank * 9), progress),
                    Mathf.Lerp(2 + rank, 26 + arch * 32 + rank * 5, progress));
                var tint = color;
                tint.a *= shardFade * (.92f - rank * .08f);
                AddDiamondFill(helper, center, Mathf.Lerp(8, 4.5f, progress),
                    Mathf.Lerp(3.8f, 2.2f, progress), tint);
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

        static void AddDiamondFill(VertexHelper helper, Vector2 center, float radiusX, float radiusY, Color32 tint) =>
            AddQuad(helper, center + Vector2.up * radiusY, center + Vector2.right * radiusX,
                center + Vector2.down * radiusY, center + Vector2.left * radiusX, tint);

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
