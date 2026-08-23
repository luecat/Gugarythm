using UnityEngine;

namespace Gugarhythm
{
    // One fully resolved presentation vertex pair.  It deliberately contains
    // no chart or judgment state, so mesh submission never re-evaluates time.
    public readonly struct GuideProjectedPoint
    {
        public readonly float Progress;
        public readonly Vector2 Center;
        public readonly float Width;
        public readonly float Alpha;

        public GuideProjectedPoint(float progress, Vector2 center, float width, float alpha)
        {
            Progress = progress;
            Center = center;
            Width = width;
            Alpha = alpha;
        }
    }
}
