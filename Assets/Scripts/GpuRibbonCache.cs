using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Versioned, chart-addressed storage for immutable ribbon meshes.  The
    // first load performs all sampling; later loads only deserialize vertices.
    public static class GpuRibbonCache
    {
        const int Magic = 0x47525055; // UPRG
        const int FormatVersion = 3;
        const int BuildKeyVersion = 2;
        const int MaximumChunkCount = 4096;
        const int MaximumVertexCount = 8_000_000;
        const int MaximumIndexCount = 24_000_000;
        static readonly Dictionary<string, WeakReference<GpuRibbonBuildResult>> Memory = new();

        public static string ComputeKey(RuntimeChart chart)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(BuildKeyVersion);
                writer.Write(FormatVersion);
                WriteString(writer, chart.DefaultTimeScaleGroup);
                var values = new List<double>();
                foreach (var pair in chart.TimeScaleGroups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    WriteString(writer, pair.Key);
                    WriteString(writer, pair.Value?.SourceId);
                    values.Clear();
                    pair.Value?.AppendCacheFingerprintValues(values);
                    writer.Write(values.Count);
                    foreach (var value in values) writer.Write(value);
                }
                writer.Write(-1);

                writer.Write(chart.Guides.Count);
                foreach (var guide in chart.Guides)
                {
                    writer.Write(guide != null);
                    if (guide == null) continue;
                    WriteGuidePoint(writer, guide.Start);
                    WriteGuidePoint(writer, guide.Head);
                    WriteGuidePoint(writer, guide.Tail);
                    WriteGuidePoint(writer, guide.End);
                    writer.Write(guide.Color);
                    writer.Write(guide.Ease);
                    writer.Write(guide.HeadOpacity);
                    writer.Write(guide.TailOpacity);
                }

                writer.Write(chart.HoldPaths.Count);
                foreach (var path in chart.HoldPaths)
                {
                    writer.Write(path != null);
                    if (path == null) continue;
                    writer.Write(path.RootIndex);
                    writer.Write(path.Segments.Count);
                    foreach (var segment in path.Segments)
                    {
                        WriteNoteGeometry(writer, segment.Start);
                        WriteNoteGeometry(writer, segment.End);
                        writer.Write(segment.Ease);
                        writer.Write(segment.Critical);
                    }
                    writer.Write(path.RenderRuns.Count);
                    foreach (var run in path.RenderRuns)
                    {
                        writer.Write(run.FirstSegmentIndex);
                        writer.Write(run.LastSegmentIndex);
                        writer.Write(run.Critical);
                    }
                }

                writer.Write(chart.FallbackConnectors.Count);
                foreach (var connector in chart.FallbackConnectors)
                {
                    writer.Write(connector != null);
                    if (connector == null) continue;
                    WriteNoteGeometry(writer, connector.Start);
                    WriteNoteGeometry(writer, connector.End);
                    writer.Write(connector.Ease);
                    writer.Write(connector.Critical);
                }
            }
            stream.Position = 0;
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        public static GpuRibbonBuildResult LoadOrBuild(RuntimeChart chart,
            IReadOnlyDictionary<RuntimeGuide, GuideRenderCache> guideCaches, out bool cacheHit)
        {
            var key = ComputeKey(chart);
            if (Memory.TryGetValue(key, out var weak) && weak.TryGetTarget(out var memoryResult))
            {
                cacheHit = true;
                return memoryResult;
            }

            var directory = Path.Combine(Application.persistentDataPath, "GpuRibbonCache");
            var path = Path.Combine(directory, key + ".bin");
            if (TryRead(path, key, out var diskResult))
            {
                Memory[key] = new WeakReference<GpuRibbonBuildResult>(diskResult);
                cacheHit = true;
                return diskResult;
            }

            var result = GpuRibbonMeshBuilder.Build(chart, guideCaches);
            Memory[key] = new WeakReference<GpuRibbonBuildResult>(result);
            cacheHit = false;
            string temporary = null;
            try
            {
                Directory.CreateDirectory(directory);
                temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
                Write(temporary, key, result);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                temporary = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               NotSupportedException)
            {
                Debug.LogWarning("GPU ribbon cache write skipped: " + exception.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(temporary))
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception) { /* A stale temp cache is harmless. */ }
                }
            }
            return result;
        }

        public static void Write(string path, string key, GpuRibbonBuildResult result)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Cache path is required.", nameof(path));
            if (result == null) throw new ArgumentNullException(nameof(result));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
            writer.Write(Magic);
            writer.Write(FormatVersion);
            WriteString(writer, key);
            writer.Write(result.GuidePathCount);
            writer.Write(result.HoldPathCount);
            writer.Write(result.VertexCount);
            writer.Write(result.GroupNames.Count);
            foreach (var group in result.GroupNames) WriteString(writer, group);
            writer.Write(result.HoldRootStates.Count);
            foreach (var pair in result.HoldRootStates.OrderBy(pair => pair.Value))
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value);
            }
            writer.Write(result.Chunks.Count);
            foreach (var chunk in result.Chunks)
            {
                writer.Write((byte)chunk.Kind);
                writer.Write(chunk.Vertices.Length);
                foreach (var vertex in chunk.Vertices)
                {
                    writer.Write(vertex.position.x);
                    writer.Write(vertex.position.y);
                    writer.Write(vertex.position.z);
                    writer.Write(vertex.color.r);
                    writer.Write(vertex.color.g);
                    writer.Write(vertex.color.b);
                    writer.Write(vertex.color.a);
                    writer.Write(vertex.uv0.x);
                    writer.Write(vertex.uv0.y);
                }
                writer.Write(chunk.Indices.Length);
                foreach (var index in chunk.Indices) writer.Write(index);
            }
        }

        public static bool TryRead(string path, string expectedKey, out GpuRibbonBuildResult result)
        {
            result = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new BinaryReader(stream, Encoding.UTF8, false);
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != FormatVersion ||
                    !string.Equals(ReadString(reader), expectedKey, StringComparison.Ordinal)) return false;
                var restored = new GpuRibbonBuildResult
                {
                    GuidePathCount = ReadCount(reader, MaximumVertexCount),
                    HoldPathCount = ReadCount(reader, MaximumVertexCount),
                    VertexCount = ReadCount(reader, MaximumVertexCount),
                };
                var groupCount = ReadCount(reader, 65535);
                for (var index = 0; index < groupCount; index++) restored.GroupNames.Add(ReadString(reader));
                var rootCount = ReadCount(reader, 65535);
                for (var index = 0; index < rootCount; index++)
                    restored.HoldRootStates.Add(reader.ReadInt32(), reader.ReadInt32());
                var chunkCount = ReadCount(reader, MaximumChunkCount);
                var actualVertices = 0;
                for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    var kind = (GpuRibbonKind)reader.ReadByte();
                    if (!Enum.IsDefined(typeof(GpuRibbonKind), kind)) return false;
                    var vertexCount = ReadCount(reader, MaximumVertexCount);
                    actualVertices += vertexCount;
                    if (actualVertices > MaximumVertexCount) return false;
                    if (vertexCount == 0 || vertexCount % 2 != 0) return false;
                    var vertices = new UIVertex[vertexCount];
                    for (var vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                    {
                        var vertex = UIVertex.simpleVert;
                        vertex.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        vertex.color = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                        vertex.uv0 = new Vector4(reader.ReadSingle(), reader.ReadSingle(), 0, 0);
                        vertex.uv1 = vertex.uv2 = vertex.uv3 = Vector4.zero;
                        vertices[vertexIndex] = vertex;
                    }
                    var indexCount = ReadCount(reader, MaximumIndexCount);
                    if (indexCount == 0 || indexCount % 6 != 0) return false;
                    var indices = new int[indexCount];
                    for (var index = 0; index < indexCount; index++)
                    {
                        indices[index] = reader.ReadInt32();
                        if (indices[index] < 0 || indices[index] >= vertexCount) return false;
                    }
                    restored.Chunks.Add(new GpuRibbonChunkData(kind, vertices, indices));
                }
                if (actualVertices != restored.VertexCount || stream.Position != stream.Length) return false;
                result = restored;
                return true;
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or
                                               InvalidDataException or NotSupportedException or OverflowException)
            {
                return false;
            }
        }

        static int ReadCount(BinaryReader reader, int maximum)
        {
            var value = reader.ReadInt32();
            if (value < 0 || value > maximum) throw new InvalidDataException("GPU ribbon cache count is invalid.");
            return value;
        }

        static void WriteString(BinaryWriter writer, string value) => writer.Write(value ?? string.Empty);
        static string ReadString(BinaryReader reader) => reader.ReadString();

        static void WriteGuidePoint(BinaryWriter writer, RuntimeGuidePoint point)
        {
            writer.Write(point.Time);
            writer.Write(point.Lane);
            writer.Write(point.Size);
            WriteString(writer, point.TimeScaleGroup);
        }

        static void WriteNoteGeometry(BinaryWriter writer, RuntimeNote note)
        {
            writer.Write(note != null);
            if (note == null) return;
            writer.Write(note.Index);
            writer.Write(note.Time);
            writer.Write(note.Lane);
            writer.Write(note.Size);
            writer.Write(note.HoldRootIndex);
            WriteString(writer, note.TimeScaleGroup);
        }
    }
}
