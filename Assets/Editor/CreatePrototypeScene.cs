using Gugarhythm;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.SceneManagement;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

public static class CreatePrototypeScene
{
    const string LibraryScenePath = "Assets/Scenes/LibraryScene.unity";
    const string StartupScenePath = "Assets/Scenes/StartupScene.unity";
    const string SettingsScenePath = "Assets/Scenes/SettingsScene.unity";
    const string ChartEditorScenePath = "Assets/Scenes/ChartEditorScene.unity";
    const string GameplayScenePath = "Assets/Scenes/RhythmPrototype.unity";
    const string ApplicationIconPath = "Assets/Art/AppIcon/gugarhythm-icon.png";
    const string SplashScreenPath = "Assets/Art/SplashScreen/gugarhythm-splash.png";
    const string BuildIdentityPath = "Assets/Resources/BuildIdentity.txt";

    [MenuItem("GUGArhythm/Open Rhythm Prototype")]
    public static void Open()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Single);
    }

    [MenuItem("GUGArhythm/Open Chart Library")]
    public static void OpenLibrary()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(LibraryScenePath, OpenSceneMode.Single);
    }

    [MenuItem("GUGArhythm/Open Settings")]
    public static void OpenSettings()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(SettingsScenePath, OpenSceneMode.Single);
    }

    [MenuItem("GUGArhythm/Open Chart Editor")]
    public static void OpenChartEditor()
    {
        EnsurePlayerScenes();
        EditorSceneManager.OpenScene(ChartEditorScenePath, OpenSceneMode.Single);
    }

    static void EnsurePlayerScenes()
    {
        if (!File.Exists(StartupScenePath) || !File.Exists(LibraryScenePath) || !File.Exists(SettingsScenePath) || !File.Exists(ChartEditorScenePath) || !File.Exists(GameplayScenePath)) Build();
    }

    [MenuItem("GUGArhythm/Rebuild Player Scenes")]
    public static void Build()
    {
        var splash = ImportSplashSprite();
        BuildStartupScene(splash);
        BuildScene(LibraryScenePath, "GUGArhythm Library");
        BuildScene(SettingsScenePath, "GUGArhythm Settings");
        BuildScene(ChartEditorScenePath, "GUGArhythm Chart Editor");
        BuildScene(GameplayScenePath, "GUGArhythm Prototype");
        EditorBuildSettings.scenes = PlayerScenes();
        AssetDatabase.SaveAssets();
    }

    static void BuildScene(string path, string rootName)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject(rootName);
        root.AddComponent<GugarhythmLandscapePrototype>();
        EditorSceneManager.SaveScene(scene, path);
    }

    static void BuildStartupScene(Sprite splash)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("GUGArhythm Startup");
        var startup = root.AddComponent<GugarhythmStartupSplash>();
        startup.Configure(splash, GugarhythmStartupSplash.DefaultDisplaySeconds);
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

    [MenuItem("GUGArhythm/Build Android Debug APK")]
    public static void BuildAndroidDebug()
    {
        EnsurePlayerScenes();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        WriteBuildIdentity();
        ConfigureApplicationIcon();
        ConfigureSplashScreen();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.luecat.gugarhythm");
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.productName = "GUGArhythm";
        ConfigureLandscapeAutorotation();
        var directory = "Builds";
        Directory.CreateDirectory(directory);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = PlayerBuildScenePaths(),
            locationPathName = Path.Combine(directory, "GUGArhythm-debug.apk"),
            target = BuildTarget.Android,
            options = BuildOptions.Development,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new System.Exception($"Android build failed: {report.summary.result}");
    }

    [MenuItem("GUGArhythm/Build iOS Development")]
    public static void BuildIosDevelopment()
    {
        EnsurePlayerScenes();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
        WriteBuildIdentity();
        ConfigureApplicationIcon();
        ConfigureSplashScreen();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.luecat.gugarhythm");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.productName = "GUGArhythm";
        ConfigureLandscapeAutorotation();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = PlayerBuildScenePaths(),
            locationPathName = Path.Combine("Builds", "iOS"),
            target = BuildTarget.iOS,
            options = BuildOptions.Development | BuildOptions.AllowDebugging,
        });
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception($"iOS development build failed: {report.summary.result}");
    }

    static void WriteBuildIdentity()
    {
        var revision = Environment.GetEnvironmentVariable("GUGARYTHM_SOURCE_REVISION");
        if (string.IsNullOrWhiteSpace(revision)) revision = SourceRevision();
        var contents = string.Join("\n", PlayerSettings.bundleVersion, PlayerSettings.iOS.buildNumber, revision) + "\n";
        File.WriteAllText(BuildIdentityPath, contents);
        AssetDatabase.ImportAsset(BuildIdentityPath, ImportAssetOptions.ForceSynchronousImport);
    }

    static string SourceRevision()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse --short HEAD",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null) return "unknown";
            var revision = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || string.IsNullOrEmpty(revision)) return "unknown";
            return HasWorkingTreeChanges() ? $"{revision}-dirty" : revision;
        }
        catch { return "unknown"; }
    }

    static bool HasWorkingTreeChanges()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "status --porcelain",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null) return true;
            var changes = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode != 0 || !string.IsNullOrWhiteSpace(changes);
        }
        catch { return true; }
    }

    static void ConfigureApplicationIcon()
    {
        AssetDatabase.ImportAsset(ApplicationIconPath, ImportAssetOptions.ForceSynchronousImport);
        var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(ApplicationIconPath);
        if (icon == null) throw new System.Exception($"Application icon not found: {ApplicationIconPath}");
        var iconSizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Android, IconKind.Application);
        var icons = new Texture2D[iconSizes.Length];
        for (var index = 0; index < icons.Length; index++) icons[index] = icon;
        PlayerSettings.SetIcons(NamedBuildTarget.Android, icons, IconKind.Application);
        ConfigureAndroidPlatformIcons(icon);
        ConfigureIosPlatformIcons(icon);
        AssetDatabase.SaveAssets();
    }

    static void ConfigureIosPlatformIcons(Texture2D icon)
    {
        foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.iOS))
        {
            var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.iOS, kind);
            for (var index = 0; index < slots.Length; index++)
            {
                var textures = new Texture2D[slots[index].maxLayerCount];
                for (var layer = 0; layer < textures.Length; layer++) textures[layer] = icon;
                slots[index].SetTextures(textures);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.iOS, kind, slots);
        }
    }

    static void ConfigureLandscapeAutorotation()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
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
