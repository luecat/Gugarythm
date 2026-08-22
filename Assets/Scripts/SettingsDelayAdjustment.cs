using System;

namespace Gugarhythm
{
    public static class SettingsDelayAdjustment
    {
        public const double MinimumSeconds = -.3d;
        public const double MaximumSeconds = .3d;
        public const double StepSeconds = .001d;

        public static double Clamp(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            return Math.Clamp(value, MinimumSeconds, MaximumSeconds);
        }

        public static double Step(double value, double delta) => Clamp(Clamp(value) + delta);
    }
}
