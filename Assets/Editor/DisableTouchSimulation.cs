using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;

[InitializeOnLoad]
public static class DisableTouchSimulation
{
    const string SettingsTypeName = "UnityEngine.InputSystem.Editor.InputEditorUserSettings";

    static DisableTouchSimulation()
    {
        EditorApplication.delayCall += Apply;
        EditorApplication.playModeStateChanged += _ => EditorApplication.delayCall += Apply;
    }

    [MenuItem("Gugarythm/Disable Touch Simulation")]
    public static void Apply()
    {
        EnableInputSystemBackend();

        // This is the same project-local option exposed by Input Debugger as
        // "Simulate Touch Input From Mouse or Pen". It is internal in the
        // package API, so reflection is required to switch it off reliably.
        var settingsType = typeof(InputSystem).Assembly.GetType(SettingsTypeName);
        var property = settingsType?.GetProperty("simulateTouch", BindingFlags.Public | BindingFlags.Static);
        if (property == null)
            throw new InvalidOperationException("Unable to locate the Input System touch-simulation setting.");

        property.SetValue(null, false);
        TouchSimulation.Disable();
        if (Mouse.current != null && !Mouse.current.enabled)
            InputSystem.EnableDevice(Mouse.current);

        var mouse = Mouse.current;
        var flagsField = typeof(InputDevice).GetField("m_DeviceFlags", BindingFlags.NonPublic | BindingFlags.Instance);
        var flags = mouse == null ? "none" : flagsField?.GetValue(mouse)?.ToString() ?? "unknown";
        Debug.Log($"GUGARYTHM_MOUSE_READY enabled={mouse?.enabled ?? false} added={mouse?.added ?? false} " +
                  $"native={mouse?.native ?? false} flags={flags} touchSimulation={TouchSimulation.instance?.enabled ?? false} " +
                  $"position={mouse?.position.ReadValue() ?? Vector2.zero}");
    }

    static void EnableInputSystemBackend()
    {
        // Mirror the Input System package's Unity 6 PlayerSettings lookup so
        // Unity's in-memory settings and ProjectSettings.asset stay in sync.
        var buildProfileType = typeof(BuildProfile);
        var globalSettingsField = buildProfileType.GetField("s_GlobalPlayerSettings", BindingFlags.Static | BindingFlags.NonPublic);
        var globalSettings = globalSettingsField?.GetValue(null) as PlayerSettings;
        if (globalSettings == null) throw new InvalidOperationException("Unable to locate Unity global PlayerSettings.");
        SetInputSystem(globalSettings);

        var activeProfile = BuildProfile.GetActiveBuildProfile();
        if (activeProfile != null)
        {
            var overrideField = buildProfileType.GetField("m_PlayerSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            var profileSettings = overrideField?.GetValue(activeProfile) as PlayerSettings;
            if (profileSettings != null) SetInputSystem(profileSettings);
        }
    }

    static void SetInputSystem(PlayerSettings playerSettings)
    {
        var serializedSettings = new SerializedObject(playerSettings);
        var activeInputHandler = serializedSettings.FindProperty("activeInputHandler");
        if (activeInputHandler == null) throw new InvalidOperationException("Unable to locate activeInputHandler.");
        if (activeInputHandler.intValue == 1) return;
        activeInputHandler.intValue = 1; // Input System Package (New).
        serializedSettings.ApplyModifiedProperties();
    }
}
