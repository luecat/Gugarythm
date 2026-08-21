using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Gugarythm
{
    public sealed class SonolusLandscapePrototype : MonoBehaviour
    {
        public readonly struct NoteSurfaceQuad
        {
            public readonly Vector2 UpperLeft;
            public readonly Vector2 UpperRight;
            public readonly Vector2 LowerRight;
            public readonly Vector2 LowerLeft;

            public NoteSurfaceQuad(Vector2 upperLeft, Vector2 upperRight, Vector2 lowerRight, Vector2 lowerLeft)
            {
                UpperLeft = upperLeft;
                UpperRight = upperRight;
                LowerRight = lowerRight;
                LowerLeft = lowerLeft;
            }
        }

        public enum HoldConnectorRenderMode { AnchorClipped, NaturalPassThrough }

        [Flags]
        public enum JudgmentAudioRoute
        {
            None = 0,
            GradeOneShot = 1 << 0,
            PerfectOneShot = 1 << 1,
            FlickOneShot = 1 << 2,
            ActivateHoldLoop = 1 << 3,
            DeactivateHoldLoop = 1 << 4,
        }

        /// <summary>
        /// Maps resolved judgments to audio intent without depending on Unity
        /// playback. Terminal roots are remembered until the next chart reset,
        /// so a late/out-of-order checkpoint cannot revive an ended Hold.
        /// </summary>
        public sealed class HoldJudgmentAudioState
        {
            readonly HoldSoundGate gate = new();
            readonly HashSet<int> endedRoots = new();

            public bool ShouldPlay => gate.ShouldPlay;
            public int ActiveCount => gate.ActiveCount;

            public JudgmentAudioRoute Route(JudgmentEvent judgment)
            {
                var note = judgment.Note;
                if (note == null || judgment.Grade == JudgmentGrade.Pending) return JudgmentAudioRoute.None;

                var root = note.HoldRootIndex;
                var isTail = note.IsHoldTerminal || note.HoldCheckpointSource == HoldCheckpointSource.Tail;
                if (isTail)
                {
                    if (root >= 0)
                    {
                        endedRoots.Add(root);
                        gate.Deactivate(root);
                    }

                    if (judgment.Grade == JudgmentGrade.Miss)
                        return root >= 0 ? JudgmentAudioRoute.DeactivateHoldLoop : JudgmentAudioRoute.None;
                    var oneShot = note.Kind == RuntimeNoteKind.Flick
                        ? JudgmentAudioRoute.FlickOneShot
                        : JudgmentAudioRoute.PerfectOneShot;
                    return root >= 0 ? oneShot | JudgmentAudioRoute.DeactivateHoldLoop : oneShot;
                }

                var isInterior = note.HoldCheckpointSource is HoldCheckpointSource.Mid or HoldCheckpointSource.Auto;
                if (isInterior)
                {
                    if (root < 0) return JudgmentAudioRoute.None;
                    if (judgment.Grade == JudgmentGrade.Miss)
                    {
                        gate.Deactivate(root);
                        return JudgmentAudioRoute.DeactivateHoldLoop;
                    }
                    if (endedRoots.Contains(root)) return JudgmentAudioRoute.None;
                    gate.Activate(root);
                    return JudgmentAudioRoute.ActivateHoldLoop;
                }

                if (judgment.Grade == JudgmentGrade.Miss) return JudgmentAudioRoute.None;
                return note.Kind == RuntimeNoteKind.Flick
                    ? JudgmentAudioRoute.FlickOneShot
                    : JudgmentAudioRoute.GradeOneShot;
            }

            public void Clear()
            {
                gate.Clear();
                endedRoots.Clear();
            }
        }

        // Mapping measured directly from the original 1280x732 lane artwork.
        // CanvasScaler matches width, so Free Aspect/editor windows can be taller
        // than 1080 logical units. Derive Y from the live viewport instead of
        // assuming 16:9; this keeps note edges on the gray texture guides.
        const float ReferenceWidth = 1920f;
        const float LaneTextureWidth = 1280f;
        const float LaneTextureHeight = 732f;
        const float LaneTextureCenterX = 638.8049f;
        const float HitSourceY = 500f;
        const float JudgmentStripSourceHeight = 45f;
        const float CentralHalfLanes = 6f;
        // ±6 are the centres of the outermost buttons; their normal half-size
        // reaches the visible lane boundary at ±6.5.
        const float VisibleTrackLaneEdge = CentralHalfLanes + .5f;
        const float PerspectiveDepthRatio = 3.2f;
        public const float NoteApproachDurationSeconds = 2f;
        const float InitialOffscreenLeadSeconds = .25f;
        // Curves are sampled on fixed chart-time boundaries. A denser grid
        // keeps curved ribbons smooth, while stable boundaries prevent the
        // entire tessellation from shifting whenever the visible end is clipped.
        const int ConnectorPathSegments = 128;
        // All thirteen lane boundaries (-6 through 6) were measured from the
        // alignment reference. Keeping every boundary avoids the one-to-two
        // pixel drift caused by interpolating only the even gray guides.
        // Fits are in source-image pixels: x = intercept + slope * y.
        static readonly float[] LaneGuideIntercepts =
        {
            616.0356f, 620.9612f, 624.5489f, 628.4903f, 631.5389f, 635.4715f, 638.8049f,
            642.5187f, 646.0649f, 649.5068f, 653.0450f, 656.5548f, 660.2418f,
        };
        static readonly float[] LaneGuideSlopes =
        {
            -.8379661f, -.7036342f, -.5590519f, -.4198532f, -.2774788f, -.1406074f, .0000444f,
            .1412126f, .2827021f, .4205463f, .5611308f, .7017399f, .8439814f,
        };
        // Note height is independent of note.Size.  The original game makes a
        // falling note read as a flat sticker on the lane rather than a
        // billboard: its upper and lower edges sample their own lane depth, but
        // their screen-space separation remains the judgment-edge height.
        // At the judgment edge the lane artwork's purple judgment strip is
        // about 45 px tall while a size-1 note spans about 147.5 px. The atlas
        // note sprites include transparent glow padding; Next SEKAI expands
        // their render quad by 2.325 so the visible body (not the padded image)
        // shares the judgment strip's height.
        const float ButtonSpriteTransform = 2.325f;
        const float ButtonHeightRatio = 45f / 147.5f * ButtonSpriteTransform;
        const float NoteCapRatio = 93f / 354f;
        const float NoteTextureHeight = 186f;
        // hold-green/yellow are 306px wide with a 240px opaque center.
        // Convert the USC-authored button body width to the ribbon's quad width
        // so their visible (non-transparent) edges meet exactly.
        const float HoldConnectorTextureWidth = 306f;
        const float HoldConnectorVisibleTextureWidth = 240f;
        const float HoldConnectorVisibleUvInset = (HoldConnectorTextureWidth - HoldConnectorVisibleTextureWidth) / HoldConnectorTextureWidth * .5f;
        const float HoldHeadTextureWidth = 354f;
        // Both Hold head atlases and both connector atlases use a 240px solid
        // center.  Match those cores rather than their soft outer glow.
        const float HoldHeadCoreTextureWidth = 240f;
        // Button sprites begin their visible antialiased edge at pixel 44.
        // Using the old 40px glow bound made every normal Tap visibly narrow.
        const float NormalButtonVisibleEdgePaddingPixels = 44f;
        const int MouseContactId = int.MinValue;
        // Missed notes keep travelling beyond the judgment line until their
        // sprite leaves the viewport. Successful hits return to the pool at once.
        const float NoteExitMargin = 140f;
        const float JudgmentDisplayDuration = .35f;
        public const float InputLaneFeedbackDuration = .12f;
        const int InputLaneFeedbackGridCellCount = VirtualSliderInput.CellCount / 2;
        const float HoldLoopVolume = .55f;
        const float HoldLoopFadeDuration = .04f;

        readonly Dictionary<string, Texture2D> buttonTextures = new(StringComparer.Ordinal);
        readonly Dictionary<string, Texture2D> traceTextures = new(StringComparer.Ordinal);
        readonly Dictionary<int, HorizontalSlicedRawImage> noteViews = new();
        readonly Dictionary<int, HorizontalSlicedRawImage> persistentHoldHeadViews = new();
        readonly HashSet<int> renderedPersistentHoldHeads = new();
        readonly Dictionary<RuntimeConnector, TaperedConnectorGraphic> connectorViews = new();
        readonly Dictionary<RuntimeSimLine, SimLineGraphic> simLineViews = new();
        readonly Dictionary<RuntimeGuide, TaperedConnectorGraphic> guideViews = new();
        readonly Dictionary<int, RuntimeNote> holdRoots = new();
        readonly Dictionary<int, List<RuntimeNote>> holdCheckpoints = new();
        readonly HoldJudgmentAudioState holdAudioState = new();
        readonly Stack<HorizontalSlicedRawImage> notePool = new();
        readonly Stack<TaperedConnectorGraphic> connectorPool = new();
        readonly Stack<SimLineGraphic> simLinePool = new();
        readonly Stack<TaperedConnectorGraphic> guidePool = new();
        readonly Dictionary<int, TouchMemory> touches = new();
        readonly List<InputToken> inputBatch = new();
        readonly List<ActiveContact> contacts = new();
        readonly List<ContactPathSegment> contactPaths = new();
        readonly VirtualSliderInput virtualSlider = new();
        readonly TaperedConnectorGraphic[] inputLaneFeedback = new TaperedConnectorGraphic[InputLaneFeedbackGridCellCount];
        readonly float[] inputLaneFeedbackUntil = new float[InputLaneFeedbackGridCellCount];
        readonly float[] connectorPathSamples = new float[ConnectorPathSegments + 3];
        readonly ScoreState scoreState = new();
        readonly List<IChartImporter> importers = new() { new GgrChartImporter() };

        Texture2D backgroundTexture;
        Texture2D laneTexture;
        Texture2D damageTexture;
        readonly Texture2D[] flickNormalCenterTextures = new Texture2D[6];
        readonly Texture2D[] flickNormalSideTextures = new Texture2D[6];
        readonly Texture2D[] flickCriticalCenterTextures = new Texture2D[6];
        readonly Texture2D[] flickCriticalSideTextures = new Texture2D[6];
        // Logical square sizes used by the six official arrow layouts. The
        // cropped atlas sprites are wider than they are tall, so width and
        // height must be reconstructed independently from these values.
        static readonly float[] FlickLogicalSizes = { 44f, 66f, 96f, 128f, 159f, 190f };
        const float FlickArrowScale = .82f;
        Texture2D holdGreenConnectorTexture;
        Texture2D holdYellowConnectorTexture;
        Texture2D holdMidMintTexture;
        Texture2D holdMidYellowTexture;
        Texture2D traceDiamondMintTexture;
        Texture2D traceDiamondPinkTexture;
        Texture2D traceDiamondYellowTexture;
        AudioClip perfectSound;
        AudioClip greatSound;
        AudioClip goodSound;
        AudioClip holdSound;
        AudioClip flickSound;
        AudioClip criticalFlickSound;
        AudioClip stageSound;
        RuntimeChart chart;
        LocalChartEntry currentLibraryEntry;
        JudgmentEngine judgmentEngine;
        AudioSource music;
        AudioSource effects;
        AudioSource holdEffects;
        readonly AudioSource[] calibrationTickSources = new AudioSource[CalibrationTickCount];
        RectTransform canvasRoot;
        RectTransform backgroundLayer;
        RectTransform stage;
        RectTransform safeAreaRoot;
        RectTransform guideLayer;
        RectTransform connectorLayer;
        RectTransform simLineLayer;
        RectTransform persistentHoldHeadLayer;
        RectTransform noteLayer;
        RectTransform menuPanel;
        RectTransform libraryBackdrop;
        RectTransform settingsPanel;
        RectTransform settingsAudioPanel;
        RectTransform settingsTagsPanel;
        RectTransform difficultyTagConfirmationPanel;
        RectTransform chartEditorPanel;
        RectTransform deleteChartConfirmationPanel;
        RectTransform importDecisionPanel;
        RectTransform pauseOverlay;
        RectTransform pauseMenuContent;
        RectTransform resultPanel;
        Text accuracyLabel;
        Text comboLabel;
        Text judgmentLabel;
        Text loadStatus;
        Text libraryCountLabel;
        Text librarySortLabel;
        Text librarySortModeLabel;
        RectTransform libraryDirectionIcon;
        Text detailTitleLabel;
        Text detailArtistLabel;
        Text detailDifficultyLabel;
        Text detailAccuracyLabel;
        Text detailCoverTitleLabel;
        InputField librarySearchInput;
        InputField chartEditorTitleInput;
        InputField chartEditorAuthorInput;
        InputField chartEditorDifficultyNameInput;
        InputField chartEditorLevelInput;
        RectTransform libraryListContent;
        RectTransform difficultyButtonContent;
        RectTransform chartEditorTagContent;
        RectTransform settingsTagContent;
        InputField settingsTagInput;
        Text importDecisionText;
        Text resultText;
        Text speedLabel;
        Text settingsMusicVolumeLabel;
        Text settingsKeyVolumeLabel;
        Text settingsDelayLabel;
        Text difficultyTagConfirmationText;
        Text calibrationLabel;
        Text calibrationOffsetLabel;
        Text chartEditorSubtitleLabel;
        Text chartEditorStatusLabel;
        Button calibrationTapButton;
        Button calibrationDecreaseOffsetButton;
        Button calibrationIncreaseOffsetButton;
        Button calibrationResetOffsetButton;
        Button settingsAudioNavigationButton;
        Button settingsTagsNavigationButton;
        Text resumeCountdownLabel;
        Text pauseTitle;
        Button startButton;
        LocalChartEntry selectedLibraryEntry;
        LocalChartEntry chartEditorEntry;
        string selectedDifficultyName = "";
        string pendingDifficultyTagDelete;
        ChartLibrarySort librarySort = ChartLibrarySort.Accuracy;
        bool librarySortAscending;
        Button pauseButton;
        RectTransform calibrationPanel;
        Toggle autoPlayToggle;
        Slider speedSlider;
        Slider settingsMusicVolumeSlider;
        Slider settingsKeyVolumeSlider;
        Material laneMaterial;
        Material missedHoldMaterial;
        bool running;
        string pendingImportFileName;
        byte[] pendingImportBytes;
        RuntimeChart pendingImportChart;
        bool loading;
        bool musicLoadSucceeded;
        bool paused;
        int audioDeviceChangePending;
        bool resumeNeedsAudioReschedule;
        Coroutine resumeCoroutine;
        Coroutine holdFadeCoroutine;
        Rect appliedSafeArea = new(-1, -1, -1, -1);
        double scheduledDsp;
        double pauseDsp;
        double accumulatedPause;
        double lastObservedSongTime;
        double interruptedSongTime;
        double audioOffsetSeconds;
        double settingsDelayOffsetSeconds;
        double visualOffsetSeconds;
        double calibrationStartDsp;
        readonly List<double> calibrationOffsets = new();
        bool calibrationActive;
        float scrollSpeed = 8f;
        float judgmentHideAt = -1f;

        static float CanvasHeight => ReferenceWidth * Screen.height / Math.Max(1, Screen.width);
        static float TopY => CanvasHeight * .5f;
        static float HitY => TopY - HitSourceY / LaneTextureHeight * CanvasHeight;
        public static int JudgmentDebugCellCount => VirtualSliderInput.CellCount;
        public static float JudgmentDebugCellWidth => VirtualSliderInput.CellWidth;
        public static float JudgmentInputBandHeight(float canvasHeight) =>
            JudgmentStripSourceHeight / LaneTextureHeight * canvasHeight;
        public static float InputLaneFeedbackBottom(float canvasHeight)
        {
            var hitY = canvasHeight * .5f - HitSourceY / LaneTextureHeight * canvasHeight;
            return hitY - JudgmentInputBandHeight(canvasHeight) * .5f;
        }
        public static float InputLaneFeedbackTop(float canvasHeight) =>
            InputLaneFeedbackBottom(canvasHeight) + JudgmentInputBandHeight(canvasHeight);
        public static float JudgmentInputTop(float canvasHeight)
        {
            var hitY = canvasHeight * .5f - HitSourceY / LaneTextureHeight * canvasHeight;
            return hitY + 3f * JudgmentInputBandHeight(canvasHeight);
        }
        public static bool IsJudgmentInputBand(float canvasY, float canvasHeight)
        {
            return canvasY >= -canvasHeight * .5f && canvasY <= JudgmentInputTop(canvasHeight);
        }
        static float JudgmentInputGridStripTop(float canvasHeight)
        {
            var hitY = canvasHeight * .5f - HitSourceY / LaneTextureHeight * canvasHeight;
            return hitY + JudgmentInputBandHeight(canvasHeight) * .5f;
        }
        public static int JudgmentInputGridRow(float canvasY, float canvasHeight) =>
            Mathf.FloorToInt((canvasY - JudgmentInputGridStripTop(canvasHeight)) / JudgmentInputBandHeight(canvasHeight));
        public static float InputLaneAtCanvasX(float canvasX) => Mathf.Clamp(
            ScreenXToLane(canvasX, 1f), VirtualSliderInput.MinimumLane, VirtualSliderInput.MaximumLane);
        // Gray input feedback deliberately stops at the visible central track.
        // Touches beyond its two outer divider lines remain on -6 or +6.
        public static float CanvasXAtInputLane(float lane) => X(lane, 1f);
        public static float JudgmentDebugCanvasXAtLane(float lane) => Mathf.Lerp(
            -ReferenceWidth * .5f, ReferenceWidth * .5f,
            Mathf.InverseLerp(VirtualSliderInput.MinimumLane, VirtualSliderInput.MaximumLane, lane));
        public static float JudgmentLaneCanvasX(float lane) => X(lane, 1f);
        public static bool ShouldContinueTrackedContact(bool wasTracking, bool isInInputBand) =>
            wasTracking || isInInputBand;
        public static int InputLaneFeedbackCell(float lane) => VirtualSliderInput.CellAt(lane);
        // LaneWidth takes a half-width, while the visual feedback is authored
        // as one full button-width across the track.
        public static float InputLaneFeedbackWidth => 1f;
        public static int InputLaneFeedbackGridCell(int inputCell) =>
            Mathf.Clamp(inputCell / 2, 0, InputLaneFeedbackGridCellCount - 1);
        static float NoteExitY => -TopY - NoteExitMargin;
        static float NearTrackProgress => (TopY - NoteExitY) / Mathf.Max(1, TopY - HitY);
        static float NearTrackApproach => 1f + (NearTrackProgress - 1f) / PerspectiveDepthRatio;
        // HitSourceY is the centre of the 45px judgment strip. Notes and
        // gameplay connectors leave only after reaching its lower edge.
        static float JudgmentBottomApproach => 1f + (JudgmentStripSourceHeight * .5f / HitSourceY) / PerspectiveDepthRatio;

        // Every note starts from the same far plane exactly two seconds before
        // its judgment time, so a chart's first note cannot appear abruptly
        // just because the viewport or scroll setting changes.
        float ApproachDuration => NoteApproachDurationSeconds;

        static double FirstWaterfallVisualTime(RuntimeChart runtimeChart)
        {
            if (runtimeChart == null) return 0;
            var firstTime = runtimeChart.Notes.Where(note => note != null && note.Visible).Select(note => note.Time)
                .Concat(runtimeChart.Connectors.SelectMany(connector => new[] { connector.Start, connector.End })
                    .Where(note => note != null).Select(note => note.Time))
                .DefaultIfEmpty(0d).Min();
            return double.IsFinite(firstTime) ? firstTime : 0;
        }

        public static double InitialWaterfallSongTime(double firstVisualTime, double bgmOffset, double audioOffset,
            double approachDuration, double offscreenLead)
        {
            if (!double.IsFinite(firstVisualTime)) firstVisualTime = 0;
            if (!double.IsFinite(bgmOffset)) bgmOffset = 0;
            if (!double.IsFinite(audioOffset)) audioOffset = 0;
            approachDuration = double.IsFinite(approachDuration) ? Math.Max(0, approachDuration) : 0;
            offscreenLead = double.IsFinite(offscreenLead) ? Math.Max(0, offscreenLead) : 0;
            var waterfallStart = firstVisualTime - approachDuration - offscreenLead;
            var earliestAudioSafeStart = -bgmOffset + audioOffset;
            return Math.Min(0d, Math.Min(waterfallStart, earliestAudioSafeStart));
        }

        void Awake()
        {
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;
            Application.targetFrameRate = 120;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            QualitySettings.vSyncCount = 0;
            scrollSpeed = PlayerPrefs.GetFloat("gugarythm-scroll-speed", 8f);
            var storedAudioOffset = PlayerPrefs.GetFloat("gugarythm-audio-offset-seconds", 0f);
            audioOffsetSeconds = SanitizeAudioOffset(storedAudioOffset);
            settingsDelayOffsetSeconds = SettingsDelayAdjustment.Clamp(PlayerPrefs.GetFloat("gugarythm-settings-delay-offset-seconds", (float)audioOffsetSeconds));
            audioOffsetSeconds = settingsDelayOffsetSeconds;
            if (Math.Abs(audioOffsetSeconds - storedAudioOffset) > .000001d)
            {
                PlayerPrefs.SetFloat("gugarythm-audio-offset-seconds", (float)audioOffsetSeconds);
                PlayerPrefs.Save();
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            // TouchSimulation can leave the real Mouse device disabled across
            // editor play sessions. Desktop input is adapted explicitly below.
            EnsureDesktopMouseAvailable();
#endif
            EnhancedTouchSupport.Enable();
            LoadArtwork();
            BuildInterface();
            SetStatus("請匯入 GGR 封包。");
        }

        IEnumerator Start()
        {
            // Every scene owns a fresh presentation/controller.  Only the
            // selected package crosses the boundary through ChartSelectionSession.
            if (GugarythmSceneRouter.IsLibrary)
            {
                SetGameplayStageVisible(false);
                SetMenuHudVisible(false);
                menuPanel.gameObject.SetActive(true);
                settingsPanel.gameObject.SetActive(false);
                RestoreLibrarySelection();
                RefreshLibraryUI();
                yield break;
            }

            if (GugarythmSceneRouter.IsSettings)
            {
                SetGameplayStageVisible(false);
                SetMenuHudVisible(false);
                menuPanel.gameObject.SetActive(false);
                settingsPanel.gameObject.SetActive(true);
                yield break;
            }

            if (GugarythmSceneRouter.IsChartEditor)
            {
                SetGameplayStageVisible(false);
                SetMenuHudVisible(false);
                menuPanel.gameObject.SetActive(false);
                settingsPanel.gameObject.SetActive(false);
                chartEditorPanel.gameObject.SetActive(true);
                PopulateChartEditor();
                yield break;
            }

            menuPanel.gameObject.SetActive(false);
            settingsPanel.gameObject.SetActive(false);
            chartEditorPanel.gameObject.SetActive(false);
            if (!ChartSelectionSession.Ensure().TryGetSelection(out var entry, out var bytes))
            {
                GugarythmSceneRouter.OpenLibrary();
                yield break;
            }

            SetGameplayStageVisible(true);
            yield return LoadGameplaySelection(entry, bytes);
        }

        void SetMenuHudVisible(bool visible)
        {
            if (accuracyLabel != null) accuracyLabel.transform.parent.gameObject.SetActive(visible);
            if (comboLabel != null) comboLabel.gameObject.SetActive(false);
            if (judgmentLabel != null) judgmentLabel.gameObject.SetActive(visible);
            if (pauseButton != null) pauseButton.gameObject.SetActive(false);
        }

        void SetGameplayStageVisible(bool visible)
        {
            if (backgroundLayer != null) backgroundLayer.gameObject.SetActive(visible);
            if (stage != null) stage.gameObject.SetActive(visible);
        }

        void RestoreLibrarySelection()
        {
            if (!ChartSelectionSession.Ensure().TryGetSelection(out var remembered, out _)) return;
            var entry = LocalChartLibrary.Load().FirstOrDefault(candidate => candidate.Id == remembered.Id);
            if (entry == null) return;
            selectedLibraryEntry = entry;
            currentLibraryEntry = entry;
            selectedDifficultyName = entry.DifficultyName ?? string.Empty;
        }

        IEnumerator LoadGameplaySelection(LocalChartEntry entry, byte[] bytes)
        {
            loading = true;
            startButton.interactable = false;
            SetStatus("正在載入 " + entry.Title + "…");
            yield return null;

            var result = new GgrChartImporter().Import(entry.SourceFile, bytes, null);
            if (!result.Success)
            {
                Debug.LogError("無法載入跨場景選取的譜面：" + result.Error);
                GugarythmSceneRouter.OpenLibrary();
                yield break;
            }

            chart = result.Chart;
            musicLoadSucceeded = false;
            if (chart.BgmBytes != null) yield return LoadMusic(chart.BgmBytes, chart.BgmExtension, chart.BgmStartDelaySeconds);
            if (!musicLoadSucceeded)
            {
                Debug.LogError("跨場景選取的 GGR 音樂無法解碼。");
                GugarythmSceneRouter.OpenLibrary();
                yield break;
            }

            currentLibraryEntry = entry;
            selectedLibraryEntry = entry;
            selectedDifficultyName = entry.DifficultyName ?? string.Empty;
            loading = false;
            startButton.interactable = true;
            StartGameplay();
        }

        void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
            StopCalibrationTickAudio();
            ClearHoldSound();
#if UNITY_EDITOR || UNITY_STANDALONE
            TouchSimulation.Disable();
#endif
            if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable();
            if (laneMaterial != null) Destroy(laneMaterial);
            if (missedHoldMaterial != null) Destroy(missedHoldMaterial);
        }

        void Update()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            // The Input System editor setting can re-enable TouchSimulation
            // after Awake during a domain reload. That component disables the
            // source Mouse device, which also prevents all UI buttons from
            // receiving pointer events. Restore the real mouse every frame;
            // gameplay converts it to touch semantics in CollectMouseAsTouch.
            EnsureDesktopMouseAvailable();
#endif
            PollNativeImport();
            UpdateSafeAreaLayout();
            UpdateInputLaneFeedback();
            UpdateDesktopSpeedControls();
            UpdateLatencyCalibration();
            if (judgmentHideAt >= 0 && Time.unscaledTime >= judgmentHideAt)
                ShowJudgment("", Color.white);
            if (Interlocked.Exchange(ref audioDeviceChangePending, 0) != 0)
            {
                if (ShouldPauseForAudioConfigurationChange(true, running, paused)) PauseForAudioDeviceChange();
                else ClearHoldSound();
            }
            if (!running || paused || chart == null || judgmentEngine == null) return;
            var songTime = CurrentSongTime();
            lastObservedSongTime = songTime;
            CollectInput();
            // Input remains fully routed to JudgmentEngine below.  Do not draw
            // a full-depth lane flash here: it reads as a reflected Hold bar
            // beneath the button rather than input feedback.
            var events = judgmentEngine.Process(songTime, inputBatch, contacts, contactPaths, autoPlayToggle != null && autoPlayToggle.isOn);
            if (events.Count > 0)
            {
                foreach (var judgment in events) OnJudgment(judgment);
            }
            UpdateVisuals(songTime + visualOffsetSeconds);
            RefreshHud();
            if (songTime > chart.LastNoteTime + .75 && chart.Notes.All(note => !note.Judged || note.Grade != JudgmentGrade.Pending)) FinishGame();
        }

        IEnumerator ImportBytes(string fileName, byte[] bytes)
        {
            ClearHoldSound();
            loading = true;
            startButton.interactable = false;
            SetStatus("正在匯入 " + fileName + "…");
            yield return null;
            var header = bytes.Length <= 16 ? bytes : bytes[..16];
            ImportResult result = null;
            foreach (var importer in importers)
            {
                if (!importer.CanImport(fileName, header)) continue;
                result = importer.Import(fileName, bytes, null);
                if (result.Success) break;
            }
            if (result == null) result = ImportResult.Fail("不支援的譜面格式。");
            if (!result.Success)
            {
                SetStatus(result.Error);
                loading = false;
                yield break;
            }
            chart = result.Chart;
            if (chart.BgmBytes != null)
            {
                musicLoadSucceeded = false;
                yield return LoadMusic(chart.BgmBytes, chart.BgmExtension, chart.BgmStartDelaySeconds);
                if (!musicLoadSucceeded) { SetStatus("GGR 音樂格式不支援或無法解碼。"); loading = false; yield break; }
            }
            else SetStatus("GGR 缺少 USC 譜面或音樂。");

            PresentImportStorageDecision(fileName, bytes, chart);
            var warning = chart.Warnings.Count > 0 ? $" · {chart.Warnings.Count} 個解析警告" : "";
            SetStatus($"{chart.Title} · {chart.PlayableCount:N0} notes · {chart.SourceFormat}{warning}");
            loading = false;
        }

        void UpdateDesktopSpeedControls()
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            if (menuPanel == null || !menuPanel.gameObject.activeInHierarchy || speedSlider == null || Keyboard.current == null) return;
            var direction = Keyboard.current.leftArrowKey.wasPressedThisFrame ? -1 :
                Keyboard.current.rightArrowKey.wasPressedThisFrame ? 1 : 0;
            if (direction == 0) return;
            AdjustScrollSpeed(direction * .1f);
#endif
        }

        void AdjustScrollSpeed(float delta)
        {
            if (speedSlider == null) return;
            SetScrollSpeed(Mathf.Round((scrollSpeed + delta) * 10f) / 10f);
        }

        public float ScrollSpeed => scrollSpeed;

        public void SetDesktopScrollSpeed(float value) => SetScrollSpeed(value);

#if UNITY_EDITOR
        public void StartLoadedChartForEditor() => StartGame();
#endif

        void SetScrollSpeed(float value)
        {
            value = Mathf.Clamp(value, 1f, 20f);
            scrollSpeed = value;
            if (speedSlider != null && !Mathf.Approximately(speedSlider.value, value))
                speedSlider.SetValueWithoutNotify(value);
            if (speedLabel != null)
                speedLabel.text = $"{value:F1}";
            PlayerPrefs.SetFloat("gugarythm-scroll-speed", value);
        }

        void SetSettingsMusicVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (music != null) music.volume = value;
            if (settingsMusicVolumeLabel != null) settingsMusicVolumeLabel.text = $"{value * 100f:0}%";
            PlayerPrefs.SetFloat("gugarythm-music-volume", value);
            PlayerPrefs.Save();
        }

        void SetSettingsKeyVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (effects != null) effects.volume = value;
            if (holdEffects != null) holdEffects.volume = value;
            if (settingsKeyVolumeLabel != null) settingsKeyVolumeLabel.text = $"{value * 100f:0}%";
            PlayerPrefs.SetFloat("gugarythm-key-volume", value);
            PlayerPrefs.Save();
        }

        void AdjustSettingsDelay(double delta)
        {
            settingsDelayOffsetSeconds = SettingsDelayAdjustment.Step(settingsDelayOffsetSeconds, delta);
            audioOffsetSeconds = settingsDelayOffsetSeconds;
            PlayerPrefs.SetFloat("gugarythm-audio-offset-seconds", (float)audioOffsetSeconds);
            PlayerPrefs.SetFloat("gugarythm-settings-delay-offset-seconds", (float)settingsDelayOffsetSeconds);
            PlayerPrefs.Save();
            RefreshSettingsDelayLabel();
        }

        void RefreshSettingsDelayLabel()
        {
            if (settingsDelayLabel != null)
                settingsDelayLabel.text = $"{settingsDelayOffsetSeconds * 1000d:+0;-0;0} ms";
        }

        void ShowSettingsAudio()
        {
            if (settingsAudioPanel == null || settingsTagsPanel == null) return;
            settingsAudioPanel.gameObject.SetActive(true);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsAudioNavigationButton.GetComponent<Image>().color = new Color(.08f, .28f, .42f);
            settingsTagsNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
        }

        void ShowSettingsTags()
        {
            if (settingsAudioPanel == null || settingsTagsPanel == null) return;
            settingsAudioPanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(true);
            settingsAudioNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsTagsNavigationButton.GetComponent<Image>().color = new Color(.08f, .28f, .42f);
        }

        void OpenAutoAdjustPanel()
        {
            // The new automatic-adjustment flow will be designed separately.
        }

        void StartLatencyCalibration()
        {
            if (running || calibrationPanel == null) return;
            StopCalibrationTickAudio();
            calibrationOffsets.Clear();
            calibrationStartDsp = AudioDeviceRecovery.ChartAnchorDspForAudioOffset(AudioSettings.dspTime + .8d, audioOffsetSeconds);
            ScheduleCalibrationTicks();
            calibrationActive = true;
            calibrationPanel.gameObject.SetActive(true);
            RefreshManualAudioOffsetControls();
            calibrationLabel.gameObject.SetActive(true);
            calibrationLabel.text = "四拍循環中\n按下 TAP 測試延遲";
            calibrationTapButton.gameObject.SetActive(true);
            calibrationTapButton.interactable = true;
            RefreshCalibrationOffsetLabel();
        }

        void ReturnFromLatencyCalibration()
        {
            calibrationActive = false;
            StopCalibrationTickAudio();
            RefreshManualAudioOffsetControls();
            calibrationPanel?.gameObject.SetActive(false);
        }

        void RestartLatencyCalibration()
        {
            calibrationActive = false;
            StartLatencyCalibration();
        }

        void UpdateLatencyCalibration()
        {
            if (!calibrationActive || calibrationPanel == null) return;
            if (AudioSettings.dspTime >= CalibrationBeatDsp(CalibrationBeatsPerRound - 1) + CalibrationTapWindowSeconds)
            {
                if (LatencyCalibrationMath.TryGetAverageOffset(calibrationOffsets, out var average))
                {
                    SetAudioOffset(average);
                    calibrationLabel.text = $"四拍平均  {average * 1000d:+0;-0;0} ms";
                }
                calibrationOffsets.Clear();
                calibrationStartDsp = AudioSettings.dspTime + .15d;
                ScheduleCalibrationTicks();
            }
        }

        void RegisterCalibrationTapFromButton() => RegisterCalibrationTap(InputEventDspTime(InputState.currentTime));

        void RegisterCalibrationTap(double inputDsp)
        {
            if (!calibrationActive || calibrationOffsets.Count >= CalibrationBeatsPerRound) return;
            var expectedBeatDsp = CalibrationBeatDsp(calibrationOffsets.Count);
            var offset = CalibrationAudioOffsetForTap(inputDsp, expectedBeatDsp);
            if (!LatencyCalibrationMath.IsTapOffsetValid(offset)) return;
            calibrationOffsets.Add(offset);
            calibrationLabel.text = $"本次偏移  {offset * 1000d:+0;-0;0} ms\n第 {calibrationOffsets.Count}/4 拍";
            calibrationOffsetLabel.text = $"目前套用  {audioOffsetSeconds * 1000d:+0;-0;0} ms\n按下 TAP 測試你這次的聲音與畫面差距";
        }

        public static double CalibrationAudioOffsetForTap(double inputDsp, double audibleBeatDsp)
        {
            if (double.IsNaN(inputDsp) || double.IsInfinity(inputDsp) ||
                double.IsNaN(audibleBeatDsp) || double.IsInfinity(audibleBeatDsp)) return 0;
            return audibleBeatDsp - inputDsp;
        }

        public static double SanitizeAudioOffset(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= .3d ? value : 0d;

        public static bool CanAdjustAudioOffsetManually(bool calibrationIsActive) => !calibrationIsActive;

        void SetAudioOffset(double value)
        {
            audioOffsetSeconds = double.IsNaN(value) || double.IsInfinity(value) ? 0d : Math.Clamp(value, -.3d, .3d);
            PlayerPrefs.SetFloat("gugarythm-audio-offset-seconds", (float)audioOffsetSeconds);
            PlayerPrefs.Save();
            RefreshCalibrationOffsetLabel();
        }

        void SetManualAudioOffset(double value)
        {
            if (!CanAdjustAudioOffsetManually(calibrationActive)) return;
            SetAudioOffset(value);
        }

        void AdjustAudioOffset(double delta) => SetManualAudioOffset(audioOffsetSeconds + delta);

        void RefreshManualAudioOffsetControls()
        {
            var interactable = CanAdjustAudioOffsetManually(calibrationActive);
            if (calibrationDecreaseOffsetButton != null) calibrationDecreaseOffsetButton.interactable = interactable;
            if (calibrationIncreaseOffsetButton != null) calibrationIncreaseOffsetButton.interactable = interactable;
            if (calibrationResetOffsetButton != null) calibrationResetOffsetButton.interactable = interactable;
        }

        void RefreshCalibrationOffsetLabel()
        {
            if (calibrationOffsetLabel != null)
                calibrationOffsetLabel.text = $"聲音偏移  {audioOffsetSeconds * 1000d:+0;-0;0} ms\n＋延後聲音／−提前聲音；不影響判定";
        }

        const int CalibrationBeatsPerRound = LatencyCalibrationMath.TapsPerRound;
        const int CalibrationRoundCount = 1;
        const int CalibrationTickCount = CalibrationBeatsPerRound;
        const double CalibrationBeatDurationSeconds = .6d;
        const double CalibrationTapWindowSeconds = .3d;

        double CalibrationBeatDsp(int beatIndex) =>
            calibrationStartDsp + beatIndex * CalibrationBeatDurationSeconds + audioOffsetSeconds;

        void ScheduleCalibrationTicks()
        {
            if (perfectSound == null) return;
            for (var beatIndex = 0; beatIndex < CalibrationTickCount; beatIndex++)
            {
                var source = calibrationTickSources[beatIndex];
                source.clip = beatIndex % CalibrationBeatsPerRound == CalibrationBeatsPerRound - 1 && greatSound != null
                    ? greatSound
                    : perfectSound;
                source.PlayScheduled(CalibrationBeatDsp(beatIndex));
            }
        }

        void StopCalibrationTickAudio()
        {
            foreach (var source in calibrationTickSources)
                if (source != null) source.Stop();
        }

        static AudioClip PrependLeadingSilence(AudioClip source, double leadingSilenceSeconds)
        {
            if (source == null || !double.IsFinite(leadingSilenceSeconds) || leadingSilenceSeconds <= 1e-9 ||
                source.samples <= 0 || source.channels <= 0 || source.frequency <= 0)
                return source;

            var silenceSamples = (int)Math.Round(leadingSilenceSeconds * source.frequency);
            if (silenceSamples <= 0) return source;
            var sourceData = new float[source.samples * source.channels];
            if (!source.GetData(sourceData, 0)) return source;

            var paddedSamples = checked(source.samples + silenceSamples);
            var paddedData = new float[paddedSamples * source.channels];
            Array.Copy(sourceData, 0, paddedData, silenceSamples * source.channels, sourceData.Length);
            var padded = AudioClip.Create(source.name + " (leading silence)", paddedSamples, source.channels, source.frequency, false);
            if (!padded.SetData(paddedData, 0))
            {
                Destroy(padded);
                return source;
            }
            return padded;
        }

        IEnumerator LoadMusic(byte[] bytes, string extension, double leadingSilenceSeconds = 0)
        {
            musicLoadSucceeded = false;
            music.clip = null;
            string path;
            var audioCacheReady = true;
            try
            {
                var cache = Path.Combine(Application.persistentDataPath, "AudioCache");
                Directory.CreateDirectory(cache);
                var hash = LocalChartLibrary.Sha256(bytes);
                path = Path.Combine(cache, hash + (string.IsNullOrEmpty(extension) ? ".mp3" : extension));
                if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            }
            catch (Exception)
            {
                path = null;
                audioCacheReady = false;
            }
            if (!audioCacheReady) yield break;
            var type = extension?.ToLowerInvariant() switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                ".m4a" or ".aac" => AudioType.ACC,
                ".flac" => AudioType.UNKNOWN,
                _ => AudioType.MPEG,
            };
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            if (request.downloadHandler is DownloadHandlerAudioClip audioHandler) audioHandler.streamAudio = false;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            try
            {
                var decodedClip = DownloadHandlerAudioClip.GetContent(request);
                music.clip = PrependLeadingSilence(decodedClip, leadingSilenceSeconds);
                if (music.clip != decodedClip && decodedClip != null) Destroy(decodedClip);
                musicLoadSucceeded = music.clip != null;
            }
            catch (Exception)
            {
                music.clip = null;
            }
        }

        void StartGame()
        {
            if (GugarythmSceneRouter.IsLibrary)
            {
                if (loading || selectedLibraryEntry == null) return;
                if (!LocalChartLibrary.TryReadSource(selectedLibraryEntry, out var bytes))
                {
                    SetStatus("找不到已儲存的 GGR 檔案。請重新匯入。");
                    return;
                }

                if (!ChartSelectionSession.Ensure().SetSelection(selectedLibraryEntry, bytes))
                {
                    SetStatus("無法準備所選譜面。");
                    return;
                }

                GugarythmSceneRouter.OpenGameplay();
                return;
            }

            StartGameplay();
        }

        void StartGameplay()
        {
            if (loading || chart == null || music.clip == null) return;
            CancelResumeCountdown();
            ResetRuntime();
            menuPanel.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(true);
            running = true;
            paused = false;
            accumulatedPause = 0;
            Interlocked.Exchange(ref audioDeviceChangePending, 0);
            resumeNeedsAudioReschedule = false;
            effects.UnPause();
            holdEffects.UnPause();
            var playbackReadyDsp = AudioSettings.dspTime + .25d;
            var initialSongTime = InitialWaterfallSongTime(FirstWaterfallVisualTime(chart), chart.BgmOffset,
                audioOffsetSeconds, NoteApproachDurationSeconds, InitialOffscreenLeadSeconds);
            scheduledDsp = playbackReadyDsp - chart.BgmOffset - initialSongTime;
            music.time = 0;
            // Prebuild every chart object at its off-screen perspective
            // position before the scheduled audio begins. Objects then move
            // through the track instead of being created at the screen edge.
            lastObservedSongTime = CurrentSongTime();
            UpdateVisuals(lastObservedSongTime + visualOffsetSeconds);
            music.PlayScheduled(scheduledDsp + audioOffsetSeconds);
            if (stageSound != null) effects.PlayOneShot(stageSound, .72f);
            ShowJudgment("", Color.white);
        }

        void PauseGame()
        {
            if (!running || paused) return;
            paused = true;
            pauseDsp = AudioSettings.dspTime;
            music.Pause();
            effects.Pause();
            holdEffects.Pause();
            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            pauseTitle.text = "暫停";
            pauseButton.gameObject.SetActive(false);
            pauseMenuContent.gameObject.SetActive(true);
            resumeCountdownLabel.gameObject.SetActive(false);
            pauseOverlay.gameObject.SetActive(true);
        }

        public static bool ShouldPauseForAudioConfigurationChange(bool deviceWasChanged, bool isRunning, bool isPaused) =>
            deviceWasChanged && isRunning && !isPaused;

        void HandleAudioConfigurationChanged(bool deviceWasChanged)
        {
            if (deviceWasChanged) Interlocked.Exchange(ref audioDeviceChangePending, 1);
        }

        void PauseForAudioDeviceChange()
        {
            if (!running || paused) return;
            interruptedSongTime = lastObservedSongTime;
            resumeNeedsAudioReschedule = true;
            paused = true;
            music.Stop();
            effects.Stop();
            ClearHoldSound();
            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            pauseTitle.text = "音訊裝置已變更\n請重新同步";
            pauseButton.gameObject.SetActive(false);
            pauseMenuContent.gameObject.SetActive(true);
            resumeCountdownLabel.gameObject.SetActive(false);
            pauseOverlay.gameObject.SetActive(true);
        }

        void ContinueGame()
        {
            if (!running || !paused || resumeCoroutine != null) return;
            resumeCoroutine = StartCoroutine(ResumeAfterCountdown());
        }

        IEnumerator ResumeAfterCountdown()
        {
            pauseMenuContent.gameObject.SetActive(false);
            resumeCountdownLabel.gameObject.SetActive(true);
            for (var count = 3; count >= 1; count--)
            {
                resumeCountdownLabel.text = count.ToString();
                yield return new WaitForSecondsRealtime(1);
            }

            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            if (AudioDeviceRecovery.ShouldRescheduleAfterAudioInterruption(resumeNeedsAudioReschedule))
            {
                var nextDsp = AudioSettings.dspTime + .25;
                var clipTime = AudioDeviceRecovery.ClipTimeForChartTime(interruptedSongTime, chart.BgmOffset, audioOffsetSeconds, music.clip.length);
                var playbackDsp = AudioDeviceRecovery.PlaybackDspForChartTime(nextDsp, interruptedSongTime, chart.BgmOffset, audioOffsetSeconds);
                music.Stop();
                music.time = clipTime;
                scheduledDsp = AudioDeviceRecovery.ScheduledDspForRecovery(nextDsp, interruptedSongTime, chart.BgmOffset);
                accumulatedPause = 0;
                music.PlayScheduled(playbackDsp);
                resumeNeedsAudioReschedule = false;
            }
            else
            {
                accumulatedPause += AudioSettings.dspTime - pauseDsp;
                music.UnPause();
            }
            effects.UnPause();
            holdEffects.UnPause();
            paused = false;
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(true);
            resumeCoroutine = null;
        }

        void RestartGame()
        {
            if (!running) return;
            CancelResumeCountdown();
            music.Stop();
            effects.Stop();
            ClearHoldSound();
            StartGame();
        }

        void ExitToMenu()
        {
            CancelResumeCountdown();
            running = false;
            paused = false;
            Interlocked.Exchange(ref audioDeviceChangePending, 0);
            resumeNeedsAudioReschedule = false;
            music.Stop();
            effects.Stop();
            ClearHoldSound();
            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            ClearInputLaneFeedback();
            ReleaseAllViews();
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);
            RefreshHud();
            ShowJudgment("", Color.white);
            GugarythmSceneRouter.OpenLibrary();
        }

        void CancelResumeCountdown()
        {
            if (resumeCoroutine == null) return;
            StopCoroutine(resumeCoroutine);
            resumeCoroutine = null;
        }

        void ResetRuntime()
        {
            ClearHoldSound();
            foreach (var note in chart.Notes) note.Grade = JudgmentGrade.Pending;
            BuildHoldRenderState();
            scoreState.Reset();
            judgmentEngine = new JudgmentEngine(chart.Notes, scoreState);
            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            ClearInputLaneFeedback();
            ReleaseAllViews();
            RefreshHud();
        }

        void BuildHoldRenderState()
        {
            holdRoots.Clear();
            holdCheckpoints.Clear();
            foreach (var point in chart.Connectors.SelectMany(connector => new[] { connector.Start, connector.End })
                         .Where(point => point != null && point.HoldRootIndex == point.Index).Distinct())
                holdRoots[point.Index] = point;
            foreach (var note in chart.Notes.Where(note => note.HoldRootIndex >= 0))
            {
                if (note.HoldRootIndex == note.Index) holdRoots[note.Index] = note;
                if (note.HoldCheckpointSource == HoldCheckpointSource.None) continue;
                if (!holdCheckpoints.TryGetValue(note.HoldRootIndex, out var checkpoints))
                    holdCheckpoints[note.HoldRootIndex] = checkpoints = new List<RuntimeNote>();
                checkpoints.Add(note);
            }
            foreach (var checkpoints in holdCheckpoints.Values)
                checkpoints.Sort((left, right) => left.Time.CompareTo(right.Time));
        }

        double CurrentSongTime() => AudioSettings.dspTime - scheduledDsp - accumulatedPause - chart.BgmOffset;

        void CollectInput()
        {
            inputBatch.Clear();
            contacts.Clear();
            contactPaths.Clear();
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
            var seen = new HashSet<int>();
            foreach (var touch in Touch.activeTouches)
            {
                var id = touch.touchId;
                var eventTime = InputEventSongTime(touch.time);
                var isInInputBand = TryScreenToLane(touch.screenPosition, out var lane, out var gridRow);
                var wasTracking = touches.TryGetValue(id, out var memory);
                if (!ShouldContinueTrackedContact(wasTracking, isInInputBand)) continue;
                if (!isInInputBand)
                {
                    seen.Add(id);
                    lane = InputLaneAtCanvasX(ScreenToCanvasX(touch.screenPosition.x));
                    if (touch.time > memory.LastInputRecordTime + 1e-7)
                    {
                        if (touch.phase is UnityEngine.InputSystem.TouchPhase.Ended or UnityEngine.InputSystem.TouchPhase.Canceled)
                        {
                            contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                                memory.Lane, lane, true));
                            virtualSlider.End(id, eventTime, lane, inputBatch);
                            touches.Remove(id);
                            continue;
                        }
                        if (Vector2.SqrMagnitude(touch.screenPosition - memory.ScreenPosition) > .01f)
                            contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                                memory.Lane, lane, false));
                        memory.LastInputRecordTime = touch.time;
                        memory.EventTime = eventTime;
                        memory.Lane = lane;
                        memory.ScreenPosition = touch.screenPosition;
                        touches[id] = memory;
                    }
                    contacts.Add(new ActiveContact(id, lane, memory.StartTime));
                    continue;
                }
                seen.Add(id);
                var entering = !wasTracking;
                if (entering)
                    memory = new TouchMemory { Lane = lane, GridRow = gridRow, EventTime = eventTime, StartTime = eventTime, LastInputRecordTime = double.NegativeInfinity };
                if (touch.time > memory.LastInputRecordTime + 1e-7)
                {
                    if (touch.phase is UnityEngine.InputSystem.TouchPhase.Ended or UnityEngine.InputSystem.TouchPhase.Canceled)
                    {
                        if (!entering)
                        {
                            contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                                memory.Lane, lane, true));
                            virtualSlider.End(id, eventTime, lane, inputBatch);
                        }
                    }
                    else if (entering || touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                        virtualSlider.Begin(id, eventTime, lane, inputBatch);
                    else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved && Vector2.SqrMagnitude(touch.screenPosition - memory.ScreenPosition) > .01f)
                    {
                        virtualSlider.Move(id, eventTime, lane, inputBatch);
                        if (memory.GridRow != gridRow)
                            inputBatch.Add(new InputToken(id, RuntimeNoteKind.Tap, eventTime, lane));
                        contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                            memory.Lane, lane, false));
                    }
                    memory.LastInputRecordTime = touch.time;
                    memory.EventTime = eventTime;
                    memory.Lane = lane;
                    memory.GridRow = gridRow;
                    memory.ScreenPosition = touch.screenPosition;
                    touches[id] = memory;
                }
                if (touch.phase is not UnityEngine.InputSystem.TouchPhase.Ended and not UnityEngine.InputSystem.TouchPhase.Canceled)
                    contacts.Add(new ActiveContact(id, lane, memory.StartTime));
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            CollectMouseAsTouch(seen);
#endif
            foreach (var id in touches.Keys.Where(id => !seen.Contains(id)).ToArray())
            {
                touches.Remove(id);
                virtualSlider.Cancel(id);
            }
        }

#if UNITY_EDITOR || UNITY_STANDALONE
        static void EnsureDesktopMouseAvailable()
        {
            if (TouchSimulation.instance != null && TouchSimulation.instance.enabled)
                TouchSimulation.Disable();
            if (Mouse.current != null && !Mouse.current.enabled)
                InputSystem.EnableDevice(Mouse.current);
        }

        void CollectMouseAsTouch(ISet<int> seen)
        {
            // Keep desktop testing on the same Input System backend as Android;
            // Android does not support the legacy/new "Both" configuration.
            var mouse = Mouse.current;
            if (mouse == null || !mouse.enabled) return;
            var pressed = mouse.leftButton.isPressed;
            var beganThisFrame = mouse.leftButton.wasPressedThisFrame;
            var endedThisFrame = mouse.leftButton.wasReleasedThisFrame;
            if (!pressed && !beganThisFrame && !endedThisFrame) return;

            var position = mouse.position.ReadValue();
            var eventTime = CurrentSongTime();
            var isInInputBand = TryScreenToLane(position, out var lane, out var gridRow);
            var wasTracking = touches.TryGetValue(MouseContactId, out var memory);
            if (!ShouldContinueTrackedContact(wasTracking, isInInputBand)) return;
            if (!isInInputBand)
            {
                lane = InputLaneAtCanvasX(ScreenToCanvasX(position.x));
                if (endedThisFrame)
                {
                    contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime, eventTime,
                        memory.Lane, lane, true));
                    virtualSlider.End(MouseContactId, eventTime, lane, inputBatch);
                    touches.Remove(MouseContactId);
                    return;
                }
                if (Vector2.SqrMagnitude(position - memory.ScreenPosition) > .01f)
                    contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime, eventTime,
                        memory.Lane, lane, false));
                memory.Lane = lane;
                memory.ScreenPosition = position;
                memory.EventTime = eventTime;
                memory.LastInputRecordTime = eventTime;
                touches[MouseContactId] = memory;
                if (pressed)
                {
                    seen.Add(MouseContactId);
                    contacts.Add(new ActiveContact(MouseContactId, lane, memory.StartTime));
                }
                return;
            }
            var began = !wasTracking;
            if (endedThisFrame && !began)
            {
                contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime, eventTime,
                    memory.Lane, lane, true));
                virtualSlider.End(MouseContactId, eventTime, lane, inputBatch);
                return;
            }
            if (began)
            {
                memory = new TouchMemory
                {
                    Lane = lane,
                    GridRow = gridRow,
                    ScreenPosition = position,
                    EventTime = eventTime,
                    StartTime = eventTime,
                    LastInputRecordTime = eventTime,
                };
                virtualSlider.Begin(MouseContactId, eventTime, lane, inputBatch);
            }
            else if (Vector2.SqrMagnitude(position - memory.ScreenPosition) > .01f)
            {
                virtualSlider.Move(MouseContactId, eventTime, lane, inputBatch);
                if (memory.GridRow != gridRow)
                    inputBatch.Add(new InputToken(MouseContactId, RuntimeNoteKind.Tap, eventTime, lane));
                contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime, eventTime,
                    memory.Lane, lane, false));
                memory.Lane = lane;
                memory.GridRow = gridRow;
                memory.ScreenPosition = position;
                memory.EventTime = eventTime;
                memory.LastInputRecordTime = eventTime;
            }

            touches[MouseContactId] = memory;
            if (!pressed) return;
            seen.Add(MouseContactId);
            contacts.Add(new ActiveContact(MouseContactId, lane, memory.StartTime));
        }
#endif

        double InputEventDspTime(double inputTime) =>
            AudioSettings.dspTime - (InputState.currentTime - inputTime);

        double InputEventSongTime(double inputTime) =>
            InputEventDspTime(inputTime) - scheduledDsp - accumulatedPause - chart.BgmOffset;

        static float ScreenToCanvasY(float screenY) => (screenY / Math.Max(1, Screen.height) - .5f) * CanvasHeight;
        static float ScreenToCanvasX(float screenX) => (screenX / Math.Max(1, Screen.width) - .5f) * ReferenceWidth;

        bool TryScreenToLane(Vector2 screenPosition, out float lane, out int gridRow)
        {
            var canvasY = ScreenToCanvasY(screenPosition.y);
            if (!IsJudgmentInputBand(canvasY, CanvasHeight))
            {
                lane = default;
                gridRow = default;
                return false;
            }
            // The visible input region intentionally fills the canvas width:
            // canvas left/right are the two outer virtual-slider lanes.
            var canvasX = ScreenToCanvasX(screenPosition.x);
            lane = InputLaneAtCanvasX(canvasX);
            gridRow = JudgmentInputGridRow(canvasY, CanvasHeight);
            return true;
        }

        void OnJudgment(JudgmentEvent judgment)
        {
            var color = judgment.Grade switch
            {
                JudgmentGrade.Perfect => new Color(.65f, 1f, 1f),
                JudgmentGrade.Great => new Color(1f, .84f, .38f),
                JudgmentGrade.Good => new Color(.52f, 1f, .66f),
                _ => new Color(1f, .34f, .55f),
            };
            ShowJudgment(judgment.Grade.ToString().ToUpperInvariant(), color);
            PlayJudgmentSound(judgment);
            if (judgment.Grade != JudgmentGrade.Miss)
            {
                SpawnHitParticle(judgment.Note);
            }
        }

        void PlayJudgmentSound(JudgmentEvent judgment)
        {
            var wasPlaying = holdAudioState.ShouldPlay;
            var route = holdAudioState.Route(judgment);
            if (wasPlaying != holdAudioState.ShouldPlay)
                TransitionHoldSound(holdAudioState.ShouldPlay);

            if (effects == null) return;
            AudioClip clip;
            if ((route & JudgmentAudioRoute.FlickOneShot) != 0)
                clip = judgment.Note.Critical && criticalFlickSound != null ? criticalFlickSound : flickSound;
            else if ((route & JudgmentAudioRoute.PerfectOneShot) != 0)
                clip = perfectSound;
            else if ((route & JudgmentAudioRoute.GradeOneShot) != 0)
                clip = judgment.Grade switch
            {
                JudgmentGrade.Perfect => perfectSound,
                JudgmentGrade.Great => greatSound,
                JudgmentGrade.Good => goodSound,
                _ => null,
            };
            else clip = null;
            if (clip != null) effects.PlayOneShot(clip, .78f);
        }

        void TransitionHoldSound(bool shouldPlay)
        {
            CancelHoldSoundFade();
            if (holdEffects == null) return;
            if (shouldPlay)
            {
                if (holdSound == null) return;
                if (!holdEffects.isPlaying)
                {
                    if (holdEffects.clip != holdSound) holdEffects.clip = holdSound;
                    holdEffects.volume = 0;
                    holdEffects.Play();
                }
                holdFadeCoroutine = StartCoroutine(FadeHoldSound(HoldLoopVolume, false));
            }
            else if (holdEffects.isPlaying)
                holdFadeCoroutine = StartCoroutine(FadeHoldSound(0, true));
            else
                holdEffects.volume = 0;
        }

        IEnumerator FadeHoldSound(float targetVolume, bool stopWhenSilent)
        {
            var startVolume = holdEffects.volume;
            var elapsed = 0f;
            while (elapsed < HoldLoopFadeDuration)
            {
                if (paused)
                {
                    yield return null;
                    continue;
                }
                elapsed += Time.unscaledDeltaTime;
                holdEffects.volume = Mathf.Lerp(startVolume, targetVolume,
                    Mathf.Clamp01(elapsed / HoldLoopFadeDuration));
                yield return null;
            }
            holdEffects.volume = targetVolume;
            if (stopWhenSilent && !holdAudioState.ShouldPlay) holdEffects.Stop();
            holdFadeCoroutine = null;
        }

        void CancelHoldSoundFade()
        {
            if (holdFadeCoroutine == null) return;
            StopCoroutine(holdFadeCoroutine);
            holdFadeCoroutine = null;
        }

        void ClearHoldSound()
        {
            holdAudioState.Clear();
            CancelHoldSoundFade();
            if (holdEffects == null) return;
            holdEffects.Stop();
            holdEffects.clip = null;
            holdEffects.volume = 0;
        }

        void UpdateVisuals(double visualTime)
        {
            renderedPersistentHoldHeads.Clear();
            foreach (var guide in chart.Guides)
            {
                var headApproach = ApproachProgress(guide.Head.Time, visualTime, guide.Head.TimeScaleGroup);
                var tailApproach = ApproachProgress(guide.Tail.Time, visualTime, guide.Tail.TimeScaleGroup);
                var headY = ScreenY(PerspectiveProgress(headApproach));
                var show = HasVisibleDecorationSegment(headApproach, tailApproach);
                if (!show)
                {
                    if (guideViews.TryGetValue(guide, out var oldGuide)) ReleaseGuide(guide, oldGuide);
                    continue;
                }
                if (!guideViews.TryGetValue(guide, out var guideLine))
                {
                    guideLine = AcquireGuide();
                    guideViews[guide] = guideLine;
                    guideLine.color = GuideColor(guide.Color);
                }
                SetGuidePath(guideLine, guide, visualTime, headApproach, tailApproach);
            }

            foreach (var simLine in chart.SimLines)
            {
                var aApproach = ApproachProgress(simLine.A, visualTime);
                var bApproach = ApproachProgress(simLine.B, visualTime);
                var aScreen = PerspectiveProgress(aApproach);
                var bScreen = PerspectiveProgress(bApproach);
                var aY = ScreenY(aScreen);
                var bY = ScreenY(bScreen);
                var leadingApproach = Mathf.Max(aApproach, bApproach);
                var trailingApproach = Mathf.Min(aApproach, bApproach);
                var visible = HasVisibleDecorationSegment(leadingApproach, trailingApproach);
                if (!visible)
                {
                    if (simLineViews.TryGetValue(simLine, out var oldLine)) ReleaseSimLine(simLine, oldLine);
                    continue;
                }
                if (!simLineViews.TryGetValue(simLine, out var line))
                {
                    line = AcquireSimLine();
                    simLineViews[simLine] = line;
                }
                if (aApproach > 1 || bApproach > 1)
                {
                    var clippedProgress = Mathf.InverseLerp(aApproach, bApproach, 1);
                    var clippedLane = Mathf.Lerp(simLine.A.Lane, simLine.B.Lane, clippedProgress);
                    if (aApproach > 1) { aScreen = 1; aY = HitY; }
                    else { bScreen = 1; bY = HitY; }
                    if (aApproach > 1)
                        line.SetGeometry(new Vector2(X(clippedLane, aScreen), aY), new Vector2(X(simLine.B.Lane, bScreen), bY),
                            Mathf.Lerp(.65f, 2.25f, Mathf.Clamp01((aScreen + bScreen) * .5f)));
                    else
                        line.SetGeometry(new Vector2(X(simLine.A.Lane, aScreen), aY), new Vector2(X(clippedLane, bScreen), bY),
                            Mathf.Lerp(.65f, 2.25f, Mathf.Clamp01((aScreen + bScreen) * .5f)));
                    continue;
                }
                var depth = Mathf.Clamp01((aScreen + bScreen) * .5f);
                line.SetGeometry(
                    new Vector2(X(simLine.A.Lane, aScreen), aY),
                    new Vector2(X(simLine.B.Lane, bScreen), bY),
                    Mathf.Lerp(.65f, 2.25f, depth));
            }

            foreach (var note in chart.Notes)
            {
                var approachProgress = ApproachProgress(note, visualTime);
                var screenProgress = PerspectiveProgress(approachProgress);
                var y = ScreenY(screenProgress);
                var visible = note.Visible && y >= NoteExitY &&
                    !ShouldHideHoldHead(note, approachProgress);
                if (!visible)
                {
                    if (noteViews.TryGetValue(note.Index, out var oldView)) ReleaseNoteView(note.Index, oldView);
                    continue;
                }
                if (!noteViews.TryGetValue(note.Index, out var view))
                {
                    view = AcquireNoteView(noteLayer);
                    noteViews[note.Index] = view;
                    ApplyNoteTexture(view, note);
                }
                var height = NoteSurfaceHeight(screenProgress);
                // The sprite has transparent side padding.  Expand only the
                // quad required for its visible body to meet the authored left
                // and right lane boundaries; centering the complete bitmap in
                // the lane made the visible key look too narrow.
                var bodyWidth = LaneWidth(note.Lane, note.Size, screenProgress);
                var renderWidth = NoteRenderQuadWidth(bodyWidth, height, note);
                if (note.HoldRootIndex == note.Index)
                    renderWidth = ClampInBoundsHoldHeadWidth(renderWidth, note.Lane, note.Size, screenProgress);
                var renderSize = note.Size * renderWidth / Mathf.Max(.001f, bodyWidth);
                ApplyNoteSurfaceQuad(view, BuildNoteSurfaceQuad(note.Lane, renderSize, screenProgress, height));
                view.color = IsHoldMid(note) ? Color.clear : Color.white;
                var traceParticle = view.transform.Find("Trace Particle")?.GetComponent<RawImage>();
                if (traceParticle != null)
                {
                    // Both official tick layouts use the same square as the
                    // note's depth-scaled height. Their textures distinguish
                    // the larger SlideTick from the smaller Trace diamond.
                    var particleAspect = traceParticle.texture == null ? 1f :
                        traceParticle.texture.width / (float)Mathf.Max(1, traceParticle.texture.height);
                    traceParticle.rectTransform.sizeDelta = new Vector2(height * particleAspect, height);
                    traceParticle.color = Color.white;
                }
                var flickArrow = view.transform.Find("Flick Arrow")?.GetComponent<RawImage>();
                if (flickArrow != null && flickArrow.gameObject.activeSelf && flickArrow.texture != null)
                {
                    var spriteIndex = FlickSpriteIndex(note.Size);
                    var arrowBaseWidth = LaneWidth(note.Lane, Mathf.Min(note.Size, 3f) * .5f, screenProgress) * FlickArrowScale;
                    var logicalSize = FlickLogicalSizes[spriteIndex];
                    var arrowWidth = arrowBaseWidth * flickArrow.texture.width / logicalSize;
                    var arrowHeight = arrowBaseWidth * flickArrow.texture.height / logicalSize;
                    // The source layout is logically square, but its atlas crop
                    // is not. Applying the width expansion to both axes was the
                    // reason side arrows became huge and vertically stretched.
                    flickArrow.rectTransform.sizeDelta = new Vector2(arrowWidth, arrowHeight);
                    var animationProgress = Mathf.Repeat(Time.unscaledTime, .5f) / .5f;
                    var laneUnit = LaneWidth(0, 1f, screenProgress) * .5f;
                    flickArrow.rectTransform.anchoredPosition = new Vector2(
                        note.Direction * laneUnit * animationProgress,
                        arrowHeight * .5f + laneUnit * 2 * animationProgress);
                    flickArrow.color = new Color(1, 1, 1, 1 - animationProgress * animationProgress * animationProgress);
                }
            }

            foreach (var connector in chart.Connectors)
            {
                var startApproach = ApproachProgress(connector.Start, visualTime);
                var endApproach = ApproachProgress(connector.End, visualTime);
                var startScreen = PerspectiveProgress(startApproach);
                var endScreen = PerspectiveProgress(endApproach);
                var startY = ScreenY(startScreen);
                var endY = ScreenY(endScreen);
                var holdMode = ResolveConnectorRenderMode(connector);
                var show = holdMode == HoldConnectorRenderMode.AnchorClipped
                    ? endApproach < JudgmentBottomApproach
                    : endY >= NoteExitY;
                if (!show)
                {
                    if (connectorViews.TryGetValue(connector, out var old)) ReleaseConnector(connector, old);
                    continue;
                }
                if (!connectorViews.TryGetValue(connector, out var line))
                {
                    line = AcquireConnector();
                    connectorViews[connector] = line;
                    line.texture = connector.Critical ? holdYellowConnectorTexture : holdGreenConnectorTexture;
                    // The atlas already carries 0.8 center / 0.4 shoulder
                    // alpha. The reference recording applies a further slide
                    // opacity of about 0.62, yielding a ~0.5 center opacity.
                    line.color = new Color(1, 1, 1, .62f);
                }
                line.material = IsHoldCurrentlyMissed(connector) ? missedHoldMaterial : null;
                SetConnectorPath(line, connector, visualTime, startApproach, endApproach, holdMode);
                if (connector.Start.HoldRootIndex >= 0 && startApproach >= 1f && endApproach <= 1f &&
                    holdRoots.TryGetValue(connector.Start.HoldRootIndex, out var root) &&
                    ShouldRenderPersistentHoldHead(root))
                {
                    var headT = FindConnectorProgress(connector, visualTime, 1f, startApproach, endApproach);
                    RenderPersistentHoldHead(root, connector, headT);
                }
            }
            foreach (var pair in persistentHoldHeadViews.ToArray())
                if (!renderedPersistentHoldHeads.Contains(pair.Key)) ReleasePersistentHoldHead(pair.Key, pair.Value);
        }

        void RenderPersistentHoldHead(RuntimeNote root, RuntimeConnector connector, float progress)
        {
            var rootIndex = root.Index;
            renderedPersistentHoldHeads.Add(rootIndex);
            if (!persistentHoldHeadViews.TryGetValue(rootIndex, out var view))
            {
                view = AcquireNoteView(persistentHoldHeadLayer);
                persistentHoldHeadViews[rootIndex] = view;
                var trace = ShouldUseTracePersistentHoldVisual(root);
                var traceKey = root.Critical ? "yellow" : "mint";
                view.texture = trace
                    ? traceTextures.TryGetValue(traceKey, out var traceTexture) ? traceTexture : null
                    : buttonTextures.TryGetValue(root.Critical ? "yellow" : "mint", out var holdTexture) ? holdTexture : null;
                view.color = Color.white;
                view.capRatio = NoteCapRatio;
                var particle = view.transform.Find("Trace Particle")?.GetComponent<RawImage>();
                if (particle != null) particle.gameObject.SetActive(false);
                var flickArrow = view.transform.Find("Flick Arrow")?.GetComponent<RawImage>();
                if (flickArrow != null) flickArrow.gameObject.SetActive(false);
            }
            var laneProgress = EaseConnector(progress, connector.Ease);
            var lane = Mathf.Lerp(connector.Start.Lane, connector.End.Lane, laneProgress);
            var size = Mathf.Lerp(connector.Start.Size, connector.End.Size, laneProgress);
            var screenProgress = PerspectiveProgress(1f);
            var height = NoteSurfaceHeight(screenProgress);
            // Match the descending head's visible body to the same pair of
            // lane boundaries at the judgment line.
            var bodyWidth = LaneWidth(lane, size, screenProgress);
            var renderWidth = HoldHeadRenderQuadWidth(bodyWidth, height, root.Critical);
            renderWidth = ClampInBoundsHoldHeadWidth(renderWidth, lane, size, screenProgress);
            var renderSize = size * renderWidth / Mathf.Max(.001f, bodyWidth);
            ApplyNoteSurfaceQuad(view, BuildNoteSurfaceQuad(lane, renderSize, screenProgress, height));
        }

        static float ClampInBoundsHoldHeadWidth(float renderWidth, float lane, float size, float screenProgress)
        {
            // Authored notes may deliberately extend beyond the playable lanes.
            // Only constrain heads whose authored body is fully inside the
            // visible ±6.5 track bounds.  Deliberate chart out-of-bounds paths
            // retain their original extent.
            if (lane - size < -VisibleTrackLaneEdge || lane + size > VisibleTrackLaneEdge) return renderWidth;
            var center = X(lane, screenProgress);
            var left = X(-VisibleTrackLaneEdge, screenProgress);
            var right = X(VisibleTrackLaneEdge, screenProgress);
            return Mathf.Min(renderWidth, 2f * Mathf.Min(center - left, right - center));
        }

        bool IsHoldCurrentlyMissed(RuntimeConnector connector)
        {
            if (connector?.Start == null || connector.Start.HoldRootIndex < 0) return false;
            var latestGrade = holdRoots.TryGetValue(connector.Start.HoldRootIndex, out var root)
                ? root.Grade : JudgmentGrade.Pending;
            if (holdCheckpoints.TryGetValue(connector.Start.HoldRootIndex, out var checkpoints))
                foreach (var checkpoint in checkpoints)
                    if (checkpoint.Grade != JudgmentGrade.Pending) latestGrade = checkpoint.Grade;
            return latestGrade == JudgmentGrade.Miss;
        }

        void SetGuidePath(TaperedConnectorGraphic line, RuntimeGuide guide, double visualTime, float headApproach, float tailApproach)
        {
            var approachSpan = headApproach - tailApproach;
            var nearT = approachSpan <= 1e-5f ? 0 : FindGuideProgress(guide, visualTime, 1f, headApproach, tailApproach);
            var farT = approachSpan <= 1e-5f ? 1 : FindGuideProgress(guide, visualTime, 0f, headApproach, tailApproach);
            var sampleCount = BuildStablePathSamples(nearT, farT);
            line.BeginPath(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var t = connectorPathSamples[index];
                var lane = InterpolateGuide(guide, t, point => point.Lane);
                var size = Mathf.Max(.01f, InterpolateGuide(guide, t, point => point.Size));
                var approach = GuideApproach(guide, visualTime, t);
                var screenProgress = Mathf.Clamp(PerspectiveProgress(approach), 0, NearTrackProgress);
                var alpha = Mathf.Lerp(guide.HeadOpacity, guide.TailOpacity, t);
                line.SetPathPoint(index, new Vector2(X(lane, screenProgress), ScreenY(screenProgress)), LaneWidth(lane, size, screenProgress), alpha);
            }
            line.EndPath();
        }

        float FindGuideProgress(RuntimeGuide guide, double visualTime, float target, float headApproach, float tailApproach)
        {
            if (target >= headApproach) return 0;
            if (target <= tailApproach) return 1;
            var low = 0f;
            var high = 1f;
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var middle = (low + high) * .5f;
                if (GuideApproach(guide, visualTime, middle) > target) low = middle;
                else high = middle;
            }
            return (low + high) * .5f;
        }

        float GuideApproach(RuntimeGuide guide, double visualTime, float progress)
        {
            var time = guide.Head.Time + (guide.Tail.Time - guide.Head.Time) * progress;
            var group = string.IsNullOrEmpty(guide.Head.TimeScaleGroup)
                ? guide.Tail.TimeScaleGroup : guide.Head.TimeScaleGroup;
            return ApproachProgress(time, visualTime, group);
        }

        static float InterpolateGuide(RuntimeGuide guide, float progress, Func<RuntimeGuidePoint, float> value)
        {
            if (guide.Ease != -1)
                return Mathf.Lerp(value(guide.Head), value(guide.Tail), EaseConnector(progress, guide.Ease));

            // Spline guides use the surrounding start/end points as tangents.
            // They are decoration geometry only and may intentionally leave the
            // central lane range, so do not clamp the resulting lane value.
            var p0 = value(guide.Start);
            var p1 = value(guide.Head);
            var p2 = value(guide.Tail);
            var p3 = value(guide.End);
            var t2 = progress * progress;
            var t3 = t2 * progress;
            return .5f * ((2 * p1) + (-p0 + p2) * progress + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
        }

        static Color GuideColor(int color) => color switch
        {
            4 => new Color(214 / 255f, 179 / 255f, 98 / 255f, .32f),
            3 => new Color(214 / 255f, 115 / 255f, 123 / 255f, .32f),
            2 => new Color(115 / 255f, 165 / 255f, 214 / 255f, .32f),
            1 => new Color(214 / 255f, 115 / 255f, 205 / 255f, .32f),
            5 => new Color(115 / 255f, 214 / 255f, 205 / 255f, .32f),
            6 => new Color(28 / 255f, 34 / 255f, 48 / 255f, .32f),
            _ => new Color(115 / 255f, 214 / 255f, 157 / 255f, .32f),
        };

        void SetConnectorPath(TaperedConnectorGraphic line, RuntimeConnector connector, double visualTime, float startApproach, float endApproach,
            HoldConnectorRenderMode holdMode)
        {
            var approachSpan = startApproach - endApproach;
            if (approachSpan <= 1e-5f)
            {
                line.BeginPath(2);
                SetConnectorPoint(line, 0, connector, 0, startApproach);
                SetConnectorPoint(line, 1, connector, 1, endApproach);
                line.EndPath();
                return;
            }

            var nearApproach = holdMode == HoldConnectorRenderMode.AnchorClipped ? 1f : NearTrackApproach;
            var nearT = FindConnectorProgress(connector, visualTime, nearApproach, startApproach, endApproach);
            var farT = FindConnectorProgress(connector, visualTime, 0f, startApproach, endApproach);
            var sampleCount = BuildStablePathSamples(nearT, farT);
            line.BeginPath(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var t = connectorPathSamples[index];
                SetConnectorPoint(line, index, connector, t, ConnectorApproach(connector, visualTime, t));
            }
            line.EndPath();
        }

        float FindConnectorProgress(RuntimeConnector connector, double visualTime, float target, float startApproach, float endApproach)
        {
            if (target >= startApproach) return 0;
            if (target <= endApproach) return 1;
            var low = 0f;
            var high = 1f;
            for (var iteration = 0; iteration < 20; iteration++)
            {
                var middle = (low + high) * .5f;
                if (ConnectorApproach(connector, visualTime, middle) > target) low = middle;
                else high = middle;
            }
            return (low + high) * .5f;
        }

        float ConnectorApproach(RuntimeConnector connector, double visualTime, float progress)
        {
            var time = connector.Start.Time + (connector.End.Time - connector.Start.Time) * progress;
            var group = string.IsNullOrEmpty(connector.Start.TimeScaleGroup)
                ? connector.End.TimeScaleGroup : connector.Start.TimeScaleGroup;
            return ApproachProgress(time, visualTime, group);
        }

        int BuildStablePathSamples(float nearT, float farT)
        {
            nearT = Mathf.Clamp01(nearT);
            farT = Mathf.Clamp01(farT);
            if (farT < nearT) (nearT, farT) = (farT, nearT);

            var count = 0;
            connectorPathSamples[count++] = nearT;
            var firstBoundary = Mathf.FloorToInt(nearT * ConnectorPathSegments) + 1;
            var lastBoundary = Mathf.CeilToInt(farT * ConnectorPathSegments) - 1;
            for (var boundary = firstBoundary; boundary <= lastBoundary; boundary++)
                connectorPathSamples[count++] = boundary / (float)ConnectorPathSegments;
            connectorPathSamples[count++] = farT;
            return count;
        }

        static void SetConnectorPoint(TaperedConnectorGraphic line, int index, RuntimeConnector connector, float timeProgress, float approachProgress)
        {
            var laneProgress = EaseConnector(timeProgress, connector.Ease);
            var lane = Mathf.Lerp(connector.Start.Lane, connector.End.Lane, laneProgress);
            var size = Mathf.Lerp(connector.Start.Size, connector.End.Size, laneProgress);
            var screenProgress = Mathf.Clamp(PerspectiveProgress(approachProgress), 0, NearTrackProgress);
            // A Hold belongs to exactly its authored lane span.  Expanding the
            // connector for its texture's transparent shoulder made adjacent
            // Hold ribbons overlap even when their chart lanes did not.
            var laneWidth = HoldConnectorLaneWidth(LaneWidth(lane, size, screenProgress));
            line.SetPathPoint(index, new Vector2(X(lane, screenProgress), ScreenY(screenProgress)), laneWidth);
        }

        static float EaseConnector(float progress, int ease) => ease switch
        {
            1 => 1f - Mathf.Cos(progress * Mathf.PI * .5f),
            2 => Mathf.Sin(progress * Mathf.PI * .5f),
            3 => progress < .5f ? 2 * progress * progress : 1 - Mathf.Pow(-2 * progress + 2, 2) * .5f,
            _ => progress,
        };

        // approach=0 is the far spawn plane; approach=1 is the judgment edge.
        float ApproachProgress(RuntimeNote note, double visualTime) =>
            ApproachProgress(note.Time, visualTime, note.TimeScaleGroup);

        float ApproachProgress(double noteTime, double visualTime, string timeScaleGroup) =>
            1f - (float)((chart.VisualPosition(noteTime, timeScaleGroup) - chart.VisualPosition(visualTime, timeScaleGroup)) / ApproachDuration);

        // Perspective projection of constant-depth motion. The derivatives at
        // both boundaries are continued linearly to keep off-stage clipping
        // stable and avoid the singularity of an unbounded projective curve.
        static float PerspectiveProgress(float approachProgress)
        {
            if (approachProgress <= 0) return approachProgress / PerspectiveDepthRatio;
            if (approachProgress >= 1) return 1f + (approachProgress - 1f) * PerspectiveDepthRatio;
            return approachProgress / (PerspectiveDepthRatio - (PerspectiveDepthRatio - 1f) * approachProgress);
        }

        static float ScreenY(float screenProgress) => Mathf.LerpUnclamped(TopY, HitY, screenProgress);
        /// <summary>
        /// Keep note height on the same continuous depth function as the lane
        /// edges.  A note becomes smaller toward the vanishing point and grows
        /// smoothly all the way to the judgment edge without a near-track step.
        /// </summary>
        public static float NoteSurfaceHeight(float screenProgress)
        {
            var clampedProgress = Mathf.Clamp01(screenProgress);
            return LaneWidth(0, 1f, clampedProgress) * ButtonHeightRatio;
        }

        public static NoteSurfaceQuad BuildNoteSurfaceQuad(float lane, float size, float screenProgress, float height)
        {
            var centerY = ScreenY(screenProgress);
            var upperY = centerY + height * .5f;
            var lowerY = centerY - height * .5f;
            var upperProgress = ScreenProgressAtY(upperY);
            var lowerProgress = ScreenProgressAtY(lowerY);
            return new NoteSurfaceQuad(
                new Vector2(X(lane - size, upperProgress), upperY),
                new Vector2(X(lane + size, upperProgress), upperY),
                new Vector2(X(lane + size, lowerProgress), lowerY),
                new Vector2(X(lane - size, lowerProgress), lowerY));
        }

        static void ApplyNoteSurfaceQuad(HorizontalSlicedRawImage view, NoteSurfaceQuad quad)
        {
            var center = (quad.UpperLeft + quad.UpperRight + quad.LowerRight + quad.LowerLeft) * .25f;
            var width = Mathf.Max(quad.UpperRight.x - quad.UpperLeft.x, quad.LowerRight.x - quad.LowerLeft.x);
            var height = Mathf.Max(quad.UpperLeft.y - quad.LowerLeft.y, quad.UpperRight.y - quad.LowerRight.y);
            view.rectTransform.anchoredPosition = center;
            view.rectTransform.sizeDelta = new Vector2(width, height);
            view.SetSurfaceQuad(quad.UpperLeft - center, quad.UpperRight - center, quad.LowerRight - center, quad.LowerLeft - center);
        }

        static float ScreenProgressAtY(float y) => (TopY - y) / (TopY - HitY);
        public static bool HasVisibleDecorationSegment(float leadingApproach, float trailingApproach) => trailingApproach < 1f;
        public static bool ShouldHideJudgedVisual(JudgmentGrade grade, float approachProgress) =>
            approachProgress >= JudgmentBottomApproach && grade != JudgmentGrade.Pending;
        public static bool ShouldHideAttachedHoldParticle(RuntimeNote note, float approachProgress) =>
            note != null && IsHoldMid(note) && !note.Judged && note.HoldRootIndex >= 0 && approachProgress >= 1f;
        public static bool ShouldRenderPersistentHoldHead(RuntimeNote root) =>
            root != null && root.Visible && root.Judged;
        public static bool ShouldUseTracePersistentHoldVisual(RuntimeNote root) =>
            root != null && IsTrace(root);
        static bool ShouldHideHoldHead(RuntimeNote note, float approachProgress) =>
            ShouldHideAttachedHoldParticle(note, approachProgress)
                ? true
                : note.IsHoldTerminal
                ? approachProgress >= 1f
                : note.HoldRootIndex == note.Index
                ? approachProgress >= 1f
                : ShouldHideJudgedVisual(note.Grade, approachProgress);
        public static HoldConnectorRenderMode ResolveHoldConnectorRenderMode(bool rootSucceeded, JudgmentGrade nextCheckpointGrade) =>
            HoldConnectorRenderMode.AnchorClipped;
        public static bool ShouldClipHoldConnector(RuntimeConnector connector) =>
            connector != null &&
            ((connector.Start != null && connector.Start.HoldRootIndex >= 0) ||
             (connector.End != null && connector.End.HoldRootIndex >= 0));

        HoldConnectorRenderMode ResolveConnectorRenderMode(RuntimeConnector connector)
        {
            return ShouldClipHoldConnector(connector)
                ? HoldConnectorRenderMode.AnchorClipped
                : HoldConnectorRenderMode.NaturalPassThrough;
        }

        static bool IsJudged(RuntimeNote note) => note.Grade != JudgmentGrade.Pending;
        static float X(float lane, float screenProgress)
        {
            var sourceY = (TopY - ScreenY(screenProgress)) * LaneTextureHeight / CanvasHeight;
            var guide = Mathf.Clamp(Mathf.FloorToInt(lane + CentralHalfLanes), 0, LaneGuideIntercepts.Length - 2);
            var guideLane = -CentralHalfLanes + guide;
            var t = lane - guideLane;
            var left = LaneGuideIntercepts[guide] + LaneGuideSlopes[guide] * sourceY;
            var right = LaneGuideIntercepts[guide + 1] + LaneGuideSlopes[guide + 1] * sourceY;
            var sourceX = Mathf.LerpUnclamped(left, right, t);
            // The captured lane art is about 1.2 source pixels left of its
            // bitmap midpoint. Rebase chart lane zero to the actual viewport
            // midpoint instead of inheriting that crop offset.
            var sourceCenter = LaneGuideIntercepts[(int)CentralHalfLanes] +
                LaneGuideSlopes[(int)CentralHalfLanes] * sourceY;
            return (sourceX - sourceCenter) / LaneTextureWidth * ReferenceWidth;
        }

        static float LaneWidth(float lane, float size, float screenProgress) =>
            Mathf.Max(12, X(lane + size, screenProgress) - X(lane - size, screenProgress));

        static float ScreenXToLane(float canvasX, float screenProgress)
        {
            if (canvasX <= X(-CentralHalfLanes, screenProgress))
            {
                var left = X(-CentralHalfLanes, screenProgress);
                var span = X(-CentralHalfLanes + 1, screenProgress) - left;
                return -CentralHalfLanes + (canvasX - left) / span;
            }
            for (var guide = 0; guide < LaneGuideIntercepts.Length - 1; guide++)
            {
                var leftLane = -CentralHalfLanes + guide;
                var left = X(leftLane, screenProgress);
                var right = X(leftLane + 1, screenProgress);
                if (canvasX <= right) return leftLane + (canvasX - left) / (right - left);
            }
            var finalLeft = X(CentralHalfLanes - 1, screenProgress);
            var finalSpan = X(CentralHalfLanes, screenProgress) - finalLeft;
            return CentralHalfLanes - 1 + (canvasX - finalLeft) / finalSpan;
        }
        static bool IsTrace(RuntimeNote note)
        {
            var archetype = note.Archetype ?? string.Empty;
            // Trace slide heads/tails use the same slim body and diamond as
            // ordinary Trace notes. They are passive hold checkpoints rather
            // than full mint Tap/Release buttons.
            return archetype.IndexOf("Trace", StringComparison.OrdinalIgnoreCase) >= 0 ||
                archetype.StartsWith("USC Trace", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsHoldMid(RuntimeNote note) =>
            (note.Archetype ?? string.Empty).EndsWith("SlideTickNote", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Both Trace notes and USC Slide tick/attach particles use the child
        /// particle image. A hold mid deliberately has no parent body, so it
        /// must not be gated by the Trace-only visibility rule.
        /// </summary>
        public static bool ShouldShowNoteParticle(RuntimeNote note, bool hasParticleTexture) =>
            hasParticleTexture && note.Visible && (IsTrace(note) || IsHoldMid(note));

        static bool IsDamage(RuntimeNote note) =>
            (note.Archetype ?? string.Empty).IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0;

        static float NoteOuterPaddingPixels(RuntimeNote note)
        {
            if (IsDamage(note)) return 21f;
            if (IsTrace(note)) return note.Critical ? 30f : 41f;
            return note.Critical ? 28f : NormalButtonVisibleEdgePaddingPixels;
        }

        public static float NoteRenderQuadWidth(float bodyWidth, float height, RuntimeNote note)
        {
            if (note.HoldRootIndex == note.Index)
                return HoldHeadRenderQuadWidth(bodyWidth, height, note.Critical);
            var padding = height * NoteOuterPaddingPixels(note) / NoteTextureHeight;
            return bodyWidth + padding * 2;
        }

        public static float NoteBodyWidth(float quadWidth, float height, RuntimeNote note) =>
            quadWidth - height * NoteOuterPaddingPixels(note) / NoteTextureHeight * 2;

        public static float HoldHeadRenderQuadWidth(float bodyWidth, float height, bool critical)
        {
            return bodyWidth * HoldHeadTextureWidth / HoldHeadCoreTextureWidth;
        }

        public static float HoldHeadVisibleCoreWidth(float renderWidth) =>
            renderWidth * HoldHeadCoreTextureWidth / HoldHeadTextureWidth;

        public static float HoldConnectorRenderWidth(float bodyWidth) =>
            bodyWidth * HoldConnectorTextureWidth / HoldConnectorVisibleTextureWidth;

        public static float HoldConnectorLaneWidth(float bodyWidth) => bodyWidth;
        public static float HoldConnectorSourceUvInset => HoldConnectorVisibleUvInset;

        public static float HoldConnectorRenderWidth(float bodyWidth, float lane, float size, float screenProgress)
        {
            var headQuadWidth = HoldHeadRenderQuadWidth(bodyWidth, 0, false);
            var clippedHeadQuadWidth = ClampInBoundsHoldHeadWidth(headQuadWidth, lane, size, screenProgress);
            return HoldConnectorRenderWidth(HoldHeadVisibleCoreWidth(clippedHeadQuadWidth));
        }

        public static float HoldConnectorVisibleBodyWidth(float renderWidth) =>
            renderWidth * HoldConnectorVisibleTextureWidth / HoldConnectorTextureWidth;

        void FinishGame()
        {
            CancelResumeCountdown();
            running = false;
            paused = false;
            music.Stop();
            ClearHoldSound();
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(false);
            ReleaseAllViews();
            RefreshHud();
            if (currentLibraryEntry != null)
                LocalChartLibrary.UpdateBestAccuracy(currentLibraryEntry.Id, (float)scoreState.AccuracyPercent(chart.PlayableCount));
            resultPanel.gameObject.SetActive(true);
            resultText.text = $"ACCURACY  {scoreState.AccuracyPercent(chart.PlayableCount):F4}%\n\nMAX COMBO  {scoreState.MaxCombo:N0}\n\nPERFECT  {scoreState.Perfect:N0}\nGREAT  {scoreState.Great:N0}\nGOOD  {scoreState.Good:N0}\nMISS  {scoreState.Miss:N0}";
        }

        void LoadArtwork()
        {
            backgroundTexture = Resources.Load<Texture2D>("Gugarhythm/background/gugarhythm-background");
            laneTexture = Resources.Load<Texture2D>("Gugarhythm/lane/gugarhythm-lane");
            foreach (var name in new[] { "purple", "cyan", "mint", "white", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("Gugarhythm/official/buttons/button-" + name) ??
                    Resources.Load<Texture2D>("Gugarhythm/buttons/button-" + name);
                if (texture != null) buttonTextures[name] = texture;
            }
            foreach (var name in new[] { "mint", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("Gugarhythm/official/traces/trace-" + name) ??
                    Resources.Load<Texture2D>("Gugarhythm/traces/trace-" + name);
                if (texture != null) traceTextures[name] = texture;
            }
            damageTexture = Resources.Load<Texture2D>("Gugarhythm/official/damage/damage-purple") ??
                Resources.Load<Texture2D>("Gugarhythm/damage/damage-purple");
            for (var index = 0; index < 6; index++)
            {
                var suffix = (index + 1).ToString();
                flickNormalCenterTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-normal-center-" + suffix);
                flickNormalSideTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-normal-side-" + suffix);
                flickCriticalCenterTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-critical-center-" + suffix);
                flickCriticalSideTextures[index] = Resources.Load<Texture2D>("Gugarhythm/flicks/flick-critical-side-" + suffix);
            }
            holdGreenConnectorTexture = Resources.Load<Texture2D>("Gugarhythm/connectors/hold-green");
            holdYellowConnectorTexture = Resources.Load<Texture2D>("Gugarhythm/connectors/hold-yellow");
            holdMidMintTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/slide-tick-mint") ??
                Resources.Load<Texture2D>("Gugarhythm/particles/hold-mid-mint");
            holdMidYellowTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/slide-tick-yellow") ??
                Resources.Load<Texture2D>("Gugarhythm/particles/hold-mid-yellow");
            traceDiamondMintTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/trace-diamond-mint");
            traceDiamondPinkTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/trace-diamond-pink");
            traceDiamondYellowTexture = Resources.Load<Texture2D>("Gugarhythm/official/particles/trace-diamond-yellow");
            perfectSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/perfect");
            greatSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/great");
            goodSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/good");
            holdSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/hold-loop");
            flickSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/flick");
            criticalFlickSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/critical-flick");
            stageSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/stage");
        }

        void BuildInterface()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            var gameCamera = cameraObject.GetComponent<Camera>();
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            gameCamera.backgroundColor = Color.black;
            cameraObject.transform.position = new Vector3(0, 0, -10);
            gameCamera.orthographic = true;
            // Unity 6 reports "No cameras rendering" when the only camera has
            // an empty culling mask, even though the game uses an overlay
            // canvas. Keep a normal camera active for a stable Game/Android view.
            gameCamera.cullingMask = ~0;
            music = gameObject.AddComponent<AudioSource>();
            music.playOnAwake = false; music.spatialBlend = 0;
            effects = gameObject.AddComponent<AudioSource>();
            effects.playOnAwake = false; effects.spatialBlend = 0;
            holdEffects = gameObject.AddComponent<AudioSource>();
            holdEffects.playOnAwake = false;
            holdEffects.spatialBlend = 0;
            holdEffects.loop = true;
            holdEffects.volume = 0;
            for (var index = 0; index < calibrationTickSources.Length; index++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0;
                source.volume = .65f;
                source.clip = perfectSound;
                calibrationTickSources[index] = source;
            }
            var canvasObject = new GameObject("Rhythm Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var eventSystemObject = new GameObject("Event System", typeof(EventSystem));
            // A module created entirely at runtime has no UI action asset until
            // defaults are assigned explicitly. Use the Input System module on
            // every platform: StandaloneInputModule reads UnityEngine.Input and
            // throws every frame when active input handling is Input System only.
            var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
            inputModule.pointerBehavior = UIPointerBehavior.SingleMouseOrPenButMultiTouchAndTrack;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            canvasRoot = canvasObject.GetComponent<RectTransform>();
            var root = canvasRoot;
            Panel("Base", root, new Color(.015f, .02f, .06f), Vector2.zero, Vector2.zero, true);
            backgroundLayer = RawPanel("Background", root, backgroundTexture, new Color(1, 1, 1, .72f), Vector2.zero, Vector2.zero, true);
            stage = Panel("Rhythm Stage", root, new Color(0, 0, 0, .05f), Vector2.zero, Vector2.zero, true);
            var trackObject = new GameObject("Track Depth", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var trackRect = trackObject.GetComponent<RectTransform>(); trackRect.SetParent(stage, false); Fill(trackRect);
            var trackGraphic = trackObject.GetComponent<TaperedConnectorGraphic>(); trackGraphic.raycastTarget = false; trackGraphic.color = new Color(0, 0, .035f, .72f);
            trackGraphic.SetGeometry(
                new Vector2((X(-CentralHalfLanes, 0) + X(CentralHalfLanes, 0)) * .5f, TopY),
                new Vector2((X(-CentralHalfLanes, NearTrackProgress) + X(CentralHalfLanes, NearTrackProgress)) * .5f, NoteExitY),
                X(CentralHalfLanes, 0) - X(-CentralHalfLanes, 0),
                X(CentralHalfLanes, NearTrackProgress) - X(-CentralHalfLanes, NearTrackProgress));
            var lane = RawPanel("Perspective Lane", stage, laneTexture, new Color(1, 1, 1, .92f), Vector2.zero, Vector2.zero, true);
            var laneArtOffset = (LaneTextureWidth * .5f - LaneTextureCenterX) / LaneTextureWidth * ReferenceWidth;
            lane.offsetMin = new Vector2(laneArtOffset, 0);
            lane.offsetMax = new Vector2(laneArtOffset, 0);
            var laneShader = Shader.Find("Gugarythm/Black Transparent UI");
            if (laneShader != null)
            {
                laneMaterial = new Material(laneShader);
                lane.GetComponent<RawImage>().material = laneMaterial;
            }
            var missedHoldShader = Shader.Find("Gugarythm/Desaturate UI");
            if (missedHoldShader != null) missedHoldMaterial = new Material(missedHoldShader);
            BuildInputLaneFeedback(stage);
            guideLayer = Layer("Decoration Guides", stage);
            connectorLayer = Layer("Hold Connectors", stage);
            simLineLayer = Layer("Synchronization Lines", stage);
            persistentHoldHeadLayer = Layer("Persistent Hold Heads", stage);
            noteLayer = Layer("Notes", stage);
            safeAreaRoot = Layer("Safe Area UI", root);
            BuildHud(safeAreaRoot, root);
            BuildMenu(safeAreaRoot);
            BuildSettings(safeAreaRoot);
            BuildChartEditor(safeAreaRoot);
            BuildImportDecision(safeAreaRoot);
            BuildPauseOverlay(safeAreaRoot);
            BuildResult(safeAreaRoot);
            UpdateSafeAreaLayout(true);
            SetGameplayStageVisible(false);
        }

        void BuildInputLaneFeedback(RectTransform root)
        {
            var layer = Layer("Input Lane Feedback", root);
            for (var cell = 0; cell < inputLaneFeedback.Length; cell++)
            {
                var flash = new GameObject($"Input Lane Flash {cell + 1:00}", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic))
                    .GetComponent<TaperedConnectorGraphic>();
                flash.rectTransform.SetParent(layer, false);
                Fill(flash.rectTransform);
                flash.color = new Color(.25f, .25f, .28f, .42f);
                flash.drawGlow = false;
                flash.drawEdges = false;
                flash.fillAlphaScale = 1;
                flash.fillAlphaLimit = 1;
                flash.raycastTarget = false;

                var lane = VirtualSliderInput.MinimumLane + (cell + .5f) * InputLaneFeedbackWidth;
                var halfWidth = InputLaneFeedbackWidth * .5f;
                // A press only illuminates its judgment cell.  Drawing the
                // same track from the vanishing point to the foreground made
                // the feedback read as a large translucent reflection.
                var width = X(lane + halfWidth, 1f) - X(lane - halfWidth, 1f);
                flash.SetGeometry(
                    new Vector2(X(lane, 1f), InputLaneFeedbackBottom(CanvasHeight)),
                    new Vector2(X(lane, 1f), InputLaneFeedbackTop(CanvasHeight)),
                    width, width);
                flash.gameObject.SetActive(false);
                inputLaneFeedback[cell] = flash;
            }
        }

        void FlashInputLane(float lane)
        {
            var inputCell = InputLaneFeedbackCell(lane);
            if (inputCell < 0 || inputCell >= VirtualSliderInput.CellCount) return;
            var inputLeftLane = VirtualSliderInput.MinimumLane + inputCell * VirtualSliderInput.CellWidth;
            var inputRightLane = inputLeftLane + VirtualSliderInput.CellWidth;
            var inputLeft = CanvasXAtInputLane(inputLeftLane);
            var inputRight = CanvasXAtInputLane(inputRightLane);
            for (var track = 0; track < inputLaneFeedback.Length; track++)
            {
                var trackLane = VirtualSliderInput.MinimumLane + (track + .5f) * InputLaneFeedbackWidth;
                var halfWidth = InputLaneFeedbackWidth * .5f;
                // Highlight every real perspective lane covered by the flat
                // virtual judgment cell at the purple judgment-strip depth.
                var trackLeft = X(trackLane - halfWidth, 1f);
                var trackRight = X(trackLane + halfWidth, 1f);
                if (trackRight < inputLeft || trackLeft > inputRight) continue;
                inputLaneFeedbackUntil[track] = Mathf.Max(inputLaneFeedbackUntil[track], Time.unscaledTime + InputLaneFeedbackDuration);
                inputLaneFeedback[track].gameObject.SetActive(true);
            }
        }

        void UpdateInputLaneFeedback()
        {
            for (var cell = 0; cell < inputLaneFeedback.Length; cell++)
                if (inputLaneFeedback[cell] != null && inputLaneFeedback[cell].gameObject.activeSelf &&
                    Time.unscaledTime >= inputLaneFeedbackUntil[cell])
                    inputLaneFeedback[cell].gameObject.SetActive(false);
        }

        void ClearInputLaneFeedback()
        {
            for (var cell = 0; cell < inputLaneFeedback.Length; cell++)
            {
                inputLaneFeedbackUntil[cell] = 0;
                if (inputLaneFeedback[cell] != null) inputLaneFeedback[cell].gameObject.SetActive(false);
            }
        }

        void BuildHud(RectTransform root, RectTransform canvasRoot)
        {
            var accuracy = Panel("Accuracy", root, new Color(.04f, .08f, .20f, .72f), new Vector2(280, 72), Vector2.zero);
            PinToAnchor(accuracy, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24));
            Outline(accuracy.gameObject, new Color(.55f, .75f, 1f, .75f), 2);
            accuracyLabel = Label("ACCURACY  0.0000%", accuracy, 22); Fill(accuracyLabel.rectTransform);
            comboLabel = Label("COMBO\n0", root, 52); comboLabel.rectTransform.sizeDelta = new Vector2(360, 170);
            PinToAnchor(comboLabel.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-324, 80));
            comboLabel.gameObject.SetActive(false);
            // Judgment feedback belongs to the full-screen canvas rather than
            // the safe-area container, whose midpoint can shift on cutout devices.
            judgmentLabel = Label("", canvasRoot, 48); judgmentLabel.rectTransform.sizeDelta = new Vector2(620, 80);
            PinToAnchor(judgmentLabel.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero);
            pauseButton = MakeButton("暫停", root, new Vector2(-24, -24), PauseGame, new Vector2(150, 64));
            PinToAnchor(pauseButton.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -24));
            pauseButton.gameObject.SetActive(false);
        }

        void UpdateSafeAreaLayout(bool force = false)
        {
            if (safeAreaRoot == null || Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            if (!force && safe == appliedSafeArea) return;
            appliedSafeArea = safe;

            safeAreaRoot.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeAreaRoot.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;

            var logicalSafeSize = new Vector2(
                ReferenceWidth * safe.width / Screen.width,
                CanvasHeight * safe.height / Screen.height);
            // 曲庫是主畫面，不是置中的彈窗；必須覆蓋整個可用安全區域。
            if (menuPanel != null)
            {
                Fill(menuPanel);
                menuPanel.localScale = Vector3.one;
            }
            // Settings is a destination scene as well, so its root follows the
            // safe area exactly instead of retaining the old modal dimensions.
            if (settingsPanel != null)
            {
                Fill(settingsPanel);
                settingsPanel.localScale = Vector3.one;
            }
            FitOverlayPanel(importDecisionPanel, new Vector2(620, 420), logicalSafeSize);
            if (!GugarythmSceneRouter.IsSettings)
                FitOverlayPanel(calibrationPanel, new Vector2(520, 760), logicalSafeSize);
            FitOverlayPanel(pauseMenuContent, new Vector2(620, 520), logicalSafeSize);
            FitOverlayPanel(resultPanel, new Vector2(620, 650), logicalSafeSize);
        }

        static void FitOverlayPanel(RectTransform panel, Vector2 designSize, Vector2 available)
        {
            if (panel == null) return;
            const float safePadding = 32f;
            var scale = Mathf.Min(1f,
                Mathf.Max(.1f, (available.x - safePadding) / designSize.x),
                Mathf.Max(.1f, (available.y - safePadding) / designSize.y));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .5f);
            panel.pivot = new Vector2(.5f, .5f);
            panel.sizeDelta = designSize;
            panel.anchoredPosition = Vector2.zero;
            panel.localScale = Vector3.one * scale;
        }

        static void PinToAnchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
        }

        void BuildMenu(RectTransform root)
        {
            // The safe-area container intentionally stops at a device cutout.
            // Keep the library's left-column color behind it so the excluded
            // strip never reveals the gameplay stage underneath.
            var fullScreenRoot = root.parent as RectTransform;
            libraryBackdrop = Panel("Library Cutout Backdrop", fullScreenRoot, new Color(.16f, .16f, .16f, 1f), Vector2.zero, Vector2.zero, true);
            libraryBackdrop.SetSiblingIndex(root.GetSiblingIndex());
            libraryBackdrop.gameObject.SetActive(GugarythmSceneRouter.IsLibrary);
            menuPanel = Panel("Chart Library", root, new Color(.11f, .11f, .11f, 1f), new Vector2(1500, 820), Vector2.zero);
            Fill(menuPanel);
            menuPanel.localScale = Vector3.one;
            var library = Panel("Library Pane", menuPanel, new Color(.16f, .16f, .16f, 1f), Vector2.zero, Vector2.zero, true);
            library.anchorMin = new Vector2(0, 0); library.anchorMax = new Vector2(.244f, 1); library.offsetMin = Vector2.zero; library.offsetMax = Vector2.zero;
            var divider = Panel("Library Divider", menuPanel, new Color(.27f, .27f, .27f, 1f), Vector2.zero, Vector2.zero, true);
            divider.anchorMin = new Vector2(.244f, 0); divider.anchorMax = new Vector2(.244f, 1); divider.offsetMin = Vector2.zero; divider.offsetMax = new Vector2(1, 0); divider.GetComponent<Image>().raycastTarget = false;
            var detail = Panel("Detail Pane", menuPanel, new Color(.10f, .10f, .10f, 1f), Vector2.zero, Vector2.zero, true);
            detail.anchorMin = new Vector2(.244f, 0); detail.anchorMax = new Vector2(1, 1); detail.offsetMin = new Vector2(1, 0); detail.offsetMax = Vector2.zero;

            var brand = Label("GUGARYTHM", library, 19); brand.color = new Color(.68f, .68f, .68f); brand.alignment = TextAnchor.MiddleLeft; brand.rectTransform.sizeDelta = new Vector2(260, 36); PinToAnchor(brand.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -34));
            var heading = Label("譜面保管庫", library, 30); heading.alignment = TextAnchor.MiddleLeft; heading.rectTransform.sizeDelta = new Vector2(270, 50); PinToAnchor(heading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -74));
            var countBadge = Panel("Chart Count Badge", library, new Color(.24f, .24f, .24f), new Vector2(42, 42), Vector2.zero);
            PinToAnchor(countBadge, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-38, -42));
            var countBadgeText = Label("", countBadge, 18); Fill(countBadgeText.rectTransform); libraryCountLabel = countBadgeText;
            librarySearchInput = MakeInputField("搜尋", library, Vector2.zero, new Vector2(0, 56));
            var searchRect = librarySearchInput.GetComponent<RectTransform>(); searchRect.anchorMin = new Vector2(0, 1); searchRect.anchorMax = new Vector2(1, 1); searchRect.pivot = new Vector2(.5f, 1); searchRect.offsetMin = new Vector2(34, -204); searchRect.offsetMax = new Vector2(-22, -148);
            librarySearchInput.onValueChanged.AddListener(_ => RefreshLibraryUI());
            const int libraryHeaderFontSize = 22;
            const float librarySortCenterY = -250;
            librarySortLabel = Label("排序", library, libraryHeaderFontSize); librarySortLabel.color = new Color(.62f, .62f, .62f); librarySortLabel.alignment = TextAnchor.MiddleCenter; librarySortLabel.rectTransform.sizeDelta = new Vector2(72, 46); PinToAnchor(librarySortLabel.rectTransform, new Vector2(0, 1), new Vector2(0, .5f), new Vector2(28, librarySortCenterY));
            librarySortModeLabel = Label("準確率", library, libraryHeaderFontSize); librarySortModeLabel.color = new Color(.9f, .9f, .9f); librarySortModeLabel.alignment = TextAnchor.MiddleCenter; librarySortModeLabel.rectTransform.sizeDelta = new Vector2(112, 46); PinToAnchor(librarySortModeLabel.rectTransform, new Vector2(0, 1), new Vector2(0, .5f), new Vector2(112, librarySortCenterY));
            MakeInvisibleButton(librarySortModeLabel.rectTransform, CycleLibrarySort);
            libraryDirectionIcon = Panel("Sort Direction", library, Color.clear, new Vector2(58, 52), Vector2.zero);
            // Rotate around the icon centre so ascending and descending arrows share the same visual X position.
            PinToAnchor(libraryDirectionIcon, new Vector2(0, 1), new Vector2(.5f, .5f), new Vector2(248, librarySortCenterY));
            AddSortArrowIcon(libraryDirectionIcon);
            MakeInvisibleButton(libraryDirectionIcon, () => { librarySortAscending = !librarySortAscending; RefreshLibraryUI(); });
            libraryListContent = MakeVerticalScroll("Library Scroll", library, Vector2.zero, new Vector2(0, 0));
            var listRoot = libraryListContent.parent.GetComponent<RectTransform>(); listRoot.anchorMin = new Vector2(0, 0); listRoot.anchorMax = new Vector2(1, 1); listRoot.offsetMin = new Vector2(22, 100); listRoot.offsetMax = new Vector2(-22, -300);

            var importButton = MakeOutlinedButton("＋ 匯入 GGR", library, Vector2.zero, RequestImport, new Vector2(0, 64));
            var importRect = importButton.GetComponent<RectTransform>(); importRect.anchorMin = new Vector2(0, 0); importRect.anchorMax = new Vector2(1, 0); importRect.pivot = new Vector2(.5f, 0); importRect.offsetMin = new Vector2(22, 22); importRect.offsetMax = new Vector2(-22, 86);

            var breadcrumb = Label("LIBRARY   /   CHART DETAIL", detail, 18); breadcrumb.color = new Color(.64f, .64f, .64f); breadcrumb.alignment = TextAnchor.MiddleLeft; breadcrumb.rectTransform.sizeDelta = new Vector2(480, 38); PinToAnchor(breadcrumb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -52));
            var gear = MakeOutlinedButton("", detail, Vector2.zero, OpenSettings, new Vector2(66, 66));
            PinToAnchor(gear.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -48));
            AddGearIcon(gear.GetComponent<RectTransform>());
            var edit = MakeOutlinedButton("", detail, Vector2.zero, OpenChartEditor, new Vector2(66, 66));
            PinToAnchor(edit.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-126, -48));
            AddPencilIcon(edit.GetComponent<RectTransform>());
            var cover = Panel("Cover Placeholder", detail, new Color(.19f, .30f, .42f), new Vector2(495, 495), Vector2.zero);
            PinToAnchor(cover, new Vector2(.24f, .5f), new Vector2(.5f, .5f), new Vector2(0, -45));
            // Keep the no-cover fallback visually consistent with the Web archive
            // rather than showing a blank utility tile.
            cover.gameObject.AddComponent<RectMask2D>();
            var coverMagenta = Panel("Cover Magenta", cover, new Color(.61f, .33f, .45f), new Vector2(760, 250), new Vector2(-112, 205));
            coverMagenta.localRotation = Quaternion.Euler(0, 0, -45);
            var coverCyan = Panel("Cover Cyan", cover, new Color(.29f, .55f, .68f), new Vector2(760, 320), new Vector2(0, 15));
            coverCyan.localRotation = Quaternion.Euler(0, 0, -45);
            var coverBlue = Panel("Cover Blue", cover, new Color(.23f, .35f, .77f), new Vector2(760, 245), new Vector2(145, -190));
            coverBlue.localRotation = Quaternion.Euler(0, 0, -45);
            var coverLetter = Label("G", cover, 142); coverLetter.color = new Color(1f, 1f, 1f, .16f); Fill(coverLetter.rectTransform);
            var coverBrand = Label("GUGARYTHM\nCHART ARCHIVE", cover, 15); coverBrand.alignment = TextAnchor.UpperRight; coverBrand.rectTransform.sizeDelta = new Vector2(210, 70); coverBrand.rectTransform.anchoredPosition = new Vector2(112, 185);
            detailCoverTitleLabel = Label("選擇一份譜面", cover, 56); detailCoverTitleLabel.alignment = TextAnchor.LowerLeft; detailCoverTitleLabel.rectTransform.sizeDelta = new Vector2(430, 190); detailCoverTitleLabel.rectTransform.anchoredPosition = new Vector2(-18, -128);
            var detailKicker = Label("CHART DETAIL", detail, 18); detailKicker.color = new Color(.64f, .64f, .64f); detailKicker.alignment = TextAnchor.MiddleLeft; detailKicker.rectTransform.sizeDelta = new Vector2(320, 34); PinToAnchor(detailKicker.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 305));
            detailTitleLabel = Label("選擇一份譜面", detail, 58); detailTitleLabel.alignment = TextAnchor.MiddleLeft; detailTitleLabel.rectTransform.sizeDelta = new Vector2(620, 92); PinToAnchor(detailTitleLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 183.5f));
            detailArtistLabel = Label("", detail, 25); detailArtistLabel.color = new Color(.68f, .68f, .68f); detailArtistLabel.alignment = TextAnchor.MiddleLeft; detailArtistLabel.rectTransform.sizeDelta = new Vector2(620, 48); PinToAnchor(detailArtistLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 113.5f));
            var infoDivider = Panel("Detail Divider", detail, new Color(.28f, .28f, .28f), new Vector2(0, 1), Vector2.zero); infoDivider.anchorMin = new Vector2(.51f, .5f); infoDivider.anchorMax = new Vector2(.94f, .5f); infoDivider.offsetMin = new Vector2(0, 72); infoDivider.offsetMax = new Vector2(0, 73); infoDivider.GetComponent<Image>().raycastTarget = false;
            detailDifficultyLabel = Label("選擇難度", detail, 17); detailDifficultyLabel.color = new Color(.68f, .68f, .68f); detailDifficultyLabel.alignment = TextAnchor.MiddleLeft; detailDifficultyLabel.rectTransform.sizeDelta = new Vector2(440, 38); PinToAnchor(detailDifficultyLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 36));
            difficultyButtonContent = new GameObject("Difficulty Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            difficultyButtonContent.SetParent(detail, false);
            difficultyButtonContent.anchorMin = difficultyButtonContent.anchorMax = new Vector2(0, .5f);
            difficultyButtonContent.pivot = new Vector2(0, .5f);
            difficultyButtonContent.sizeDelta = new Vector2(450, 76);
            difficultyButtonContent.anchoredPosition = new Vector2(0, -26);
            difficultyButtonContent.anchorMin = difficultyButtonContent.anchorMax = new Vector2(.51f, .5f);
            detailAccuracyLabel = Label("BEST ACCURACY\n<size=52>—</size>", detail, 18); detailAccuracyLabel.supportRichText = true; detailAccuracyLabel.alignment = TextAnchor.UpperLeft; detailAccuracyLabel.rectTransform.sizeDelta = new Vector2(460, 100); PinToAnchor(detailAccuracyLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, -134));
            startButton = MakeFlatButton("▶  開始遊戲", detail, Vector2.zero, StartGame, new Vector2(0, 82), new Color(.06f, .58f, .96f));
            var startRect = startButton.GetComponent<RectTransform>(); startRect.anchorMin = new Vector2(.51f, .5f); startRect.anchorMax = new Vector2(.94f, .5f); startRect.pivot = new Vector2(.5f, .5f); startRect.offsetMin = new Vector2(0, -300.5f); startRect.offsetMax = new Vector2(0, -218.5f);
            startButton.interactable = false;
            RefreshLibraryUI();
        }

        void BuildSettings(RectTransform root)
        {
            settingsPanel = Panel("Settings", root, new Color(.10f, .10f, .10f, 1f), Vector2.zero, Vector2.zero, true);
            var title = Label("設定", settingsPanel, 42);
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.sizeDelta = new Vector2(560, 72);
            PinToAnchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(64, -42));
            var back = MakeFlatButton("返回曲庫", settingsPanel, Vector2.zero, ReturnFromSettings, new Vector2(180, 58), new Color(.18f, .18f, .18f));
            PinToAnchor(back.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-52, -48));

            var navigation = Panel("Settings Navigation", settingsPanel, new Color(.13f, .13f, .13f, 1f), new Vector2(270, 760), new Vector2(-600, -20));
            settingsAudioNavigationButton = MakeFlatButton("遊戲", navigation, new Vector2(0, 285), ShowSettingsAudio, new Vector2(220, 68), new Color(.08f, .28f, .42f));
            settingsTagsNavigationButton = MakeFlatButton("標籤", navigation, new Vector2(0, 205), ShowSettingsTags, new Vector2(220, 68), new Color(.18f, .18f, .18f));
            var card = Panel("Settings Audio Panel", settingsPanel, new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));
            settingsAudioPanel = card;

            var musicVolumeTitle = Label("音樂音量", card, 24);
            musicVolumeTitle.alignment = TextAnchor.MiddleLeft;
            musicVolumeTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            musicVolumeTitle.rectTransform.anchoredPosition = new Vector2(0, 280);
            settingsMusicVolumeSlider = MakeSlider(card, new Vector2(0, 225), 0f, 1f, PlayerPrefs.GetFloat("gugarythm-music-volume", 1f), SetSettingsMusicVolume);
            settingsMusicVolumeSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 18);
            settingsMusicVolumeLabel = Label("100%", card, 20);
            settingsMusicVolumeLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            settingsMusicVolumeLabel.rectTransform.anchoredPosition = new Vector2(0, 175);

            var keyVolumeTitle = Label("按鍵音量", card, 24);
            keyVolumeTitle.alignment = TextAnchor.MiddleLeft;
            keyVolumeTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            keyVolumeTitle.rectTransform.anchoredPosition = new Vector2(0, 105);
            settingsKeyVolumeSlider = MakeSlider(card, new Vector2(0, 50), 0f, 1f, PlayerPrefs.GetFloat("gugarythm-key-volume", 1f), SetSettingsKeyVolume);
            settingsKeyVolumeSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 18);
            settingsKeyVolumeLabel = Label("100%", card, 20);
            settingsKeyVolumeLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            settingsKeyVolumeLabel.rectTransform.anchoredPosition = new Vector2(0, 0);
            SetSettingsMusicVolume(settingsMusicVolumeSlider.value);
            SetSettingsKeyVolume(settingsKeyVolumeSlider.value);

            var delayTitle = Label("延遲調整", card, 24);
            delayTitle.alignment = TextAnchor.MiddleLeft;
            delayTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            delayTitle.rectTransform.anchoredPosition = new Vector2(0, -245);
            MakeFlatButton("−1 ms", card, new Vector2(-300, -300), () => AdjustSettingsDelay(-SettingsDelayAdjustment.StepSeconds), new Vector2(150, 52), new Color(.06f, .58f, .96f));
            Panel("Delay Value Background", card, new Color(.18f, .18f, .18f), new Vector2(180, 52), new Vector2(-100, -300));
            settingsDelayLabel = Label("", card, 20);
            settingsDelayLabel.alignment = TextAnchor.MiddleCenter;
            settingsDelayLabel.rectTransform.sizeDelta = new Vector2(180, 52);
            settingsDelayLabel.rectTransform.anchoredPosition = new Vector2(-100, -300);
            MakeFlatButton("＋1 ms", card, new Vector2(100, -300), () => AdjustSettingsDelay(SettingsDelayAdjustment.StepSeconds), new Vector2(150, 52), new Color(.06f, .58f, .96f));
            MakeFlatButton("自動調整", card, new Vector2(300, -300), OpenAutoAdjustPanel, new Vector2(150, 52), new Color(.18f, .28f, .38f));
            RefreshSettingsDelayLabel();

            var speedTitle = Label("速度", card, 24);
            speedTitle.alignment = TextAnchor.MiddleLeft;
            speedTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            speedTitle.rectTransform.anchoredPosition = new Vector2(0, -70);
            speedSlider = MakeSlider(card, new Vector2(0, -120), 1f, 20f, scrollSpeed, SetScrollSpeed);
            speedSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 18);
            speedLabel = Label("", card, 20);
            speedLabel.alignment = TextAnchor.MiddleLeft;
            speedLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            speedLabel.rectTransform.anchoredPosition = new Vector2(0, -165);
            SetScrollSpeed(scrollSpeed);

            settingsTagsPanel = Panel("Settings Tags Panel", settingsPanel, new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));
            var tagTitle = Label("難度標籤", settingsTagsPanel, 32); tagTitle.alignment = TextAnchor.MiddleLeft; tagTitle.rectTransform.sizeDelta = new Vector2(940, 62); tagTitle.rectTransform.anchoredPosition = new Vector2(0, 330);
            var tagDescription = Label("拖移可調整順序（上到下對應左到右）", settingsTagsPanel, 22); tagDescription.color = new Color(.72f, .82f, 1f, 1); tagDescription.rectTransform.sizeDelta = new Vector2(940, 44); tagDescription.rectTransform.anchoredPosition = new Vector2(0, 275);
            settingsTagInput = MakeInputField("新增難度標籤", settingsTagsPanel, new Vector2(-100, 190), new Vector2(650, 56));
            MakeFlatButton("＋ 新增", settingsTagsPanel, new Vector2(350, 190), CreateDifficultyTag, new Vector2(150, 56), new Color(.06f, .58f, .96f));
            settingsTagContent = new GameObject("Settings Difficulty Tags", typeof(RectTransform)).GetComponent<RectTransform>(); settingsTagContent.SetParent(settingsTagsPanel, false); settingsTagContent.anchorMin = settingsTagContent.anchorMax = new Vector2(.5f, .5f); settingsTagContent.pivot = new Vector2(.5f, .5f); settingsTagContent.sizeDelta = new Vector2(850, 430); settingsTagContent.anchoredPosition = new Vector2(0, -100);
            RefreshSettingsTags();
            difficultyTagConfirmationPanel = Panel("Difficulty Tag Confirmation", settingsTagsPanel, new Color(.07f, .07f, .07f, .99f), new Vector2(560, 300), Vector2.zero);
            Outline(difficultyTagConfirmationPanel.gameObject, new Color(.78f, .28f, .28f), 2);
            var confirmationTitle = Label("刪除難度標籤？", difficultyTagConfirmationPanel, 30);
            confirmationTitle.rectTransform.sizeDelta = new Vector2(500, 54);
            confirmationTitle.rectTransform.anchoredPosition = new Vector2(0, 92);
            difficultyTagConfirmationText = Label("", difficultyTagConfirmationPanel, 20);
            difficultyTagConfirmationText.rectTransform.sizeDelta = new Vector2(500, 54);
            difficultyTagConfirmationText.rectTransform.anchoredPosition = new Vector2(0, 35);
            var confirmTagDelete = MakeFlatButton("刪除", difficultyTagConfirmationPanel, new Vector2(120, -82), ConfirmDifficultyTagDelete, new Vector2(160, 54), new Color(.68f, .12f, .12f));
            confirmTagDelete.GetComponentInChildren<Text>().color = Color.white;
            MakeOutlinedButton("取消", difficultyTagConfirmationPanel, new Vector2(-120, -82), CancelDifficultyTagDelete, new Vector2(160, 54));
            difficultyTagConfirmationPanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsPanel.gameObject.SetActive(false);
        }

        void BuildChartEditor(RectTransform root)
        {
            chartEditorPanel = Panel("Chart Editor", root, new Color(.10f, .10f, .10f, 1f), Vector2.zero, Vector2.zero, true);
            var breadcrumb = Label("LIBRARY   /   EDIT CHART", chartEditorPanel, 18);
            breadcrumb.color = new Color(.64f, .64f, .64f);
            breadcrumb.alignment = TextAnchor.MiddleLeft;
            breadcrumb.rectTransform.sizeDelta = new Vector2(480, 38);
            PinToAnchor(breadcrumb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(80, -52));

            var back = MakeOutlinedButton("返回曲庫", chartEditorPanel, Vector2.zero, ReturnFromChartEditor, new Vector2(130, 48));
            PinToAnchor(back.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-56, -48));

            var card = Panel("Chart Editor Card", chartEditorPanel, new Color(.14f, .14f, .14f, 1f), new Vector2(740, 680), new Vector2(0, 0));
            var title = Label("編輯譜面", card, 42);
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.sizeDelta = new Vector2(620, 64);
            title.rectTransform.anchoredPosition = new Vector2(0, 275);
            chartEditorSubtitleLabel = Label("", card, 18);
            chartEditorSubtitleLabel.alignment = TextAnchor.MiddleLeft;
            chartEditorSubtitleLabel.color = new Color(.65f, .65f, .65f);
            chartEditorSubtitleLabel.rectTransform.sizeDelta = new Vector2(620, 36);
            chartEditorSubtitleLabel.rectTransform.anchoredPosition = new Vector2(0, 220);

            BuildEditorField(card, "歌曲名稱", out chartEditorTitleInput, new Vector2(0, 177.5f));
            BuildEditorField(card, "作者名稱", out chartEditorAuthorInput, new Vector2(0, 77.5f));
            BuildEditorField(card, "難度標籤", out chartEditorDifficultyNameInput, new Vector2(0, -22.5f));
            chartEditorDifficultyNameInput.gameObject.SetActive(false);
            chartEditorTagContent = new GameObject("Difficulty Tag Options", typeof(RectTransform)).GetComponent<RectTransform>();
            chartEditorTagContent.SetParent(card, false);
            chartEditorTagContent.sizeDelta = new Vector2(620, 56);
            chartEditorTagContent.anchoredPosition = new Vector2(0, -34.5f);
            BuildEditorField(card, "難度數字", out chartEditorLevelInput, new Vector2(0, -122.5f));
            chartEditorStatusLabel = Label("", card, 17);
            chartEditorStatusLabel.color = new Color(.92f, .54f, .54f);
            chartEditorStatusLabel.alignment = TextAnchor.MiddleLeft;
            chartEditorStatusLabel.rectTransform.sizeDelta = new Vector2(620, 32);
            chartEditorStatusLabel.rectTransform.anchoredPosition = new Vector2(0, -245.5f);

            MakeFlatButton("儲存變更", card, new Vector2(100, -253.5f), SaveChartEditor, new Vector2(300, 64), new Color(.06f, .58f, .96f));
            var delete = MakeOutlinedButton("刪除譜面", card, new Vector2(-220, -253.5f), PromptDeleteChart, new Vector2(180, 64));
            var deleteText = delete.GetComponentInChildren<Text>();
            deleteText.color = new Color(1f, .48f, .48f);
            Outline(delete.gameObject, new Color(.72f, .26f, .26f), 1);

            deleteChartConfirmationPanel = Panel("Delete Chart Confirmation", chartEditorPanel, new Color(.07f, .07f, .07f, .99f), new Vector2(520, 300), Vector2.zero);
            Outline(deleteChartConfirmationPanel.gameObject, new Color(.78f, .28f, .28f), 2);
            var confirmTitle = Label("確定要刪除這份譜面？", deleteChartConfirmationPanel, 28);
            confirmTitle.rectTransform.sizeDelta = new Vector2(450, 52);
            confirmTitle.rectTransform.anchoredPosition = new Vector2(0, 88);
            var confirmCopy = Label("此操作會移除本機 GGR，無法復原。", deleteChartConfirmationPanel, 18);
            confirmCopy.color = new Color(.72f, .72f, .72f);
            confirmCopy.rectTransform.sizeDelta = new Vector2(450, 42);
            confirmCopy.rectTransform.anchoredPosition = new Vector2(0, 35);
            MakeOutlinedButton("取消", deleteChartConfirmationPanel, new Vector2(-112, -82), CancelDeleteChart, new Vector2(170, 56));
            var confirmDelete = MakeFlatButton("確認刪除", deleteChartConfirmationPanel, new Vector2(112, -82), ConfirmDeleteChart, new Vector2(170, 56), new Color(.65f, .18f, .18f));
            confirmDelete.GetComponentInChildren<Text>().color = Color.white;
            deleteChartConfirmationPanel.gameObject.SetActive(false);
            chartEditorPanel.gameObject.SetActive(false);
        }

        static void BuildEditorField(RectTransform parent, string label, out InputField input, Vector2 position)
        {
            var fieldLabel = Label(label, parent, 18);
            fieldLabel.alignment = TextAnchor.MiddleLeft;
            fieldLabel.color = new Color(.72f, .72f, .72f);
            fieldLabel.rectTransform.sizeDelta = new Vector2(620, 34);
            fieldLabel.rectTransform.anchoredPosition = position + new Vector2(0, 35);
            input = MakeInputField(label, parent, position + new Vector2(0, -12), new Vector2(620, 56));
        }

        void PopulateChartEditor()
        {
            if (!ChartSelectionSession.Ensure().TryGetSelection(out var remembered, out _))
            {
                chartEditorEntry = null;
                chartEditorSubtitleLabel.text = "請先在曲庫選取一份譜面。";
                chartEditorStatusLabel.text = "沒有可編輯的譜面。";
                return;
            }

            chartEditorEntry = LocalChartLibrary.Load().FirstOrDefault(entry => entry.Id == remembered.Id);
            if (chartEditorEntry == null)
            {
                chartEditorSubtitleLabel.text = "找不到原本選取的譜面。";
                chartEditorStatusLabel.text = "譜面可能已被刪除。";
                return;
            }

            chartEditorTitleInput.text = chartEditorEntry.Title ?? string.Empty;
            chartEditorAuthorInput.text = chartEditorEntry.Artist ?? string.Empty;
            chartEditorDifficultyNameInput.text = chartEditorEntry.DifficultyName ?? string.Empty;
            chartEditorLevelInput.text = chartEditorEntry.DifficultyLevel ?? string.Empty;
            if (ChartSelectionSession.Ensure().TryGetEditorDraft(out var draftTitle, out var draftArtist, out var draftTag, out var draftLevel))
            {
                chartEditorTitleInput.text = draftTitle; chartEditorAuthorInput.text = draftArtist; chartEditorDifficultyNameInput.text = draftTag; chartEditorLevelInput.text = draftLevel;
            }
            chartEditorSubtitleLabel.text = string.IsNullOrWhiteSpace(chartEditorEntry.DifficultyName)
                ? string.Empty
                : chartEditorEntry.DifficultyName;
            chartEditorStatusLabel.text = string.Empty;
            RefreshChartEditorTagOptions();
        }

        void OpenChartEditor()
        {
            if (selectedLibraryEntry == null)
            {
                SetStatus("請先選取一份譜面。");
                return;
            }
            if (!LocalChartLibrary.TryReadSource(selectedLibraryEntry, out var bytes) ||
                !ChartSelectionSession.Ensure().SetSelection(selectedLibraryEntry, bytes))
            {
                SetStatus("找不到已儲存的 GGR 檔案。請重新匯入。");
                return;
            }
            GugarythmSceneRouter.OpenChartEditor();
        }

        void SaveChartEditor()
        {
            if (chartEditorEntry == null) return;
            if (!LocalChartLibrary.TryUpdateChartDetails(chartEditorEntry.Id, chartEditorTitleInput.text, chartEditorAuthorInput.text,
                    chartEditorDifficultyNameInput.text, chartEditorLevelInput.text, out var updated))
            {
                chartEditorStatusLabel.text = "歌曲名稱不可空白。";
                return;
            }

            currentLibraryEntry = updated;
            selectedLibraryEntry = updated;
            selectedDifficultyName = updated.DifficultyName ?? string.Empty;
            if (LocalChartLibrary.TryReadSource(updated, out var bytes)) ChartSelectionSession.Ensure().SetSelection(updated, bytes);
            ChartSelectionSession.Ensure().ClearEditorDraft();
            GugarythmSceneRouter.OpenLibrary();
        }

        void RefreshChartEditorTagOptions()
        {
            if (chartEditorTagContent == null || chartEditorDifficultyNameInput == null) return;
            ClearChildren(chartEditorTagContent);
            var tags = LocalChartLibrary.LoadDifficultyTags().ToArray();
            for (var index = 0; index < tags.Length; index++)
            {
                var tag = tags[index];
                var active = string.Equals(chartEditorDifficultyNameInput.text, tag, StringComparison.OrdinalIgnoreCase);
                var button = MakeFlatButton(tag, chartEditorTagContent, Vector2.zero,
                    () => { chartEditorDifficultyNameInput.text = string.Equals(chartEditorDifficultyNameInput.text, tag, StringComparison.OrdinalIgnoreCase) ? string.Empty : tag; RefreshChartEditorTagOptions(); },
                    new Vector2(145, 36), new Color(.18f, .18f, .18f));
                var rect = button.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0, .5f);
                rect.pivot = new Vector2(0, .5f);
                rect.anchoredPosition = new Vector2(index * 155, 0);
                if (active) Outline(button.gameObject, new Color(.05f, .60f, 1f), 2);
            }
            var add = MakeFlatButton("＋", chartEditorTagContent, Vector2.zero, OpenSettingsForDifficultyTags, new Vector2(36, 36), new Color(.06f, .58f, .96f));
            var addRect = add.GetComponent<RectTransform>(); addRect.anchorMin = addRect.anchorMax = new Vector2(0, .5f); addRect.pivot = new Vector2(0, .5f); addRect.anchoredPosition = new Vector2(tags.Length * 155 + 10, 0);
        }

        void OpenSettingsForDifficultyTags()
        {
            ChartSelectionSession.Ensure().SetEditorDraft(chartEditorTitleInput.text, chartEditorAuthorInput.text, chartEditorDifficultyNameInput.text, chartEditorLevelInput.text);
            GugarythmSceneRouter.OpenSettings();
        }

        void CreateDifficultyTag()
        {
            if (!LocalChartLibrary.TryCreateDifficultyTag(settingsTagInput.text, out var error)) { settingsTagInput.text = error; return; }
            settingsTagInput.text = string.Empty; RefreshSettingsTags();
        }

        void PromptDeleteDifficultyTag(string tag)
        {
            pendingDifficultyTagDelete = tag;
            difficultyTagConfirmationText.text = $"確定要刪除「{tag}」嗎？";
            difficultyTagConfirmationPanel.gameObject.SetActive(true);
        }

        void CancelDifficultyTagDelete()
        {
            pendingDifficultyTagDelete = null;
            difficultyTagConfirmationPanel?.gameObject.SetActive(false);
        }

        void ConfirmDifficultyTagDelete()
        {
            if (!string.IsNullOrWhiteSpace(pendingDifficultyTagDelete))
                LocalChartLibrary.DeleteDifficultyTag(pendingDifficultyTagDelete);
            CancelDifficultyTagDelete();
            RefreshSettingsTags();
        }

        void RefreshSettingsTags()
        {
            if (settingsTagContent == null) return; ClearChildren(settingsTagContent);
            var tags = LocalChartLibrary.LoadDifficultyTags();
            for (var index = 0; index < tags.Count; index++)
            {
                var tag = tags[index];
                var row = Panel("Difficulty Tag Row", settingsTagContent, new Color(.18f, .18f, .18f), new Vector2(850, 56), Vector2.zero);
                row.anchorMin = row.anchorMax = new Vector2(.5f, 1); row.anchoredPosition = new Vector2(0, -index * 64 - 28);
                var handle = Label("☰", row, 20); handle.alignment = TextAnchor.MiddleCenter; handle.rectTransform.sizeDelta = new Vector2(48, 56); handle.rectTransform.anchoredPosition = new Vector2(-380, 0);
                var label = Label(tag, row, 18); label.color = Color.white; label.raycastTarget = false; label.horizontalOverflow = HorizontalWrapMode.Overflow; label.verticalOverflow = VerticalWrapMode.Truncate; label.alignment = TextAnchor.MiddleLeft; label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one; label.rectTransform.offsetMin = new Vector2(80, 0); label.rectTransform.offsetMax = new Vector2(-130, 0);
                var drag = row.gameObject.AddComponent<DifficultyTagDragHandle>(); drag.Index = index; drag.Moved = (from, to) => { LocalChartLibrary.MoveDifficultyTag(from, to); RefreshSettingsTags(); };
                var delete = MakeOutlinedButton("刪除", row, new Vector2(360, 0), () => PromptDeleteDifficultyTag(tag), new Vector2(96, 42));
                var deleteText = delete.GetComponentInChildren<Text>();
                deleteText.color = new Color(1f, .35f, .35f);
                Outline(delete.gameObject, new Color(.78f, .28f, .28f), 1);
            }
        }

        void PromptDeleteChart()
        {
            if (chartEditorEntry != null) deleteChartConfirmationPanel.gameObject.SetActive(true);
        }

        void CancelDeleteChart() => deleteChartConfirmationPanel.gameObject.SetActive(false);

        void ConfirmDeleteChart()
        {
            if (chartEditorEntry == null || !LocalChartLibrary.TryDelete(chartEditorEntry.Id))
            {
                chartEditorStatusLabel.text = "刪除失敗，請稍後再試。";
                deleteChartConfirmationPanel.gameObject.SetActive(false);
                return;
            }
            ChartSelectionSession.Ensure().Clear();
            GugarythmSceneRouter.OpenLibrary();
        }

        void ReturnFromChartEditor() => GugarythmSceneRouter.OpenLibrary();

        void CycleLibrarySort()
        {
            librarySort = librarySort == ChartLibrarySort.Accuracy ? ChartLibrarySort.Difficulty :
                librarySort == ChartLibrarySort.Difficulty ? ChartLibrarySort.Title : ChartLibrarySort.Accuracy;
            RefreshLibraryUI();
        }

        void RefreshLibraryUI()
        {
            if (libraryListContent == null) return;
            var entries = LocalChartLibrary.Load();
            var groups = ChartLibraryGrouping.Group(entries);
            var filter = librarySearchInput == null ? string.Empty : librarySearchInput.text.Trim();
            if (!string.IsNullOrEmpty(filter))
            {
                groups = groups.Where(group => ContainsIgnoreCase(group.Title, filter) || ContainsIgnoreCase(group.Artist, filter) ||
                    group.Difficulties.Any(entry => ContainsIgnoreCase(entry.Author, filter))).ToList();
            }

            if (selectedLibraryEntry != null)
            {
                var refreshed = entries.FirstOrDefault(entry => entry.Id == selectedLibraryEntry.Id);
                if (refreshed != null) selectedLibraryEntry = refreshed;
            }
            if (selectedLibraryEntry == null && groups.Count > 0) SelectLibraryEntry(groups[0].Difficulties[0], false);
            if (selectedLibraryEntry != null && string.IsNullOrWhiteSpace(selectedDifficultyName)) selectedDifficultyName = selectedLibraryEntry.DifficultyName ?? string.Empty;

            groups = ChartLibraryGrouping.Sort(groups, librarySort, librarySortAscending, selectedDifficultyName).ToList();
            libraryCountLabel.text = groups.Count.ToString();
            librarySortModeLabel.text = librarySort == ChartLibrarySort.Accuracy ? "準確率" : librarySort == ChartLibrarySort.Difficulty ? "難度" : "曲名";
            libraryDirectionIcon.localRotation = Quaternion.Euler(0, 0, librarySortAscending ? 180 : 0);
            ClearChildren(libraryListContent);
            const float rowHeight = 102f;
            libraryListContent.sizeDelta = new Vector2(0, Mathf.Max(libraryListContent.parent.GetComponent<RectTransform>().rect.height, groups.Count * rowHeight + 8));
            for (var index = 0; index < groups.Count; index++) BuildLibraryRow(groups[index], index, rowHeight);
            RefreshDetailUI(groups);
        }

        void BuildLibraryRow(LocalChartGroup group, int index, float rowHeight)
        {
            var hasSelectedDifficulty = group.FindDifficulty(selectedDifficultyName);
            var selected = selectedLibraryEntry != null && group.GroupId == selectedLibraryEntry.GroupId;
            var row = Panel("Chart Row", libraryListContent, selected ? new Color(.12f, .25f, .36f) : new Color(.16f, .16f, .16f), new Vector2(0, rowHeight - 2), Vector2.zero);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(.5f, 1);
            row.offsetMin = new Vector2(0, -rowHeight * (index + 1) + 2);
            row.offsetMax = new Vector2(0, -rowHeight * index);
            if (selected) Outline(row.gameObject, new Color(.05f, .60f, 1f), 2);
            if (index > 0)
            {
                var divider = Panel("Chart Divider", row, new Color(.27f, .27f, .27f, .72f), new Vector2(0, 1), Vector2.zero);
                divider.anchorMin = new Vector2(0, 1); divider.anchorMax = new Vector2(1, 1); divider.offsetMin = new Vector2(16, -1); divider.offsetMax = new Vector2(-16, 0);
                divider.GetComponent<Image>().raycastTarget = false;
            }
            var title = Label(group.Title, row, 21); title.alignment = TextAnchor.MiddleLeft; title.rectTransform.anchorMin = new Vector2(0, 1); title.rectTransform.anchorMax = new Vector2(1, 1); title.rectTransform.pivot = new Vector2(0, 1); title.rectTransform.offsetMin = new Vector2(24, -58); title.rectTransform.offsetMax = new Vector2(-78, -24);
            var artist = Label(group.Artist, row, 16); artist.alignment = TextAnchor.MiddleLeft; artist.color = new Color(.67f, .67f, .67f); artist.rectTransform.anchorMin = new Vector2(0, 1); artist.rectTransform.anchorMax = new Vector2(1, 1); artist.rectTransform.pivot = new Vector2(0, 1); artist.rectTransform.offsetMin = new Vector2(24, -88); artist.rectTransform.offsetMax = new Vector2(-78, -59);
            if (hasSelectedDifficulty != null)
            {
                var level = Label(hasSelectedDifficulty.DifficultyLevel, row, 20); level.color = new Color(.78f, .78f, .78f); level.rectTransform.sizeDelta = new Vector2(62, 50); PinToAnchor(level.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-18, 0));
            }
            MakeInvisibleButton(row, () => SelectLibraryEntry(hasSelectedDifficulty ?? group.Difficulties[0], true));
        }

        void SelectLibraryEntry(LocalChartEntry entry, bool loadSource)
        {
            if (entry == null) return;
            selectedLibraryEntry = entry;
            currentLibraryEntry = entry;
            selectedDifficultyName = entry.DifficultyName ?? string.Empty;
            if (GugarythmSceneRouter.IsLibrary)
            {
                if (startButton != null) startButton.interactable = true;
                RefreshLibraryUI();
                return;
            }

            if (loadSource) StartCoroutine(LoadLibraryEntry(entry));
            else RefreshLibraryUI();
        }

        IEnumerator LoadLibraryEntry(LocalChartEntry entry)
        {
            if (!LocalChartLibrary.TryReadSource(entry, out var bytes)) { SetStatus("找不到已儲存的 GGR 檔案。請重新匯入。"); yield break; }
            loading = true;
            startButton.interactable = false;
            SetStatus("正在載入 " + entry.Title + "…");
            yield return null;
            var result = new GgrChartImporter().Import(entry.SourceFile, bytes, null);
            if (!result.Success) { SetStatus("譜面載入失敗：" + result.Error); loading = false; yield break; }
            chart = result.Chart;
            musicLoadSucceeded = false;
            if (chart.BgmBytes != null) yield return LoadMusic(chart.BgmBytes, chart.BgmExtension, chart.BgmStartDelaySeconds);
            if (!musicLoadSucceeded) { SetStatus("GGR 音樂格式不支援或無法解碼。"); loading = false; yield break; }
            currentLibraryEntry = entry;
            selectedLibraryEntry = entry;
            startButton.interactable = true;
            SetStatus($"{chart.Title} · {chart.PlayableCount:N0} notes · {DisplayDifficulty(chart)}");
            loading = false;
            RefreshLibraryUI();
        }

        void RefreshDetailUI(IReadOnlyList<LocalChartGroup> groups)
        {
            ClearChildren(difficultyButtonContent);
            var group = selectedLibraryEntry == null ? null : groups.FirstOrDefault(item => item.GroupId == selectedLibraryEntry.GroupId);
            if (group == null)
            {
                detailTitleLabel.text = "選擇一份譜面";
                detailArtistLabel.text = string.Empty;
                detailCoverTitleLabel.text = "選擇一份\n譜面";
                detailDifficultyLabel.text = "選擇難度";
                detailAccuracyLabel.text = "BEST ACCURACY\n<size=52>—</size>";
                return;
            }
            detailTitleLabel.text = group.Title;
            detailArtistLabel.text = group.Artist;
            detailCoverTitleLabel.text = group.Title.Replace(" ", "\n");
            detailDifficultyLabel.text = "選擇難度";
            var current = group.Difficulties.FirstOrDefault(entry => entry.Id == selectedLibraryEntry.Id) ?? group.Difficulties[0];
            for (var index = 0; index < group.Difficulties.Count; index++)
            {
                var entry = group.Difficulties[index];
                var text = DisplayDifficulty(entry);
                var active = entry.Id == current.Id;
                var button = MakeFlatButton(text, difficultyButtonContent, new Vector2(index * 150, 0),
                    () => SelectLibraryEntry(entry, true), new Vector2(136, 52), active ? new Color(.10f, .20f, .29f) : new Color(.15f, .15f, .15f));
                var buttonRect = button.GetComponent<RectTransform>();
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0, .5f);
                buttonRect.pivot = new Vector2(0, .5f);
                buttonRect.anchoredPosition = new Vector2(index * 150, 0);
                Outline(button.gameObject, active ? new Color(.08f, .62f, 1f) : new Color(.34f, .34f, .34f), active ? 3 : 1);
                button.GetComponentInChildren<Text>().color = active ? new Color(.22f, .68f, 1f) : new Color(.78f, .78f, .78f);
            }
            detailAccuracyLabel.text = current.BestAccuracy < 0 ? "BEST ACCURACY\n<size=52>—</size>" : $"BEST ACCURACY\n<size=52>{current.BestAccuracy:F2}%</size>";
        }

        static bool ContainsIgnoreCase(string value, string part) => (value ?? string.Empty).IndexOf(part ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;

        static void ClearChildren(RectTransform parent)
        {
            if (parent == null) return;
            for (var index = parent.childCount - 1; index >= 0; index--) Destroy(parent.GetChild(index).gameObject);
        }

        void BuildImportDecision(RectTransform root)
        {
            importDecisionPanel = Panel("Import Storage Decision", root, new Color(.04f, .06f, .14f, .98f), new Vector2(620, 420), Vector2.zero);
            Outline(importDecisionPanel.gameObject, new Color(.4f, .8f, 1f, .85f), 3);
            var title = Label("偵測到相同曲名", importDecisionPanel, 36);
            title.rectTransform.sizeDelta = new Vector2(560, 70);
            title.rectTransform.anchoredPosition = new Vector2(0, 135);
            importDecisionText = Label("", importDecisionPanel, 22);
            importDecisionText.rectTransform.sizeDelta = new Vector2(540, 100);
            importDecisionText.rectTransform.anchoredPosition = new Vector2(0, 55);
            MakeButton("合併儲存", importDecisionPanel, new Vector2(120, -70), () => CommitPendingImport(true), new Vector2(210, 62));
            MakeButton("單獨儲存", importDecisionPanel, new Vector2(-120, -70), () => CommitPendingImport(false), new Vector2(210, 62));
            MakeButton("取消", importDecisionPanel, new Vector2(0, -150), CancelPendingImport, new Vector2(180, 52));
            importDecisionPanel.gameObject.SetActive(false);
        }

        void PresentImportStorageDecision(string fileName, byte[] bytes, RuntimeChart importedChart)
        {
            var matchingGroupId = LocalChartLibrary.FindMatchingGroupId(importedChart.Title, importedChart.Artist);
            if (string.IsNullOrWhiteSpace(matchingGroupId))
            {
                SaveToLocalLibrary(fileName, bytes, importedChart, LocalChartLibrary.NewGroupId());
                return;
            }

            pendingImportFileName = fileName;
            pendingImportBytes = bytes;
            pendingImportChart = importedChart;
            importDecisionText.text = $"「{importedChart.Title}」已存在。\n要將 {DisplayDifficulty(importedChart)} 合併到同一首歌嗎？";
            importDecisionPanel.gameObject.SetActive(true);
        }

        static string DisplayDifficulty(RuntimeChart importedChart) =>
            string.IsNullOrWhiteSpace(importedChart.DifficultyName) ? "未標示難度" :
            string.IsNullOrWhiteSpace(importedChart.DifficultyLevel) ? importedChart.DifficultyName :
            importedChart.DifficultyName + " " + importedChart.DifficultyLevel;

        static string DisplayDifficulty(LocalChartEntry entry) =>
            string.IsNullOrWhiteSpace(entry.DifficultyName) ? "未標示難度" :
            string.IsNullOrWhiteSpace(entry.DifficultyLevel) ? entry.DifficultyName :
            entry.DifficultyName + " " + entry.DifficultyLevel;

        void CommitPendingImport(bool merge)
        {
            if (pendingImportChart == null) return;
            var groupId = merge
                ? LocalChartLibrary.FindMatchingGroupId(pendingImportChart.Title, pendingImportChart.Artist)
                : null;
            SaveToLocalLibrary(pendingImportFileName, pendingImportBytes, pendingImportChart,
                string.IsNullOrWhiteSpace(groupId) ? LocalChartLibrary.NewGroupId() : groupId);
            ClearPendingImport();
        }

        void CancelPendingImport()
        {
            startButton.interactable = music.clip != null;
            ClearPendingImport();
        }

        void ClearPendingImport()
        {
            pendingImportFileName = null;
            pendingImportBytes = null;
            pendingImportChart = null;
            if (importDecisionPanel != null) importDecisionPanel.gameObject.SetActive(false);
        }

        void OpenSettings()
        {
            if (selectedLibraryEntry != null && LocalChartLibrary.TryReadSource(selectedLibraryEntry, out var bytes))
                ChartSelectionSession.Ensure().SetSelection(selectedLibraryEntry, bytes);
            GugarythmSceneRouter.OpenSettings();
        }

        void ReturnFromSettings()
        {
            calibrationActive = false;
            StopCalibrationTickAudio();
            if (ChartSelectionSession.Ensure().TryGetEditorDraft(out _, out _, out _, out _)) GugarythmSceneRouter.OpenChartEditor();
            else GugarythmSceneRouter.OpenLibrary();
        }

        void BuildLatencyCalibration(RectTransform root)
        {
            calibrationPanel = Panel("Latency Calibration Preview", root, new Color(.04f, .06f, .14f, .98f), new Vector2(520, 760), new Vector2(590, -20));
            Outline(calibrationPanel.gameObject, new Color(.4f, .8f, 1f, .85f), 3);
            var title = Label("延遲測試預覽", calibrationPanel, 34);
            title.rectTransform.sizeDelta = new Vector2(450, 62);
            title.rectTransform.anchoredPosition = new Vector2(0, 325);
            calibrationLabel = Label("", calibrationPanel, 24);
            calibrationLabel.rectTransform.sizeDelta = new Vector2(440, 80);
            calibrationLabel.rectTransform.anchoredPosition = new Vector2(0, 250);
            calibrationTapButton = MakeButton("TAP\n點擊節拍", calibrationPanel, new Vector2(0, 70), RegisterCalibrationTapFromButton, new Vector2(420, 260));
            calibrationOffsetLabel = Label("", calibrationPanel, 20);
            calibrationOffsetLabel.rectTransform.sizeDelta = new Vector2(470, 64);
            calibrationOffsetLabel.rectTransform.anchoredPosition = new Vector2(0, -115);
            calibrationDecreaseOffsetButton = MakeButton("−10 ms", calibrationPanel, new Vector2(-150, -75), () => AdjustAudioOffset(-.01d), new Vector2(140, 48));
            calibrationIncreaseOffsetButton = MakeButton("＋10 ms", calibrationPanel, new Vector2(0, -75), () => AdjustAudioOffset(.01d), new Vector2(140, 48));
            calibrationResetOffsetButton = MakeButton("歸零", calibrationPanel, new Vector2(150, -75), () => SetManualAudioOffset(0), new Vector2(110, 48));
            MakeButton("重新開始", calibrationPanel, new Vector2(-92, -220), RestartLatencyCalibration, new Vector2(170, 42));
            MakeButton("停止預覽", calibrationPanel, new Vector2(92, -220), ReturnFromLatencyCalibration, new Vector2(170, 42));
            RefreshCalibrationOffsetLabel();
            RefreshManualAudioOffsetControls();
            calibrationPanel.gameObject.SetActive(false);
        }

        void BuildResult(RectTransform root)
        {
            resultPanel = Panel("Result", root, new Color(.04f, .06f, .14f, .96f), new Vector2(620, 650), Vector2.zero); Outline(resultPanel.gameObject, new Color(.9f, .5f, 1f, .75f), 3);
            var title = Label("RESULT", resultPanel, 38); title.rectTransform.sizeDelta = new Vector2(580, 70); title.rectTransform.anchoredPosition = new Vector2(0, 260);
            resultText = Label("", resultPanel, 27); resultText.rectTransform.sizeDelta = new Vector2(540, 440); resultText.rectTransform.anchoredPosition = new Vector2(0, 25);
            MakeButton("返回曲庫", resultPanel, new Vector2(0, -270), GugarythmSceneRouter.OpenLibrary);
            resultPanel.gameObject.SetActive(false);
        }

        void BuildPauseOverlay(RectTransform root)
        {
            pauseOverlay = Panel("Pause Overlay", root, new Color(0, 0, 0, .72f), Vector2.zero, Vector2.zero, true);
            pauseOverlay.GetComponent<Image>().raycastTarget = true;
            pauseMenuContent = Panel("Pause Menu", pauseOverlay, new Color(.04f, .06f, .14f, .98f), new Vector2(620, 520), Vector2.zero);
            Outline(pauseMenuContent.gameObject, new Color(.4f, .8f, 1f, .85f), 3);
            pauseTitle = Label("暫停", pauseMenuContent, 42);
            pauseTitle.rectTransform.sizeDelta = new Vector2(560, 120);
            pauseTitle.rectTransform.anchoredPosition = new Vector2(0, 180);
            MakeButton("繼續", pauseMenuContent, new Vector2(0, 80), ContinueGame, new Vector2(360, 82));
            MakeButton("重新開始", pauseMenuContent, new Vector2(0, -30), RestartGame, new Vector2(360, 82));
            MakeButton("退出", pauseMenuContent, new Vector2(0, -140), ExitToMenu, new Vector2(360, 82));
            resumeCountdownLabel = Label("3", pauseOverlay, 128);
            Fill(resumeCountdownLabel.rectTransform);
            resumeCountdownLabel.gameObject.SetActive(false);
            pauseOverlay.gameObject.SetActive(false);
        }

        void RequestImport()
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel("匯入 GGR", "", "ggr");
            if (!string.IsNullOrEmpty(path)) StartCoroutine(ImportPath(path));
#elif UNITY_ANDROID
            NativeChartPicker.OpenFile();
            SetStatus("請在系統檔案選擇器選取 GGR…");
#else
            SetStatus("目前請將譜面放入 StreamingAssets，或使用 Android 匯入。");
#endif
        }

        IEnumerator ImportPath(string path)
        {
            if (path.StartsWith("ERROR:", StringComparison.Ordinal)) { SetStatus("匯入失敗：" + path[6..]); yield break; }
            if (!File.Exists(path)) { SetStatus("匯入檔案不存在。"); yield break; }
            if (!Path.GetExtension(path).Equals(".ggr", StringComparison.OrdinalIgnoreCase)) { SetStatus("請選擇 GGR 封包。"); yield break; }
            var bytes = File.ReadAllBytes(path);
            yield return ImportBytes(Path.GetFileName(path), bytes);
        }

        void PollNativeImport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var path = NativeChartPicker.ConsumeResult();
            if (!string.IsNullOrEmpty(path) && !loading) StartCoroutine(ImportPath(path));
#endif
        }

        void SaveToLocalLibrary(string fileName, byte[] bytes, RuntimeChart importedChart, string groupId)
        {
            try
            {
                currentLibraryEntry = LocalChartLibrary.Save(fileName, bytes, importedChart, groupId);
                selectedLibraryEntry = currentLibraryEntry;
                selectedDifficultyName = currentLibraryEntry.DifficultyName ?? string.Empty;
                startButton.interactable = music.clip != null;
                RefreshLibraryUI();
            }
            catch (Exception exception) { importedChart.Warnings.Add("本機曲庫保存失敗：" + exception.Message); }
        }

        HorizontalSlicedRawImage AcquireNoteView(RectTransform parent)
        {
            if (notePool.Count > 0)
            {
                var pooled = notePool.Pop();
                pooled.rectTransform.SetParent(parent, false);
                pooled.gameObject.SetActive(true);
                pooled.transform.SetAsLastSibling();
                return pooled;
            }
            var go = new GameObject("Runtime Note", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalSlicedRawImage));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.sizeDelta = new Vector2(100, 30);
            var view = go.GetComponent<HorizontalSlicedRawImage>(); view.color = Color.white; view.raycastTarget = false; view.capRatio = NoteCapRatio;
            var particle = RawPanel("Trace Particle", view.rectTransform, null, Color.white, new Vector2(52, 52), Vector2.zero).GetComponent<RawImage>();
            particle.raycastTarget = false;
            particle.gameObject.SetActive(false);
            var flickArrow = RawPanel("Flick Arrow", view.rectTransform, null, Color.white, new Vector2(72, 58), new Vector2(0, 32)).GetComponent<RawImage>();
            flickArrow.raycastTarget = false;
            flickArrow.gameObject.SetActive(false);
            return view;
        }

        void ApplyNoteTexture(HorizontalSlicedRawImage view, RuntimeNote note)
        {
            var archetype = note.Archetype ?? string.Empty;
            var trace = IsTrace(note);
            var holdMid = IsHoldMid(note);
            var damage = IsDamage(note);
            var flick = note.Kind == RuntimeNoteKind.Flick;
            var traceKey = note.Critical ? "yellow" :
                archetype.IndexOf("Flick", StringComparison.OrdinalIgnoreCase) >= 0 ? "pink" : "mint";
            var buttonKey = note.Critical ? "yellow" :
                archetype.IndexOf("Slide", StringComparison.OrdinalIgnoreCase) >= 0 ? "mint" :
                note.Kind == RuntimeNoteKind.Sustain ? "mint" : "cyan";

            if (damage) view.texture = damageTexture;
            else if (holdMid) view.texture = null;
            else if (flick && trace) view.texture = traceTextures.TryGetValue(traceKey, out var traceFlickTexture) ? traceFlickTexture : null;
            else if (flick) view.texture = buttonTextures.TryGetValue(note.Critical ? "yellow" : "pink", out var flickButtonTexture) ? flickButtonTexture : null;
            else if (trace) view.texture = traceTextures.TryGetValue(traceKey, out var traceTexture) ? traceTexture : null;
            else view.texture = buttonTextures.TryGetValue(buttonKey, out var buttonTexture) ? buttonTexture : null;

            // A hold mid is deliberately particle-only. The parent graphic must
            // be transparent because a null texture otherwise falls back to the
            // UI white texture and produces an unwanted bar.
            view.color = holdMid ? Color.clear : Color.white;
            view.capRatio = NoteCapRatio;
            var particle = view.transform.Find("Trace Particle")?.GetComponent<RawImage>();
            if (particle != null)
            {
                particle.texture = holdMid
                    ? note.Critical ? holdMidYellowTexture : holdMidMintTexture
                    : traceKey == "yellow" ? traceDiamondYellowTexture :
                    traceKey == "pink" ? traceDiamondPinkTexture : traceDiamondMintTexture;
                particle.gameObject.SetActive(ShouldShowNoteParticle(note, particle.texture != null));
            }
            var flickArrow = view.transform.Find("Flick Arrow")?.GetComponent<RawImage>();
            if (flickArrow != null)
            {
                var side = note.Direction != 0;
                var index = FlickSpriteIndex(note.Size);
                flickArrow.texture = note.Critical
                    ? side ? flickCriticalSideTextures[index] : flickCriticalCenterTextures[index]
                    : side ? flickNormalSideTextures[index] : flickNormalCenterTextures[index];
                flickArrow.uvRect = note.Direction > 0 ? new Rect(1, 0, -1, 1) : new Rect(0, 0, 1, 1);
                flickArrow.color = Color.white;
                flickArrow.gameObject.SetActive(flick && flickArrow.texture != null);
            }
        }

        static int FlickSpriteIndex(float size) => Mathf.Clamp(Mathf.RoundToInt(size * 2), 1, 6) - 1;

        void ReleaseNoteView(int index, HorizontalSlicedRawImage view) { noteViews.Remove(index); view.gameObject.SetActive(false); notePool.Push(view); }
        void ReleasePersistentHoldHead(int rootIndex, HorizontalSlicedRawImage view) { persistentHoldHeadViews.Remove(rootIndex); view.gameObject.SetActive(false); notePool.Push(view); }
        TaperedConnectorGraphic AcquireConnector()
        {
            if (connectorPool.Count > 0)
            {
                var pooled = connectorPool.Pop();
                pooled.gameObject.SetActive(true);
                ConfigureHoldGraphic(pooled);
                return pooled;
            }
            var go = new GameObject("Hold Connector", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(connectorLayer, false); Fill(rect);
            var graphic = go.GetComponent<TaperedConnectorGraphic>(); graphic.raycastTarget = false;
            ConfigureHoldGraphic(graphic);
            return graphic;
        }
        void ReleaseConnector(RuntimeConnector connector, TaperedConnectorGraphic line) { connectorViews.Remove(connector); line.gameObject.SetActive(false); connectorPool.Push(line); }
        SimLineGraphic AcquireSimLine()
        {
            if (simLinePool.Count > 0)
            {
                var pooled = simLinePool.Pop(); pooled.gameObject.SetActive(true); return pooled;
            }
            var go = new GameObject("Synchronization Line", typeof(RectTransform), typeof(CanvasRenderer), typeof(SimLineGraphic));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(simLineLayer, false); Fill(rect);
            var graphic = go.GetComponent<SimLineGraphic>(); graphic.raycastTarget = false;
            graphic.color = new Color(.78f, .83f, 1f, .28f);
            return graphic;
        }
        void ReleaseSimLine(RuntimeSimLine simLine, SimLineGraphic line) { simLineViews.Remove(simLine); line.gameObject.SetActive(false); simLinePool.Push(line); }
        TaperedConnectorGraphic AcquireGuide()
        {
            if (guidePool.Count > 0)
            {
                var pooled = guidePool.Pop(); pooled.gameObject.SetActive(true); ConfigureGuideGraphic(pooled); pooled.material = null; return pooled;
            }
            var go = new GameObject("Decoration Guide", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(guideLayer, false); Fill(rect);
            var graphic = go.GetComponent<TaperedConnectorGraphic>(); graphic.raycastTarget = false; ConfigureGuideGraphic(graphic); graphic.material = null; return graphic;
        }
        static void ConfigureHoldGraphic(TaperedConnectorGraphic graphic)
        {
            // Color, edge softness, and alpha are baked into the official
            // 306x4 seamless Hold texture. Drawing extra procedural bands was
            // what made overlapping Holds turn cloudy and merge with Guides.
            graphic.drawGlow = false; graphic.drawEdges = false;
            graphic.fillAlphaScale = 1; graphic.fillAlphaLimit = 1;
            graphic.sourceUvInset = HoldConnectorVisibleUvInset;
        }
        static void ConfigureGuideGraphic(TaperedConnectorGraphic graphic)
        {
            // USC guides are colored lane-surface regions, not merely their
            // outlines.  SetGuidePath already samples each point's left and
            // right lane boundaries, so retain that filled projection.
            graphic.texture = null;
            graphic.drawGlow = false; graphic.drawEdges = false;
            graphic.fillAlphaScale = 1; graphic.fillAlphaLimit = 1;
            graphic.sourceUvInset = 0;
        }
        void ReleaseGuide(RuntimeGuide guide, TaperedConnectorGraphic line) { guideViews.Remove(guide); line.gameObject.SetActive(false); guidePool.Push(line); }
        void ReleaseAllViews() { foreach (var pair in persistentHoldHeadViews.ToArray()) ReleasePersistentHoldHead(pair.Key, pair.Value); foreach (var pair in noteViews.ToArray()) ReleaseNoteView(pair.Key, pair.Value); foreach (var pair in connectorViews.ToArray()) ReleaseConnector(pair.Key, pair.Value); foreach (var pair in simLineViews.ToArray()) ReleaseSimLine(pair.Key, pair.Value); foreach (var pair in guideViews.ToArray()) ReleaseGuide(pair.Key, pair.Value); }

        void SpawnHitParticle(RuntimeNote note)
        {
            var tint = ResolveHitEffectColor(note);
            var x = X(note.Lane, 1f);
            var noteWidth = Mathf.Clamp(LaneWidth(note.Lane, note.Size, 1f), 64f, 154f);
            var particleRoot = new GameObject("Judgment Pulse", typeof(RectTransform), typeof(CanvasRenderer), typeof(HitBurstGraphic)).GetComponent<RectTransform>();
            particleRoot.SetParent(stage, false); particleRoot.sizeDelta = new Vector2(310, 150); particleRoot.anchoredPosition = new Vector2(x, HitY);
            particleRoot.SetAsLastSibling();
            var burst = particleRoot.GetComponent<HitBurstGraphic>();
            burst.raycastTarget = false;
            burst.color = tint;
            burst.upperWidth = noteWidth;
            burst.SetProgress(0);
            StartCoroutine(AnimateHitEffect(particleRoot, burst));
        }

        public static Color ResolveHitEffectColor(RuntimeNote note)
        {
            if (note.Critical) return new Color(1f, .82f, .12f, .9f);
            if (IsTrace(note) || note.Kind == RuntimeNoteKind.Sustain) return new Color(.12f, 1f, .58f, .84f);
            return note.Kind == RuntimeNoteKind.Flick
                ? new Color(1f, .2f, .67f, .86f)
                : new Color(.28f, .82f, 1f, .84f);
        }

        IEnumerator AnimateHitEffect(RectTransform particleRoot, HitBurstGraphic burst)
        {
            const float Duration = 15f / 60f;
            for (var elapsed = 0f; elapsed < Duration; elapsed += Time.unscaledDeltaTime)
            {
                burst.SetProgress(elapsed / Duration);
                yield return null;
            }
            Destroy(particleRoot.gameObject);
        }

        void RefreshHud()
        {
            accuracyLabel.text = $"ACCURACY  {scoreState.AccuracyPercent(chart?.PlayableCount ?? 0):F4}%";
            comboLabel.text = "COMBO\n" + scoreState.Combo;
            comboLabel.gameObject.SetActive(running && scoreState.Combo > 0);
        }
        void SetStatus(string message) { if (loadStatus != null) loadStatus.text = message; }
        void ShowJudgment(string value, Color color)
        {
            judgmentLabel.text = value;
            judgmentLabel.color = color;
            judgmentHideAt = string.IsNullOrEmpty(value) ? -1f : Time.unscaledTime + JudgmentDisplayDuration;
        }

        static Button MakeButton(string text, RectTransform parent, Vector2 position, Action action, Vector2? size = null)
        {
            var panel = Panel(text, parent, new Color(.1f, .62f, .78f), size ?? new Vector2(300, 82), position); panel.GetComponent<Image>().raycastTarget = true; Outline(panel.gameObject, Color.white, 2);
            var label = Label(text, panel, 27); Fill(label.rectTransform); var button = panel.gameObject.AddComponent<Button>(); button.onClick.AddListener(() => action()); return button;
        }

        static Button MakeFlatButton(string text, RectTransform parent, Vector2 position, Action action, Vector2 size, Color color)
        {
            var panel = Panel(text, parent, color, size, position);
            var image = panel.GetComponent<Image>(); image.raycastTarget = true;
            var label = Label(text, panel, 24); Fill(label.rectTransform);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            return button;
        }

        static Button MakeOutlinedButton(string text, RectTransform parent, Vector2 position, Action action, Vector2 size)
        {
            var panel = Panel(text, parent, new Color(.16f, .16f, .16f), size, position);
            var image = panel.GetComponent<Image>();
            image.raycastTarget = true;
            Outline(panel.gameObject, new Color(.42f, .42f, .42f), 1);
            var label = Label(text, panel, 18);
            label.color = new Color(.78f, .78f, .78f);
            Fill(label.rectTransform);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            return button;
        }

        static void MakeInvisibleButton(RectTransform target, Action action)
        {
            var image = target.GetComponent<Image>();
            Button button;
            if (image == null)
            {
                var overlay = new GameObject("Button Hit Area", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                var rect = overlay.GetComponent<RectTransform>();
                rect.SetParent(target.parent, false);
                rect.anchorMin = target.anchorMin;
                rect.anchorMax = target.anchorMax;
                rect.pivot = target.pivot;
                rect.sizeDelta = target.sizeDelta;
                rect.anchoredPosition = target.anchoredPosition;
                image = overlay.GetComponent<Image>();
                image.color = new Color(1, 1, 1, 0);
                button = overlay.AddComponent<Button>();
            }
            else button = target.GetComponent<Button>() ?? target.gameObject.AddComponent<Button>();
            image.raycastTarget = true;
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());
        }

        static InputField MakeInputField(string placeholder, RectTransform parent, Vector2 position, Vector2 size)
        {
            var panel = Panel("Search", parent, new Color(.12f, .12f, .12f), size, position);
            Outline(panel.gameObject, new Color(.30f, .30f, .30f), 1);
            var text = Label("", panel, 18); text.alignment = TextAnchor.MiddleLeft; text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one; text.rectTransform.offsetMin = new Vector2(18, 0); text.rectTransform.offsetMax = new Vector2(-18, 0);
            var place = Label(placeholder, panel, 18); place.color = new Color(.58f, .58f, .58f); place.alignment = TextAnchor.MiddleLeft; place.rectTransform.anchorMin = Vector2.zero; place.rectTransform.anchorMax = Vector2.one; place.rectTransform.offsetMin = new Vector2(18, 0); place.rectTransform.offsetMax = new Vector2(-18, 0);
            var input = panel.gameObject.AddComponent<InputField>();
            input.targetGraphic = panel.GetComponent<Image>(); input.textComponent = text; input.placeholder = place;
            return input;
        }

        static void AddGearIcon(RectTransform parent)
        {
            var icon = new GameObject("Gear Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(GearIconGraphic));
            var rect = icon.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(34, 34);
            var graphic = icon.GetComponent<GearIconGraphic>();
            graphic.color = new Color(.84f, .84f, .84f);
            graphic.raycastTarget = false;
        }

        static void AddPencilIcon(RectTransform parent)
        {
            var icon = new GameObject("Pencil Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(PencilIconGraphic));
            var rect = icon.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(34, 34);
            rect.anchoredPosition = new Vector2(2, 2);
            icon.GetComponent<PencilIconGraphic>().raycastTarget = false;
        }

        static void AddSortArrowIcon(RectTransform parent)
        {
            var color = new Color(.10f, .62f, 1f);
            AddSortArrowPart("Stem", parent, new Vector2(4, 24), new Vector2(0, 5), 0, color);
            AddSortArrowPart("Left Wing", parent, new Vector2(4, 13), new Vector2(-5, -10), 45, color);
            AddSortArrowPart("Right Wing", parent, new Vector2(4, 13), new Vector2(5, -10), -45, color);
        }

        static void AddSortArrowPart(string name, RectTransform parent, Vector2 size, Vector2 position, float rotation, Color color)
        {
            var part = Panel(name, parent, color, size, position);
            part.localRotation = Quaternion.Euler(0, 0, rotation);
            part.GetComponent<Image>().raycastTarget = false;
        }

        sealed class GearIconGraphic : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vertices)
            {
                vertices.Clear();
                const int segments = 32;
                const float innerRadius = 5.2f;
                const float rootRadius = 10.8f;
                const float toothRadius = 15.2f;
                var tint = color;
                for (var index = 0; index < segments; index++)
                {
                    var next = (index + 1) % segments;
                    var outerA = GearPoint(index, segments, rootRadius, toothRadius);
                    var outerB = GearPoint(next, segments, rootRadius, toothRadius);
                    var innerA = GearPoint(index, segments, innerRadius, innerRadius);
                    var innerB = GearPoint(next, segments, innerRadius, innerRadius);
                    var baseIndex = vertices.currentVertCount;
                    vertices.AddVert(outerA, tint, Vector2.zero);
                    vertices.AddVert(outerB, tint, Vector2.zero);
                    vertices.AddVert(innerB, tint, Vector2.zero);
                    vertices.AddVert(innerA, tint, Vector2.zero);
                    vertices.AddTriangle(baseIndex, baseIndex + 1, baseIndex + 2);
                    vertices.AddTriangle(baseIndex, baseIndex + 2, baseIndex + 3);
                }
            }

            static Vector2 GearPoint(int index, int segments, float rootRadius, float toothRadius)
            {
                var angle = (index / (float)segments * Mathf.PI * 2f) + Mathf.PI * .25f;
                var isTooth = index % 4 is 0 or 1;
                var radius = isTooth ? toothRadius : rootRadius;
                return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }
        }

        sealed class PencilIconGraphic : MaskableGraphic
        {
            protected override void OnPopulateMesh(VertexHelper vertices)
            {
                vertices.Clear();
                // A single diagonal pencil silhouette: triangular graphite tip,
                // long body and a darker end cap, so it remains legible at 34 px.
                AddPolygon(vertices, new[]
                {
                    new Vector2(-14, -10), new Vector2(-10, -14),
                    new Vector2(10, 6), new Vector2(6, 10),
                }, new Color(.86f, .86f, .86f));
                AddPolygon(vertices, new[]
                {
                    new Vector2(6, 10), new Vector2(10, 6),
                    new Vector2(14, 10), new Vector2(10, 14),
                }, new Color(.52f, .52f, .52f));
                AddPolygon(vertices, new[]
                {
                    new Vector2(-18, -18), new Vector2(-14, -10), new Vector2(-10, -14),
                }, new Color(.95f, .95f, .95f));
            }

            static void AddPolygon(VertexHelper vertices, Vector2[] points, Color tint)
            {
                var first = vertices.currentVertCount;
                for (var index = 0; index < points.Length; index++) vertices.AddVert(points[index], tint, Vector2.zero);
                for (var index = 1; index < points.Length - 1; index++) vertices.AddTriangle(first, first + index, first + index + 1);
            }
        }

        static RectTransform MakeVerticalScroll(string name, RectTransform parent, Vector2 position, Vector2 size)
        {
            var root = Panel(name, parent, new Color(.12f, .12f, .12f), size, position);
            var mask = root.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = false;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root, false); content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1); content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, size.y);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = root;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = .135f;
            scroll.scrollSensitivity = 28;
            var track = Panel("Scroll Track", root, new Color(.35f, .35f, .35f, .34f), new Vector2(6, 0), Vector2.zero);
            track.anchorMin = new Vector2(1, 0); track.anchorMax = new Vector2(1, 1); track.pivot = new Vector2(1, .5f);
            track.offsetMin = new Vector2(-10, 8); track.offsetMax = new Vector2(-4, -8);
            var handle = Panel("Scroll Handle", track, new Color(.12f, .62f, 1f, .9f), new Vector2(6, 40), Vector2.zero);
            handle.anchorMin = new Vector2(0, 1); handle.anchorMax = new Vector2(1, 1); handle.pivot = new Vector2(.5f, 1);
            handle.offsetMin = new Vector2(0, -40); handle.offsetMax = Vector2.zero;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, .9f),
                highlightedColor = Color.white,
                pressedColor = Color.white,
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, .4f),
                colorMultiplier = 1,
                fadeDuration = .1f,
            };
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            return content;
        }

        static Toggle MakeToggle(string text, RectTransform parent, Vector2 position)
        {
            var panel = Panel("Auto Play Toggle", parent, new Color(.08f, .13f, .26f, .95f), new Vector2(300, 48), position);
            var background = panel.GetComponent<Image>();
            background.raycastTarget = true;
            Outline(panel.gameObject, new Color(.45f, .75f, 1f, .8f), 2);
            var check = Panel("Checkmark", panel, new Color(.25f, 1f, .76f, .95f), new Vector2(30, 30), new Vector2(-118, 0)).GetComponent<Image>();
            check.raycastTarget = false;
            var label = Label(text, panel, 22);
            label.rectTransform.sizeDelta = new Vector2(300, 42);
            label.rectTransform.anchoredPosition = Vector2.zero;
            var toggle = panel.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = check;
            toggle.isOn = false;
            return toggle;
        }

        static RectTransform Layer(string name, RectTransform parent)
        {
            var layer = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            layer.SetParent(parent, false); Fill(layer); return layer;
        }

        static Slider MakeSlider(RectTransform parent, Vector2 position, float minimum, float maximum, float initial, Action<float> changed)
        {
            var root = Panel("Speed Slider", parent, new Color(.08f, .13f, .26f, .95f), new Vector2(520, 18), position);
            Outline(root.gameObject, new Color(.45f, .75f, 1f, .8f), 2);
            var fill = Panel("Fill", root, new Color(.25f, 1f, .76f, .9f), Vector2.zero, Vector2.zero, true);
            fill.offsetMin = new Vector2(3, 3); fill.offsetMax = new Vector2(-3, -3);
            var handle = Panel("Handle", root, new Color(.95f, 1f, 1f), new Vector2(28, 42), Vector2.zero);
            Outline(handle.gameObject, new Color(.4f, 1f, .8f), 2);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.minValue = minimum; slider.maxValue = maximum; slider.value = initial; slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>();
            slider.onValueChanged.AddListener(value => changed(value));
            return slider;
        }

        static RectTransform Panel(string name, RectTransform parent, Color color, Vector2 size, Vector2 position, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false); go.GetComponent<Image>().color = color;
            if (stretch) Fill(rect); else { rect.sizeDelta = size; rect.anchoredPosition = position; } return rect;
        }

        static RectTransform RawPanel(string name, RectTransform parent, Texture texture, Color color, Vector2 size, Vector2 position, bool stretch = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage)); var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            var image = go.GetComponent<RawImage>(); image.texture = texture; image.color = color; image.raycastTarget = false;
            if (stretch) Fill(rect); else { rect.sizeDelta = size; rect.anchoredPosition = position; } return rect;
        }

        static Text Label(string content, RectTransform parent, int size)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); var text = go.GetComponent<Text>(); text.rectTransform.SetParent(parent, false);
            // High-resolution phones can be wider than 1440 px in landscape,
            // so screen width alone must not decide whether mobile text is scaled.
            var mobileScale = Application.isMobilePlatform || (Screen.width > 0 && Screen.width <= 1440) ? 1.18f : 1f;
            text.text = content; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = Mathf.RoundToInt(size * mobileScale); text.fontStyle = FontStyle.Normal; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
            // Labels sit above their buttons visually but must not intercept the
            // pointer raycast intended for the clickable parent graphic.
            text.raycastTarget = false;
            return text;
        }

        static void Fill(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; }
        static void Outline(GameObject go, Color color, int width) { var outline = go.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(width, -width); }
        struct TouchMemory { public float Lane; public int GridRow; public Vector2 ScreenPosition; public double EventTime; public double StartTime; public double LastInputRecordTime; }
    }
}
