#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Gugarhythm.Editor
{
    public static class ChartVaultIosPostBuild
    {
        const string AppCallbackScheme = "com.luecat.gugarhythm";

        [PostProcessBuild(100)]
        public static void ConfigureChartVault(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var projectPath = PBXProject.GetPBXProjectPath(path);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);
            project.AddFrameworkToProject(project.GetUnityMainTargetGuid(), "Security.framework", false);
            project.WriteToFile(projectPath);

            var plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            EnsureCallbackScheme(plist);
            // Exposes persistentDataPath (== Documents on iOS, where the
            // performance JSONL logs land) over USB file sharing / AFC, so
            // logs can be pulled straight off the device (Finder's Files
            // tab, or `afcclient --documents com.luecat.gugarhythm`) instead
            // of needing a full idevicebackup2 backup each time. See
            // perf-judgment-plan-2026-09-07.md section 9.8.
            plist.root.SetBoolean("UIFileSharingEnabled", true);
            plist.root.SetBoolean("LSSupportsOpeningDocumentsInPlace", true);
            plist.WriteToFile(plistPath);
        }

        static void EnsureCallbackScheme(PlistDocument plist)
        {
            var root = plist.root;
            var urlTypes = root.values.TryGetValue("CFBundleURLTypes", out var existingTypes)
                ? existingTypes.AsArray()
                : root.CreateArray("CFBundleURLTypes");
            PlistElementArray firstSchemes = null;
            foreach (var typeElement in urlTypes.values)
            {
                var type = typeElement.AsDict();
                if (!type.values.TryGetValue("CFBundleURLSchemes", out var existingSchemes)) continue;
                var schemes = existingSchemes.AsArray();
                firstSchemes ??= schemes;
                foreach (var schemeElement in schemes.values)
                    if (schemeElement.AsString() == AppCallbackScheme)
                        return;
            }

            if (firstSchemes != null)
            {
                firstSchemes.AddString(AppCallbackScheme);
                return;
            }

            var callbackType = urlTypes.AddDict();
            callbackType.SetString("CFBundleURLName", AppCallbackScheme);
            callbackType.CreateArray("CFBundleURLSchemes").AddString(AppCallbackScheme);
        }
    }
}
#endif
