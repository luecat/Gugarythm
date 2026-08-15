using UnityEngine;
using UnityEngine.UI;

namespace Gugarythm
{
    public sealed class TaperedConnectorGraphic : MaskableGraphic
    {
        public Texture texture;
        public override Texture mainTexture => texture != null ? texture : Texture2D.whiteTexture;
        public bool drawGlow = true;
        public bool drawEdges = true;
        public float glowAlphaScale = .3f;
        public float glowAlphaLimit = .12f;
        public float fillAlphaScale = .6f;
        public float fillAlphaLimit = .26f;
        public float edgeAlphaScale = 1.8f;
        public float edgeAlphaLimit = .72f;
        public float glowWidthScale = 1.12f;
        public float glowPadding = 2;
        public float edgeWidth = 4;

        Vector2[] path = new Vector2[2];
        float[] widths = new float[2];
        float[] alphas = new float[2];
        int pathCount;

        public void SetGeometry(Vector2 startPoint, Vector2 endPoint, float widthAtStart, float widthAtEnd)
        {
            BeginPath(2);
            SetPathPoint(0, startPoint, widthAtStart);
            SetPathPoint(1, endPoint, widthAtEnd);
            EndPath();
        }

        public void BeginPath(int pointCount)
        {
            pathCount = Mathf.Max(0, pointCount);
            if (path.Length < pathCount)
            {
                path = new Vector2[pathCount];
                widths = new float[pathCount];
                alphas = new float[pathCount];
            }
            for (var index = 0; index < pathCount; index++) alphas[index] = 1;
        }

        public void SetPathPoint(int index, Vector2 center, float width, float alpha = 1)
        {
            if (index < 0 || index >= pathCount) return;
            path[index] = center;
            widths[index] = Mathf.Max(.001f, width);
            alphas[index] = Mathf.Clamp01(alpha);
        }

        public void EndPath()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (pathCount < 2) return;
            var baseColor = color;
            var glow = baseColor; glow.a = Mathf.Min(glowAlphaLimit, baseColor.a * glowAlphaScale);
            var fill = baseColor; fill.a = Mathf.Min(fillAlphaLimit, baseColor.a * fillAlphaScale);
            var edge = baseColor; edge.a = Mathf.Min(edgeAlphaLimit, baseColor.a * edgeAlphaScale);

            for (var index = 0; index < pathCount - 1; index++)
            {
                var start = path[index];
                var end = path[index + 1];
                if ((end - start).sqrMagnitude < .0001f) continue;
                var startWidth = widths[index];
                var endWidth = widths[index + 1];
                var startAlpha = alphas[index];
                var endAlpha = alphas[index + 1];
                // Glow follows perspective too; a fixed far-end padding was the
                // source of the oversized block at the vanishing point.
                if (drawGlow) AddBand(vertexHelper, start, end, startWidth * glowWidthScale + glowPadding, endWidth * glowWidthScale + glowPadding, 0, 0,
                    WithAlpha(glow, startAlpha), WithAlpha(glow, endAlpha));
                AddBand(vertexHelper, start, end, startWidth, endWidth, 0, 0,
                    WithAlpha(fill, startAlpha), WithAlpha(fill, endAlpha));
                var startEdge = Mathf.Min(edgeWidth, startWidth * .35f);
                var endEdge = Mathf.Min(edgeWidth, endWidth * .35f);
                if (drawEdges)
                {
                    AddBand(vertexHelper, start, end, startWidth, endWidth, startEdge, endEdge,
                        WithAlpha(edge, startAlpha), WithAlpha(edge, endAlpha), false);
                    AddBand(vertexHelper, start, end, startWidth, endWidth, startEdge, endEdge,
                        WithAlpha(edge, startAlpha), WithAlpha(edge, endAlpha), true);
                }
            }
        }

        static Color32 WithAlpha(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        // Each time slice stays horizontal. This makes the ribbon meet the left and
        // right edges of the rectangular head/tail notes exactly, even on curves.
        static void AddBand(VertexHelper helper, Vector2 start, Vector2 end, float widthA, float widthB, float insetA, float insetB, Color32 tintA, Color32 tintB, bool right = false)
        {
            Vector2 a;
            Vector2 b;
            Vector2 c;
            Vector2 d;
            if (insetA <= 0 && insetB <= 0)
            {
                a = start + Vector2.left * widthA * .5f;
                b = start + Vector2.right * widthA * .5f;
                c = end + Vector2.right * widthB * .5f;
                d = end + Vector2.left * widthB * .5f;
            }
            else if (!right)
            {
                a = start + Vector2.left * widthA * .5f;
                b = start + Vector2.left * (widthA * .5f - insetA);
                c = end + Vector2.left * (widthB * .5f - insetB);
                d = end + Vector2.left * widthB * .5f;
            }
            else
            {
                a = start + Vector2.right * (widthA * .5f - insetA);
                b = start + Vector2.right * widthA * .5f;
                c = end + Vector2.right * widthB * .5f;
                d = end + Vector2.right * (widthB * .5f - insetB);
            }
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert; vertex.color = tintA;
            vertex.position = a; vertex.uv0 = new Vector2(0, 0); helper.AddVert(vertex);
            vertex.position = b; vertex.uv0 = new Vector2(1, 0); helper.AddVert(vertex);
            vertex.color = tintB;
            vertex.position = c; vertex.uv0 = new Vector2(1, 1); helper.AddVert(vertex);
            vertex.position = d; vertex.uv0 = new Vector2(0, 1); helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
