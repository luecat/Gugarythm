using System.Runtime.InteropServices;
using UnityEngine;

namespace Gugarhythm
{
    public static class ShortHapticFeedback
    {
        // Short on-time for a single tick; Android composes multi-hit bursts with off gaps.
        const long DurationMilliseconds = 10;

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaClass unityPlayerClass;
        static AndroidJavaObject currentActivity;
        static AndroidJavaClass hapticClass;
        static int pendingAndroidTicks;
#endif

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void GugaPlayShortHaptic();
#endif

        public static void Play()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // Queue every hit; Java coalesces a short window into one on/off waveform.
            // Same-frame chords are also flushed explicitly after judgment drain.
            pendingAndroidTicks++;
#elif UNITY_IOS && !UNITY_EDITOR
            GugaPlayShortHaptic();
#endif
        }

        /// <summary>
        /// Sends any queued Android ticks as one composed burst. Call once after a
        /// judgment drain so same-frame chords become multiple on/off segments.
        /// </summary>
        public static void FlushPending()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (pendingAndroidTicks <= 0) return;
            var ticks = pendingAndroidTicks;
            pendingAndroidTicks = 0;
            if (!EnsureAndroidCache()) return;
            try
            {
                hapticClass.CallStatic("playBurst", currentActivity, ticks, DurationMilliseconds);
            }
            catch (System.Exception)
            {
                // Fall back to one coalesced play() per tick if playBurst is missing.
                for (var index = 0; index < ticks; index++)
                    hapticClass.CallStatic("play", currentActivity, DurationMilliseconds);
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static bool EnsureAndroidCache()
        {
            try
            {
                if (unityPlayerClass == null)
                    unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                if (currentActivity == null)
                    currentActivity = unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity");
                if (hapticClass == null)
                    hapticClass = new AndroidJavaClass("com.gugarhythm.player.GugaHapticFeedback");
                return currentActivity != null && hapticClass != null;
            }
            catch (System.Exception)
            {
                unityPlayerClass = null;
                currentActivity = null;
                hapticClass = null;
                return false;
            }
        }
#endif
    }
}
