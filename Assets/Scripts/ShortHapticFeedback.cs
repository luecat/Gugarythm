using System.Runtime.InteropServices;
using UnityEngine;

namespace Gugarhythm
{
    public static class ShortHapticFeedback
    {
        const long DurationMilliseconds = 15;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void GugaPlayShortHaptic();
#endif

        public static void Play()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var haptic = new AndroidJavaClass("com.gugarhythm.player.GugaHapticFeedback");
            haptic.CallStatic("play", activity, DurationMilliseconds);
#elif UNITY_IOS && !UNITY_EDITOR
            GugaPlayShortHaptic();
#endif
        }
    }
}
