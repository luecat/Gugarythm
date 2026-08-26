using UnityEngine;
using System;
using System.Linq;
using System.Runtime.InteropServices;

namespace Gugarhythm
{
    public static class NativeChartPicker
    {
        const string JavaClass = "com.gugarhythm.player.GugaFilePicker";

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void GugaOpenFile();

        [DllImport("__Internal")]
        static extern IntPtr GugaConsumeResult();

        [DllImport("__Internal")]
        static extern void GugaFreeString(IntPtr value);
#endif

        public static void OpenFile()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var picker = new AndroidJavaClass(JavaClass);
            picker.CallStatic("openFile", activity);
#elif UNITY_IOS && !UNITY_EDITOR
            GugaOpenFile();
#else
            Debug.Log("NativeChartPicker.OpenFile 只會在 Android 或 iOS 裝置開啟系統選檔器。");
#endif
        }

        public static void OpenFolder()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var picker = new AndroidJavaClass(JavaClass);
            picker.CallStatic("openFolder", activity);
#else
            Debug.Log("NativeChartPicker.OpenFolder 只會在 Android 裝置開啟系統資料夾選擇器。");
#endif
        }

        public static string ConsumeResult()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using var picker = new AndroidJavaClass(JavaClass);
            return picker.CallStatic<string>("consumeResult", activity);
#elif UNITY_IOS && !UNITY_EDITOR
            var pointer = GugaConsumeResult();
            if (pointer == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUTF8(pointer);
            }
            finally
            {
                GugaFreeString(pointer);
            }
#else
            return null;
#endif
        }

        internal static string[] SplitResultPaths(string value) =>
            (value ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim()).Where(path => path.Length > 0).ToArray();
    }
}
