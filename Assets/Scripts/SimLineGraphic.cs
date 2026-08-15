using UnityEngine;
using UnityEngine.UI;

namespace Gugarythm
{
    // Thin synchronization line joining notes that share a chart moment.
    // Unlike hold/guide ribbons, thickness is perpendicular to the line.
    public sealed class SimLineGraphic : MaskableGraphic
    {
        Vector2 start;
        Vector2 end;
        float thickness = 2;

        public void SetGeometry(Vector2 startPoint, Vector2 endPoint, float lineThickness)
        {
            start = startPoint;
            end = endPoint;
            thickness = Mathf.Max(.5f, lineThickness);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var delta = end - start;
            if (delta.sqrMagnitude < .0001f) return;
            var normal = new Vector2(-delta.y, delta.x).normalized;
            var glow = color;
            glow.a *= .22f;
            AddBand(helper, normal * thickness * 2.6f, glow);
            AddBand(helper, normal * thickness * .5f, color);
        }

        void AddBand(VertexHelper helper, Vector2 offset, Color32 tint)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = start - offset; helper.AddVert(vertex);
            vertex.position = start + offset; helper.AddVert(vertex);
            vertex.position = end + offset; helper.AddVert(vertex);
            vertex.position = end - offset; helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
