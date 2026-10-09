using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Gugarhythm
{
    /// <summary>
    /// Keeps app storage bounded: removes leftover picker copies, stale-format
    /// ribbon caches, abandoned temp files and old performance logs, then trims
    /// AudioCache + GpuRibbonCache to a size budget (least recently used first).
    /// Runs once per process, before any chart is preloaded.
    /// </summary>
    public static class StorageMaintenance
    {
        public const string ImportStagingFolderName = "GugarhythmImports";
        public const long DerivedCacheBudgetBytes = 512L * 1024 * 1024;
        const int PerformanceLogKeepCount = 10;

        // Files touched this recently are never trimmed: they may belong to the
        // chart that is loading right now, or to a picker result not yet consumed.
        static readonly TimeSpan RecentGrace = TimeSpan.FromMinutes(10);
        static readonly TimeSpan StagingGrace = TimeSpan.FromHours(1);

        static bool ran;

        public static void RunOnce()
        {
            if (ran) return;
            ran = true;
            try
            {
                var persistent = Application.persistentDataPath;
                var now = DateTime.UtcNow;
                foreach (var root in ImportStagingRoots()) CleanImportStaging(root, now);
                var audio = Path.Combine(persistent, "AudioCache");
                var ribbon = Path.Combine(persistent, "GpuRibbonCache");
                DeleteStaleTemporaryFiles(audio, now);
                DeleteLegacySilenceBakedWavs(audio);
                DeleteStaleTemporaryFiles(ribbon, now);
                DeleteStaleRibbonFormats(ribbon);
                TrimToBudget(new[] { audio, ribbon }, DerivedCacheBudgetBytes, now);
                KeepNewest(Path.Combine(persistent, "PerformanceDiagnostics"), "perf-*.jsonl", PerformanceLogKeepCount);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("儲存空間整理失敗：" + exception.Message);
            }
        }

        /// <summary>Marks a cache file as recently used so budget trimming keeps it.</summary>
        public static void Touch(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.SetLastWriteTimeUtc(path, DateTime.UtcNow); }
            catch (Exception) { /* Best effort; worst case the file is trimmed earlier. */ }
        }

        /// <summary>
        /// Deletes a native picker copy once its bytes are in memory. Only paths
        /// inside a GugarhythmImports staging root are ever deleted.
        /// </summary>
        public static void DeleteImportedCopy(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var full = Path.GetFullPath(path);
                foreach (var root in ImportStagingRoots())
                {
                    var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!full.StartsWith(rootFull, StringComparison.Ordinal)) continue;
                    if (File.Exists(full)) File.Delete(full);
                    else if (Directory.Exists(full)) Directory.Delete(full, true);
                    // iOS stages each file in its own UUID folder; drop it once empty.
                    var parent = Path.GetDirectoryName(full);
                    if (!string.IsNullOrEmpty(parent) &&
                        parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar != rootFull &&
                        Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                        Directory.Delete(parent);
                    return;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("無法刪除匯入暫存檔：" + exception.Message);
            }
        }

        static IEnumerable<string> ImportStagingRoots()
        {
            // Android: Activity.getCacheDir() == Application.temporaryCachePath.
            // iOS: NSTemporaryDirectory() == Path.GetTempPath().
            var roots = new List<string>();
            if (!string.IsNullOrEmpty(Application.temporaryCachePath))
                roots.Add(Path.Combine(Application.temporaryCachePath, ImportStagingFolderName));
            try
            {
                var temp = Path.GetTempPath();
                if (!string.IsNullOrEmpty(temp)) roots.Add(Path.Combine(temp, ImportStagingFolderName));
            }
            catch (Exception) { /* No temp path on this platform. */ }
            return roots.Distinct(StringComparer.Ordinal);
        }

        static void CleanImportStaging(string root, DateTime now)
        {
            if (!Directory.Exists(root)) return;
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                try
                {
                    if (now - entry.LastWriteTimeUtc < StagingGrace) continue;
                    if (entry is DirectoryInfo directory) directory.Delete(true);
                    else entry.Delete();
                }
                catch (Exception) { /* Retry next launch. */ }
            }
        }

        static void DeleteStaleTemporaryFiles(string directory, DateTime now)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.tmp-*"))
            {
                try { if (now - file.LastWriteTimeUtc >= StagingGrace) file.Delete(); }
                catch (Exception) { /* Retry next launch. */ }
            }
        }

        // Older builds padded BGM with leading silence and cached it as raw PCM WAV
        // ("<hash>.<ext>.lead<ms>.wav"). Playback now delays PlayScheduled instead.
        static void DeleteLegacySilenceBakedWavs(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.lead*.wav"))
            {
                try { file.Delete(); }
                catch (Exception) { /* Retry next launch. */ }
            }
        }

        static void DeleteStaleRibbonFormats(string directory)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*.bin"))
            {
                try { if (!GpuRibbonCache.HasCurrentFormat(file.FullName)) file.Delete(); }
                catch (Exception) { /* Retry next launch. */ }
            }
        }

        static void TrimToBudget(IEnumerable<string> directories, long budget, DateTime now)
        {
            var files = directories
                .Where(Directory.Exists)
                .SelectMany(directory => new DirectoryInfo(directory).EnumerateFiles())
                .ToList();
            var total = files.Sum(file => file.Length);
            if (total <= budget) return;
            foreach (var file in files.OrderBy(file => file.LastWriteTimeUtc))
            {
                if (total <= budget) break;
                if (now - file.LastWriteTimeUtc < RecentGrace) continue;
                try
                {
                    var length = file.Length;
                    file.Delete();
                    total -= length;
                }
                catch (Exception) { /* Retry next launch. */ }
            }
        }

        static void KeepNewest(string directory, string pattern, int keep)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles(pattern)
                         .OrderByDescending(file => file.LastWriteTimeUtc).Skip(keep))
            {
                try { file.Delete(); }
                catch (Exception) { /* Retry next launch. */ }
            }
        }
    }
}
