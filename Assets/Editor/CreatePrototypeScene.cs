using Gugarythm;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Linq;

public static class CreatePrototypeScene
{
    const string LibraryScenePath = "Assets/Scenes/LibraryScene.unity";
    const string StartupScenePath = "Assets/Scenes/StartupScene.unity";
    const string SettingsScenePath = "Assets/Scenes/SettingsScene.unity";
    const string ChartEditorScenePath = "Assets/Scenes/ChartEditorScene.unity";
    const string GameplayScenePath = "Assets/Scenes/RhythmPrototype.unity";
    const string ApplicationIconPath = "Assets/Art/AppIcon/gugarythm-icon.png";
    const string SplashScreenPath = "Assets/Art/SplashScreen/gugarythm-splash.png";

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
        if (!File.Exists(StartupScenePath) || !File.Exists(LibraryScenePath) || !File.Exists(SettingsScenePath) || !File.Exists(ChartEditorScenePath) || !File.Exists(GameplayScenePath)) Build();
    }

    [MenuItem("Gugarythm/Rebuild Player Scenes")]
    public static void Build()
    {
        var splash = ImportSplashSprite();
        BuildStartupScene(splash);
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

    static void BuildStartupScene(Sprite splash)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Gugarythm Startup");
        var startup = root.AddComponent<GugarythmStartupSplash>();
        startup.Configure(splash, GugarythmStartupSplash.DefaultDisplaySeconds);
        EditorSceneManager.SaveScene(scene, StartupScenePath);
    }

    static string[] PlayerBuildScenePaths() => new[]
    {
        StartupScenePath,
        LibraryScenePath,
        SettingsScenePath,
        ChartEditorScenePath,
        GameplayScenePath,
    };

    static EditorBuildSettingsScene[] PlayerScenes() => PlayerBuildScenePaths()
        .Select(path => new EditorBuildSettingsScene(path, true)).ToArray();

    [MenuItem("Gugarythm/Build Android Debug APK")]
    public static void BuildAndroidDebug()
    {
        Build();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        ConfigureApplicationIcon();
        ConfigureSplashScreen();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.luecat.gugarythm");
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.productName = "Gugarythm";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        var directory = "Builds";
        Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = PlayerBuildScenePaths(),
            locationPathName = Path.Combine(directory, "Gugarythm-debug.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.Development,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception($"Android build failed: {report.summary.result}");
    }

    static void ConfigureApplicationIcon()
    {
        AssetDatabase.ImportAsset(ApplicationIconPath, ImportAssetOptions.ForceSynchronousImport);
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ApplicationIconPath);
        if (icon == null) throw new System.Exception($"Android application icon not found: {ApplicationIconPath}");
        var iconSizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Android, IconKind.Application);
        var icons = new Texture2D[iconSizes.Length];
        for (var index = 0; index < icons.Length; index++) icons[index] = icon;
        PlayerSettings.SetIcons(NamedBuildTarget.Android, icons, IconKind.Application);
        ConfigureAndroidPlatformIcons(icon);
        AssetDatabase.SaveAssets();
    }

    static void ConfigureAndroidPlatformIcons(Texture2D icon)
    {
        ConfigureAndroidPlatformIconKind(AndroidPlatformIconKind.Legacy, icon);
        ConfigureAndroidPlatformIconKind(AndroidPlatformIconKind.Round, icon);
        ConfigureAndroidPlatformIconKind(AndroidPlatformIconKind.Adaptive, icon);
    }

    static void ConfigureAndroidPlatformIconKind(PlatformIconKind kind, Texture2D icon)
    {
        var slots = PlayerSettings.GetPlatformIcons(BuildTargetGroup.Android, kind);
        for (var index = 0; index < slots.Length; index++)
        {
            var textures = new Texture2D[slots[index].maxLayerCount];
            for (var layer = 0; layer < textures.Length; layer++) textures[layer] = icon;
            slots[index].SetTextures(textures);
        }
        PlayerSettings.SetPlatformIcons(BuildTargetGroup.Android, kind, slots);
    }

    static void ConfigureSplashScreen()
    {
        var splash = ImportSplashSprite();
        PlayerSettings.SplashScreen.show = true;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.SplashScreen.background = splash;
        PlayerSettings.SplashScreen.backgroundPortrait = splash;
        PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];
        PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
        PlayerSettings.SplashScreen.blurBackgroundImage = false;
        PlayerSettings.SplashScreen.overlayOpacity = 0f;
        AssetDatabase.SaveAssets();
    }

    static Sprite ImportSplashSprite()
    {
        AssetDatabase.ImportAsset(SplashScreenPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(SplashScreenPath) as TextureImporter;
        if (importer == null) throw new System.Exception($"Splash screen image not found: {SplashScreenPath}");
        if (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }
        var splash = AssetDatabase.LoadAssetAtPath<Sprite>(SplashScreenPath);
        if (splash == null) throw new System.Exception($"Splash screen sprite not found: {SplashScreenPath}");
        return splash;
    }
}
