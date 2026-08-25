using System;

namespace Gugarhythm
{
    /// <summary>
    /// Smooths a block-updated DSP clock for presentation without changing
    /// the authoritative clock used by gameplay.
    /// </summary>
    public sealed class GameplayPresentationClock
    {
        bool initialized;
        bool hasOutput;
        double lastRawDspTime;
        double lastRealtime;
        double estimatedDspTime;
        double lastOutput;

        public void Reset(double dspTime, double realtime)
        {
            if (!double.IsFinite(dspTime) || !double.IsFinite(realtime))
            {
                Invalidate();
                return;
            }

            initialized = true;
            hasOutput = true;
            lastRawDspTime = dspTime;
            lastRealtime = realtime;
            estimatedDspTime = dspTime;
            lastOutput = dspTime;
        }

        public double Sample(double dspTime, double realtime, double hardResetThreshold)
        {
            if (!double.IsFinite(dspTime) || !double.IsFinite(realtime) ||
                !double.IsFinite(hardResetThreshold) || hardResetThreshold <= 0)
            {
                initialized = false;
                return hasOutput ? lastOutput : 0d;
            }

            if (!initialized)
                return Reanchor(dspTime, realtime);

            if (dspTime < lastRawDspTime || realtime < lastRealtime)
                return Reanchor(dspTime, realtime);

            var realtimeDelta = realtime - lastRealtime;
            var prediction = estimatedDspTime + realtimeDelta;
            var phaseError = dspTime - prediction;
            if (Math.Abs(phaseError) > hardResetThreshold)
                return Reanchor(dspTime, realtime);

            // AudioSettings.dspTime can advance in buffer-sized blocks. A
            // per-block phase correction changes the presentation step for a
            // single frame; extreme or reverse TimeScale multiplies that tiny
            // correction into a visible position jump. Keep normal frames on
            // the realtime-derived line and reanchor only for a real clock
            // discontinuity beyond the existing hard-reset threshold.

            lastRawDspTime = dspTime;
            lastRealtime = realtime;
            estimatedDspTime = prediction;
            lastOutput = Math.Max(lastOutput, prediction);
            return lastOutput;
        }

        public void Invalidate()
        {
            initialized = false;
            hasOutput = false;
            lastRawDspTime = 0;
            lastRealtime = 0;
            estimatedDspTime = 0;
            lastOutput = 0;
        }

        double Reanchor(double dspTime, double realtime)
        {
            initialized = true;
            lastRawDspTime = dspTime;
            lastRealtime = realtime;
            estimatedDspTime = dspTime;
            lastOutput = hasOutput ? Math.Max(lastOutput, dspTime) : dspTime;
            hasOutput = true;
            return lastOutput;
        }
    }
}
