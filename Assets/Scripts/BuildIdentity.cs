using System;
using UnityEngine;

namespace Gugarhythm
{
    // The build pipeline writes the three-line resource immediately before a
    // player build.  Keeping it out of gameplay state makes HUD provenance
    // visible without changing timing, judgment, or chart data.
    public static class BuildIdentity
    {
        const string ResourcePath = "BuildIdentity";
        static string display;

        public static string Display => display ??= ReadDisplay();

        static string ReadDisplay()
        {
            var metadata = Resources.Load<TextAsset>(ResourcePath);
            if (metadata == null) return $"BUILD v{Application.version} b? r?";
            var values = metadata.text.Replace("\r", string.Empty).Split('\n');
            var version = ValueAt(values, 0, Application.version);
            var buildNumber = ValueAt(values, 1, "?");
            var revision = ValueAt(values, 2, "?");
            return $"BUILD v{version} b{buildNumber} r{revision}";
        }

        static string ValueAt(string[] values, int index, string fallback) =>
            index >= 0 && index < values.Length && !string.IsNullOrWhiteSpace(values[index]) ? values[index].Trim() : fallback;
    }
}
