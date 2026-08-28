using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Gugarhythm
{
    public static class InputDiagnosticsSession
    {
        public const string DebugEntryId = "gugarhythm-input-diagnostics";
        const int MaximumRecords = 4096;
        const int RecentLineCount = 10;
        const string LastReportPathPreferenceKey = "gugarhythm-input-diagnostics-last-report";

        [Serializable]
        sealed class DiagnosticRecord
        {
            public string type;
            public double realtime;
            public double songTime;
            public int fingerId;
            public string phase;
            public string inputKind;
            public float lane;
            public bool inInputBand;
            public double queueMilliseconds;
            public string disposition;
            public int noteIndex = -1;
            public string grade;
            public double deltaMilliseconds;
            public string detail;
        }

        static readonly List<DiagnosticRecord> records = new(MaximumRecords);
        static readonly Queue<string> recentLines = new(RecentLineCount);
        static LocalChartEntry previousEntry;
        static byte[] previousBytes;
        static DateTime startedUtc;
        static double queueTotalMilliseconds;
        static double queueMaximumMilliseconds;
        static int queueSampleCount;
        static int callbackCount;
        static int processedCount;
        static int tokenCount;
        static int matchedCount;
        static int protectionBlockedCount;
        static int unmatchedCount;
        static int judgmentCount;
        static int hitFeedbackCount;
        static int missCount;

        public static bool Armed { get; private set; }
        public static bool CaptureActive { get; private set; }
        public static bool JudgmentProtectionEnabled { get; private set; } = true;
        public static string LastReportPath { get; private set; } = string.Empty;

        public static bool IsDebugEntry(LocalChartEntry entry) => entry != null && IsDebugEntry(entry.Id);
        public static bool IsDebugEntry(string entryId) => string.Equals(entryId, DebugEntryId, StringComparison.Ordinal);

        public static void Arm(bool judgmentProtectionEnabled, LocalChartEntry selectedEntry, byte[] selectedBytes)
        {
            EndRun("rearmed");
            previousEntry = selectedEntry;
            previousBytes = CopyBytes(selectedBytes);
            JudgmentProtectionEnabled = judgmentProtectionEnabled;
            Armed = true;
            CaptureActive = false;
            ResetCounters();
        }

        public static void BeginRun()
        {
            if (!Armed || CaptureActive) return;
            ResetCounters();
            startedUtc = DateTime.UtcNow;
            CaptureActive = true;
            Add(new DiagnosticRecord
            {
                type = "session_start",
                realtime = Time.realtimeSinceStartupAsDouble,
                detail = JudgmentProtectionEnabled ? "judgment_protection=on" : "judgment_protection=off",
            }, JudgmentProtectionEnabled ? "SESSION  Protection ON" : "SESSION  Protection OFF");
        }

        public static void RecordTouchQueued(int fingerId, double inputTime, Vector2 screenPosition,
            UnityEngine.InputSystem.TouchPhase phase)
        {
            if (!CaptureActive) return;
            callbackCount++;
            Add(new DiagnosticRecord
            {
                type = "touch_callback",
                realtime = Time.realtimeSinceStartupAsDouble,
                fingerId = fingerId,
                phase = phase.ToString(),
                queueMilliseconds = Math.Max(0d, (InputState.currentTime - inputTime) * 1000d),
                detail = $"screen=({screenPosition.x:F1},{screenPosition.y:F1})",
            }, $"TOUCH #{fingerId} {phase}");
        }

        public static void RecordTouchProcessed(int fingerId, double songTime, float lane, bool inInputBand,
            double queueMilliseconds, int emittedTokenCount)
        {
            if (!CaptureActive) return;
            processedCount++;
            queueSampleCount++;
            queueTotalMilliseconds += queueMilliseconds;
            queueMaximumMilliseconds = Math.Max(queueMaximumMilliseconds, queueMilliseconds);
            Add(new DiagnosticRecord
            {
                type = "touch_processed",
                realtime = Time.realtimeSinceStartupAsDouble,
                songTime = songTime,
                fingerId = fingerId,
                lane = lane,
                inInputBand = inInputBand,
                queueMilliseconds = queueMilliseconds,
                detail = $"tokens={emittedTokenCount}",
            }, $"MAP   #{fingerId} lane {lane:F2} {(inInputBand ? "IN" : "OUT")} +{emittedTokenCount}");
        }

        public static void RecordToken(InputToken input)
        {
            if (!CaptureActive) return;
            tokenCount++;
            Add(new DiagnosticRecord
            {
                type = "input_token",
                realtime = Time.realtimeSinceStartupAsDouble,
                songTime = input.Time,
                fingerId = input.FingerId,
                inputKind = input.Kind.ToString(),
                lane = input.Lane,
            }, $"TOKEN #{input.FingerId} {input.Kind} lane {input.Lane:F2}");
        }

        public static void RecordDecision(JudgmentInputDiagnostic diagnostic)
        {
            if (!CaptureActive) return;
            switch (diagnostic.Disposition)
            {
                case JudgmentInputDisposition.Matched: matchedCount++; break;
                case JudgmentInputDisposition.ProtectionBlocked: protectionBlockedCount++; break;
                default: unmatchedCount++; break;
            }
            Add(new DiagnosticRecord
            {
                type = "judgment_decision",
                realtime = Time.realtimeSinceStartupAsDouble,
                songTime = diagnostic.EventTime,
                fingerId = diagnostic.Input.FingerId,
                inputKind = diagnostic.Input.Kind.ToString(),
                lane = diagnostic.Input.Lane,
                disposition = diagnostic.Disposition.ToString(),
                noteIndex = diagnostic.Note?.Index ?? -1,
                grade = diagnostic.CandidateGrade.ToString(),
                deltaMilliseconds = double.IsNaN(diagnostic.Delta) ? 0d : diagnostic.Delta * 1000d,
            }, $"JUDGE {diagnostic.Disposition} note {diagnostic.Note?.Index ?? -1}");
        }

        public static void RecordJudgment(JudgmentEvent judgment)
        {
            if (!CaptureActive) return;
            judgmentCount++;
            if (judgment.Grade == JudgmentGrade.Miss) missCount++;
            Add(new DiagnosticRecord
            {
                type = "judgment_event",
                realtime = Time.realtimeSinceStartupAsDouble,
                songTime = judgment.Note?.Time ?? 0d,
                noteIndex = judgment.Note?.Index ?? -1,
                grade = judgment.Grade.ToString(),
                deltaMilliseconds = judgment.Delta * 1000d,
            }, $"EVENT {judgment.Grade} note {judgment.Note?.Index ?? -1}");
        }

        public static void RecordHitFeedback(JudgmentEvent judgment, bool particleRequested)
        {
            if (!CaptureActive) return;
            hitFeedbackCount++;
            Add(new DiagnosticRecord
            {
                type = "hit_feedback",
                realtime = Time.realtimeSinceStartupAsDouble,
                noteIndex = judgment.Note?.Index ?? -1,
                grade = judgment.Grade.ToString(),
                detail = particleRequested ? "judgment_ui,sound,particle" : "judgment_ui,sound",
            }, $"FX    {judgment.Grade} note {judgment.Note?.Index ?? -1}");
        }

        public static string EndRun(string reason)
        {
            if (!CaptureActive) return LastReportPath;
            Add(new DiagnosticRecord
            {
                type = "session_end",
                realtime = Time.realtimeSinceStartupAsDouble,
                detail = reason ?? string.Empty,
            }, $"END   {reason}");
            CaptureActive = false;
            LastReportPath = Export(reason);
            if (!string.IsNullOrEmpty(LastReportPath))
            {
                PlayerPrefs.SetString(LastReportPathPreferenceKey, LastReportPath);
                PlayerPrefs.Save();
            }
            return LastReportPath;
        }

        public static void RestorePreviousSelectionAndDisarm()
        {
            var selection = ChartSelectionSession.Ensure();
            if (previousEntry != null && previousBytes is { Length: > 0 })
                selection.SetSelection(previousEntry, previousBytes);
            else selection.Clear();
            previousEntry = null;
            previousBytes = null;
            Armed = false;
        }

        public static string EndRunRestoreAndDisarm(string reason)
        {
            var path = EndRun(reason);
            RestorePreviousSelectionAndDisarm();
            return path;
        }

        public static string BuildOverlayText()
        {
            var average = queueSampleCount == 0 ? 0d : queueTotalMilliseconds / queueSampleCount;
            var builder = new StringBuilder(768);
            builder.Append("INPUT DIAGNOSTICS  Protection ")
                .Append(JudgmentProtectionEnabled ? "ON" : "OFF")
                .Append("\nTouch ").Append(callbackCount)
                .Append("  Processed ").Append(processedCount)
                .Append("  Token ").Append(tokenCount)
                .Append("\nMatched ").Append(matchedCount)
                .Append("  Blocked ").Append(protectionBlockedCount)
                .Append("  Unmatched ").Append(unmatchedCount)
                .Append("\nEvent ").Append(judgmentCount)
                .Append("  Feedback ").Append(hitFeedbackCount)
                .Append("  Miss ").Append(missCount)
                .Append("\nQueue ms avg ").Append(average.ToString("F2", CultureInfo.InvariantCulture))
                .Append("  max ").Append(queueMaximumMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
            foreach (var line in recentLines) builder.Append('\n').Append(line);
            return builder.ToString();
        }

        public static bool TryReadLastReport(out string report, out string path)
        {
            path = LastReportPath;
            if (string.IsNullOrEmpty(path)) path = PlayerPrefs.GetString(LastReportPathPreferenceKey, string.Empty);
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    report = File.ReadAllText(path);
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("無法讀取輸入診斷報告：" + exception.Message);
            }
            report = string.Empty;
            return false;
        }

        public static bool ClearLastReport()
        {
            var path = LastReportPath;
            if (string.IsNullOrEmpty(path)) path = PlayerPrefs.GetString(LastReportPathPreferenceKey, string.Empty);
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
                LastReportPath = string.Empty;
                PlayerPrefs.DeleteKey(LastReportPathPreferenceKey);
                PlayerPrefs.Save();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("無法刪除輸入診斷報告：" + exception.Message);
                return false;
            }
        }

        static string Export(string reason)
        {
            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "InputDiagnostics");
                Directory.CreateDirectory(directory);
                var fileName = $"input-{startedUtc:yyyyMMdd-HHmmss}-{Sanitize(reason)}.jsonl";
                var path = Path.Combine(directory, fileName);
                using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
                for (var index = 0; index < records.Count; index++)
                    writer.WriteLine(JsonConvert.SerializeObject(records[index], Formatting.None));
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogError("輸入診斷報告寫入失敗：" + exception.Message);
                return string.Empty;
            }
        }

        static void Add(DiagnosticRecord record, string recent)
        {
            if (records.Count < MaximumRecords) records.Add(record);
            if (recentLines.Count >= RecentLineCount) recentLines.Dequeue();
            recentLines.Enqueue(recent);
        }

        static void ResetCounters()
        {
            records.Clear();
            recentLines.Clear();
            queueTotalMilliseconds = 0d;
            queueMaximumMilliseconds = 0d;
            queueSampleCount = 0;
            callbackCount = 0;
            processedCount = 0;
            tokenCount = 0;
            matchedCount = 0;
            protectionBlockedCount = 0;
            unmatchedCount = 0;
            judgmentCount = 0;
            hitFeedbackCount = 0;
            missCount = 0;
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "ended";
            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
                builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
            return builder.ToString().Trim('-');
        }

        static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0) return null;
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
