using Gugarythm;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

public static class CreatePrototypeScene
{
    const string LibraryScenePath = "Assets/Scenes/LibraryScene.unity";
    const string SettingsScenePath = "Assets/Scenes/SettingsScene.unity";
    const string ChartEditorScenePath = "Assets/Scenes/ChartEditorScene.unity";
    const string GameplayScenePath = "Assets/Scenes/RhythmPrototype.unity";

    [MenuItem("Gugarythm/Open Rhythm Prototype")]
    public static void Open()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
    }

    [MenuItem("Gugarythm/Open Chart Library")]
    public static void OpenLibrary()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(LibraryScenePath, OpenSceneMode.Single);
    }

    [MenuItem("Gugarythm/Open Settings")]
    public static void OpenSettings()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(SettingsScenePath, OpenSceneMode.Single);
    }

    [MenuItem("Gugarythm/Open Chart Editor")]
    public static void OpenChartEditor()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(ChartEditorScenePath, OpenSceneMode.Single);
    }

    static void EnsurePlayerScenes()
    {
        if (!File.Exists(LibraryScenePath) || !File.Exists(SettingsScenePath) || !File.Exists(ChartEditorScenePath) || !File.Exists(GameplayScenePath)) Build();
    }

    [MenuItem("Gugarythm/Rebuild Player Scenes")]
    public static void Build()
    {
        BuildScene(LibraryScenePath, "Gugarythm Library");
        BuildScene(SettingsScenePath, "Gugarythm Settings");
        BuildScene(ChartEditorScenePath, "Gugarythm Chart Editor");
        BuildScene(GameplayScenePath, "Gugarythm Prototype");
        EditorBuildSettings.scenes = PlayerScenes();
        AssetDatabase.SaveAssets();
    }

    static void BuildScene(string path, string rootName)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject(rootName);
        root.AddComponent<SonolusLandscapePrototype>();
        EditorSceneManager.SaveScene(scene, path);
    }

    static EditorBuildSettingsScene[] PlayerScenes() => new[]
    {
        new EditorBuildSettingsScene(LibraryScenePath, true),
        new EditorBuildSettingsScene(SettingsScenePath, true),
        new EditorBuildSettingsScene(ChartEditorScenePath, true),
        new EditorBuildSettingsScene(GameplayScenePath, true),
    };

    [MenuItem("Gugarythm/Build Android Debug APK")]
    public static void BuildAndroidDebug()
    {
        Build();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.luecat.gugarythm");
        PlayerSettings.productName = "Gugarythm";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        var directory = "Builds";
        Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { LibraryScenePath, SettingsScenePath, ChartEditorScenePath, GameplayScenePath },
            locationPathName = Path.Combine(directory, "Gugarythm-debug.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.Development,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception($"Android build failed: {report.summary.result}");
    }
}
