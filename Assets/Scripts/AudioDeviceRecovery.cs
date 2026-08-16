using UnityEngine;

namespace Gugarythm
{
    public static class AudioDeviceRecovery
    {
        public static float ClipTimeForChartTime(double chartTime, double bgmOffset, float clipLength)
        {
            var clipTime = chartTime + bgmOffset;
            return Mathf.Clamp((float)clipTime, 0f, Mathf.Max(0f, clipLength));
        }

        public static double ScheduledDspForChartTime(double nextDsp, double chartTime, double bgmOffset) =>
            nextDsp - chartTime - bgmOffset;
    }
}
