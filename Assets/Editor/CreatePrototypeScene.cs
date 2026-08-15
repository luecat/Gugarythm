using Gugarythm;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;

public static class CreatePrototypeScene
{
    [MenuItem("Gugarythm/Open Rhythm Prototype")]
    public static void Open()
    {
        if (!System.IO.File.Exists("Assets/Scenes/RhythmPrototype.unity")) Build();
        EditorSceneManager.OpenScene("Assets/Scenes/RhythmPrototype.unity", OpenSceneMode.Single);
    }

    [MenuItem("Gugarythm/Rebuild Rhythm Prototype")]
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Gugarythm Prototype");
        root.AddComponent<SonolusLandscapePrototype>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/RhythmPrototype.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/RhythmPrototype.unity", true) };
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Gugarythm/Build Android Debug APK")]
    public static void BuildAndroidDebug()
    {
        Build();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.luecat.gugarythm");
        PlayerSettings.productName = "Gugarythm";
        var directory = "Builds";
        Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/RhythmPrototype.unity" },
            locationPathName = Path.Combine(directory, "Gugarythm-debug.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.Development,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception($"Android build failed: {report.summary.result}");
    }
}
