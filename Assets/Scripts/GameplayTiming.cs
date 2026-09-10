using System;
using UnityEngine;

namespace Gugarhythm
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

        // FMOD on iOS rejects seeking to the inclusive clip end (and sometimes
        // to time 0 on a channel left at EOF). Keep one sample / 20ms of
        // headroom so pause-seek and restart never pass an invalid position.
        public static float SeekableClipTime(float clipTime, float clipLength, int samples = 0, int frequency = 0)
        {
            if (!float.IsFinite(clipTime) || clipLength <= 0f) return 0f;
            var endGuard = frequency > 0 ? Math.Max(1f / frequency, 0.02f) : 0.02f;
            var maxTime = Math.Max(0f, clipLength - endGuard);
            if (samples > 1 && frequency > 0)
                maxTime = Math.Min(maxTime, (samples - 1) / (float)frequency);
            return (float)Math.Clamp(clipTime, 0, maxTime);
        }

        public static void ApplyClipTime(AudioSource source, float clipTime)
        {
            if (source == null) return;
            var clip = source.clip;
            if (clip == null || clip.samples <= 0 || clip.frequency <= 0) return;
            var safe = SeekableClipTime(clipTime, clip.length, clip.samples, clip.frequency);
            var sample = (int)Math.Clamp(Math.Round(safe * clip.frequency), 0, clip.samples - 1);
            source.timeSamples = sample;
        }

        public static void RewindToStart(AudioSource source)
        {
            if (source == null) return;
            if (source.isPlaying) source.Stop();
            var clip = source.clip;
            if (clip == null) return;
            source.clip = clip;
            ApplyClipTime(source, 0f);
        }

        public static double PlaybackDspForChartTime(double nextDsp, double chartTime, double chartBgmOffset,
            double deviceOffset) => nextDsp + Math.Max(0, -chartTime - chartBgmOffset + deviceOffset);

        public static double ScheduledDspForRecovery(double nextDsp, double chartTime, double chartBgmOffset) =>
            ScheduledDspForChartTime(nextDsp, chartTime, chartBgmOffset);

        public static double ReplaceDeviceOffset(double replacementOffset) =>
            double.IsFinite(replacementOffset) ? Math.Clamp(replacementOffset, -.3, .3) : 0;

        public static double ReplaceInputOffset(double replacementOffset) =>
            double.IsFinite(replacementOffset) ? Math.Clamp(replacementOffset, -.3, .3) : 0;

        public static double ApplyInputOffset(double rawSongTime, double inputOffset) =>
            rawSongTime + ReplaceInputOffset(inputOffset);
    }
}
