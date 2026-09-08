using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public enum HitParticleEffectMode
    {
        ParticleScatter,
        ShardBreak,
        BrokenRing,
        // Appended rather than inserted first so existing PlayerPrefs values
        // saved under the old 3-mode enum keep pointing at the same mode.
        None,
    }

    // A deterministic, texture-free judgment burst. All drawing lives in the
    // static Draw() method so HitBurstBatchGraphic can render many bursts
    // (one per simultaneous chord hit) into a single mesh/CanvasRenderer
    // instead of one GameObject per burst. This single-instance Graphic is
    // kept only for call sites that still want one burst on its own
    // RectTransform; the batched gameplay path uses Draw() directly.
    public sealed class HitBurstGraphic : MaskableGraphic
    {
        public const float DurationSeconds = 18f / 60f;

        [Range(0, 1)] public float progress;
        public float upperWidth = 96;
        public HitParticleEffectMode effectMode;

        public void SetProgress(float value)
        {
            progress = Mathf.Clamp01(value);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            Draw(helper, Vector2.zero, upperWidth, effectMode, color, progress);
        }

        public static void Draw(VertexHelper helper, Vector2 origin, float upperWidth,
            HitParticleEffectMode effectMode, Color tint, float progress)
        {
            if (effectMode == HitParticleEffectMode.None) return;
            DrawContact(helper, origin, upperWidth, tint, progress);
            switch (effectMode)
            {
                case HitParticleEffectMode.ShardBreak:
                    DrawShardBreak(helper, origin, tint, progress);
                    break;
                case HitParticleEffectMode.BrokenRing:
                    DrawBrokenRing(helper, origin, upperWidth, tint, progress);
                    break;
                default:
                    DrawParticleScatter(helper, origin, tint, progress);
                    break;
            }
        }

        static void DrawContact(VertexHelper helper, Vector2 origin, float upperWidth, Color baseTint, float progress)
        {
            var fade = 1f - Mathf.Clamp01(progress / .28f);
            var tint = Color.Lerp(baseTint, Color.white, .38f);
            tint.a *= fade;
            var halfWidth = upperWidth * Mathf.Lerp(.58f, .92f, progress);
            AddQuad(helper, origin, new Vector2(-halfWidth, 3f), new Vector2(halfWidth, 3f),
                new Vector2(halfWidth, -3f), new Vector2(-halfWidth, -3f), tint);
            AddDiamondFill(helper, origin, Vector2.zero, upperWidth * Mathf.Lerp(.28f, .42f, progress),
                Mathf.Lerp(10f, 5f, progress), tint);
        }

        static void DrawParticleScatter(VertexHelper helper, Vector2 origin, Color baseTint, float progress)
        {
            var travel = EaseOutCubic(progress);
            var fade = 1f - Mathf.Clamp01((progress - .42f) / .58f);
            for (var index = 0; index < 22; index++)
            {
                var angle = (index * 137.5f + 12f) * Mathf.Deg2Rad;
                var variation = Hash01(index * 17 + 5);
                var distance = Mathf.Lerp(34f, 142f, variation) * travel;
                var center = new Vector2(
                    Mathf.Cos(angle) * distance,
                    Mathf.Sin(angle) * distance * .72f + travel * 18f);
                var tint = baseTint;
                tint.a *= fade * Mathf.Lerp(.58f, 1f, Hash01(index * 23 + 2));
                var size = Mathf.Lerp(3.5f, 9f, Hash01(index * 31 + 9)) * Mathf.Lerp(1f, .55f, progress);
                if ((index & 3) == 0)
                    AddRotatedRect(helper, origin, center, size * 2.2f, size * .52f, angle, tint);
                else
                    AddDiamondFill(helper, origin, center, size, size * .72f, tint);
            }
        }

        static void DrawShardBreak(VertexHelper helper, Vector2 origin, Color baseTint, float progress)
        {
            var travel = EaseOutCubic(progress);
            var fade = 1f - Mathf.Clamp01((progress - .5f) / .5f);
            for (var index = 0; index < 10; index++)
            {
                var spread = Mathf.Lerp(-1f, 1f, index / 9f);
                var rank = index % 3;
                var center = new Vector2(
                    spread * Mathf.Lerp(16f, 132f + rank * 12f, travel),
                    Mathf.Lerp(2f, 42f + (1f - Mathf.Abs(spread)) * 74f + rank * 10f, travel));
                var tint = baseTint;
                tint.a *= fade * (.96f - rank * .1f);
                var rotation = (spread * 42f + index * 29f + progress * (index % 2 == 0 ? 120f : -120f)) * Mathf.Deg2Rad;
                var width = Mathf.Lerp(18f, 10f, progress) * (1f + rank * .12f);
                var height = Mathf.Lerp(34f, 18f, progress) * (1f + rank * .08f);
                AddShard(helper, origin, center, width, height, rotation, tint);
            }
        }

        static void DrawBrokenRing(VertexHelper helper, Vector2 origin, float upperWidth, Color baseTint, float progress)
        {
            var travel = EaseOutCubic(progress);
            var fade = 1f - Mathf.Clamp01((progress - .48f) / .52f);
            var radiusX = Mathf.Lerp(upperWidth * .42f, 154f, travel);
            var radiusY = Mathf.Lerp(13f, 68f, travel);
            var tint = baseTint;
            tint.a *= fade * .92f;
            for (var segment = 0; segment < 8; segment++)
            {
                var startAngle = segment * 45f + (segment % 2 == 0 ? 5f : 12f);
                var endAngle = startAngle + (segment % 3 == 0 ? 22f : 28f);
                AddEllipseArc(helper, origin, radiusX, radiusY, startAngle, endAngle,
                    Mathf.Lerp(7f, 2.5f, progress), tint);
            }

            for (var index = 0; index < 8; index++)
            {
                var angle = (index * 47f + 18f) * Mathf.Deg2Rad;
                var distance = Mathf.Lerp(28f, 124f, travel) * Mathf.Lerp(.72f, 1f, Hash01(index + 41));
                var center = new Vector2(Mathf.Cos(angle) * distance, Mathf.Sin(angle) * distance * .62f + travel * 10f);
                var particleTint = baseTint;
                particleTint.a *= fade * .72f;
                var size = Mathf.Lerp(7f, 3f, progress);
                AddDiamondFill(helper, origin, center, size, size * .7f, particleTint);
            }
        }

        static float EaseOutCubic(float value) => 1f - Mathf.Pow(1f - Mathf.Clamp01(value), 3f);

        static float Hash01(int value)
        {
            var hash = Mathf.Sin(value * 12.9898f) * 43758.5453f;
            return hash - Mathf.Floor(hash);
        }

        static void AddEllipseArc(VertexHelper helper, Vector2 origin, float radiusX, float radiusY,
            float startDegrees, float endDegrees, float width, Color32 tint)
        {
            const int Steps = 3;
            var previousAngle = startDegrees * Mathf.Deg2Rad;
            var previous = new Vector2(Mathf.Cos(previousAngle) * radiusX, Mathf.Sin(previousAngle) * radiusY);
            for (var step = 1; step <= Steps; step++)
            {
                var angle = Mathf.Lerp(startDegrees, endDegrees, step / (float)Steps) * Mathf.Deg2Rad;
                var current = new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
                AddLine(helper, origin, previous, current, width, tint);
                previous = current;
            }
        }

        static void AddRotatedRect(VertexHelper helper, Vector2 origin, Vector2 center, float width, float height,
            float angle, Color32 tint)
        {
            var right = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * width * .5f;
            var up = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * height * .5f;
            AddQuad(helper, origin, center - right + up, center + right + up,
                center + right - up, center - right - up, tint);
        }

        static void AddShard(VertexHelper helper, Vector2 origin, Vector2 center, float width, float height,
            float angle, Color32 tint)
        {
            var forward = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var right = new Vector2(-forward.y, forward.x);
            AddTriangle(helper, origin, center + forward * height * .58f,
                center - forward * height * .42f + right * width * .5f,
                center - forward * height * .2f - right * width * .5f, tint);
        }

        static void AddLine(VertexHelper helper, Vector2 origin, Vector2 start, Vector2 end, float width, Color32 tint)
        {
            var direction = (end - start).normalized;
            var normal = new Vector2(-direction.y, direction.x) * width * .5f;
            AddQuad(helper, origin, start - normal, start + normal, end + normal, end - normal, tint);
        }

        static void AddDiamondFill(VertexHelper helper, Vector2 origin, Vector2 center, float radiusX, float radiusY, Color32 tint) =>
            AddQuad(helper, origin, center + Vector2.up * radiusY, center + Vector2.right * radiusX,
                center + Vector2.down * radiusY, center + Vector2.left * radiusX, tint);

        static void AddQuad(VertexHelper helper, Vector2 origin, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 tint)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = origin + a; helper.AddVert(vertex);
            vertex.position = origin + b; helper.AddVert(vertex);
            vertex.position = origin + c; helper.AddVert(vertex);
            vertex.position = origin + d; helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
            helper.AddTriangle(first, first + 2, first + 3);
        }

        static void AddTriangle(VertexHelper helper, Vector2 origin, Vector2 a, Vector2 b, Vector2 c, Color32 tint)
        {
            var first = helper.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = tint;
            vertex.position = origin + a; helper.AddVert(vertex);
            vertex.position = origin + b; helper.AddVert(vertex);
            vertex.position = origin + c; helper.AddVert(vertex);
            helper.AddTriangle(first, first + 1, first + 2);
        }
    }
}
