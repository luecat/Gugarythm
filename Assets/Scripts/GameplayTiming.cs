using System;

namespace Gugarythm
{
    /// <summary>
    /// Owns the conversion between the chart clock and audio playback phase.
    /// The device offset intentionally never participates in chart-time mapping.
    /// </summary>
    public static class GameplayTiming
    {
        public static double ChartTimeAtDsp(double dspTime, double scheduledDsp, double accumulatedPause,
            double chartBgmOffset) => dspTime - scheduledDsp - accumulatedPause - chartBgmOffset;

        public static double ScheduledDspForChartTime(double nextDsp, double chartTime, double chartBgmOffset) =>
            nextDsp - chartTime - chartBgmOffset;

        public static double PlaybackDspForSchedule(double scheduledDsp, double deviceOffset) =>
            scheduledDsp + deviceOffset;

        public static double ChartAnchorDspForDeviceOffset(double nextDsp, double deviceOffset) =>
            nextDsp + Math.Max(0, -deviceOffset);

        public static double EarliestAudioSafeChartTime(double chartBgmOffset, double deviceOffset) =>
            -chartBgmOffset + deviceOffset;

        public static float ClipTimeForChartTime(double chartTime, double chartBgmOffset, double deviceOffset,
            float clipLength)
        {
            var clipTime = chartTime + chartBgmOffset - deviceOffset;
            return (float)Math.Clamp(clipTime, 0, Math.Max(0, clipLength));
        }

        public static double PlaybackDspForChartTime(double nextDsp, double chartTime, double chartBgmOffset,
            double deviceOffset) => nextDsp + Math.Max(0, -chartTime - chartBgmOffset + deviceOffset);

        public static double ScheduledDspForRecovery(double nextDsp, double chartTime, double chartBgmOffset) =>
            ScheduledDspForChartTime(nextDsp, chartTime, chartBgmOffset);

        public static double ReplaceDeviceOffset(double replacementOffset) =>
            double.IsFinite(replacementOffset) ? Math.Clamp(replacementOffset, -.3, .3) : 0;
    }
}
