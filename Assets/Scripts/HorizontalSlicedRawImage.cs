using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // A three-slice UI image: the source's left and right caps keep their shape,
    // while only the center section stretches horizontally.
    [AddComponentMenu("UI/Horizontal Sliced Raw Image")]
    public sealed class HorizontalSlicedRawImage : MaskableGraphic
    {
        public RawImage TraceParticle { get; set; }
        public RawImage FlickArrow { get; set; }
        [SerializeField] Texture sourceTexture;
        [SerializeField, Range(0, .49f)] float sourceCapRatio = .3f;
        Vector2 surfaceUpperLeft;
        Vector2 surfaceUpperRight;
        Vector2 surfaceLowerRight;
        Vector2 surfaceLowerLeft;
        float surfaceHorizontalStart;
        float surfaceHorizontalEnd = 1;
        bool useSurfaceQuad;

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

        /// <summary>
        /// Draw the sliced image on a locally positioned surface quadrilateral.
        /// The caller owns the RectTransform's center and submits corners relative
        /// to that center, so the UI hierarchy and child effects remain unchanged.
        /// </summary>
        public void SetSurfaceQuad(Vector2 upperLeft, Vector2 upperRight, Vector2 lowerRight, Vector2 lowerLeft,
            float horizontalStart = 0, float horizontalEnd = 1)
        {
            var clampedStart = Mathf.Clamp01(horizontalStart);
            var clampedEnd = Mathf.Clamp(horizontalEnd, clampedStart, 1);
            // Most on-screen notes hold a stationary quad for several frames
            // (a fully transparent Hold mid in particular). Skip the mesh
            // rebuild when nothing about the submitted geometry changed.
            if (useSurfaceQuad &&
                surfaceUpperLeft == upperLeft && surfaceUpperRight == upperRight &&
                surfaceLowerRight == lowerRight && surfaceLowerLeft == lowerLeft &&
                Mathf.Approximately(surfaceHorizontalStart, clampedStart) &&
                Mathf.Approximately(surfaceHorizontalEnd, clampedEnd))
                return;
            surfaceUpperLeft = upperLeft;
            surfaceUpperRight = upperRight;
            surfaceLowerRight = lowerRight;
            surfaceLowerLeft = lowerLeft;
            surfaceHorizontalStart = clampedStart;
            surfaceHorizontalEnd = clampedEnd;
            useSurfaceQuad = true;
            SetVerticesDirty();
        }

        public void ClearSurfaceQuad()
        {
            if (!useSurfaceQuad) return;
            useSurfaceQuad = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;

            var ratio = Mathf.Clamp(sourceCapRatio, 0, .49f);
            if (useSurfaceQuad)
            {
                AddSurfaceSlices(helper, ratio);
                return;
            }
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

        void AddSurfaceSlices(VertexHelper helper, float ratio)
        {
            if (ratio <= .001f || sourceTexture == null)
            {
                AddSurfaceQuad(helper, surfaceUpperLeft, surfaceUpperRight, surfaceLowerRight, surfaceLowerLeft,
                    surfaceHorizontalStart, surfaceHorizontalEnd);
                return;
            }

            var averageHeight = ((surfaceUpperLeft - surfaceLowerLeft).magnitude +
                (surfaceUpperRight - surfaceLowerRight).magnitude) * .5f;
            var clippedWidestEdge = Mathf.Max((surfaceUpperRight - surfaceUpperLeft).magnitude,
                (surfaceLowerRight - surfaceLowerLeft).magnitude);
            var horizontalSpan = surfaceHorizontalEnd - surfaceHorizontalStart;
            if (horizontalSpan <= .0001f || clippedWidestEdge <= .0001f) return;
            var widestEdge = clippedWidestEdge / horizontalSpan;
            var sourceCapPixels = sourceTexture.width * ratio;
            var capWidth = Mathf.Min(widestEdge * .5f, averageHeight * sourceCapPixels / Mathf.Max(1, sourceTexture.height));
            var capFraction = widestEdge <= .001f ? .5f : capWidth / widestEdge;
            capFraction = Mathf.Clamp(capFraction, 0, .5f);

            AddMappedSurfaceSegment(helper, surfaceHorizontalStart, Mathf.Min(surfaceHorizontalEnd, capFraction),
                capFraction, ratio);
            AddMappedSurfaceSegment(helper, Mathf.Max(surfaceHorizontalStart, capFraction),
                Mathf.Min(surfaceHorizontalEnd, 1 - capFraction), capFraction, ratio);
            AddMappedSurfaceSegment(helper, Mathf.Max(surfaceHorizontalStart, 1 - capFraction), surfaceHorizontalEnd,
                capFraction, ratio);
        }

        void AddMappedSurfaceSegment(VertexHelper helper, float start, float end, float capFraction, float sourceRatio)
        {
            if (end <= start + .0001f) return;
            var horizontalSpan = surfaceHorizontalEnd - surfaceHorizontalStart;
            var localStart = (start - surfaceHorizontalStart) / horizontalSpan;
            var localEnd = (end - surfaceHorizontalStart) / horizontalSpan;
            AddSurfaceQuad(helper,
                Vector2.LerpUnclamped(surfaceUpperLeft, surfaceUpperRight, localStart),
                Vector2.LerpUnclamped(surfaceUpperLeft, surfaceUpperRight, localEnd),
                Vector2.LerpUnclamped(surfaceLowerLeft, surfaceLowerRight, localEnd),
                Vector2.LerpUnclamped(surfaceLowerLeft, surfaceLowerRight, localStart),
                SourceUAt(start, capFraction, sourceRatio), SourceUAt(end, capFraction, sourceRatio));
        }

        static float SourceUAt(float horizontalPosition, float capFraction, float sourceRatio)
        {
            if (capFraction <= .0001f) return horizontalPosition;
            if (horizontalPosition <= capFraction)
                return horizontalPosition / capFraction * sourceRatio;
            if (horizontalPosition >= 1 - capFraction)
                return 1 - sourceRatio + (horizontalPosition - (1 - capFraction)) / capFraction * sourceRatio;
            var centerSpan = 1 - capFraction * 2;
            return sourceRatio + (horizontalPosition - capFraction) / centerSpan * (1 - sourceRatio * 2);
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

        void AddSurfaceQuad(VertexHelper helper, Vector2 upperLeft, Vector2 upperRight, Vector2 lowerRight, Vector2 lowerLeft, float u0, float u1)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = lowerLeft; vertex.uv0 = new Vector2(u0, 0); helper.AddVert(vertex);
            vertex.position = upperLeft; vertex.uv0 = new Vector2(u0, 1); helper.AddVert(vertex);
            vertex.position = upperRight; vertex.uv0 = new Vector2(u1, 1); helper.AddVert(vertex);
            vertex.position = lowerRight; vertex.uv0 = new Vector2(u1, 0); helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }
    }
}
