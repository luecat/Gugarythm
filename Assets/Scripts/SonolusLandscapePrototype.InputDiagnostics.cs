using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    public sealed partial class SonolusLandscapePrototype
    {
        readonly List<JudgmentInputDiagnostic> inputDiagnosticsDecisions = new(32);
        RectTransform settingsDebugPanel;
        Text inputDiagnosticsStatusLabel;
        Button settingsDebugNavigationButton;
        Button inputDiagnosticsStartButton;
        Toggle inputDiagnosticsProtectionToggle;
        bool inputDiagnosticsLoading;

        void BuildInputDiagnosticsSettingsSection(RectTransform navigation)
        {
            // Keep DEBUG below the player-facing account menu; the previous
            // shared position constructed DEBUG last and made 帳號 impossible to see or press.
            settingsDebugNavigationButton = MakeFlatButton("DEBUG", navigation, new Vector2(0, -35),
                ShowSettingsDebug, new Vector2(220, 68), new Color(.18f, .18f, .18f));
            settingsDebugPanel = Panel("Settings Debug Panel", settingsPanel,
                new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));

            var title = Label("Tap 輸入診斷", settingsDebugPanel, 32);
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.sizeDelta = new Vector2(900, 56);
            title.rectTransform.anchoredPosition = new Vector2(0, 315);

            var description = Label(
                "追蹤 Touch callback → queue → lane/token → 判定 → hit feedback。\n" +
                "開啟全譜面記錄後，所有譜面會在背景輸出 JSONL；報告仍在此頁複製。",
                settingsDebugPanel, 20);
            description.alignment = TextAnchor.UpperLeft;
            description.color = new Color(.75f, .82f, .92f);
            description.rectTransform.sizeDelta = new Vector2(900, 100);
            description.rectTransform.anchoredPosition = new Vector2(0, 235);

            var inputDiagnosticsRecordAllChartsToggle = MakeFigmaSlidingToggle("全譜面記錄",
                settingsDebugPanel, new Vector2(0, 145), SettingsSliderWidth,
                InputDiagnosticsSession.RecordAllChartsEnabled);
            inputDiagnosticsRecordAllChartsToggle.onValueChanged.AddListener(
                InputDiagnosticsSession.SetRecordAllChartsEnabled);

            inputDiagnosticsProtectionToggle = MakeFigmaSlidingToggle("Judgment Protection",
                settingsDebugPanel, new Vector2(0, 75), SettingsSliderWidth,
                InputDiagnosticsSession.JudgmentProtectionEnabled);
            inputDiagnosticsProtectionToggle.onValueChanged.AddListener(enabled =>
            {
                InputDiagnosticsSession.SetJudgmentProtectionEnabled(enabled);
                ConfigureInputDiagnosticsJudgmentEngine();
            });

            inputDiagnosticsStartButton = MakeFlatButton("載入並開始測試譜面", settingsDebugPanel,
                new Vector2(0, -15), () => StartCoroutine(StartInputDiagnosticsChart()),
                new Vector2(700, 68), new Color(.06f, .58f, .96f));

            MakeOutlinedButton("複製上次報告", settingsDebugPanel, new Vector2(-180, -115),
                CopyLastInputDiagnosticsReport, new Vector2(320, 58));
            MakeOutlinedButton("刪除上次報告", settingsDebugPanel, new Vector2(180, -115),
                ClearLastInputDiagnosticsReport, new Vector2(320, 58));

            inputDiagnosticsStatusLabel = Label("", settingsDebugPanel, 18);
            inputDiagnosticsStatusLabel.alignment = TextAnchor.UpperLeft;
            inputDiagnosticsStatusLabel.color = new Color(.72f, .78f, .84f);
            inputDiagnosticsStatusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            inputDiagnosticsStatusLabel.verticalOverflow = VerticalWrapMode.Overflow;
            inputDiagnosticsStatusLabel.rectTransform.sizeDelta = new Vector2(900, 150);
            inputDiagnosticsStatusLabel.rectTransform.anchoredPosition = new Vector2(0, -245);
            RefreshInputDiagnosticsSettingsStatus();
            settingsDebugPanel.gameObject.SetActive(false);
        }

        void ShowSettingsDebug()
        {
            if (settingsAudioPanel == null || settingsGamePanel == null ||
                settingsTagsPanel == null || settingsAccountPanel == null || settingsDebugPanel == null) return;
            settingsAudioPanel.gameObject.SetActive(false);
            settingsGamePanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsAccountPanel.gameObject.SetActive(false);
            settingsDebugPanel.gameObject.SetActive(true);
            SetSettingsNavigationColor(settingsAudioNavigationButton, false);
            SetSettingsNavigationColor(settingsGameNavigationButton, false);
            SetSettingsNavigationColor(settingsTagsNavigationButton, false);
            SetSettingsNavigationColor(settingsAccountNavigationButton, false);
            SetSettingsNavigationColor(settingsDebugNavigationButton, true);
            RefreshInputDiagnosticsSettingsStatus();
        }

        void HideInputDiagnosticsSettings()
        {
            if (settingsDebugPanel != null) settingsDebugPanel.gameObject.SetActive(false);
            SetSettingsNavigationColor(settingsDebugNavigationButton, false);
        }

        static void SetSettingsNavigationColor(Button button, bool selected)
        {
            if (button != null) button.GetComponent<Image>().color = selected
                ? new Color(.08f, .28f, .42f)
                : new Color(.18f, .18f, .18f);
        }

        IEnumerator StartInputDiagnosticsChart()
        {
            if (inputDiagnosticsLoading) yield break;
            inputDiagnosticsLoading = true;
            if (inputDiagnosticsStartButton != null) inputDiagnosticsStartButton.interactable = false;
            SetInputDiagnosticsStatus("正在讀取內建測試譜面…");

            byte[] bytes = null;
            string loadError = null;
            yield return InputDiagnosticsChartLoader.Load((loadedBytes, error) =>
            {
                bytes = loadedBytes;
                loadError = error;
            });

            if (bytes == null || bytes.Length == 0)
            {
                SetInputDiagnosticsStatus(string.IsNullOrEmpty(loadError) ? "測試譜面載入失敗。" : loadError);
                inputDiagnosticsLoading = false;
                if (inputDiagnosticsStartButton != null) inputDiagnosticsStartButton.interactable = true;
                yield break;
            }

            var import = new GgrChartImporter().Import("Input-Diagnostics.ggr", bytes, null);
            if (!import.Success)
            {
                SetInputDiagnosticsStatus("測試譜面驗證失敗：" + import.Error);
                inputDiagnosticsLoading = false;
                if (inputDiagnosticsStartButton != null) inputDiagnosticsStartButton.interactable = true;
                yield break;
            }

            var selection = ChartSelectionSession.Ensure();
            selection.TryGetSelection(out var previousEntry, out var previousBytes);
            var debugEntry = new LocalChartEntry
            {
                Id = InputDiagnosticsSession.DebugEntryId,
                Title = "Input Diagnostics",
                Artist = "Gugarhythm Debug",
                Author = "Gugarhythm",
                DifficultyName = "DEBUG",
                DifficultyLevel = "INPUT",
                GroupId = InputDiagnosticsSession.DebugEntryId,
                BestAccuracy = -1f,
                Format = "ggr",
                SourceFile = "Input-Diagnostics.ggr",
                NoteCount = import.Chart?.PlayableCount ?? 0,
                ImportedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            InputDiagnosticsSession.Arm(previousEntry, previousBytes);
            if (!selection.SetSelection(debugEntry, bytes))
            {
                InputDiagnosticsSession.RestorePreviousSelectionAndDisarm();
                SetInputDiagnosticsStatus("無法建立測試譜面工作階段。");
                inputDiagnosticsLoading = false;
                if (inputDiagnosticsStartButton != null) inputDiagnosticsStartButton.interactable = true;
                yield break;
            }

            SetInputDiagnosticsStatus("測試譜面已就緒，正在進入遊戲…");
            GugarhythmSceneRouter.OpenGameplay();
        }

        void BeginInputDiagnosticsRunIfNeeded()
        {
            if (!InputDiagnosticsSession.ShouldCapture(currentLibraryEntry)) return;
            InputDiagnosticsSession.BeginRun(currentLibraryEntry);
        }

        void ConfigureInputDiagnosticsJudgmentEngine()
        {
            if (judgmentEngine != null)
                judgmentEngine.JudgmentProtectionEnabled =
                    InputDiagnosticsSession.JudgmentProtectionEnabled;
        }

        void RecordInputDiagnosticsDecisions()
        {
            if (!InputDiagnosticsSession.CaptureActive) return;
            for (var index = 0; index < inputDiagnosticsDecisions.Count; index++)
                InputDiagnosticsSession.RecordDecision(inputDiagnosticsDecisions[index]);
        }

        void EndInputDiagnosticsRun(string reason, bool restoreSelection)
        {
            if (!InputDiagnosticsSession.Armed && !InputDiagnosticsSession.CaptureActive) return;
            if (restoreSelection && InputDiagnosticsSession.Armed)
                InputDiagnosticsSession.EndRunRestoreAndDisarm(reason);
            else InputDiagnosticsSession.EndRun(reason);
        }

        void CopyLastInputDiagnosticsReport()
        {
            if (!InputDiagnosticsSession.TryReadLastReport(out var report, out var path))
            {
                SetInputDiagnosticsStatus("目前沒有可複製的診斷報告。");
                return;
            }
            GUIUtility.systemCopyBuffer = report;
            SetInputDiagnosticsStatus("已複製上次 JSONL 報告：\n" + path);
        }

        void ClearLastInputDiagnosticsReport()
        {
            SetInputDiagnosticsStatus(InputDiagnosticsSession.ClearLastReport()
                ? "已刪除上次診斷報告。"
                : "診斷報告刪除失敗，請查看 Unity／裝置記錄。");
        }

        void RefreshInputDiagnosticsSettingsStatus()
        {
            if (inputDiagnosticsStatusLabel == null) return;
            if (InputDiagnosticsSession.TryReadLastReport(out _, out var path))
                inputDiagnosticsStatusLabel.text = "上次報告：\n" + path;
            else inputDiagnosticsStatusLabel.text =
                "尚無報告。建議先用 Protection ON 跑一次，再關閉後重跑同一組動作做對照。";
        }

        void SetInputDiagnosticsStatus(string message)
        {
            if (inputDiagnosticsStatusLabel != null) inputDiagnosticsStatusLabel.text = message ?? string.Empty;
        }
    }
}
