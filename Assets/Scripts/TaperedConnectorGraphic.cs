using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Profiling;
#endif

namespace Gugarhythm
{
    public sealed class TaperedConnectorGraphic : MaskableGraphic
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly ProfilerMarker MeshRebuildProfiler = new("Gugarhythm.HoldMeshRebuild");
#endif
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
        [Range(0, .49f)] public float sourceUvInset;

        Vector2[] path = new Vector2[2];
        float[] widths = new float[2];
        float[] alphas = new float[2];
        int pathCount;
        int previousPathCount;
        ulong previousGeometryHash;

        struct RibbonSection
        {
            public Vector2 Center;
            public Vector2 PositiveOffset;
            public Vector2 NegativeOffset;
            public float Width;
            public float Alpha;
            public float V;
        }

        enum StripSide
        {
            Full,
            Left,
            Right,
        }

        const float DirectionEpsilonSqr = .0001f;
        const float TriangleAreaEpsilon = .00001f;
        RibbonSection[] sections = new RibbonSection[4];
        int[] coalescedPathIndices = new int[2];
        int sectionCount;

        public int GeometryRevision { get; private set; }

        public void SetGeometry(Vector2 startPoint, Vector2 endPoint, float widthAtStart, float widthAtEnd)
        {
            BeginPath(2);
            SetPathPoint(0, startPoint, widthAtStart);
            SetPathPoint(1, endPoint, widthAtEnd);
            EndPath();
        }

        public void BeginPath(int pointCount)
        {
            previousPathCount = pathCount;
            previousGeometryHash = GeometryHash();
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
            EndPathIfChanged();
        }

        public bool EndPathIfChanged()
        {
            if (previousPathCount == pathCount && previousGeometryHash == GeometryHash()) return false;
            GeometryRevision++;
            SetVerticesDirty();
            return true;
        }

        ulong GeometryHash()
        {
            unchecked
            {
                var hash = 1469598103934665603UL;
                hash = (hash ^ (uint)pathCount) * 1099511628211UL;
                for (var index = 0; index < pathCount; index++)
                {
                    hash = (hash ^ (uint)System.BitConverter.SingleToInt32Bits(path[index].x)) * 1099511628211UL;
                    hash = (hash ^ (uint)System.BitConverter.SingleToInt32Bits(path[index].y)) * 1099511628211UL;
                    hash = (hash ^ (uint)System.BitConverter.SingleToInt32Bits(widths[index])) * 1099511628211UL;
                    hash = (hash ^ (uint)System.BitConverter.SingleToInt32Bits(alphas[index])) * 1099511628211UL;
                }
                return hash;
            }
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            using var profilerScope = MeshRebuildProfiler.Auto();
#endif
            vertexHelper.Clear();
            if (pathCount < 2) return;
            var baseColor = color;
            var glow = baseColor; glow.a = Mathf.Min(glowAlphaLimit, baseColor.a * glowAlphaScale);
            var fill = baseColor; fill.a = Mathf.Min(fillAlphaLimit, baseColor.a * fillAlphaScale);
            var edge = baseColor; edge.a = Mathf.Min(edgeAlphaLimit, baseColor.a * edgeAlphaScale);

            BuildSections();
            if (sectionCount < 2) return;
            // Glow follows perspective too; a fixed far-end padding was the
            // source of the oversized block at the vanishing point.
            if (drawGlow)
                AddStrip(vertexHelper, glowWidthScale, glowPadding, 0, StripSide.Full, glow);
            AddStrip(vertexHelper, 1, 0, 0, StripSide.Full, fill);
            if (drawEdges)
            {
                AddStrip(vertexHelper, 1, 0, edgeWidth, StripSide.Left, edge);
                AddStrip(vertexHelper, 1, 0, edgeWidth, StripSide.Right, edge);
            }
        }

        static Color32 WithAlpha(Color color, float multiplier)
        {
            color.a *= multiplier;
            return color;
        }

        void BuildSections()
        {
            EnsureGeometryCapacity(pathCount);
            sectionCount = 0;

            var coalescedCount = 0;
            for (var index = 0; index < pathCount; index++)
            {
                if (coalescedCount > 0 &&
                    (path[index] - path[coalescedPathIndices[coalescedCount - 1]]).sqrMagnitude < DirectionEpsilonSqr)
                {
                    // A later coincident sample is the authoritative state at
                    // that position, including its width and alpha.
                    coalescedPathIndices[coalescedCount - 1] = index;
                }
                else
                {
                    coalescedPathIndices[coalescedCount++] = index;
                }
            }
            if (coalescedCount < 2) return;

            var totalDistance = 0f;
            for (var index = 1; index < coalescedCount; index++)
                totalDistance += Vector2.Distance(path[coalescedPathIndices[index - 1]], path[coalescedPathIndices[index]]);
            var distance = 0f;

            for (var index = 0; index < coalescedCount; index++)
            {
                if (index > 0)
                    distance += Vector2.Distance(path[coalescedPathIndices[index - 1]], path[coalescedPathIndices[index]]);
                var v = totalDistance > 0 ? distance / totalDistance : (float)index / (coalescedCount - 1);
                var pathIndex = coalescedPathIndices[index];
                // Width is authored in projected lane space, not in path-normal
                // space. Keep every time slice horizontal so aspect ratio and
                // sharp lane movement cannot rotate or enlarge the ribbon.
                AddSection(pathIndex, Vector2.left, v);
            }
        }

        void EnsureGeometryCapacity(int count)
        {
            if (coalescedPathIndices.Length < count)
                coalescedPathIndices = new int[Mathf.Max(count, coalescedPathIndices.Length * 2)];
            var requiredSections = count * 2;
            if (sections.Length < requiredSections)
                sections = new RibbonSection[Mathf.Max(requiredSections, sections.Length * 2)];
        }

        void AddSection(int pathIndex, Vector2 offset, float v)
        {
            AddSection(pathIndex, offset, -offset, v);
        }

        void AddSection(int pathIndex, Vector2 positiveOffset, Vector2 negativeOffset, float v)
        {
            sections[sectionCount++] = new RibbonSection
            {
                Center = path[pathIndex],
                PositiveOffset = positiveOffset,
                NegativeOffset = negativeOffset,
                Width = widths[pathIndex],
                Alpha = alphas[pathIndex],
                V = v,
            };
        }

        void AddStrip(VertexHelper helper, float widthScale, float widthPadding, float edgeInset, StripSide side, Color tint)
        {
            var uvMin = Mathf.Clamp(sourceUvInset, 0, .49f);
            var uvMax = 1 - uvMin;
            var first = helper.currentVertCount;
            var previousA = Vector2.zero;
            var previousB = Vector2.zero;

            for (var index = 0; index < sectionCount; index++)
            {
                StripVertices(sections[index], widthScale, widthPadding, edgeInset, side, out var a, out var b);
                var vertex = UIVertex.simpleVert;
                vertex.color = WithAlpha(tint, sections[index].Alpha);
                vertex.position = a;
                vertex.uv0 = new Vector2(uvMin, sections[index].V);
                helper.AddVert(vertex);
                vertex.position = b;
                vertex.uv0 = new Vector2(uvMax, sections[index].V);
                helper.AddVert(vertex);

                if (index > 0)
                {
                    var previousFirst = first + (index - 1) * 2;
                    var currentFirst = first + index * 2;
                    AddTriangleIfArea(helper, previousA, previousB, b,
                        previousFirst, previousFirst + 1, currentFirst + 1);
                    AddTriangleIfArea(helper, previousA, b, a,
                        previousFirst, currentFirst + 1, currentFirst);
                }
                previousA = a;
                previousB = b;
            }
        }

        static void StripVertices(RibbonSection section, float widthScale, float widthPadding,
            float edgeInset, StripSide side, out Vector2 a, out Vector2 b)
        {
            var width = section.Width * widthScale + widthPadding;
            var outerHalfWidth = width * .5f;
            var inset = Mathf.Min(edgeInset, width * .35f);
            var innerHalfWidth = Mathf.Max(0, outerHalfWidth - inset);
            switch (side)
            {
                case StripSide.Left:
                    a = section.Center + section.PositiveOffset * outerHalfWidth;
                    b = section.Center + section.PositiveOffset * innerHalfWidth;
                    break;
                case StripSide.Right:
                    a = section.Center + section.NegativeOffset * innerHalfWidth;
                    b = section.Center + section.NegativeOffset * outerHalfWidth;
                    break;
                default:
                    a = section.Center + section.PositiveOffset * outerHalfWidth;
                    b = section.Center + section.NegativeOffset * outerHalfWidth;
                    break;
            }
        }

        static void AddTriangleIfArea(VertexHelper helper, Vector2 a, Vector2 b, Vector2 c,
            int aIndex, int bIndex, int cIndex)
        {
            var twiceArea = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (twiceArea > TriangleAreaEpsilon) helper.AddTriangle(aIndex, bIndex, cIndex);
            else if (twiceArea < -TriangleAreaEpsilon) helper.AddTriangle(aIndex, cIndex, bIndex);
        }
    }
}
