using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public enum GpuRibbonKind
    {
        Guide,
        HoldNormal,
        HoldCritical,
    }

    // A chunk's time-scale group and authored visual-position span, used to
    // cull whole chunks that cannot possibly be on screen this frame without
    // touching the (immutable) vertex data itself. Group -1 or an infinite
    // span means "unknown span" and always renders, matching the behavior
    // before per-chunk culling existed.
    public sealed class GpuRibbonChunkData
    {
        public readonly GpuRibbonKind Kind;
        public readonly UIVertex[] Vertices;
        public readonly int[] Indices;
        public readonly int GroupIndex;
        public readonly double MinVisualPosition;
        public readonly double MaxVisualPosition;

        public GpuRibbonChunkData(GpuRibbonKind kind, UIVertex[] vertices, int[] indices,
            int groupIndex = -1, double minVisualPosition = double.NegativeInfinity,
            double maxVisualPosition = double.PositiveInfinity)
        {
            Kind = kind;
            Vertices = vertices ?? Array.Empty<UIVertex>();
            Indices = indices ?? Array.Empty<int>();
            GroupIndex = groupIndex;
            MinVisualPosition = minVisualPosition;
            MaxVisualPosition = maxVisualPosition;
        }
    }

    // CPU parity oracle for the GPU shader.  These constants are measured from
    // the same 1280x732 lane artwork used by GugarhythmLandscapePrototype.
    public static class GpuRibbonProjection
    {
        public const float ReferenceWidth = 1920f;
        public const float LaneTextureWidth = 1280f;
        public const float LaneTextureHeight = 732f;
        public const float HitSourceY = 500f;
        public const float CentralHalfLanes = 6f;
        public const float PerspectiveDepthRatio = 3.2f;

        static readonly float[] Intercepts =
        {
            616.0356f, 620.9612f, 624.5489f, 628.4903f, 631.5389f, 635.4715f, 638.8049f,
            642.5187f, 646.0649f, 649.5068f, 653.0450f, 656.5548f, 660.2418f,
        };

        static readonly float[] Slopes =
        {
            -.8379661f, -.7036342f, -.5590519f, -.4198532f, -.2774788f, -.1406074f, .0000444f,
            .1412126f, .2827021f, .4205463f, .5611308f, .7017399f, .8439814f,
        };

        public static float Perspective(float approach)
        {
            if (approach <= 0) return approach / PerspectiveDepthRatio;
            if (approach >= 1) return 1f + (approach - 1f) * PerspectiveDepthRatio;
            return approach / (PerspectiveDepthRatio - (PerspectiveDepthRatio - 1f) * approach);
        }

        public static float HitY(float canvasHeight) => canvasHeight * .5f - HitSourceY / LaneTextureHeight * canvasHeight;

        public static float ScreenY(float screenProgress, float canvasHeight) =>
            Mathf.LerpUnclamped(canvasHeight * .5f, HitY(canvasHeight), screenProgress);

        public static float LaneX(float lane, float screenProgress, float canvasHeight)
        {
            var sourceY = (canvasHeight * .5f - ScreenY(screenProgress, canvasHeight)) * LaneTextureHeight / canvasHeight;
            var guide = Mathf.Clamp(Mathf.FloorToInt(lane + CentralHalfLanes), 0, Intercepts.Length - 2);
            var guideLane = -CentralHalfLanes + guide;
            var t = lane - guideLane;
            var left = Intercepts[guide] + Slopes[guide] * sourceY;
            var right = Intercepts[guide + 1] + Slopes[guide + 1] * sourceY;
            var sourceX = Mathf.LerpUnclamped(left, right, t);
            var centerIndex = (int)CentralHalfLanes;
            var sourceCenter = Intercepts[centerIndex] + Slopes[centerIndex] * sourceY;
            return (sourceX - sourceCenter) / LaneTextureWidth * ReferenceWidth;
        }

        public static Vector2 ProjectEdge(float lane, float size, float relativeVisualPosition,
            float currentRelativeVisualPosition, float approachDuration, float side, float canvasHeight,
            float nearTrackProgress)
        {
            var approach = 1f - (relativeVisualPosition - currentRelativeVisualPosition) /
                Mathf.Max(.0001f, approachDuration);
            var screen = Mathf.Clamp(Perspective(approach), 0, nearTrackProgress);
            var center = LaneX(lane, screen, canvasHeight);
            var width = Mathf.Max(12f, LaneX(lane + size, screen, canvasHeight) - LaneX(lane - size, screen, canvasHeight));
            return new Vector2(center + side * width * .5f, ScreenY(screen, canvasHeight));
        }

        // For a fixed lane, LaneX(lane, sourceY) is affine in sourceY (the
        // only runtime-varying input once lane is baked): its guide segment,
        // interpolation weight t, and therefore its Intercepts/Slopes lerp
        // are all constant. This returns that affine form as
        // LaneX(lane, sourceY) == constant + slope * sourceY, letting a
        // caller bake per-vertex lane/size into plain coefficients so the
        // vertex shader never re-derives them per frame (see Vertex below
        // and GpuRibbonUI.shader). Kept in exact algebraic lock-step with
        // LaneX above — any change there must be mirrored here.
        public static void LaneProjectionCoefficients(float lane, out float constant, out float slope)
        {
            var guide = Mathf.Clamp(Mathf.FloorToInt(lane + CentralHalfLanes), 0, Intercepts.Length - 2);
            var guideLane = -CentralHalfLanes + guide;
            var t = lane - guideLane;
            var sourceXIntercept = Mathf.LerpUnclamped(Intercepts[guide], Intercepts[guide + 1], t);
            var sourceXSlope = Mathf.LerpUnclamped(Slopes[guide], Slopes[guide + 1], t);
            var centerIndex = (int)CentralHalfLanes;
            constant = (sourceXIntercept - Intercepts[centerIndex]) / LaneTextureWidth * ReferenceWidth;
            slope = (sourceXSlope - Slopes[centerIndex]) / LaneTextureWidth * ReferenceWidth;
        }

        public static UIVertex Vertex(float lane, float size, double visualPosition, float side,
            float textureV, int groupIndex, int auxiliaryIndex, float alpha)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(lane, size, (float)visualPosition);
            vertex.color = new Color32(255, 255, 255,
                (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255));
            vertex.uv0 = new Vector4(side < 0 ? 0 : 1, textureV, groupIndex, auxiliaryIndex);
            // uv1 carries the baked (center constant, center slope, width
            // constant, width slope) quadruple described above, so the GPU
            // ribbon shader's vertex stage needs only two multiply-adds
            // instead of the Intercepts/Slopes array lookups LaneX performs.
            LaneProjectionCoefficients(lane, out var centerConstant, out var centerSlope);
            LaneProjectionCoefficients(lane - size, out var leftConstant, out var leftSlope);
            LaneProjectionCoefficients(lane + size, out var rightConstant, out var rightSlope);
            vertex.uv1 = new Vector4(centerConstant, centerSlope,
                rightConstant - leftConstant, rightSlope - leftSlope);
            // A Canvas streams TexCoord0 as a two-component channel: only its
            // xy survive batching, so the group and auxiliary indices these
            // used to ride in (uv0.zw) reached the shader as zeroes. Every
            // ribbon outside time-scale group 0 then resolved its approach
            // against group 0's position and left the screen entirely, and
            // every Guide drew with colour index 0. Carry them in TexCoord2
            // instead, which EnableRibbonVertexChannels declares and which
            // therefore arrives intact.
            vertex.uv2 = new Vector4(groupIndex, auxiliaryIndex, 0, 0);
            vertex.uv3 = Vector4.zero;
            return vertex;
        }
    }
}
