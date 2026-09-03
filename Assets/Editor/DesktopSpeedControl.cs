using Gugarhythm;
using UnityEditor;
using UnityEngine;

public sealed class DesktopSpeedControl : EditorWindow
{
    const string PreferenceKey = "gugarhythm-scroll-speed";

    [MenuItem("Gugarhythm/Desktop Speed Control")]
    public static void Open()
    {
        var window = GetWindow<DesktopSpeedControl>(true, "Gugarhythm Speed", true);
        window.minSize = new Vector2(360, 150);
        window.Show();
    }

    [MenuItem("Gugarhythm/Speed/Decrease 0.1")]
    public static void Decrease() => Apply(Mathf.Round((CurrentSpeed() - .1f) * 10f) / 10f);

    [MenuItem("Gugarhythm/Speed/Increase 0.1")]
    public static void Increase() => Apply(Mathf.Round((CurrentSpeed() + .1f) * 10f) / 10f);

    void OnGUI()
    {
        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Desktop speed control", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Mouse: use the native controls below. Touch controls in the game remain enabled.", MessageType.Info);

        var current = CurrentSpeed();
        EditorGUI.BeginChangeCheck();
        var selected = EditorGUILayout.Slider("Speed", current, 1f, 20f);
        if (EditorGUI.EndChangeCheck()) Apply(selected);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Decrease 0.1", GUILayout.Height(36))) Apply(Mathf.Round((current - .1f) * 10f) / 10f);
        if (GUILayout.Button("Increase 0.1", GUILayout.Height(36))) Apply(Mathf.Round((current + .1f) * 10f) / 10f);
        EditorGUILayout.EndHorizontal();
    }

    static float CurrentSpeed()
    {
        GugarhythmPreferenceMigration.Migrate();
        var controller = Object.FindFirstObjectByType<GugarhythmLandscapePrototype>();
        return controller != null ? controller.ScrollSpeed : PlayerPrefs.GetFloat(PreferenceKey, 8f);
    }

    static void Apply(float value)
    {
        value = Mathf.Clamp(value, 1f, 20f);
        PlayerPrefs.SetFloat(PreferenceKey, value);
        var controller = Object.FindFirstObjectByType<GugarhythmLandscapePrototype>();
        if (controller != null) controller.SetDesktopScrollSpeed(value);
    }
}
