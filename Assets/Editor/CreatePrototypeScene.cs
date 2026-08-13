using Gugarythm;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
}
