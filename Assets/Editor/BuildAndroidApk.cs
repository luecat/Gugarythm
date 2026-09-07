using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Gugarhythm.Editor
{
    public static class BuildAndroidApk
    {
        const string OutputPath = "Builds/GUGArhythm-debug.apk";

        [MenuItem("Build/Build Android Debug APK")]
        public static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            Debug.Log($"[BuildAndroidApk] result={summary.result} totalTime={summary.totalTime} " +
                $"totalSize={summary.totalSize} errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                $"output={summary.outputPath}");
        }
    }
}
