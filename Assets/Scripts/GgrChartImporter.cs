using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gugarhythm
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
                chart.BgmBytes = NormalizeWavLengths(package.AudioName, package.AudioBytes);
                chart.BgmExtension = EffectiveAudioExtension(package.AudioName, package.AudioBytes);
                chart.BgmOffset += FiniteNumber(manifest["offset"]);
                if (manifest["title"]?.Type == JTokenType.String) chart.Title = (string)manifest["title"];
                if (manifest["artist"]?.Type == JTokenType.String) chart.Artist = (string)manifest["artist"];
                if (manifest["author"]?.Type == JTokenType.String) chart.Author = (string)manifest["author"];
                if (manifest["metadata"] != null && package.MetadataBytes == null)
                    chart.Warnings.Add(MissingMetadataWarning);
                else if (package.MetadataBytes != null)
                {
                    try { ApplyMetadata(chart, JObject.Parse(Utf8.GetString(package.MetadataBytes))); }
                    catch (Exception) { chart.Warnings.Add(InvalidMetadataWarning); }
                }
                if (manifest["cover"] != null && package.CoverBytes == null)
                    chart.Warnings.Add(MissingCoverWarning);
                else if (package.CoverBytes != null)
                {
                    var texture = DecodeCoverTexture(package.CoverBytes, true);
                    if (texture != null) chart.CoverBytes = package.CoverBytes;
                    else chart.Warnings.Add(InvalidCoverWarning);
                    UnityEngine.Object.Destroy(texture);
                }
                return ImportResult.Ok(chart);
            }
            catch (GgrPackageException exception) { return ImportResult.Fail(exception.Message); }
            catch (Exception) { return ImportResult.Fail(Unsupported); }
        }

        internal static Texture2D DecodeCoverTexture(byte[] bytes, bool markNonReadable)
        {
            if (bytes == null || bytes.Length == 0) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, bytes, markNonReadable)) return texture;
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        static JObject ParseManifest(byte[] bytes)
        {
            try { return JObject.Parse(Utf8.GetString(bytes)); }
            catch (Exception) { throw new GgrPackageException(Unsupported); }
        }

        static void ApplyMetadata(RuntimeChart chart, JObject metadata)
        {
            chart.Title = ReadText(metadata, false, "title", "name") ?? chart.Title;
            chart.Artist = ReadText(metadata, false, "artist", "composer") ?? chart.Artist;
            chart.Author = ReadText(metadata, false, "author", "charter", "chartAuthor") ?? chart.Author;
            chart.DifficultyName = ReadText(metadata, false, "difficulty", "difficultyName") ?? chart.DifficultyName;
            chart.DifficultyLevel = ReadText(metadata, true, "level", "rating", "difficultyLevel") ?? chart.DifficultyLevel;
        }

        static string ReadText(JObject metadata, bool allowNumbers, params string[] names)
        {
            foreach (var name in names)
            {
                var token = metadata[name];
                if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined) continue;
                if (token.Type != JTokenType.String && (!allowNumbers || token.Type is not (JTokenType.Integer or JTokenType.Float))) continue;
                var value = token.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return null;
        }

        const string CurrentPackageFormat = "gugarhythm-package";
        static readonly string LegacyPackageFormat = "guga" + "rythm-package";

        static bool IsVersionOne(JObject manifest) =>
            TryGetString(manifest, "format", out var format) &&
            (format == CurrentPackageFormat || format == LegacyPackageFormat) &&
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

        static byte[] NormalizeWavLengths(string archiveName, byte[] audioBytes)
        {
            if (!string.Equals(Path.GetExtension(archiveName), ".wav", StringComparison.OrdinalIgnoreCase) ||
                audioBytes?.Length < 20 || audioBytes[0] != (byte)'R' || audioBytes[1] != (byte)'I' ||
                audioBytes[2] != (byte)'F' || audioBytes[3] != (byte)'F' || audioBytes[8] != (byte)'W' ||
                audioBytes[9] != (byte)'A' || audioBytes[10] != (byte)'V' || audioBytes[11] != (byte)'E')
                return audioBytes;

            var normalized = (byte[])audioBytes.Clone();
            var changed = false;
            if (IsUnknownLength(normalized, 4))
            {
                WriteU32(normalized, 4, (uint)(normalized.Length - 8));
                changed = true;
            }
            for (var offset = 12; offset + 8 <= normalized.Length;)
            {
                var size = ReadU32(normalized, offset + 4);
                if (normalized[offset] == (byte)'d' && normalized[offset + 1] == (byte)'a' &&
                    normalized[offset + 2] == (byte)'t' && normalized[offset + 3] == (byte)'a')
                {
                    if (IsUnknownLength(normalized, offset + 4))
                    {
                        WriteU32(normalized, offset + 4, (uint)(normalized.Length - offset - 8));
                        changed = true;
                    }
                    break;
                }
                var next = (long)offset + 8 + size + (size & 1);
                if (next > normalized.Length) return audioBytes;
                offset = (int)next;
            }
            return changed ? normalized : audioBytes;
        }

        static bool IsUnknownLength(byte[] bytes, int offset) => ReadU32(bytes, offset) == uint.MaxValue;

        static uint ReadU32(byte[] bytes, int offset) =>
            (uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24);

        static void WriteU32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }

        static double FiniteNumber(JToken token)
        {
            if (token?.Type is not (JTokenType.Float or JTokenType.Integer)) return 0;
            var value = (double)token;
            return double.IsFinite(value) ? value : 0;
        }
    }
}
