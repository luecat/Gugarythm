using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Static document-preview decoration layer.  It deliberately batches only
    // a bounded number of Guide spans so a dense chart can never invalidate
    // the grid by exceeding Unity UI's 65k-vertex mesh limit.
    public sealed class ChartPreviewGuideGraphic : MaskableGraphic
    {
        struct Segment
        {
            public Vector2 Start;
            public Vector2 End;
            public float StartWidth;
            public float EndWidth;
            public Color StartColor;
            public Color EndColor;
        }

        readonly List<Segment> segments = new();

        public void BeginFrame() => segments.Clear();

        public void AddSegment(Vector2 start, Vector2 end, float startWidth, float endWidth,
            Color startColor, Color endColor)
        {
            if (!float.IsFinite(start.x) || !float.IsFinite(start.y) ||
                !float.IsFinite(end.x) || !float.IsFinite(end.y)) return;
            segments.Add(new Segment
            {
                Start = start,
                End = end,
                StartWidth = Mathf.Max(.001f, startWidth),
                EndWidth = Mathf.Max(.001f, endWidth),
                StartColor = startColor,
                EndColor = endColor,
            });
        }

        public void EndFrame() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            foreach (var segment in segments)
            {
                if (Mathf.Abs(segment.End.y - segment.Start.y) < .0001f)
                {
                    var left = Mathf.Min(segment.Start.x - segment.StartWidth * .5f,
                        segment.End.x - segment.EndWidth * .5f);
                    var right = Mathf.Max(segment.Start.x + segment.StartWidth * .5f,
                        segment.End.x + segment.EndWidth * .5f);
                    AddQuad(helper, new Vector2(left, segment.Start.y - 1f),
                        new Vector2(left, segment.Start.y + 1f),
                        new Vector2(right, segment.End.y + 1f),
                        new Vector2(right, segment.End.y - 1f),
                        segment.StartColor, segment.EndColor);
                    continue;
                }

                var startOffset = Vector2.right * (segment.StartWidth * .5f);
                var endOffset = Vector2.right * (segment.EndWidth * .5f);
                AddQuad(helper, segment.Start - startOffset, segment.Start + startOffset,
                    segment.End + endOffset, segment.End - endOffset,
                    segment.StartColor, segment.EndColor);
            }
        }

        static void AddQuad(VertexHelper helper, Vector2 lowerLeft, Vector2 upperLeft,
            Vector2 upperRight, Vector2 lowerRight, Color leftColor, Color rightColor)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.position = lowerLeft;
            vertex.color = leftColor;
            helper.AddVert(vertex);
            vertex.position = upperLeft;
            helper.AddVert(vertex);
            vertex.position = upperRight;
            vertex.color = rightColor;
            helper.AddVert(vertex);
            vertex.position = lowerRight;
            helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
