using UnityEngine;

namespace Gugarythm
{
    public static class AudioDeviceRecovery
    {
        public static double ChartAnchorDspForAudioOffset(double nextDsp, double audioOffset) =>
            nextDsp + System.Math.Max(0, -audioOffset);

        public static float ClipTimeForChartTime(double chartTime, double bgmOffset, double audioOffset, float clipLength)
        {
            var clipTime = chartTime + bgmOffset - audioOffset;
            return Mathf.Clamp((float)clipTime, 0f, Mathf.Max(0f, clipLength));
        }

        public static double ScheduledDspForChartTime(double nextDsp, double chartTime, double bgmOffset) =>
            nextDsp - chartTime - bgmOffset;

        public static double PlaybackDspForChartTime(double nextDsp, double chartTime, double bgmOffset, double audioOffset) =>
            nextDsp + System.Math.Max(0, -chartTime - bgmOffset + audioOffset);

        public static double ScheduledDspForPlayback(double playbackDsp, float clipTime) =>
            playbackDsp - clipTime;

        public static double ScheduledDspForRecovery(double nextDsp, double chartTime, double bgmOffset) =>
            ScheduledDspForChartTime(nextDsp, chartTime, bgmOffset);

        public static bool ShouldRescheduleAfterAudioInterruption(bool resumeNeedsAudioReschedule) =>
            resumeNeedsAudioReschedule;
    }
}
