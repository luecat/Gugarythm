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
        RectTransform inputDiagnosticsHudPanel;
        Text inputDiagnosticsHudLabel;
        Text inputDiagnosticsStatusLabel;
        Button settingsDebugNavigationButton;
        Button inputDiagnosticsStartButton;
        Toggle inputDiagnosticsProtectionToggle;
        bool inputDiagnosticsLoading;
        float nextInputDiagnosticsHudRefresh;

        void BuildInputDiagnosticsSettingsSection(RectTransform navigation)
        {
            settingsDebugNavigationButton = MakeFlatButton("DEBUG", navigation, new Vector2(0, 45),
                ShowSettingsDebug, new Vector2(220, 68), new Color(.18f, .18f, .18f));
            settingsDebugPanel = Panel("Settings Debug Panel", settingsPanel,
                new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));

            var title = Label("Tap 輸入診斷", settingsDebugPanel, 32);
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.sizeDelta = new Vector2(900, 56);
            title.rectTransform.anchoredPosition = new Vector2(0, 315);

            var description = Label(
                "實體裝置專用測試：追蹤 Touch callback → queue → lane/token → 判定 → hit feedback。\n" +
                "譜面包含孤立 Tap、鄰近 Tap、停留按壓與同指滑動。結束或返回時自動匯出 JSONL。",
                settingsDebugPanel, 20);
            description.alignment = TextAnchor.UpperLeft;
            description.color = new Color(.75f, .82f, .92f);
            description.rectTransform.sizeDelta = new Vector2(900, 100);
            description.rectTransform.anchoredPosition = new Vector2(0, 235);

            inputDiagnosticsProtectionToggle = MakeFigmaSlidingToggle("Judgment Protection",
                settingsDebugPanel, new Vector2(0, 115), SettingsSliderWidth,
                PlayerPrefs.GetInt("gugarhythm-input-diagnostics-protection", 1) != 0);
            inputDiagnosticsProtectionToggle.onValueChanged.AddListener(enabled =>
            {
                PlayerPrefs.SetInt("gugarhythm-input-diagnostics-protection", enabled ? 1 : 0);
                PlayerPrefs.Save();
            });

            inputDiagnosticsStartButton = MakeFlatButton("載入並開始測試譜面", settingsDebugPanel,
                new Vector2(0, 25), () => StartCoroutine(StartInputDiagnosticsChart()),
                new Vector2(700, 68), new Color(.06f, .58f, .96f));

            MakeOutlinedButton("複製上次報告", settingsDebugPanel, new Vector2(-180, -75),
                CopyLastInputDiagnosticsReport, new Vector2(320, 58));
            MakeOutlinedButton("刪除上次報告", settingsDebugPanel, new Vector2(180, -75),
                ClearLastInputDiagnosticsReport, new Vector2(320, 58));

            inputDiagnosticsStatusLabel = Label("", settingsDebugPanel, 18);
            inputDiagnosticsStatusLabel.alignment = TextAnchor.UpperLeft;
            inputDiagnosticsStatusLabel.color = new Color(.72f, .78f, .84f);
            inputDiagnosticsStatusLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            inputDiagnosticsStatusLabel.verticalOverflow = VerticalWrapMode.Overflow;
            inputDiagnosticsStatusLabel.rectTransform.sizeDelta = new Vector2(900, 150);
            inputDiagnosticsStatusLabel.rectTransform.anchoredPosition = new Vector2(0, -210);
            RefreshInputDiagnosticsSettingsStatus();
            settingsDebugPanel.gameObject.SetActive(false);
        }

        void BuildInputDiagnosticsHud(RectTransform root)
        {
            inputDiagnosticsHudPanel = Panel("Input Diagnostics HUD", root,
                new Color(.015f, .03f, .08f, .90f), new Vector2(690, 500), Vector2.zero);
            PinToAnchor(inputDiagnosticsHudPanel, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-24, -112));
            Outline(inputDiagnosticsHudPanel.gameObject, new Color(.95f, .55f, .18f, .9f), 2);
            inputDiagnosticsHudPanel.GetComponent<Image>().raycastTarget = false;
            inputDiagnosticsHudLabel = Label("INPUT DIAGNOSTICS\n等待測試工作階段…",
                inputDiagnosticsHudPanel, 17);
            inputDiagnosticsHudLabel.alignment = TextAnchor.UpperLeft;
            inputDiagnosticsHudLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            inputDiagnosticsHudLabel.verticalOverflow = VerticalWrapMode.Overflow;
            inputDiagnosticsHudLabel.rectTransform.anchorMin = Vector2.zero;
            inputDiagnosticsHudLabel.rectTransform.anchorMax = Vector2.one;
            inputDiagnosticsHudLabel.rectTransform.offsetMin = new Vector2(14, 10);
            inputDiagnosticsHudLabel.rectTransform.offsetMax = new Vector2(-12, -10);
            inputDiagnosticsHudPanel.gameObject.SetActive(false);
        }

        void ShowSettingsDebug()
        {
            if (settingsAudioPanel == null || settingsGamePanel == null ||
                settingsTagsPanel == null || settingsDebugPanel == null) return;
            settingsAudioPanel.gameObject.SetActive(false);
            settingsGamePanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsDebugPanel.gameObject.SetActive(true);
            SetSettingsNavigationColor(settingsAudioNavigationButton, false);
            SetSettingsNavigationColor(settingsGameNavigationButton, false);
            SetSettingsNavigationColor(settingsTagsNavigationButton, false);
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
            InputDiagnosticsSession.Arm(inputDiagnosticsProtectionToggle == null ||
                inputDiagnosticsProtectionToggle.isOn, previousEntry, previousBytes);
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
            if (!InputDiagnosticsSession.IsDebugEntry(currentLibraryEntry) || !InputDiagnosticsSession.Armed) return;
            InputDiagnosticsSession.BeginRun();
            nextInputDiagnosticsHudRefresh = 0f;
            if (inputDiagnosticsHudPanel != null) inputDiagnosticsHudPanel.gameObject.SetActive(true);
        }

        void ConfigureInputDiagnosticsJudgmentEngine()
        {
            if (judgmentEngine != null && InputDiagnosticsSession.CaptureActive)
                judgmentEngine.JudgmentProtectionEnabled = InputDiagnosticsSession.JudgmentProtectionEnabled;
        }

        void UpdateInputDiagnosticsHud()
        {
            var visible = gameplayStageVisible && InputDiagnosticsSession.CaptureActive;
            if (inputDiagnosticsHudPanel != null && inputDiagnosticsHudPanel.gameObject.activeSelf != visible)
                inputDiagnosticsHudPanel.gameObject.SetActive(visible);
            if (!visible || inputDiagnosticsHudLabel == null || Time.unscaledTime < nextInputDiagnosticsHudRefresh) return;
            nextInputDiagnosticsHudRefresh = Time.unscaledTime + .1f;
            inputDiagnosticsHudLabel.text = InputDiagnosticsSession.BuildOverlayText();
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
            if (restoreSelection) InputDiagnosticsSession.EndRunRestoreAndDisarm(reason);
            else InputDiagnosticsSession.EndRun(reason);
            if (inputDiagnosticsHudPanel != null) inputDiagnosticsHudPanel.gameObject.SetActive(false);
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
