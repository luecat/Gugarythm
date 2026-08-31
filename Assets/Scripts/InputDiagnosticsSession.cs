using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Gugarhythm
{
    public static class InputDiagnosticsSession
    {
        public const string DebugEntryId = "gugarhythm-input-diagnostics";
        const string LastReportPathPreferenceKey = "gugarhythm-input-diagnostics-last-report";
        const string RecordAllChartsPreferenceKey = "gugarhythm-input-diagnostics-record-all-charts";
        const string JudgmentProtectionPreferenceKey = "gugarhythm-input-diagnostics-protection";
        const int WriterFlushInterval = 256;

        [Serializable]
        struct DiagnosticRecord
        {
            public string type;
            public double realtime;
            public double songTime;
            public int fingerId;
            public string phase;
            public string inputKind;
            public string inputSource;
            public float lane;
            public bool inInputBand;
            public double queueMilliseconds;
            public string disposition;
            public int noteIndex;
            public string grade;
            public double deltaMilliseconds;
            public string detail;
        }

        static readonly Queue<DiagnosticRecord> pendingRecords = new();
        static readonly object writerGate = new();
        static LocalChartEntry previousEntry;
        static byte[] previousBytes;
        static StreamWriter reportWriter;
        static Thread reportWriterThread;
        static Exception reportWriterFailure;
        static string activeReportPath = string.Empty;
        static bool reportWriterStopping;
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
        public static bool IsDebugRun { get; private set; }
        public static bool JudgmentProtectionEnabled =>
            PlayerPrefs.GetInt(JudgmentProtectionPreferenceKey, 1) != 0;
        public static string LastReportPath { get; private set; } = string.Empty;
        public static bool RecordAllChartsEnabled =>
            PlayerPrefs.GetInt(RecordAllChartsPreferenceKey, 0) != 0;

        public static bool IsDebugEntry(LocalChartEntry entry) => entry != null && IsDebugEntry(entry.Id);
        public static bool IsDebugEntry(string entryId) => string.Equals(entryId, DebugEntryId, StringComparison.Ordinal);

        public static void SetRecordAllChartsEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(RecordAllChartsPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static void SetJudgmentProtectionEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(JudgmentProtectionPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static bool ShouldCapture(LocalChartEntry entry) => entry != null &&
            ((IsDebugEntry(entry) && Armed) || RecordAllChartsEnabled);

        public static void Arm(LocalChartEntry selectedEntry, byte[] selectedBytes)
        {
            EndRun("rearmed");
            previousEntry = selectedEntry;
            previousBytes = CopyBytes(selectedBytes);
            Armed = true;
            CaptureActive = false;
            IsDebugRun = false;
            ResetCounters();
        }

        public static void BeginRun(LocalChartEntry entry)
        {
            if (CaptureActive || !ShouldCapture(entry)) return;
            ResetCounters();
            startedUtc = DateTime.UtcNow;
            activeReportPath = StartReportStream();
            if (string.IsNullOrEmpty(activeReportPath)) return;
            CaptureActive = true;
            IsDebugRun = IsDebugEntry(entry) && Armed;
            var mode = IsDebugRun ? "debug" : "background";
            var protection = JudgmentProtectionEnabled ? "on" : "off";
            var audioDelayMilliseconds = GameplayTimingPreferences.LoadDeviceOffset() * 1000d;
            var inputDelayMilliseconds = GameplayTimingPreferences.LoadInputOffset() * 1000d;
            Add(new DiagnosticRecord
            {
                type = "session_start",
                realtime = Time.realtimeSinceStartupAsDouble,
                noteIndex = -1,
                detail = $"mode={mode};chart_id={entry.Id};chart_title={entry.Title};" +
                    $"judgment_protection={protection};protection_mode=source_aware_shared_lane;" +
                    $"audio_delay_ms={audioDelayMilliseconds:0.###};input_delay_ms={inputDelayMilliseconds:0.###}",
            });
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
                noteIndex = -1,
                phase = phase.ToString(),
                queueMilliseconds = Math.Max(0d, (InputState.currentTime - inputTime) * 1000d),
                detail = $"screen=({screenPosition.x:F1},{screenPosition.y:F1})",
            });
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
                noteIndex = -1,
                lane = lane,
                inInputBand = inInputBand,
                queueMilliseconds = queueMilliseconds,
                detail = $"tokens={emittedTokenCount}",
            });
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
                noteIndex = -1,
                inputKind = input.Kind.ToString(),
                inputSource = input.Source.ToString(),
                lane = input.Lane,
            });
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
                inputSource = diagnostic.Input.Source.ToString(),
                lane = diagnostic.Input.Lane,
                disposition = diagnostic.Disposition.ToString(),
                noteIndex = diagnostic.Note?.Index ?? -1,
                grade = diagnostic.CandidateGrade.ToString(),
                deltaMilliseconds = double.IsNaN(diagnostic.Delta) ? 0d : diagnostic.Delta * 1000d,
                detail = diagnostic.Disposition == JudgmentInputDisposition.ProtectionBlocked
                    ? "source_aware_shared_lane"
                    : null,
            });
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
                lane = judgment.HitLane ?? 0f,
                deltaMilliseconds = judgment.Delta * 1000d,
            });
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
                lane = judgment.HitLane ?? 0f,
                detail = particleRequested ? "judgment_ui,sound,particle" : "judgment_ui,sound",
            });
        }

        public static string EndRun(string reason)
        {
            if (!CaptureActive) return LastReportPath;
            Add(new DiagnosticRecord
            {
                type = "session_end",
                realtime = Time.realtimeSinceStartupAsDouble,
                noteIndex = -1,
                detail = reason ?? string.Empty,
            });
            CaptureActive = false;
            IsDebugRun = false;
            StopReportStream();
            LastReportPath = activeReportPath;
            activeReportPath = string.Empty;
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

        static string StartReportStream()
        {
            try
            {
                var directory = Path.Combine(Application.persistentDataPath, "InputDiagnostics");
                Directory.CreateDirectory(directory);
                var fileName = $"input-{startedUtc:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.jsonl";
                var path = Path.Combine(directory, fileName);
                reportWriterFailure = null;
                reportWriterStopping = false;
                reportWriter = new StreamWriter(path, false, new UTF8Encoding(false));
                reportWriterThread = new Thread(WritePendingRecords)
                {
                    IsBackground = true,
                    Name = "Gugarhythm Input Diagnostics Writer",
                };
                reportWriterThread.Start();
                return path;
            }
            catch (Exception exception)
            {
                reportWriter?.Dispose();
                reportWriter = null;
                reportWriterThread = null;
                Debug.LogError("無法開始輸入診斷報告：" + exception.Message);
                return string.Empty;
            }
        }

        static void WritePendingRecords()
        {
            var recordsSinceFlush = 0;
            var serializer = JsonSerializer.CreateDefault();
            try
            {
                while (true)
                {
                    DiagnosticRecord record;
                    lock (writerGate)
                    {
                        while (pendingRecords.Count == 0 && !reportWriterStopping)
                            Monitor.Wait(writerGate);
                        if (pendingRecords.Count == 0 && reportWriterStopping) break;
                        record = pendingRecords.Dequeue();
                    }

                    serializer.Serialize(reportWriter, record);
                    reportWriter.WriteLine();
                    recordsSinceFlush++;
                    if (recordsSinceFlush < WriterFlushInterval) continue;
                    reportWriter.Flush();
                    recordsSinceFlush = 0;
                }
                reportWriter.Flush();
            }
            catch (Exception exception)
            {
                reportWriterFailure = exception;
            }
            finally
            {
                reportWriter?.Dispose();
            }
        }

        static void StopReportStream()
        {
            var thread = reportWriterThread;
            if (thread == null) return;
            lock (writerGate)
            {
                reportWriterStopping = true;
                Monitor.PulseAll(writerGate);
            }
            thread.Join();
            reportWriterThread = null;
            reportWriter = null;
            reportWriterStopping = false;
            if (reportWriterFailure != null)
                Debug.LogError("輸入診斷報告寫入失敗：" + reportWriterFailure.Message);
        }

        static void Add(DiagnosticRecord record)
        {
            lock (writerGate)
            {
                pendingRecords.Enqueue(record);
                Monitor.Pulse(writerGate);
            }
        }

        static void ResetCounters()
        {
            lock (writerGate) pendingRecords.Clear();
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

        static byte[] CopyBytes(byte[] source)
        {
            if (source == null || source.Length == 0) return null;
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
