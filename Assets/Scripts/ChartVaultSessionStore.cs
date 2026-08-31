using System;
using System.Runtime.InteropServices;

namespace Gugarhythm
{
    public static class ChartVaultSessionStore
    {
        const string SessionKey = "chart-vault-session";
        const string PendingStateKey = "chart-vault-pending-state";
        const string PendingVerifierKey = "chart-vault-pending-verifier";

        public static string Load()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return ReadIos(SessionKey);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var storage = new AndroidJavaClass("com.gugarhythm.player.GugaSecureStorage");
                return Normalize(storage.CallStatic<string>("read", player, SessionKey));
            }
            catch (Exception) { return null; }
#else
            return null;
#endif
        }

        public static void Save(string token)
        {
            token = Normalize(token);
            if (token == null) return;
#if UNITY_IOS && !UNITY_EDITOR
            GugaSecureStore(SessionKey, token);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var storage = new AndroidJavaClass("com.gugarhythm.player.GugaSecureStorage");
                storage.CallStatic("write", player, SessionKey, token);
            }
            catch (Exception) { }
#endif
        }

        public static void Clear()
        {
#if UNITY_IOS && !UNITY_EDITOR
            GugaSecureDelete(SessionKey);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var storage = new AndroidJavaClass("com.gugarhythm.player.GugaSecureStorage");
                storage.CallStatic("delete", player, SessionKey);
            }
            catch (Exception) { }
#endif
        }

        public static bool TryLoadPendingLogin(out string state, out string verifier)
        {
            state = null;
            verifier = null;
#if UNITY_IOS && !UNITY_EDITOR
            state = ReadIos(PendingStateKey);
            verifier = ReadIos(PendingVerifierKey);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var storage = new AndroidJavaClass("com.gugarhythm.player.GugaSecureStorage");
                state = Normalize(storage.CallStatic<string>("read", player, PendingStateKey));
                verifier = Normalize(storage.CallStatic<string>("read", player, PendingVerifierKey));
            }
            catch (Exception) { }
#endif
            return state != null && verifier != null;
        }

        public static void SavePendingLogin(string state, string verifier)
        {
            state = Normalize(state);
            verifier = Normalize(verifier);
            if (state == null || verifier == null) return;
#if UNITY_IOS && !UNITY_EDITOR
            GugaSecureStore(PendingStateKey, state);
            GugaSecureStore(PendingVerifierKey, verifier);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var storage = new AndroidJavaClass("com.gugarhythm.player.GugaSecureStorage");
                storage.CallStatic("write", player, PendingStateKey, state);
                storage.CallStatic("write", player, PendingVerifierKey, verifier);
            }
            catch (Exception) { }
#endif
        }

        public static void ClearPendingLogin()
        {
#if UNITY_IOS && !UNITY_EDITOR
            GugaSecureDelete(PendingStateKey);
            GugaSecureDelete(PendingVerifierKey);
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
                    .GetStatic<AndroidJavaObject>("currentActivity");
                using var storage = new AndroidJavaClass("com.gugarhythm.player.GugaSecureStorage");
                storage.CallStatic("delete", player, PendingStateKey);
                storage.CallStatic("delete", player, PendingVerifierKey);
            }
            catch (Exception) { }
#endif
        }

        static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 43) return null;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '-' && character != '_')
                    return null;
            return value;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern IntPtr GugaSecureRead(string key);
        [DllImport("__Internal")] static extern void GugaSecureStore(string key, string value);
        [DllImport("__Internal")] static extern void GugaSecureDelete(string key);
        [DllImport("__Internal")] static extern void GugaFreeString(IntPtr value);

        static string ReadIos(string key)
        {
            try
            {
                var native = GugaSecureRead(key);
                if (native == IntPtr.Zero) return null;
                try { return Normalize(Marshal.PtrToStringAnsi(native)); }
                finally { GugaFreeString(native); }
            }
            catch (Exception) { return null; }
        }
#endif
    }
}
