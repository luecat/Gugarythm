using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gugarythm
{
    public sealed class GgrChartImporter : IChartImporter
    {
        const string Unsupported = "不支援的 GGR 格式或版本。";
        const string MissingResources = "GGR 缺少 USC 譜面或音樂。";
        const string InvalidChart = "GGR 的 USC 譜面無效。";
        const string InvalidMetadataWarning = "GGR metadata.json 無效，已忽略。";
        const string MissingMetadataWarning = "GGR metadata.json 缺少，已忽略。";
        const string InvalidCoverWarning = "GGR cover 無法解碼，已使用預設封面。";
        const string MissingCoverWarning = "GGR cover 缺少，已使用預設封面。";
        static readonly UTF8Encoding Utf8 = new(false, true);

        public bool CanImport(string fileName, byte[] header) => fileName.EndsWith(".ggr", StringComparison.OrdinalIgnoreCase);

        public ImportResult Import(string fileName, byte[] data, System.Collections.Generic.IReadOnlyDictionary<string, byte[]> companionFiles = null)
        {
            try
            {
                var package = GgrPackageReader.Read(data);
                var manifest = ParseManifest(package.ManifestBytes);
                if (!IsVersionOne(manifest))
                    return ImportResult.Fail(Unsupported);

                if (!manifest.TryGetValue("chart", StringComparison.Ordinal, out _) ||
                    !manifest.TryGetValue("audio", StringComparison.Ordinal, out _))
                    return ImportResult.Fail(MissingResources);
                if (!TryGetString(manifest, "chart", out var chartName) || !TryGetString(manifest, "audio", out var audioName))
                    return ImportResult.Fail(Unsupported);
                if (chartName != "chart.usc" || !IsAllowedAudioName(audioName) ||
                    (package.AudioName != null && audioName != package.AudioName))
                    return ImportResult.Fail(Unsupported);
                if (package.ChartBytes == null || package.AudioName == null || package.AudioBytes == null)
                    return ImportResult.Fail(MissingResources);
                if (!HasValidOptionalName(manifest, "metadata", "metadata.json") ||
                    !HasValidCoverName(manifest, package.CoverName))
                    return ImportResult.Fail(Unsupported);

                var usc = new UscChartImporter().Import("chart.usc", package.ChartBytes);
                if (!usc.Success) return ImportResult.Fail(InvalidChart);
                var chart = usc.Chart;
                chart.SourceFormat = "GGR";
                chart.BgmBytes = package.AudioBytes;
                chart.BgmExtension = EffectiveAudioExtension(package.AudioName, package.AudioBytes);
                chart.BgmOffset += FiniteNumber(manifest["offset"]);
                if (manifest["title"]?.Type == JTokenType.String) chart.Title = (string)manifest["title"];
                if (manifest["artist"]?.Type == JTokenType.String) chart.Artist = (string)manifest["artist"];
                if (manifest["author"]?.Type == JTokenType.String) chart.Author = (string)manifest["author"];
                if (manifest["metadata"] != null && package.MetadataBytes == null)
                    chart.Warnings.Add(MissingMetadataWarning);
                else if (package.MetadataBytes != null)
                {
                    try { JObject.Parse(Utf8.GetString(package.MetadataBytes)); }
                    catch (Exception) { chart.Warnings.Add(InvalidMetadataWarning); }
                }
                if (manifest["cover"] != null && package.CoverBytes == null)
                    chart.Warnings.Add(MissingCoverWarning);
                else if (package.CoverBytes != null)
                {
                    var texture = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(texture, package.CoverBytes, true)) chart.CoverBytes = package.CoverBytes;
                    else chart.Warnings.Add(InvalidCoverWarning);
                    UnityEngine.Object.Destroy(texture);
                }
                return ImportResult.Ok(chart);
            }
            catch (GgrPackageException exception) { return ImportResult.Fail(exception.Message); }
            catch (Exception) { return ImportResult.Fail(Unsupported); }
        }

        static JObject ParseManifest(byte[] bytes)
        {
            try { return JObject.Parse(Utf8.GetString(bytes)); }
            catch (Exception) { throw new GgrPackageException(Unsupported); }
        }

        static bool IsVersionOne(JObject manifest) =>
            TryGetString(manifest, "format", out var format) && format == "gugarythm-package" &&
            manifest["version"]?.Type == JTokenType.Integer && (long)manifest["version"] == 1;

        static bool TryGetString(JObject manifest, string name, out string value)
        {
            value = null;
            if (!manifest.TryGetValue(name, StringComparison.Ordinal, out var token) || token.Type != JTokenType.String) return false;
            value = (string)token;
            return true;
        }

        static bool HasValidOptionalName(JObject manifest, string name, string expected)
        {
            if (!manifest.TryGetValue(name, StringComparison.Ordinal, out var token)) return true;
            return token.Type == JTokenType.String && (string)token == expected;
        }

        static bool HasValidCoverName(JObject manifest, string packageCoverName)
        {
            if (!manifest.TryGetValue("cover", StringComparison.Ordinal, out var token)) return true;
            if (token.Type != JTokenType.String || !IsAllowedCoverName((string)token)) return false;
            return packageCoverName == null || (string)token == packageCoverName;
        }

        static bool IsAllowedAudioName(string name) =>
            name is "audio.mp3" or "audio.ogg" or "audio.wav" or "audio.m4a" or "audio.aac";

        static bool IsAllowedCoverName(string name) =>
            name is "cover.png" or "cover.jpg" or "cover.jpeg" or "cover.webp";

        static string EffectiveAudioExtension(string archiveName, byte[] audioBytes)
        {
            if (audioBytes?.Length >= 8 && audioBytes[4] == (byte)'f' && audioBytes[5] == (byte)'t' &&
                audioBytes[6] == (byte)'y' && audioBytes[7] == (byte)'p')
                return ".m4a";
            return Path.GetExtension(archiveName).ToLowerInvariant();
        }

        static double FiniteNumber(JToken token)
        {
            if (token?.Type is not (JTokenType.Float or JTokenType.Integer)) return 0;
            var value = (double)token;
            return double.IsFinite(value) ? value : 0;
        }
    }
}
