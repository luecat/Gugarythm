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
            // Count every hit. Same-frame chords are flushed as one composed burst
            // after judgment drain — do not drop extras here (iOS has no such gate).
            pendingAndroidTicks++;
#elif UNITY_IOS && !UNITY_EDITOR
            GugaPlayShortHaptic();
#endif
        }

        /// <summary>
        /// Sends any queued Android ticks as one composed on/off burst.
        /// Call once after draining judgment events for the frame.
        /// </summary>
        public static void FlushPending()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (pendingAndroidTicks <= 0) return;
            var ticks = pendingAndroidTicks;
            pendingAndroidTicks = 0;
            if (!EnsureAndroidCache()) return;
            hapticClass.CallStatic("playBurst", currentActivity, ticks, DurationMilliseconds);
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
