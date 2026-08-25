using System;
using System.Collections.Generic;

namespace Gugarhythm
{
    public static class LatencyCalibrationMath
    {
        public const int TapsPerRound = 4;
        public const int CalibrationRoundCount = 4;
        public const double TapWindowSeconds = .5d;

        public static bool TryGetCalibrationAverage(IReadOnlyList<double> offsets, out double average)
        {
            average = 0d;
            if (offsets == null || offsets.Count != CalibrationRoundCount) return false;

            double total = 0d;
            for (var index = 0; index < offsets.Count; index++)
            {
                if (!IsTapOffsetValid(offsets[index])) return false;
                total += offsets[index];
            }

            average = total / offsets.Count;
            return true;
        }

        public static bool TryGetRunningCalibrationAverage(IReadOnlyList<double> offsets, out double average)
        {
            average = 0d;
            if (offsets == null || offsets.Count == 0 || offsets.Count > CalibrationRoundCount) return false;

            double total = 0d;
            for (var index = 0; index < offsets.Count; index++)
            {
                if (!IsTapOffsetValid(offsets[index])) return false;
                total += offsets[index];
            }

            average = total / offsets.Count;
            return true;
        }

        public static bool IsCalibrationTapWithinWindow(double inputDsp, double targetDsp) =>
            !double.IsNaN(inputDsp) && !double.IsInfinity(inputDsp) &&
            !double.IsNaN(targetDsp) && !double.IsInfinity(targetDsp) &&
            Math.Abs(inputDsp - targetDsp) <= TapWindowSeconds;

        public static bool IsTapOffsetValid(double offset) =>
            !double.IsNaN(offset) && !double.IsInfinity(offset) && Math.Abs(offset) <= TapWindowSeconds;

        public static bool TryGetAverageOffset(IReadOnlyList<double> offsets, out double average)
        {
            average = 0d;
            if (offsets == null || offsets.Count != TapsPerRound) return false;

            double total = 0d;
            for (var index = 0; index < offsets.Count; index++)
            {
                if (!IsTapOffsetValid(offsets[index])) return false;
                total += offsets[index];
            }

            average = total / offsets.Count;
            return true;
        }
    }
}
