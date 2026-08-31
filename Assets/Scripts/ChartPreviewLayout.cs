using System;

namespace Gugarhythm
{
    public static class ChartPreviewLayout
    {
        public const float PrimaryWidthFraction = .25f;
        const float MinimumContentHeight = 1f;

        public static float PrimaryWidth(float documentWidth) =>
            float.IsFinite(documentWidth) && documentWidth > 0f
                ? documentWidth * PrimaryWidthFraction
                : 0f;

        public static float ContentHeight(double startTime, double endTime, float documentUnitsPerSecond)
        {
            if (!double.IsFinite(startTime) || !double.IsFinite(endTime) ||
                !float.IsFinite(documentUnitsPerSecond) || documentUnitsPerSecond <= 0f)
                return MinimumContentHeight;
            var duration = Math.Abs(endTime - startTime);
            if (!double.IsFinite(duration)) return MinimumContentHeight;
            return Math.Max(MinimumContentHeight, (float)Math.Min(float.MaxValue, duration * documentUnitsPerSecond));
        }

        // The unfolded chart document advances in time from left to right.
        // Keep its horizontal extent calculation paired with the older vertical
        // helper so preview callers never need to reinterpret chart timing.
        public static float ContentWidth(double startTime, double endTime, float documentUnitsPerSecond) =>
            ContentHeight(startTime, endTime, documentUnitsPerSecond);

        public static float DocumentY(double chartTime, double startTime, double endTime, float contentHeight)
        {
            if (!double.IsFinite(chartTime) || !double.IsFinite(startTime) || !double.IsFinite(endTime) ||
                !float.IsFinite(contentHeight) || contentHeight <= 0f)
                return 0f;
            var first = Math.Min(startTime, endTime);
            var last = Math.Max(startTime, endTime);
            var duration = last - first;
            if (duration <= double.Epsilon) return 0f;
            var normalized = Math.Max(0d, Math.Min(1d, (chartTime - first) / duration));
            return (float)(normalized * contentHeight);
        }

        public static float DocumentX(double chartTime, double startTime, double endTime, float contentWidth) =>
            DocumentY(chartTime, startTime, endTime, contentWidth);
    }
}
