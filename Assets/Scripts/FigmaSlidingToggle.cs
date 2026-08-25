using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public static class FigmaSlidingToggleLayout
    {
        public static float HandleX(bool enabled, float travel) => enabled ? Mathf.Abs(travel) : -Mathf.Abs(travel);
    }

    public static class FigmaRoundedRectangleLayout
    {
        public static float CornerStartAngleDegrees(int cornerIndex) => Mathf.Repeat(cornerIndex, 4) * 90f;
    }

    public sealed class FigmaRoundedRectangleGraphic : MaskableGraphic
    {
        [SerializeField] float cornerRadius = 12f;

        public void Configure(Color fill, float radius)
        {
            color = fill;
            cornerRadius = Mathf.Max(0f, radius);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            var rect = rectTransform.rect;
            var radius = Mathf.Min(cornerRadius, Mathf.Min(rect.width, rect.height) * .5f);
            if (radius <= .01f)
            {
                vertices.AddVert(new Vector2(rect.xMin, rect.yMin), color, Vector2.zero);
                vertices.AddVert(new Vector2(rect.xMin, rect.yMax), color, Vector2.zero);
                vertices.AddVert(new Vector2(rect.xMax, rect.yMax), color, Vector2.zero);
                vertices.AddVert(new Vector2(rect.xMax, rect.yMin), color, Vector2.zero);
                vertices.AddTriangle(0, 1, 2);
                vertices.AddTriangle(0, 2, 3);
                return;
            }

            const int segmentsPerCorner = 6;
            vertices.AddVert(rect.center, color, Vector2.zero);
            var cornerCenters = new[]
            {
                new Vector2(rect.xMax - radius, rect.yMax - radius),
                new Vector2(rect.xMin + radius, rect.yMax - radius),
                new Vector2(rect.xMin + radius, rect.yMin + radius),
                new Vector2(rect.xMax - radius, rect.yMin + radius),
            };
            for (var corner = 0; corner < cornerCenters.Length; corner++)
            {
                var startAngle = FigmaRoundedRectangleLayout.CornerStartAngleDegrees(corner);
                for (var segment = 0; segment <= segmentsPerCorner; segment++)
                {
                    var angle = (startAngle + segment * 90f / segmentsPerCorner) * Mathf.Deg2Rad;
                    vertices.AddVert(cornerCenters[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                        color, Vector2.zero);
                }
            }
            var ringCount = vertices.currentVertCount - 1;
            for (var index = 0; index < ringCount; index++)
                vertices.AddTriangle(0, index + 1, (index + 1) % ringCount + 1);
        }
    }

    public sealed class FigmaSlidingToggleVisual : MonoBehaviour
    {
        const float AnimationDuration = .12f;

        Toggle toggle;
        RectTransform handle;
        FigmaRoundedRectangleGraphic track;
        float travel;
        float startX;
        float targetX;
        float elapsed = AnimationDuration;

        public void Initialize(Toggle source, RectTransform handleRect, FigmaRoundedRectangleGraphic trackGraphic, float handleTravel)
        {
            if (toggle != null) toggle.onValueChanged.RemoveListener(AnimateTo);
            toggle = source;
            handle = handleRect;
            track = trackGraphic;
            travel = Mathf.Max(0f, handleTravel);
            toggle.onValueChanged.AddListener(AnimateTo);
            SetState(toggle.isOn, true);
        }

        public void SetState(bool enabled, bool immediate)
        {
            targetX = FigmaSlidingToggleLayout.HandleX(enabled, travel);
            track.Configure(enabled ? new Color(.19f, .84f, .68f, 1f) : new Color(.27f, .31f, .39f, 1f), 22f);
            if (immediate)
            {
                handle.anchoredPosition = new Vector2(targetX, 0f);
                elapsed = AnimationDuration;
                return;
            }
            startX = handle.anchoredPosition.x;
            elapsed = 0f;
        }

        void AnimateTo(bool enabled) => SetState(enabled, false);

        void Update()
        {
            if (elapsed >= AnimationDuration || handle == null) return;
            elapsed = Mathf.Min(AnimationDuration, elapsed + Time.unscaledDeltaTime);
            var progress = elapsed / AnimationDuration;
            progress = 1f - Mathf.Pow(1f - progress, 3f);
            handle.anchoredPosition = new Vector2(Mathf.Lerp(startX, targetX, progress), 0f);
        }

        void OnDestroy()
        {
            if (toggle != null) toggle.onValueChanged.RemoveListener(AnimateTo);
        }
    }
}
