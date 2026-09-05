using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using Unity.Profiling;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Gugarhythm
{
    public static class GugarhythmPreferenceMigration
    {
        const string CurrentPrefix = "gugarhythm-";
        static readonly string LegacyPrefix = "guga" + "rythm-";
        static bool migrated;

        public static void Migrate()
        {
            if (migrated) return;
            migrated = true;
            var changed = false;
            foreach (var suffix in new[]
                     {
                         "audio-offset-seconds", "settings-delay-offset-seconds", "scroll-speed", "upper-hidden-bar-percent",
                         "music-volume", "key-volume",
                     })
                changed |= MigrateFloat(suffix);
            changed |= MigrateString("bundled-charts-version");
            if (changed) PlayerPrefs.Save();
        }

        static bool MigrateFloat(string suffix)
        {
            var currentKey = CurrentPrefix + suffix;
            var legacyKey = LegacyPrefix + suffix;
            if (!PlayerPrefs.HasKey(legacyKey)) return false;
            if (!PlayerPrefs.HasKey(currentKey)) PlayerPrefs.SetFloat(currentKey, PlayerPrefs.GetFloat(legacyKey));
            PlayerPrefs.DeleteKey(legacyKey);
            return true;
        }

        static bool MigrateString(string suffix)
        {
            var currentKey = CurrentPrefix + suffix;
            var legacyKey = LegacyPrefix + suffix;
            if (!PlayerPrefs.HasKey(legacyKey)) return false;
            if (!PlayerPrefs.HasKey(currentKey)) PlayerPrefs.SetString(currentKey, PlayerPrefs.GetString(legacyKey));
            PlayerPrefs.DeleteKey(legacyKey);
            return true;
        }
    }

    public static class LandscapeOrientation
    {
        public static void Lock()
        {
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
        }
    }

    public static class LibrarySortPreferences
    {
        const string SortModeKey = "gugarhythm-library-sort-mode";
        const string AscendingKey = "gugarhythm-library-sort-ascending";

        public static void Load(out ChartLibrarySort sort, out bool ascending)
        {
            var storedMode = PlayerPrefs.GetInt(SortModeKey, (int)ChartLibrarySort.Accuracy);
            sort = Enum.IsDefined(typeof(ChartLibrarySort), storedMode)
                ? (ChartLibrarySort)storedMode
                : ChartLibrarySort.Accuracy;
            ascending = PlayerPrefs.GetInt(AscendingKey, 0) != 0;
        }

        public static void Save(ChartLibrarySort sort, bool ascending)
        {
            PlayerPrefs.SetInt(SortModeKey, (int)sort);
            PlayerPrefs.SetInt(AscendingKey, ascending ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static class GameplayTimingPreferences
    {
        const string LegacyDeviceOffsetKey = "gugarhythm-audio-offset-seconds";
        const string SettingsDeviceOffsetKey = "gugarhythm-settings-delay-offset-seconds";
        const string SettingsInputOffsetKey = "gugarhythm-settings-input-delay-offset-seconds";

        public static double LoadDeviceOffset()
        {
            GugarhythmPreferenceMigration.Migrate();
            var storedLegacyOffset = PlayerPrefs.GetFloat(LegacyDeviceOffsetKey, 0f);
            var legacyOffset = GugarhythmLandscapePrototype.SanitizeAudioOffset(storedLegacyOffset);
            var settingsOffset = SettingsDelayAdjustment.Clamp(PlayerPrefs.GetFloat(
                SettingsDeviceOffsetKey, (float)legacyOffset));
            var resolvedOffset = GameplayTiming.ReplaceDeviceOffset(settingsOffset);
            if (Math.Abs(resolvedOffset - storedLegacyOffset) <= .000001d) return resolvedOffset;
            PlayerPrefs.SetFloat(LegacyDeviceOffsetKey, (float)resolvedOffset);
            PlayerPrefs.Save();
            return resolvedOffset;
        }

        public static double PersistDeviceOffset(double replacementOffset)
        {
            var resolvedOffset = GameplayTiming.ReplaceDeviceOffset(replacementOffset);
            PlayerPrefs.SetFloat(LegacyDeviceOffsetKey, (float)resolvedOffset);
            PlayerPrefs.SetFloat(SettingsDeviceOffsetKey, (float)resolvedOffset);
            PlayerPrefs.Save();
            return resolvedOffset;
        }

        public static double LoadInputOffset()
        {
            GugarhythmPreferenceMigration.Migrate();
            return GameplayTiming.ReplaceInputOffset(PlayerPrefs.GetFloat(SettingsInputOffsetKey, 0f));
        }

        public static double PersistInputOffset(double replacementOffset)
        {
            var resolvedOffset = GameplayTiming.ReplaceInputOffset(replacementOffset);
            PlayerPrefs.SetFloat(SettingsInputOffsetKey, (float)resolvedOffset);
            PlayerPrefs.Save();
            return resolvedOffset;
        }
    }

    public sealed class GameplayContactCleanupBuffers
    {
        public HashSet<int> ActiveContactIds { get; } = new();
        public List<int> RemovalIds { get; } = new();

        public void BeginFrame()
        {
            ActiveContactIds.Clear();
            RemovalIds.Clear();
        }
    }

    public sealed class GameplayHudState
    {
        bool hasAccuracy;
        double accuracyNumerator;
        int accuracyTotal;
        bool hasCombo;
        int combo;
        bool comboVisible;

        public bool ShouldUpdateAccuracy(double nextAccuracyNumerator, int nextAccuracyTotal)
        {
            if (hasAccuracy && accuracyNumerator.Equals(nextAccuracyNumerator) && accuracyTotal == nextAccuracyTotal)
                return false;
            hasAccuracy = true;
            accuracyNumerator = nextAccuracyNumerator;
            accuracyTotal = nextAccuracyTotal;
            return true;
        }

        public bool ShouldUpdateCombo(int nextCombo, bool nextComboVisible)
        {
            if (hasCombo && combo == nextCombo && comboVisible == nextComboVisible) return false;
            hasCombo = true;
            combo = nextCombo;
            comboVisible = nextComboVisible;
            return true;
        }

        public void Invalidate()
        {
            hasAccuracy = false;
            hasCombo = false;
        }
    }

    public sealed partial class GugarhythmLandscapePrototype : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        static readonly ProfilerMarker GameplayFrameProfiler = new("Gugarhythm.GameplayFrame");
        static readonly ProfilerMarker UpdateVisualsProfiler = new("Gugarhythm.UpdateVisuals");
        static readonly ProfilerMarker NotesProfiler = new("Gugarhythm.UpdateVisuals.Notes");
        static readonly ProfilerMarker HoldsProfiler = new("Gugarhythm.UpdateVisuals.Holds");
        static readonly ProfilerMarker GuidesProfiler = new("Gugarhythm.UpdateVisuals.Guides");
        static readonly ProfilerMarker SimLinesProfiler = new("Gugarhythm.UpdateVisuals.SimLines");
        static readonly ProfilerMarker HoldMeshSubmissionProfiler = new("Gugarhythm.UpdateVisuals.HoldMeshSubmission");
#endif
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
            TraceOneShot = 1 << 1,
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
                var isTerminal = note.IsHoldTerminal || note.HoldCheckpointSource == HoldCheckpointSource.Tail;
                if (isTerminal)
                {
                    if (root >= 0)
                    {
                        endedRoots.Add(root);
                        gate.Deactivate(root);
                    }

                    if (judgment.Grade == JudgmentGrade.Miss)
                        return root >= 0 ? JudgmentAudioRoute.DeactivateHoldLoop : JudgmentAudioRoute.None;
                    if (note.SlideJudgeMode == SlideJudgeMode.Trace)
                        return root >= 0
                            ? JudgmentAudioRoute.TraceOneShot | JudgmentAudioRoute.DeactivateHoldLoop
                            : JudgmentAudioRoute.TraceOneShot;
                    var oneShot = note.SlideJudgeMode == SlideJudgeMode.Flick || note.Kind == RuntimeNoteKind.Flick
                        ? JudgmentAudioRoute.FlickOneShot
                        : JudgmentAudioRoute.GradeOneShot;
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
                if (note.SlideJudgeMode == SlideJudgeMode.Trace) return JudgmentAudioRoute.TraceOneShot;
                return note.SlideJudgeMode == SlideJudgeMode.Flick || note.Kind == RuntimeNoteKind.Flick
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
        const float LibraryDividerHorizontalInset = 16f;
        const float PersistentGrayDividerThickness = 2f;
        const float LaneTextureWidth = 1280f;
        const float LaneTextureHeight = 732f;
        const float LaneTextureCenterX = 638.8049f;
        const float HitSourceY = 500f;
        const float JudgmentStripSourceHeight = 45f;
        const float CentralHalfLanes = 6f;
        // Keep the black fill on the authored -6 through +6 track surface;
        // gameplay layers are clipped separately so out-of-range visuals do
        // not require widening this shape.
        const float VisibleTrackLaneEdge = CentralHalfLanes;
        const float PerspectiveDepthRatio = 3.2f;
        public const float DefaultScrollSpeed = 4f;
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
        // Button sprites begin their visible antialiased edge at pixel 44.
        // Using the old 40px glow bound made every normal Tap visibly narrow.
        const float NormalButtonVisibleEdgePaddingPixels = 44f;
        const float DifficultyButtonSpacing = 180f;
        const int MouseContactId = int.MinValue;
        // Missed notes keep travelling beyond the judgment line until their
        // sprite leaves the viewport. Successful hits return to the pool at once.
        const float NoteExitMargin = 140f;
        const float JudgmentDisplayDuration = .35f;
        public static readonly Vector2 JudgmentSpriteSize = new(330, 110);
        public static readonly float JudgmentTimingSpriteHeight = JudgmentSpriteSize.y * .5f;
        // The source wordmarks have a transparent outer margin, so a small
        // layout overlap keeps the visible letters close to the judgment text.
        public static readonly float JudgmentTimingSpriteCenterYOffset = JudgmentSpriteSize.y * .55f;
        public const float SettingsSliderWidth = 700f;
        // Keep each setting card shorter than the main slider.
        public const float FastLateDisplayWidth = SettingsSliderWidth * .5f - 24f;
        const string FastLateDisplayPreferenceKey = "gugarhythm-fast-late-display";
        const string AutoPlayPreferenceKey = "gugarhythm-auto-play";
        const string HitParticleEffectPreferenceKey = "gugarhythm-hit-particle-effect";
        const float HoldLoopVolume = .55f;
        const float HoldLoopFadeDuration = .04f;

        readonly Dictionary<string, Texture2D> buttonTextures = new(StringComparer.Ordinal);
        readonly Dictionary<string, Texture2D> traceTextures = new(StringComparer.Ordinal);
        readonly Dictionary<JudgmentGrade, Texture2D> judgmentSprites = new();
        readonly Dictionary<JudgmentTiming, Texture2D> judgmentTimingSprites = new();
        readonly JudgmentTimingStatistics judgmentTimingStatistics = new();
        readonly Dictionary<int, HorizontalSlicedRawImage> noteViews = new();
        readonly Dictionary<int, HorizontalSlicedRawImage> persistentHoldHeadViews = new();
        readonly HashSet<int> renderedPersistentHoldHeads = new();
        readonly HashSet<int> renderedNoteIds = new();
        readonly HashSet<RuntimeSimLine> renderedSimLines = new();
        readonly Dictionary<HoldRenderRun, TaperedConnectorGraphic> holdRunViews = new();
        readonly Dictionary<RuntimeConnector, TaperedConnectorGraphic> connectorViews = new();
        readonly Dictionary<RuntimeSimLine, SimLineGraphic> simLineViews = new();
        readonly Dictionary<RuntimeGuide, TaperedConnectorGraphic> guideViews = new();
        readonly Dictionary<int, RuntimeNote> holdRoots = new();
        readonly Dictionary<int, List<RuntimeNote>> holdCheckpoints = new();
        readonly Dictionary<int, bool> holdMissedByRoot = new();
        readonly HoldJudgmentAudioState holdAudioState = new();
        readonly Stack<HorizontalSlicedRawImage> notePool = new();
        readonly Stack<TaperedConnectorGraphic> connectorPool = new();
        readonly Stack<SimLineGraphic> simLinePool = new();
        readonly Stack<TaperedConnectorGraphic> guidePool = new();
        readonly Dictionary<int, TouchMemory> touches = new();
        readonly TouchInputBuffer touchInputBuffer = new();
        readonly List<BufferedTouchSample> bufferedTouchSamples = new(32);
        readonly List<InputToken> inputBatch = new();
        readonly List<ActiveContact> contacts = new();
        readonly List<ContactPathSegment> contactPaths = new();
        readonly List<JudgmentEvent> judgmentEvents = new();
        readonly GameplayContactCleanupBuffers contactCleanupBuffers = new();
        readonly GameplayHudState hudState = new();
        readonly VirtualSliderInput virtualSlider = new();
        readonly float[] connectorPathSamples = new float[ConnectorPathSegments + 3];
        readonly AdaptiveHoldTessellator holdTessellator = new();
        readonly List<HoldProjectedPoint> holdTessellationPoints = new(AdaptiveHoldTessellator.MaxPointsPerRun);
        readonly AdaptiveGuideTessellator guideTessellator = new();
        readonly List<GuideProjectedPoint> guideTessellationPoints = new(AdaptiveGuideTessellator.MaxPoints);
        readonly List<GuideVisualSpan> visibleGuideSpans = new();
        readonly List<RuntimeNote> visibleNotes = new();
        readonly List<HoldRenderRun> visibleHoldRuns = new();
        readonly List<RuntimeSimLine> visibleSimLines = new();
        readonly List<RuntimeGuide> visibleGuides = new();
        readonly Dictionary<RuntimeGuide, GuideRenderCache> guideRenderCaches = new();
        readonly HashSet<RuntimeGuide> exactCpuGuides = new();
        readonly Dictionary<RuntimeHoldPath, HoldRenderCache> holdRenderCaches = new();
        readonly Dictionary<string, HoldVisualRange> holdVisualRanges = new(StringComparer.Ordinal);
        readonly List<int> noteViewReleaseKeys = new();
        readonly List<int> persistentHeadReleaseKeys = new();
        readonly List<HoldRenderRun> holdRunReleaseKeys = new();
        readonly List<RuntimeConnector> connectorReleaseKeys = new();
        readonly List<RuntimeSimLine> simLineReleaseKeys = new();
        readonly List<RuntimeGuide> guideReleaseKeys = new();
        readonly ScoreState scoreState = new();
        readonly List<IChartImporter> importers = new() { new GgrChartImporter() };
        readonly PerformanceSampleWindow performanceSamples = new(1200, 10f);
        readonly GameplayTimingSampleSet gameplayTimingSamples = new(1200, 10f);
        readonly TimingSampleWindow rawDspDeltaSamples = new(1200, 10f);
        readonly TimingSampleWindow presentationDeltaSamples = new(1200, 10f);
        readonly TimingSampleWindow presentationPhaseErrorSamples = new(1200, 10f);
        readonly TimingSampleWindow judgmentDurationSamples = new(1200, 10f);
        readonly TimingSampleWindow inputQueueDelaySamples = new(1200, 10f);
        readonly HotPathFrameMetrics hotPathFrameMetrics = new();
        readonly HotPathTimingSampleSet hotPathTimingSamples = new(1200, 10f);
        readonly VisualFrameContext visualFrameContext = new();
        readonly FrameBudgetCounter frameBudgetCounter = new();
        readonly GuideRenderMetrics guideRenderMetrics = new();
        readonly FrameTiming[] frameTimingBuffer = new FrameTiming[1];

        Texture2D backgroundTexture;
        Func<HoldTessellationPoint, HoldProjectedPoint> holdPointProjector;
        Func<GuideRenderSample, GuideProjectedPoint> guideSampleProjector;
        RuntimeHoldPath projectingHoldPath;
        HoldRenderCache projectingHoldCache;
        double projectingHoldVisualTime;
        GuideRenderCache projectingGuideCache;
        RuntimeChart cachedGuideChart;
        ChartRenderIndex chartRenderIndex;
        int sourceGuidePathCount;
        int renderedGuidePathCount;
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
        AudioClip traceSound;
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
        Canvas gameplayStageCanvas;
        RectTransform safeAreaRoot;
        RectTransform guideLayer;
        GuideBatchGraphic guideBatch;
        RectTransform connectorLayer;
        HoldBatchGraphic holdGreenBatch;
        HoldBatchGraphic holdYellowBatch;
        HoldBatchGraphic missedHoldGreenBatch;
        HoldBatchGraphic missedHoldYellowBatch;
        GpuRibbonRenderer gpuRibbonRenderer;
        string gpuRibbonFallbackReason = string.Empty;
        RectTransform simLineLayer;
        RectTransform persistentHoldHeadLayer;
        RectTransform noteLayer;
        NoteParticleBatchGraphic holdMidMintBatch;
        NoteParticleBatchGraphic holdMidYellowBatch;
        RectMask2D connectorUpperHiddenClip;
        RectMask2D persistentHoldHeadUpperHiddenClip;
        RectMask2D noteUpperHiddenClip;
        TaperedConnectorGraphic upperHiddenMask;
        RectTransform menuPanel;
        RectTransform gameplayLoadingOverlay;
        RectTransform performanceHudPanel;
        RectTransform libraryBackdrop;
        RectTransform settingsPanel;
        RectTransform settingsAudioPanel;
        RectTransform settingsGamePanel;
        RectTransform settingsTagsPanel;
        RectTransform settingsAccountPanel;
        RectTransform difficultyTagConfirmationPanel;
        RectTransform chartEditorPanel;
        RectTransform deleteChartConfirmationPanel;
        RectTransform importDecisionPanel;
        RectTransform detailCoverFallback;
        RectTransform chartPreviewBackdrop;
        RectTransform chartPreviewPanel;
        RectTransform chartPreviewContent;
        RectTransform pauseOverlay;
        RectTransform pauseMenuContent;
        RectTransform resultPanel;
        RectTransform calibrationBackdrop;
        ChartDocumentPreviewGraphic chartPreviewGraphic;
        Text chartPreviewTitle;
        Text accuracyLabel;
        Text comboLabel;
        RawImage judgmentImage;
        RawImage judgmentTimingImage;
        Text loadStatus;
        Text gameplayLoadingLabel;
        Text performanceHudLabel;
        Text libraryCountLabel;
        Text librarySortLabel;
        Text librarySortModeLabel;
        RectTransform libraryDirectionIcon;
        Text detailTitleLabel;
        int detailTitleMaxFontSize;
        Text detailArtistLabel;
        Text detailDifficultyLabel;
        Text detailAccuracyLabel;
        RawImage detailCoverImage;
        Texture2D detailCoverTexture;
        string detailCoverEntryId;
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
        Text upperHiddenBarLabel;
        Text settingsMusicVolumeLabel;
        Text settingsKeyVolumeLabel;
        Text settingsDelayLabel;
        Text settingsInputDelayLabel;
        Text difficultyTagConfirmationText;
        Text calibrationLabel;
        Text chartEditorSubtitleLabel;
        Text chartEditorStatusLabel;
        Button calibrationTapButton;
        Button calibrationRestartButton;
        Button calibrationCloseButton;
        Button calibrationDecreaseOffsetButton;
        Button calibrationIncreaseOffsetButton;
        Button calibrationResetOffsetButton;
        Button settingsAudioNavigationButton;
        Button settingsGameNavigationButton;
        Button settingsTagsNavigationButton;
        Button settingsAccountNavigationButton;
        Button settingsAccountLoginButton;
        Button settingsAccountLogoutButton;
        Button settingsAccountManageButton;
        Button remotePublicScopeButton;
        Button remotePrivateScopeButton;
        Text settingsAccountStatusLabel;
        Text resumeCountdownLabel;
        Text pauseTitle;
        Button startButton;
        Button chartPreviewButton;
        Button localLibrarySourceButton;
        Button onlineLibrarySourceButton;
        Button importLibraryButton;
        Button refreshRemoteLibraryButton;
        Button downloadRemoteChartButton;
        LocalChartEntry selectedLibraryEntry;
        ChartLibrarySource librarySource = ChartLibrarySource.Local;
        IChartVaultClient chartVaultClient;
        RemoteChartCatalogCache remoteCatalogCache;
        RemoteChartDownloadService remoteChartDownloadService;
        RemoteChartCatalog remoteCatalog;
        RemoteChartSummary selectedRemoteChart;
        Texture2D remoteCoverTexture;
        ChartLibrarySort remoteLibrarySort = ChartLibrarySort.Title;
        RemoteChartCatalogScope remoteCatalogScope = RemoteChartCatalogScope.Public;
        bool remoteLibrarySortAscending = true;
        bool remoteLibraryScrollPositionInitialized;
        bool remoteCatalogCacheLoaded;
        bool remoteCatalogRequested;
        bool remoteCatalogLoading;
        bool remoteChartDownloading;
        bool chartVaultLoginPending;
        bool destroying;
        int remoteOperationGeneration;
        int remoteCoverGeneration;
        string chartVaultSessionToken;
        string pendingChartVaultLoginState;
        string pendingChartVaultCodeVerifier;
        string chartVaultDisplayName;
        string chartVaultExpiresAt;
        int chartVaultDeviceCount;
        bool chartVaultProfileLoading;
        bool chartVaultSessionExpired;
        LocalChartEntry chartEditorEntry;
        string selectedDifficultyName = "";
        string pendingDifficultyTagDelete;
        ChartLibrarySort librarySort = ChartLibrarySort.Accuracy;
        bool librarySortAscending;
        bool libraryScrollPositionInitialized;
        Button pauseButton;
        RectTransform calibrationPanel;
        readonly RectTransform[] calibrationProgressDots = new RectTransform[CalibrationRoundCount];
        readonly bool[] calibrationRoundSucceeded = new bool[CalibrationRoundCount];
        Toggle autoPlayToggle;
        Toggle fastLateDisplayToggle;
        readonly Button[] hitParticleEffectButtons = new Button[3];
        Slider speedSlider;
        Slider upperHiddenBarSlider;
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
        readonly GameplayPresentationClock presentationClock = new();
        Rect appliedSafeArea = new(-1, -1, -1, -1);
        float upperHiddenBarLayoutCanvasHeight = float.NaN;
        double scheduledDsp;
        double pauseDsp;
        double accumulatedPause;
        double lastObservedSongTime;
        double interruptedSongTime;
        double audioOffsetSeconds;
        double settingsDelayOffsetSeconds;
        double inputOffsetSeconds;
        double visualOffsetSeconds;
        double calibrationStartDsp;
        readonly List<double> calibrationOffsets = new();
        bool calibrationActive;
        int calibrationRoundIndex;
        bool calibrationFourthBeatTapRegistered;
        float scrollSpeed = DefaultScrollSpeed;
        float upperHiddenBarPercent;
        bool fastLateDisplayEnabled;
        bool autoPlayEnabled;
        HitParticleEffectMode hitParticleEffectMode;
        bool touchCallbacksSubscribed;
        float judgmentHideAt = -1f;
        float nextPerformanceHudRefresh;
        bool gameplayStageVisible;
        bool performanceDiagnosticsEnabled;
        double latestCpuFrameTimeMs = double.NaN;
        double latestGpuFrameTimeMs = double.NaN;
        double previousDiagnosticsRawDspTime = double.NaN;
        double previousDiagnosticsPresentationDspTime = double.NaN;
        float latestNotesMilliseconds;
        float latestHoldsMilliseconds;
        float latestGuidesMilliseconds;
        float latestSimLinesMilliseconds;
        GuideFrameSnapshot latestGuideFrameSnapshot;
        HotPathFrameSnapshot latestHotPathFrameSnapshot;
        ProfilerRecorder gcAllocationRecorder;

        const double PresentationClockFallbackHardResetThreshold = .1d;
        double presentationClockHardResetThreshold = PresentationClockFallbackHardResetThreshold;

        readonly struct HoldVisualRange
        {
            public readonly double NearTime;
            public readonly double FarTime;

            public HoldVisualRange(double nearTime, double farTime)
            {
                NearTime = nearTime;
                FarTime = farTime;
            }
        }

        static float CanvasHeight => ReferenceWidth * Screen.height / Math.Max(1, Screen.width);
        static float TopY => CanvasHeight * .5f;
        static float HitY => TopY - HitSourceY / LaneTextureHeight * CanvasHeight;
        public static int JudgmentDebugCellCount => VirtualSliderInput.CellCount;
        public static float JudgmentDebugCellWidth => VirtualSliderInput.CellWidth;
        public static float JudgmentInputBandHeight(float canvasHeight) =>
            JudgmentStripSourceHeight / LaneTextureHeight * canvasHeight;
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
        public static float JudgmentInputGridCoordinate(float canvasY, float canvasHeight) =>
            (canvasY - JudgmentInputGridStripTop(canvasHeight)) / JudgmentInputBandHeight(canvasHeight);
        public static int JudgmentInputGridRow(float canvasY, float canvasHeight) =>
            Mathf.FloorToInt(JudgmentInputGridCoordinate(canvasY, canvasHeight));
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
        static float NoteExitY => -TopY - NoteExitMargin;
        static float NearTrackProgress => (TopY - NoteExitY) / Mathf.Max(1, TopY - HitY);
        static float NearTrackApproach => 1f + (NearTrackProgress - 1f) / PerspectiveDepthRatio;
        // HitSourceY is the centre of the 45px judgment strip. Notes and
        // gameplay connectors leave only after reaching its lower edge.
        static float JudgmentBottomApproach => 1f + (JudgmentStripSourceHeight * .5f / HitSourceY) / PerspectiveDepthRatio;

        // Scroll speed 4 preserves the original two-second approach. Higher
        // values cover the same visual distance in less chart time.
        float ApproachDuration => NoteApproachDurationForScrollSpeed(scrollSpeed);

        public static float NoteApproachDurationForScrollSpeed(float value)
        {
            value = Mathf.Clamp(value, 1f, 20f);
            return NoteApproachDurationSeconds * DefaultScrollSpeed / value;
        }

        static double FirstWaterfallVisualTime(RuntimeChart runtimeChart)
        {
            if (runtimeChart == null) return 0;
            var firstTime = double.PositiveInfinity;
            foreach (var note in runtimeChart.Notes)
            {
                if (note == null || !note.Visible) continue;
                firstTime = Math.Min(firstTime, runtimeChart.VisualPosition(note.Time, note.TimeScaleGroup));
            }
            foreach (var connector in runtimeChart.Connectors)
            {
                if (connector?.Start != null)
                    firstTime = Math.Min(firstTime, runtimeChart.VisualPosition(connector.Start.Time, connector.Start.TimeScaleGroup));
                if (connector?.End != null)
                    firstTime = Math.Min(firstTime, runtimeChart.VisualPosition(connector.End.Time, connector.End.TimeScaleGroup));
            }
            return double.IsFinite(firstTime) ? firstTime : 0;
        }

        public static float DifficultyButtonWidthForText(string text) =>
            string.Equals(text, "未標示難度", StringComparison.Ordinal) ? 170f : 136f;

        static double FirstWaterfallSongTime(RuntimeChart runtimeChart)
            => FirstWaterfallSongTimeForApproachDuration(runtimeChart, NoteApproachDurationSeconds);

        static double FirstWaterfallSongTimeForApproachDuration(RuntimeChart runtimeChart, double approachDuration)
        {
            if (runtimeChart == null) return 0;
            var firstTime = double.PositiveInfinity;
            Action<RuntimeNote> Add = note =>
            {
                if (note == null || !note.Visible) return;
                var firstVisual = runtimeChart.VisualPosition(note.Time, note.TimeScaleGroup);
                var targetVisual = firstVisual - approachDuration - InitialOffscreenLeadSeconds;
                firstTime = Math.Min(firstTime, runtimeChart.TimeAtVisualPosition(targetVisual, note.TimeScaleGroup));
            };
            foreach (var note in runtimeChart.Notes) Add(note);
            foreach (var connector in runtimeChart.Connectors)
            {
                Add(connector?.Start);
                Add(connector?.End);
            }
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
            var earliestAudioSafeStart = GameplayTiming.EarliestAudioSafeChartTime(bgmOffset, audioOffset);
            return Math.Min(0d, Math.Min(waterfallStart, earliestAudioSafeStart));
        }

        void Awake()
        {
            GugarhythmPreferenceMigration.Migrate();
            holdPointProjector = ProjectHoldPoint;
            guideSampleProjector = ProjectGuideSample;
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;
            Application.deepLinkActivated += HandleChartVaultDeepLink;
            RefreshPresentationClockHardResetThreshold();
            Application.targetFrameRate = 120;
            LandscapeOrientation.Lock();
            QualitySettings.vSyncCount = 0;
            scrollSpeed = Mathf.Clamp(PlayerPrefs.GetFloat("gugarhythm-scroll-speed", DefaultScrollSpeed), 1f, 20f);
            upperHiddenBarPercent = ClampUpperHiddenBarPercent(
                PlayerPrefs.GetFloat("gugarhythm-upper-hidden-bar-percent", 0f));
            fastLateDisplayEnabled = PlayerPrefs.GetInt(FastLateDisplayPreferenceKey, 1) != 0;
            autoPlayEnabled = PlayerPrefs.GetInt(AutoPlayPreferenceKey, 0) != 0;
            hitParticleEffectMode = NormalizeHitParticleEffectMode(
                PlayerPrefs.GetInt(HitParticleEffectPreferenceKey, (int)HitParticleEffectMode.ParticleScatter));
            LibrarySortPreferences.Load(out librarySort, out librarySortAscending);
            var chartVaultStorageRoot = LocalChartLibrary.StorageDirectoryPath;
            chartVaultClient = new ChartVaultClient();
            chartVaultSessionToken = ChartVaultSessionStore.Load();
            if (!string.IsNullOrEmpty(chartVaultSessionToken))
                StartCoroutine(RefreshChartVaultProfile());
            if (string.IsNullOrEmpty(chartVaultSessionToken) &&
                ChartVaultSessionStore.TryLoadPendingLogin(out var savedLoginState, out var savedLoginVerifier))
            {
                pendingChartVaultLoginState = savedLoginState;
                pendingChartVaultCodeVerifier = savedLoginVerifier;
                chartVaultLoginPending = true;
            }
            else
            {
                ChartVaultSessionStore.ClearPendingLogin();
            }
            remoteCatalogCache = new RemoteChartCatalogCache(
                Path.Combine(chartVaultStorageRoot, "chart-vault-public-catalog.json"));
            remoteChartDownloadService = new RemoteChartDownloadService(
                chartVaultClient,
                new PhysicalChartVaultFileStore(),
                new GgrChartImporter(),
                new LocalChartLibraryGateway(),
                new RemoteChartLinkStore(Path.Combine(chartVaultStorageRoot, "chart-vault-links.json")),
                () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            audioOffsetSeconds = GameplayTimingPreferences.LoadDeviceOffset();
            settingsDelayOffsetSeconds = audioOffsetSeconds;
            inputOffsetSeconds = GameplayTimingPreferences.LoadInputOffset();
#if UNITY_EDITOR || UNITY_STANDALONE
            // TouchSimulation can leave the real Mouse device disabled across
            // editor play sessions. Desktop input is adapted explicitly below.
            EnsureDesktopMouseAvailable();
#endif
            EnhancedTouchSupport.Enable();
            SubscribeTouchCallbacks();
            LoadArtwork();
            BuildInterface();
            if (!string.IsNullOrWhiteSpace(Application.absoluteURL))
                HandleChartVaultDeepLink(Application.absoluteURL);
            SetPerformanceDiagnosticsEnabled(false);
            SetStatus("請匯入 GGR 封包。");
        }

        IEnumerator Start()
        {
            // Every scene owns a fresh presentation/controller.  Only the
            // selected package crosses the boundary through ChartSelectionSession.
            if (GugarhythmSceneRouter.IsLibrary)
            {
                SetGameplayStageVisible(false);
                SetMenuHudVisible(false);
                menuPanel.gameObject.SetActive(true);
                settingsPanel.gameObject.SetActive(false);
                RestoreLibrarySelection();
                RefreshLibraryUI();
                startButton.interactable = ShouldEnableLibraryStartButton(librarySource, selectedLibraryEntry != null);
                yield break;
            }

            if (GugarhythmSceneRouter.IsSettings)
            {
                SetGameplayStageVisible(false);
                SetMenuHudVisible(false);
                menuPanel.gameObject.SetActive(false);
                settingsPanel.gameObject.SetActive(true);
                yield break;
            }

            if (GugarhythmSceneRouter.IsChartEditor)
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
                GugarhythmSceneRouter.OpenLibrary();
                yield break;
            }

            SetGameplayStageVisible(true);
            SetGameplayLoadingVisible(true, "正在準備譜面…");
            yield return LoadGameplaySelection(entry, bytes);
        }

        void SetMenuHudVisible(bool visible)
        {
            if (accuracyLabel != null) accuracyLabel.transform.parent.gameObject.SetActive(visible);
            if (comboLabel != null) comboLabel.gameObject.SetActive(false);
            if (judgmentImage != null) judgmentImage.gameObject.SetActive(visible);
            if (judgmentTimingImage != null) judgmentTimingImage.gameObject.SetActive(visible);
            if (pauseButton != null) pauseButton.gameObject.SetActive(false);
        }

        void SetGameplayStageVisible(bool visible)
        {
            gameplayStageVisible = visible;
            if (backgroundLayer != null) backgroundLayer.gameObject.SetActive(visible);
            if (stage != null) stage.gameObject.SetActive(visible);
            if (performanceHudPanel != null) performanceHudPanel.gameObject.SetActive(visible && performanceDiagnosticsEnabled);
        }

        void SetGameplayLoadingVisible(bool visible, string message = null)
        {
            if (gameplayLoadingLabel != null && message != null) gameplayLoadingLabel.text = message;
            if (gameplayLoadingOverlay != null) gameplayLoadingOverlay.gameObject.SetActive(visible);
        }

        bool RestoreLibrarySelection()
        {
            if (!ChartSelectionSession.Ensure().TryGetSelection(out var remembered, out _)) return false;
            var entry = LocalChartLibrary.Load().FirstOrDefault(candidate => candidate.Id == remembered.Id);
            if (entry == null) return false;
            selectedLibraryEntry = entry;
            currentLibraryEntry = entry;
            selectedDifficultyName = entry.DifficultyName ?? string.Empty;
            return true;
        }

        internal static bool ShouldFetchRemoteCatalogOnSourceChange(ChartLibrarySource source,
            bool alreadyRequested) => source == ChartLibrarySource.Online && !alreadyRequested;

        internal static bool ShouldEnableLibraryStartButton(ChartLibrarySource source,
            bool hasLocalSelection) => source == ChartLibrarySource.Local && hasLocalSelection;

        IEnumerator LoadGameplaySelection(LocalChartEntry entry, byte[] bytes)
        {
            loading = true;
            startButton.interactable = false;
            SetGameplayLoadingVisible(true, "正在載入 " + entry.Title + "…");
            SetStatus("正在載入 " + entry.Title + "…");
            yield return null;

            SetGameplayLoadingVisible(true, "正在解析譜面…");
            yield return null;
            var result = new GgrChartImporter().Import(entry.SourceFile, bytes, null);
            if (!result.Success)
            {
                Debug.LogError("無法載入跨場景選取的譜面：" + result.Error);
                if (InputDiagnosticsSession.IsDebugEntry(entry))
                    EndInputDiagnosticsRun("chart-import-failed", true);
                GugarhythmSceneRouter.OpenLibrary();
                yield break;
            }

            presentationClock.Invalidate();
            chart = result.Chart;
            musicLoadSucceeded = false;
            if (chart.BgmBytes != null)
            {
                SetGameplayLoadingVisible(true, "正在準備音訊…");
                yield return LoadMusic(chart.BgmBytes, chart.BgmExtension, chart.BgmStartDelaySeconds);
            }
            if (!musicLoadSucceeded)
            {
                Debug.LogError("跨場景選取的 GGR 音樂無法解碼。");
                if (InputDiagnosticsSession.IsDebugEntry(entry))
                    EndInputDiagnosticsRun("audio-load-failed", true);
                GugarhythmSceneRouter.OpenLibrary();
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
            if (InputDiagnosticsSession.CaptureActive)
                EndInputDiagnosticsRun("scene-destroyed", true);
            destroying = true;
            remoteOperationGeneration++;
            remoteCoverGeneration++;
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
            Application.deepLinkActivated -= HandleChartVaultDeepLink;
            UnsubscribeTouchCallbacks();
            presentationClock.Invalidate();
            StopCalibrationTickAudio();
            ClearHoldSound();
#if UNITY_EDITOR || UNITY_STANDALONE
            TouchSimulation.Disable();
#endif
            if (laneMaterial != null) Destroy(laneMaterial);
            if (missedHoldMaterial != null) Destroy(missedHoldMaterial);
            gpuRibbonRenderer?.Dispose();
            gpuRibbonRenderer = null;
            if (detailCoverTexture != null) Destroy(detailCoverTexture);
            if (remoteCoverTexture != null) Destroy(remoteCoverTexture);
            if (gcAllocationRecorder.Valid) gcAllocationRecorder.Dispose();
        }

        void SubscribeTouchCallbacks()
        {
            if (touchCallbacksSubscribed) return;
            EnsureEnhancedTouchForCallbackMutation();
            Touch.onFingerDown += BufferFingerSample;
            Touch.onFingerMove += BufferFingerSample;
            Touch.onFingerUp += BufferFingerSample;
            touchCallbacksSubscribed = true;
        }

        void UnsubscribeTouchCallbacks()
        {
            if (!touchCallbacksSubscribed) return;
            // A previous scene instance can be destroyed after the next scene
            // has started, or after another owner changed global Input System
            // state. Enhanced Touch requires support to be enabled even when
            // removing callbacks, so restore it before detaching this owner.
            EnsureEnhancedTouchForCallbackMutation();
            Touch.onFingerDown -= BufferFingerSample;
            Touch.onFingerMove -= BufferFingerSample;
            Touch.onFingerUp -= BufferFingerSample;
            touchCallbacksSubscribed = false;
        }

        public static void EnsureEnhancedTouchForCallbackMutation()
        {
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
        }

        void BufferFingerSample(Finger finger)
        {
            if (!running || paused || finger == null) return;
            var touch = finger.lastTouch;
            if (!touch.valid) return;
            InputDiagnosticsSession.RecordTouchQueued(touch.touchId, touch.time,
                touch.screenPosition, touch.phase);
            touchInputBuffer.Enqueue(touch.touchId, touch.time, touch.screenPosition, touch.phase);
        }

        void Update()
        {
            var measurePerformance = performanceDiagnosticsEnabled;
            var gameplayTimingStart = measurePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (measurePerformance) GameplayFrameProfiler.Begin();
            try
            {
#endif
#if UNITY_EDITOR || UNITY_STANDALONE
            // The Input System editor setting can re-enable TouchSimulation
            // after Awake during a domain reload. That component disables the
            // source Mouse device, which also prevents all UI buttons from
            // receiving pointer events. Restore the real mouse every frame;
            // gameplay converts it to touch semantics in CollectMouseAsTouch.
            EnsureDesktopMouseAvailable();
#endif
            RefreshUpperHiddenBarLayoutIfNeeded();
            if (Interlocked.Exchange(ref audioDeviceChangePending, 0) != 0)
            {
                if (ShouldPauseForAudioConfigurationChange(true, running, paused)) PauseForAudioDeviceChange();
                else ClearHoldSound();
            }
            if (running && !paused && chart != null && judgmentEngine != null)
                UpdateGameplayFrame(measurePerformance, gameplayTimingStart);
            UpdatePerformanceHud();
            PollNativeImport();
            UpdateSafeAreaLayout();
            UpdateDesktopSpeedControls();
            UpdateLatencyCalibration();
            if (judgmentHideAt >= 0 && Time.unscaledTime >= judgmentHideAt)
                ClearJudgment();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            }
            finally
            {
                if (measurePerformance) GameplayFrameProfiler.End();
            }
#endif
        }

        void UpdateGameplayFrame(bool measurePerformance, long gameplayTimingStart)
        {
            var rawDspTime = AudioSettings.dspTime;
            var realtime = Time.realtimeSinceStartupAsDouble;
            var authoritativeSongTime = GameplayTiming.ChartTimeAtDsp(
                rawDspTime, scheduledDsp, accumulatedPause, chart.BgmOffset);
            var presentationDspTime = presentationClock.Sample(
                rawDspTime, realtime, presentationClockHardResetThreshold);
            var presentationSongTime = GameplayTiming.ChartTimeAtDsp(
                presentationDspTime, scheduledDsp, accumulatedPause, chart.BgmOffset);
            lastObservedSongTime = authoritativeSongTime;
            CollectInput();
            // Input remains fully routed to JudgmentEngine below.  Do not draw
            // a full-depth lane flash here: it reads as a reflected Hold bar
            // beneath the button rather than input feedback.
            var judgmentTimingStart = measurePerformance ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
            judgmentEngine.ProcessInto(authoritativeSongTime, inputBatch, contacts, contactPaths,
                autoPlayEnabled, judgmentEvents,
                InputDiagnosticsSession.CaptureActive ? inputDiagnosticsDecisions : null);
            RecordInputDiagnosticsDecisions();
            if (measurePerformance)
                RecordFramePacingDiagnostics(rawDspTime, presentationDspTime,
                    MillisecondsBetween(judgmentTimingStart, System.Diagnostics.Stopwatch.GetTimestamp()),
                    Time.unscaledDeltaTime);
            for (var index = 0; index < judgmentEvents.Count; index++) OnJudgment(judgmentEvents[index]);
            UpdateVisuals(presentationSongTime + visualOffsetSeconds);
            RefreshHud();
            if (measurePerformance)
            {
                gameplayTimingSamples.AddFrame(
                    MillisecondsBetween(gameplayTimingStart, System.Diagnostics.Stopwatch.GetTimestamp()),
                    latestNotesMilliseconds, latestHoldsMilliseconds, latestGuidesMilliseconds,
                    latestSimLinesMilliseconds, Time.unscaledDeltaTime);
                hotPathTimingSamples.AddFrame(latestHotPathFrameSnapshot, Time.unscaledDeltaTime);
            }
            if (authoritativeSongTime > chart.LastNoteTime + .75 && AreAllNotesResolved()) FinishGame();
        }

        bool AreAllNotesResolved()
        {
            foreach (var note in chart.Notes)
                if (note.Judged && note.Grade == JudgmentGrade.Pending) return false;
            return true;
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
            presentationClock.Invalidate();
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
            PlayerPrefs.SetFloat("gugarhythm-scroll-speed", value);
        }

        public static float ClampUpperHiddenBarPercent(float value) =>
            float.IsFinite(value) ? Mathf.Clamp(value, 0f, 100f) : 0f;

        public static float UpperHiddenBarScreenProgress(float percent) =>
            ClampUpperHiddenBarPercent(percent) / 100f;

        public static Color UpperHiddenBarMaskColor => new(.01f, .02f, .06f, 1f);

        static void ConfigureUpperHiddenBarMask(TaperedConnectorGraphic mask)
        {
            mask.raycastTarget = false;
            mask.color = UpperHiddenBarMaskColor;
            mask.drawGlow = false;
            mask.drawEdges = false;
            mask.fillAlphaScale = 1f;
            mask.fillAlphaLimit = 1f;
        }

        // iPad autorotation can settle after Awake, so cached mask vertices
        // must follow the live logical canvas height instead of startup dimensions.
        public static bool ShouldRefreshUpperHiddenBarLayout(float previousCanvasHeight, float currentCanvasHeight) =>
            float.IsFinite(currentCanvasHeight) && currentCanvasHeight > 0f &&
            (!float.IsFinite(previousCanvasHeight) ||
             !Mathf.Approximately(previousCanvasHeight, currentCanvasHeight));

        void RefreshUpperHiddenBarLayoutIfNeeded()
        {
            var currentCanvasHeight = CanvasHeight;
            if (!ShouldRefreshUpperHiddenBarLayout(upperHiddenBarLayoutCanvasHeight, currentCanvasHeight)) return;
            upperHiddenBarLayoutCanvasHeight = currentCanvasHeight;
            RefreshUpperHiddenBarGeometry();
        }

        void RefreshUpperHiddenBarGeometry()
        {
            var screenProgress = UpperHiddenBarScreenProgress(upperHiddenBarPercent);
            var clipTopPadding = Mathf.Max(0f, TopY - ScreenY(screenProgress));
            SetUpperHiddenClipPadding(connectorUpperHiddenClip, clipTopPadding);
            SetUpperHiddenClipPadding(persistentHoldHeadUpperHiddenClip, clipTopPadding);
            SetUpperHiddenClipPadding(noteUpperHiddenClip, clipTopPadding);
            if (upperHiddenMask == null || upperHiddenBarPercent <= .0001f) return;
            var topLeft = X(-VisibleTrackLaneEdge, 0f);
            var topRight = X(VisibleTrackLaneEdge, 0f);
            var bottomLeft = X(-VisibleTrackLaneEdge, screenProgress);
            var bottomRight = X(VisibleTrackLaneEdge, screenProgress);
            upperHiddenMask.SetGeometry(
                new Vector2((topLeft + topRight) * .5f, TopY),
                new Vector2((bottomLeft + bottomRight) * .5f, ScreenY(screenProgress)),
                topRight - topLeft, bottomRight - bottomLeft);
        }

        void SetUpperHiddenBarPercent(float value)
        {
            upperHiddenBarPercent = ClampUpperHiddenBarPercent(value);
            if (upperHiddenBarSlider != null && !Mathf.Approximately(upperHiddenBarSlider.value, upperHiddenBarPercent))
                upperHiddenBarSlider.SetValueWithoutNotify(upperHiddenBarPercent);
            if (upperHiddenBarLabel != null)
                upperHiddenBarLabel.text = $"{upperHiddenBarPercent:0}%";
            if (upperHiddenMask != null)
            {
                var visible = upperHiddenBarPercent > .0001f;
                if (upperHiddenMask.gameObject.activeSelf != visible)
                    upperHiddenMask.gameObject.SetActive(visible);
            }
            RefreshUpperHiddenBarGeometry();
            PlayerPrefs.SetFloat("gugarhythm-upper-hidden-bar-percent", upperHiddenBarPercent);
        }

        static void SetUpperHiddenClipPadding(RectMask2D clip, float topPadding)
        {
            if (clip == null) return;
            clip.padding = new Vector4(0f, 0f, 0f, topPadding);
        }

        void SetFastLateDisplay(bool enabled)
        {
            fastLateDisplayEnabled = enabled;
            if (fastLateDisplayToggle != null && fastLateDisplayToggle.isOn != enabled)
                fastLateDisplayToggle.SetIsOnWithoutNotify(enabled);
            fastLateDisplayToggle?.GetComponent<FigmaSlidingToggleVisual>()?.SetState(enabled, true);
            if (!enabled) SetJudgmentSprite(judgmentTimingImage, null);
            PlayerPrefs.SetInt(FastLateDisplayPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static bool ToggleAutoPlaySelection(bool enabled) => !enabled;

        void SetAutoPlayEnabled(bool enabled)
        {
            autoPlayEnabled = enabled;
            if (autoPlayToggle != null && autoPlayToggle.isOn != enabled)
                autoPlayToggle.SetIsOnWithoutNotify(enabled);
            autoPlayToggle?.GetComponent<FigmaSlidingToggleVisual>()?.SetState(enabled, true);
            PlayerPrefs.SetInt(AutoPlayPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }

        public static HitParticleEffectMode NormalizeHitParticleEffectMode(int value) =>
            value >= (int)HitParticleEffectMode.ParticleScatter && value <= (int)HitParticleEffectMode.BrokenRing
                ? (HitParticleEffectMode)value
                : HitParticleEffectMode.ParticleScatter;

        void SetHitParticleEffectMode(HitParticleEffectMode mode)
        {
            hitParticleEffectMode = NormalizeHitParticleEffectMode((int)mode);
            for (var index = 0; index < hitParticleEffectButtons.Length; index++)
            {
                var button = hitParticleEffectButtons[index];
                if (button == null) continue;
                var selected = index == (int)hitParticleEffectMode;
                button.targetGraphic.color = selected ? new Color(.06f, .58f, .96f) : new Color(.18f, .18f, .18f);
                var label = button.GetComponentInChildren<Text>();
                if (label != null) label.color = selected ? Color.white : new Color(.78f, .78f, .78f);
            }
            PlayerPrefs.SetInt(HitParticleEffectPreferenceKey, (int)hitParticleEffectMode);
            PlayerPrefs.Save();
        }

        void SetSettingsMusicVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (music != null) music.volume = value;
            if (settingsMusicVolumeLabel != null) settingsMusicVolumeLabel.text = $"{value * 100f:0}%";
            PlayerPrefs.SetFloat("gugarhythm-music-volume", value);
            PlayerPrefs.Save();
        }

        void SetSettingsKeyVolume(float value)
        {
            value = Mathf.Clamp01(value);
            if (effects != null) effects.volume = value;
            if (holdEffects != null) holdEffects.volume = value;
            if (settingsKeyVolumeLabel != null) settingsKeyVolumeLabel.text = $"{value * 100f:0}%";
            PlayerPrefs.SetFloat("gugarhythm-key-volume", value);
            PlayerPrefs.Save();
        }

        void AdjustSettingsDelay(double delta)
        {
            var nextOffset = SettingsDelayAdjustment.Step(settingsDelayOffsetSeconds, delta);
            if (Math.Abs(nextOffset - settingsDelayOffsetSeconds) <= .0000001d) return;
            settingsDelayOffsetSeconds = nextOffset;
            audioOffsetSeconds = GameplayTimingPreferences.PersistDeviceOffset(settingsDelayOffsetSeconds);
            settingsDelayOffsetSeconds = audioOffsetSeconds;
            RefreshSettingsDelayLabel();
        }

        void AdjustSettingsInputDelay(double delta)
        {
            var nextOffset = SettingsDelayAdjustment.Step(inputOffsetSeconds, delta);
            if (Math.Abs(nextOffset - inputOffsetSeconds) <= .0000001d) return;
            inputOffsetSeconds = GameplayTimingPreferences.PersistInputOffset(nextOffset);
            RefreshSettingsInputDelayLabel();
        }

        public static string DelayAdjustmentTimingHint(double delta) =>
            delta < 0d ? "LATE" : delta > 0d ? "FAST" : string.Empty;

        public static string DelayAdjustmentGuidance(double delta) =>
            delta < 0d ? "LATE 較多：往 − 調" :
            delta > 0d ? "FAST 較多：往 ＋ 調" : string.Empty;

        void RefreshSettingsDelayLabel()
        {
            if (settingsDelayLabel != null)
                settingsDelayLabel.text = $"{settingsDelayOffsetSeconds * 1000d:+0;-0;0} ms";
        }

        void RefreshSettingsInputDelayLabel()
        {
            if (settingsInputDelayLabel != null)
                settingsInputDelayLabel.text = $"{inputOffsetSeconds * 1000d:+0;-0;0} ms";
        }

        void ShowSettingsAudio()
        {
            if (settingsAudioPanel == null || settingsGamePanel == null || settingsTagsPanel == null || settingsAccountPanel == null) return;
            HideInputDiagnosticsSettings();
            settingsAudioPanel.gameObject.SetActive(true);
            settingsGamePanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsAccountPanel.gameObject.SetActive(false);
            settingsAudioNavigationButton.GetComponent<Image>().color = new Color(.08f, .28f, .42f);
            settingsGameNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsTagsNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsAccountNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
        }

        void ShowSettingsGame()
        {
            if (settingsAudioPanel == null || settingsGamePanel == null || settingsTagsPanel == null || settingsAccountPanel == null) return;
            HideInputDiagnosticsSettings();
            settingsAudioPanel.gameObject.SetActive(false);
            settingsGamePanel.gameObject.SetActive(true);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsAccountPanel.gameObject.SetActive(false);
            settingsAudioNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsGameNavigationButton.GetComponent<Image>().color = new Color(.08f, .28f, .42f);
            settingsTagsNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsAccountNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
        }

        void ShowSettingsTags()
        {
            if (settingsAudioPanel == null || settingsGamePanel == null || settingsTagsPanel == null || settingsAccountPanel == null) return;
            HideInputDiagnosticsSettings();
            settingsAudioPanel.gameObject.SetActive(false);
            settingsGamePanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(true);
            settingsAccountPanel.gameObject.SetActive(false);
            settingsAudioNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsGameNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsTagsNavigationButton.GetComponent<Image>().color = new Color(.08f, .28f, .42f);
            settingsAccountNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
        }

        void ShowSettingsAccount()
        {
            if (settingsAudioPanel == null || settingsGamePanel == null || settingsTagsPanel == null || settingsAccountPanel == null) return;
            HideInputDiagnosticsSettings();
            settingsAudioPanel.gameObject.SetActive(false);
            settingsGamePanel.gameObject.SetActive(false);
            settingsTagsPanel.gameObject.SetActive(false);
            settingsAccountPanel.gameObject.SetActive(true);
            settingsAudioNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsGameNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsTagsNavigationButton.GetComponent<Image>().color = new Color(.18f, .18f, .18f);
            settingsAccountNavigationButton.GetComponent<Image>().color = new Color(.08f, .28f, .42f);
            RefreshAccountSettings();
        }

        void OpenAutoAdjustPanel()
        {
            StartLatencyCalibration();
        }

        void StartLatencyCalibration()
        {
            if (running || calibrationPanel == null) return;
            StopCalibrationTickAudio();
            calibrationOffsets.Clear();
            Array.Clear(calibrationRoundSucceeded, 0, calibrationRoundSucceeded.Length);
            calibrationRoundIndex = 0;
            calibrationFourthBeatTapRegistered = false;
            calibrationStartDsp = GameplayTiming.ChartAnchorDspForDeviceOffset(AudioSettings.dspTime + .8d, audioOffsetSeconds);
            ScheduleCalibrationTicks();
            calibrationActive = true;
            calibrationBackdrop?.gameObject.SetActive(true);
            calibrationPanel.gameObject.SetActive(true);
            RefreshManualAudioOffsetControls();
            calibrationLabel.text = "第 1 / 4 輪 · 請在重拍按 TAP";
            calibrationTapButton.interactable = true;
            calibrationRestartButton.interactable = true;
            calibrationCloseButton.interactable = true;
            RefreshCalibrationProgress();
        }

        void ReturnFromLatencyCalibration()
        {
            calibrationActive = false;
            StopCalibrationTickAudio();
            RefreshManualAudioOffsetControls();
            calibrationBackdrop?.gameObject.SetActive(false);
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
                calibrationRoundIndex++;

                if (calibrationRoundIndex >= CalibrationRoundCount)
                {
                    calibrationLabel.text = calibrationOffsets.Count > 0
                        ? $"已調整 {audioOffsetSeconds * 1000d:+0;-0;0} ms"
                        : "未偵測到拍點";
                    calibrationActive = false;
                    StopCalibrationTickAudio();
                    RefreshManualAudioOffsetControls();
                    calibrationTapButton.interactable = false;
                    calibrationRestartButton.interactable = true;
                    calibrationCloseButton.interactable = true;
                    RefreshCalibrationProgress();
                    return;
                }

                calibrationFourthBeatTapRegistered = false;
                calibrationStartDsp = AudioSettings.dspTime + CalibrationRoundGapSeconds;
                ScheduleCalibrationTicks();
                calibrationLabel.text = $"第 {calibrationRoundIndex + 1} / {CalibrationRoundCount} 輪 · 請在重拍按 TAP";
                RefreshCalibrationProgress();
            }
        }

        void RegisterCalibrationTapFromButton() => RegisterCalibrationTap(InputEventDspTime(InputState.currentTime));

        void RegisterCalibrationTap(double inputDsp)
        {
            if (!calibrationActive || calibrationFourthBeatTapRegistered) return;
            var fourthBeatDsp = CalibrationBeatDsp(CalibrationBeatsPerRound - 1);
            if (!LatencyCalibrationMath.IsCalibrationTapWithinWindow(inputDsp, fourthBeatDsp))
            {
                calibrationLabel.text = "太早或太晚，請在重拍按 TAP";
                return;
            }
            var expectedBeatDsp = fourthBeatDsp;
            var offset = CalibrationAudioOffsetForTap(inputDsp, expectedBeatDsp);
            if (!LatencyCalibrationMath.IsTapOffsetValid(offset))
            {
                calibrationLabel.text = "太早或太晚，請在重拍按 TAP";
                return;
            }
            calibrationOffsets.Add(offset);
            calibrationFourthBeatTapRegistered = true;
            calibrationRoundSucceeded[calibrationRoundIndex] = true;
            if (LatencyCalibrationMath.TryGetRunningCalibrationAverage(calibrationOffsets, out var average))
            {
                SetAudioOffset(average);
                calibrationLabel.text = $"已調整 {audioOffsetSeconds * 1000d:+0;-0;0} ms · 本輪已收錄";
            }
            RefreshCalibrationProgress();
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
            audioOffsetSeconds = GameplayTimingPreferences.PersistDeviceOffset(value);
            settingsDelayOffsetSeconds = audioOffsetSeconds;
            RefreshSettingsDelayLabel();
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

        void RefreshCalibrationProgress()
        {
            for (var index = 0; index < calibrationProgressDots.Length; index++)
            {
                if (calibrationProgressDots[index] == null) continue;
                var image = calibrationProgressDots[index].GetComponent<Image>();
                image.color = calibrationRoundSucceeded[index]
                    ? new Color(.06f, .58f, .96f)
                    : index == calibrationRoundIndex && calibrationActive
                        ? new Color(.25f, .78f, 1f)
                        : new Color(.30f, .32f, .35f);
            }
        }

        const int CalibrationBeatsPerRound = LatencyCalibrationMath.TapsPerRound;
        const int CalibrationRoundCount = LatencyCalibrationMath.CalibrationRoundCount;
        const int CalibrationTickCount = CalibrationBeatsPerRound;
        const double CalibrationBeatDurationSeconds = .6d;
        const double CalibrationTapWindowSeconds = LatencyCalibrationMath.TapWindowSeconds;
        const double CalibrationRoundGapSeconds = .8d;

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
                source.volume = beatIndex == CalibrationBeatsPerRound - 1 ? .95f : .62f;
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
            if (GugarhythmSceneRouter.IsLibrary)
            {
                if (librarySource != ChartLibrarySource.Local) return;
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

                GugarhythmSceneRouter.OpenGameplay();
                return;
            }

            StartGameplay();
        }

        void StartGameplay()
        {
            if (loading || chart == null || music.clip == null) return;
            CancelResumeCountdown();
            presentationClock.Invalidate();
            BeginInputDiagnosticsRunIfNeeded();
            ResetRuntime();
            performanceSamples.Reset();
            gameplayTimingSamples.Reset();
            hotPathTimingSamples.Reset();
            hotPathFrameMetrics.Reset();
            frameBudgetCounter.Reset();
            latestCpuFrameTimeMs = double.NaN;
            latestGpuFrameTimeMs = double.NaN;
            latestNotesMilliseconds = 0;
            latestHoldsMilliseconds = 0;
            latestGuidesMilliseconds = 0;
            latestSimLinesMilliseconds = 0;
            latestGuideFrameSnapshot = default;
            latestHotPathFrameSnapshot = default;
            nextPerformanceHudRefresh = 0;
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
            var rawDspTime = AudioSettings.dspTime;
            var realtime = Time.realtimeSinceStartupAsDouble;
            RefreshPresentationClockHardResetThreshold();
            var playbackReadyDsp = rawDspTime + .25d;
            var firstWaterfallSongTime = FirstWaterfallSongTimeForApproachDuration(chart, ApproachDuration);
            var earliestAudioSafeStart = GameplayTiming.EarliestAudioSafeChartTime(chart.BgmOffset, audioOffsetSeconds);
            var initialSongTime = Math.Min(0d, Math.Min(firstWaterfallSongTime, earliestAudioSafeStart));
            scheduledDsp = GameplayTiming.ScheduledDspForChartTime(playbackReadyDsp, initialSongTime, chart.BgmOffset);
            music.time = 0;
            // Prebuild every chart object at its off-screen perspective
            // position before the scheduled audio begins. Only objects near
            // the visible waterfall are kept active; the pool absorbs the
            // first activation without rendering the whole chart at once.
            lastObservedSongTime = GameplayTiming.ChartTimeAtDsp(
                rawDspTime, scheduledDsp, accumulatedPause, chart.BgmOffset);
            presentationClock.Reset(rawDspTime, realtime);
            ResetFramePacingDiagnostics(rawDspTime, rawDspTime);
            SetGameplayStageVisible(true);
            UpdateVisuals(lastObservedSongTime + visualOffsetSeconds);
            SetGameplayLoadingVisible(false);
            music.PlayScheduled(GameplayTiming.PlaybackDspForSchedule(scheduledDsp, audioOffsetSeconds));
            if (stageSound != null) effects.PlayOneShot(stageSound, .72f);
            ClearJudgment();
        }

        void PauseGame()
        {
            if (!running || paused) return;
            paused = true;
            pauseDsp = AudioSettings.dspTime;
            presentationClock.Invalidate();
            music.Pause();
            effects.Pause();
            holdEffects.Pause();
            touches.Clear();
            touchInputBuffer.Clear();
            bufferedTouchSamples.Clear();
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
            presentationClock.Invalidate();
            RefreshPresentationClockHardResetThreshold();
            music.Stop();
            effects.Stop();
            ClearHoldSound();
            touches.Clear();
            touchInputBuffer.Clear();
            bufferedTouchSamples.Clear();
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
            touchInputBuffer.Clear();
            bufferedTouchSamples.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            if (AudioDeviceRecovery.ShouldRescheduleAfterAudioInterruption(resumeNeedsAudioReschedule))
            {
                var nextDsp = AudioSettings.dspTime + .25;
                var clipTime = GameplayTiming.ClipTimeForChartTime(interruptedSongTime, chart.BgmOffset, audioOffsetSeconds, music.clip.length);
                var playbackDsp = GameplayTiming.PlaybackDspForChartTime(nextDsp, interruptedSongTime, chart.BgmOffset, audioOffsetSeconds);
                music.Stop();
                music.time = clipTime;
                scheduledDsp = GameplayTiming.ScheduledDspForRecovery(nextDsp, interruptedSongTime, chart.BgmOffset);
                accumulatedPause = 0;
                music.PlayScheduled(playbackDsp);
                resumeNeedsAudioReschedule = false;
                // Hold presentation at the recovered chart anchor while DSP
                // catches the newly scheduled start instead of snapping back.
                presentationClock.Reset(nextDsp, Time.realtimeSinceStartupAsDouble);
                ResetFramePacingDiagnostics(nextDsp - .25d, nextDsp);
            }
            else
            {
                var resumeDsp = AudioSettings.dspTime;
                accumulatedPause += resumeDsp - pauseDsp;
                presentationClock.Reset(resumeDsp, Time.realtimeSinceStartupAsDouble);
                ResetFramePacingDiagnostics(resumeDsp, resumeDsp);
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
            presentationClock.Invalidate();
            music.Stop();
            effects.Stop();
            ClearHoldSound();
            StartGame();
        }

        void ExitToMenu()
        {
            CancelResumeCountdown();
            presentationClock.Invalidate();
            running = false;
            paused = false;
            Interlocked.Exchange(ref audioDeviceChangePending, 0);
            resumeNeedsAudioReschedule = false;
            music.Stop();
            effects.Stop();
            ClearHoldSound();
            touches.Clear();
            touchInputBuffer.Clear();
            bufferedTouchSamples.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            ReleaseAllViews();
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);
            RefreshHud();
            ClearJudgment();
            EndInputDiagnosticsRun("returned-to-library", true);
            GugarhythmSceneRouter.OpenLibrary();
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
            gpuRibbonRenderer?.ClearHoldStates();
            scoreState.Reset();
            judgmentTimingStatistics.Reset();
            judgmentEngine = new JudgmentEngine(chart.Notes, scoreState);
            ConfigureInputDiagnosticsJudgmentEngine();
            judgmentEvents.Clear();
            contactCleanupBuffers.BeginFrame();
            hudState.Invalidate();
            touches.Clear();
            touchInputBuffer.Clear();
            bufferedTouchSamples.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            ReleaseAllViews();
            RefreshHud();
        }

        void BuildHoldRenderState()
        {
            holdRoots.Clear();
            holdCheckpoints.Clear();
            holdMissedByRoot.Clear();
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
            var guideStackSummary = GuideStackOptimizer.CollapseExactDuplicates(chart.Guides);
            sourceGuidePathCount = guideStackSummary.SourcePathCount;
            renderedGuidePathCount = guideStackSummary.RenderPathCount;
            chartRenderIndex = new ChartRenderIndex(chart);
            BuildGuideRenderCaches();
        }

        void BuildGuideRenderCaches()
        {
            if (!ReferenceEquals(cachedGuideChart, chart))
            {
                cachedGuideChart = chart;
                guideRenderCaches.Clear();
                exactCpuGuides.Clear();
                foreach (var guide in chart.Guides)
                {
                    var cache = new GuideRenderCache(guide);
                    cache.BuildVisualSpans(chart);
                    guideRenderCaches.Add(guide, cache);
                    if (GpuRibbonGuideRouting.RequiresCpu(chart, cache)) exactCpuGuides.Add(guide);
                }
                holdRenderCaches.Clear();
                foreach (var path in chart.HoldPaths) holdRenderCaches.Add(path, new HoldRenderCache(path, chart));
            }
            var pathCapacity = 0;
            foreach (var cache in guideRenderCaches.Values) pathCapacity += cache.VisualSpanCount;
            guideBatch?.Prepare(pathCapacity, AdaptiveGuideTessellator.MaxPoints);
            var holdPathCapacity = chart.FallbackConnectors.Count;
            foreach (var path in chart.HoldPaths) holdPathCapacity += path.RenderRuns.Count;
            var holdPointCapacity = Math.Max(ConnectorPathSegments + 2, AdaptiveHoldTessellator.MaxPointsPerRun);
            holdGreenBatch?.Prepare(holdPathCapacity, holdPointCapacity);
            holdYellowBatch?.Prepare(holdPathCapacity, holdPointCapacity);
            missedHoldGreenBatch?.Prepare(holdPathCapacity, holdPointCapacity);
            missedHoldYellowBatch?.Prepare(holdPathCapacity, holdPointCapacity);
            var midMintCapacity = 0;
            var midYellowCapacity = 0;
            foreach (var note in chart.Notes)
            {
                if (!note.IsHoldMidArchetype) continue;
                if (note.Critical) midYellowCapacity++; else midMintCapacity++;
            }
            holdMidMintBatch?.Prepare(midMintCapacity);
            holdMidYellowBatch?.Prepare(midYellowCapacity);
            if (gpuRibbonRenderer == null || !ReferenceEquals(gpuRibbonRenderer.Chart, chart))
            {
                gpuRibbonRenderer?.Dispose();
                gpuRibbonRenderer = null;
                if (!GpuRibbonRenderer.TryCreate(chart, guideRenderCaches, guideLayer, connectorLayer,
                        gameplayStageCanvas, holdGreenConnectorTexture, holdYellowConnectorTexture,
                        out gpuRibbonRenderer, out gpuRibbonFallbackReason))
                {
                    Debug.LogWarning(gpuRibbonFallbackReason);
                }
            }
            var cpuGuides = gpuRibbonRenderer?.RendersGuides != true || exactCpuGuides.Count > 0;
            var cpuHolds = gpuRibbonRenderer?.RendersHolds != true;
            if (guideBatch != null) guideBatch.gameObject.SetActive(cpuGuides);
            if (holdGreenBatch != null) holdGreenBatch.gameObject.SetActive(cpuHolds);
            if (holdYellowBatch != null) holdYellowBatch.gameObject.SetActive(cpuHolds);
            if (missedHoldGreenBatch != null) missedHoldGreenBatch.gameObject.SetActive(cpuHolds);
            if (missedHoldYellowBatch != null) missedHoldYellowBatch.gameObject.SetActive(cpuHolds);
        }

        double CurrentSongTime() => GameplayTiming.ChartTimeAtDsp(
            AudioSettings.dspTime, scheduledDsp, accumulatedPause, chart.BgmOffset);

        double CurrentInputSongTime() => GameplayTiming.ApplyInputOffset(CurrentSongTime(), inputOffsetSeconds);

        void RefreshPresentationClockHardResetThreshold()
        {
            AudioSettings.GetDSPBufferSize(out var bufferLength, out _);
            var sampleRate = AudioSettings.outputSampleRate;
            var threshold = bufferLength > 0 && sampleRate > 0
                ? 2d * bufferLength / sampleRate
                : PresentationClockFallbackHardResetThreshold;
            presentationClockHardResetThreshold = double.IsFinite(threshold) && threshold > 0
                ? threshold
                : PresentationClockFallbackHardResetThreshold;
        }

        void CollectInput()
        {
            inputBatch.Clear();
            contacts.Clear();
            contactPaths.Clear();
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
            touchInputBuffer.DrainTo(bufferedTouchSamples);
            for (var index = 0; index < bufferedTouchSamples.Count; index++)
            {
                var sample = bufferedTouchSamples[index];
                var queueMilliseconds = Math.Max(0, InputState.currentTime - sample.Time) * 1000d;
                if (performanceDiagnosticsEnabled)
                    inputQueueDelaySamples.AddSample(
                        (float)queueMilliseconds, Time.unscaledDeltaTime);
                var tokenStart = inputBatch.Count;
                var inInputBand = TryScreenToLane(sample.ScreenPosition, out var diagnosticLane, out _);
                if (!inInputBand)
                    diagnosticLane = InputLaneAtCanvasX(ScreenToCanvasX(sample.ScreenPosition.x));
                var eventSongTime = InputEventSongTime(sample.Time);
                ProcessBufferedTouchSample(sample);
                if (!InputDiagnosticsSession.CaptureActive) continue;
                InputDiagnosticsSession.RecordTouchProcessed(sample.FingerId, eventSongTime,
                    diagnosticLane, inInputBand, queueMilliseconds, inputBatch.Count - tokenStart);
                for (var tokenIndex = tokenStart; tokenIndex < inputBatch.Count; tokenIndex++)
                    InputDiagnosticsSession.RecordToken(inputBatch[tokenIndex]);
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            contactCleanupBuffers.BeginFrame();
            var seenMouse = contactCleanupBuffers.ActiveContactIds;
            CollectMouseAsTouch(seenMouse);
            if (touches.ContainsKey(MouseContactId) && !seenMouse.Contains(MouseContactId))
            {
                touches.Remove(MouseContactId);
                virtualSlider.Cancel(MouseContactId);
            }
#endif
            foreach (var pair in touches)
                if (pair.Key != MouseContactId)
                    contacts.Add(new ActiveContact(pair.Key, pair.Value.Lane, pair.Value.StartTime));
        }

        void ProcessBufferedTouchSample(BufferedTouchSample sample)
        {
            var id = sample.FingerId;
            var eventTime = InputEventSongTime(sample.Time);
            var isInInputBand = TryScreenToLane(sample.ScreenPosition, out var lane, out var gridCoordinate);
            var wasTracking = touches.TryGetValue(id, out var memory);
            if (!ShouldContinueTrackedContact(wasTracking, isInInputBand)) return;

            var ended = sample.Phase is UnityEngine.InputSystem.TouchPhase.Ended or
                UnityEngine.InputSystem.TouchPhase.Canceled;
            if (!isInInputBand)
            {
                lane = InputLaneAtCanvasX(ScreenToCanvasX(sample.ScreenPosition.x));
                if (ended)
                {
                    contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                        memory.Lane, lane, true));
                    virtualSlider.End(id, eventTime, lane, inputBatch);
                    touches.Remove(id);
                    return;
                }
                if (Vector2.SqrMagnitude(sample.ScreenPosition - memory.ScreenPosition) > .01f)
                    contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                        memory.Lane, lane, false));
                memory.LastInputRecordTime = sample.Time;
                memory.EventTime = eventTime;
                memory.Lane = lane;
                memory.ScreenPosition = sample.ScreenPosition;
                touches[id] = memory;
                return;
            }

            var entering = !wasTracking;
            if (entering)
                memory = new TouchMemory
                {
                    Lane = lane,
                    EventTime = eventTime,
                    StartTime = eventTime,
                    LastInputRecordTime = double.NegativeInfinity,
                };
            if (ended)
            {
                if (!entering)
                {
                    contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                        memory.Lane, lane, true));
                    virtualSlider.End(id, eventTime, lane, inputBatch);
                }
                touches.Remove(id);
                return;
            }
            if (entering || sample.Phase == UnityEngine.InputSystem.TouchPhase.Began)
                virtualSlider.Begin(id, eventTime, lane, gridCoordinate, inputBatch);
            else if (sample.Phase == UnityEngine.InputSystem.TouchPhase.Moved &&
                     Vector2.SqrMagnitude(sample.ScreenPosition - memory.ScreenPosition) > .01f)
            {
                virtualSlider.Move(id, eventTime, lane, gridCoordinate, inputBatch);
                contactPaths.Add(new ContactPathSegment(id, memory.EventTime, eventTime,
                    memory.Lane, lane, false));
            }
            memory.LastInputRecordTime = sample.Time;
            memory.EventTime = eventTime;
            memory.Lane = lane;
            memory.ScreenPosition = sample.ScreenPosition;
            touches[id] = memory;
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
            var eventTime = CurrentInputSongTime();
            var isInInputBand = TryScreenToLane(position, out var lane, out var gridCoordinate);
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
                    ScreenPosition = position,
                    EventTime = eventTime,
                    StartTime = eventTime,
                    LastInputRecordTime = eventTime,
                };
                virtualSlider.Begin(MouseContactId, eventTime, lane, gridCoordinate, inputBatch);
            }
            else if (Vector2.SqrMagnitude(position - memory.ScreenPosition) > .01f)
            {
                virtualSlider.Move(MouseContactId, eventTime, lane, gridCoordinate, inputBatch);
                contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime, eventTime,
                    memory.Lane, lane, false));
                memory.Lane = lane;
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
            GameplayTiming.ApplyInputOffset(
                GameplayTiming.ChartTimeAtDsp(InputEventDspTime(inputTime), scheduledDsp, accumulatedPause, chart.BgmOffset),
                inputOffsetSeconds);

        static float ScreenToCanvasY(float screenY) => (screenY / Math.Max(1, Screen.height) - .5f) * CanvasHeight;
        static float ScreenToCanvasX(float screenX) => (screenX / Math.Max(1, Screen.width) - .5f) * ReferenceWidth;

        bool TryScreenToLane(Vector2 screenPosition, out float lane, out float gridCoordinate)
        {
            var canvasY = ScreenToCanvasY(screenPosition.y);
            if (!IsJudgmentInputBand(canvasY, CanvasHeight))
            {
                lane = default;
                gridCoordinate = default;
                return false;
            }
            // The visible input region intentionally fills the canvas width:
            // canvas left/right are the two outer virtual-slider lanes.
            var canvasX = ScreenToCanvasX(screenPosition.x);
            lane = InputLaneAtCanvasX(canvasX);
            gridCoordinate = JudgmentInputGridCoordinate(canvasY, CanvasHeight);
            return true;
        }

        void OnJudgment(JudgmentEvent judgment)
        {
            InputDiagnosticsSession.RecordJudgment(judgment);
            var timing = judgmentTimingStatistics.Register(judgment);
            ShowJudgment(judgment.Grade);
            ShowJudgmentTiming(timing);
            PlayJudgmentSound(judgment);
            if (judgment.Note != null && judgment.Note.HoldRootIndex >= 0)
            {
                var rootIndex = judgment.Note.HoldRootIndex;
                var missed = IsHoldCurrentlyMissed(rootIndex);
                holdMissedByRoot[rootIndex] = missed;
                gpuRibbonRenderer?.SetHoldMissed(rootIndex, missed);
            }
            if (judgment.Grade != JudgmentGrade.Miss)
            {
                SpawnHitParticle(judgment);
            }
            InputDiagnosticsSession.RecordHitFeedback(judgment, judgment.Grade != JudgmentGrade.Miss);
        }

        void PlayJudgmentSound(JudgmentEvent judgment)
        {
            var wasPlaying = holdAudioState.ShouldPlay;
            var route = holdAudioState.Route(judgment);
            if (wasPlaying != holdAudioState.ShouldPlay)
                TransitionHoldSound(holdAudioState.ShouldPlay);

            if (effects == null) return;
            AudioClip clip;
            var gradeClip = judgment.Grade switch
            {
                JudgmentGrade.Perfect => perfectSound,
                JudgmentGrade.Great => greatSound,
                JudgmentGrade.Good => goodSound,
                _ => null,
            };
            if ((route & JudgmentAudioRoute.FlickOneShot) != 0)
                clip = judgment.Note.Critical && criticalFlickSound != null ? criticalFlickSound : flickSound;
            else if ((route & JudgmentAudioRoute.TraceOneShot) != 0)
                clip = traceSound != null ? traceSound : gradeClip;
            else if ((route & JudgmentAudioRoute.GradeOneShot) != 0)
                clip = gradeClip;
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
            hotPathFrameMetrics.Reset();
            visualFrameContext.BeginFrame(chart, visualTime, performanceDiagnosticsEnabled);
            var sectionStart = PerformanceTimestamp();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled)
            {
                UpdateVisualsProfiler.Begin();
                GuidesProfiler.Begin();
            }
#endif
            renderedPersistentHoldHeads.Clear();
            gpuRibbonRenderer?.UpdateFrame(visualFrameContext, ApproachDuration, CanvasHeight, NearTrackProgress);
            var hasGpuGuides = gpuRibbonRenderer?.RendersGuides == true;
            if (hasGpuGuides && exactCpuGuides.Count == 0)
            {
                latestGuideFrameSnapshot = default;
            }
            else
            {
                guideBatch.BeginFrame();
                chartRenderIndex.QueryGuides(visualFrameContext, 0, ApproachDuration, visibleGuides);
                guideRenderMetrics.Reset();
                guideRenderMetrics.SetCandidateCount(visibleGuides.Count);
                foreach (var guide in visibleGuides)
                {
                    if (hasGpuGuides && !exactCpuGuides.Contains(guide)) continue;
                    var clippingStart = PerformanceTimestamp();
                    var cache = guideRenderCaches[guide];
                    cache.QueryVisibleSpans(visualFrameContext, ApproachDuration, visibleGuideSpans);
                    RecordHotPathSample(HotPathStage.GuideClipping, clippingStart);
                    var sampleCount = 0;
                    foreach (var span in visibleGuideSpans)
                        sampleCount += SetGuidePath(guideBatch, cache, span);
                    if (sampleCount > 0)
                        guideRenderMetrics.RecordGuide(sampleCount, sampleCount * 2,
                            System.Math.Max(0, sampleCount - visibleGuideSpans.Count) * 2);
                }
                guideBatch.EndFrame();
                guideRenderMetrics.SetDirtyCount(guideBatch.LastFrameDirtied ? 1 : 0);
                guideRenderMetrics.SetMeshBuildMilliseconds(guideBatch.MeshBuildMilliseconds);
                latestGuideFrameSnapshot = guideRenderMetrics.Snapshot();
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) GuidesProfiler.End();
#endif
            var sectionEnd = PerformanceTimestamp();
            latestGuidesMilliseconds = MillisecondsBetween(sectionStart, sectionEnd);
            sectionStart = sectionEnd;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) SimLinesProfiler.Begin();
#endif
            renderedSimLines.Clear();
            chartRenderIndex.QuerySimLines(visualFrameContext, 0,
                SimLineIndexAhead(ApproachDuration, CanvasHeight), visibleSimLines);
            foreach (var simLine in visibleSimLines)
            {
                var aApproach = ApproachProgress(simLine.A, visualTime);
                var bApproach = ApproachProgress(simLine.B, visualTime);
                var aScreen = PerspectiveProgress(aApproach);
                var bScreen = PerspectiveProgress(bApproach);
                var aY = ScreenY(aScreen);
                var bY = ScreenY(bScreen);
                var leadingApproach = Mathf.Max(aApproach, bApproach);
                var trailingApproach = Mathf.Min(aApproach, bApproach);
                var visible = Mathf.Min(aY, bY) <= TopY + 8 && HasVisibleDecorationSegment(leadingApproach, trailingApproach);
                if (!visible)
                {
                    continue;
                }
                renderedSimLines.Add(simLine);
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

            simLineReleaseKeys.Clear();
            foreach (var pair in simLineViews)
                if (!renderedSimLines.Contains(pair.Key)) simLineReleaseKeys.Add(pair.Key);
            foreach (var simLine in simLineReleaseKeys)
                if (simLineViews.TryGetValue(simLine, out var line)) ReleaseSimLine(simLine, line);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) SimLinesProfiler.End();
#endif
            sectionEnd = PerformanceTimestamp();
            latestSimLinesMilliseconds = MillisecondsBetween(sectionStart, sectionEnd);
            sectionStart = sectionEnd;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) NotesProfiler.Begin();
#endif
            renderedNoteIds.Clear();
            holdMidMintBatch?.BeginFrame();
            holdMidYellowBatch?.BeginFrame();
            chartRenderIndex.QueryNotes(visualFrameContext, ApproachDuration, ApproachDuration * 2, visibleNotes);
            foreach (var note in visibleNotes)
            {
                var approachProgress = ApproachProgress(note, visualTime);
                var screenProgress = PerspectiveProgress(approachProgress);
                var y = ScreenY(screenProgress);
                var visible = note.Visible && IsInNoteRenderWindow(y, TopY, NoteExitY) &&
                    !ShouldHideHoldHead(note, approachProgress);
                if (!visible)
                {
                    continue;
                }
                // A Hold mid is particle-only and fully transparent, so it
                // never needs a pooled note view: routing it into the shared
                // particle batch instead avoids two dirtied CanvasRenderers
                // per mid every frame.
                if (IsHoldMid(note))
                {
                    RenderHoldMidParticle(note, screenProgress);
                    continue;
                }
                renderedNoteIds.Add(note.Index);
                if (!noteViews.TryGetValue(note.Index, out var view))
                {
                    view = AcquireNoteView(noteLayer);
                    noteViews[note.Index] = view;
                    ApplyNoteTexture(view, note);
                }
                var height = NoteSurfaceHeight(screenProgress);
                if (note.HoldRootIndex == note.Index)
                    height = HoldHeadRenderHeight(height);
                // The sprite has transparent side padding.  Expand only the
                // quad required for its visible body to meet the authored left
                // and right lane boundaries; centering the complete bitmap in
                // the lane made the visible key look too narrow.
                var bodyWidth = LaneWidth(note.Lane, note.Size, screenProgress);
                var renderWidth = NoteRenderQuadWidth(bodyWidth, height, note);
                var renderSize = note.Size * renderWidth / Mathf.Max(.001f, bodyWidth);
                if (note.HoldRootIndex == note.Index)
                    ApplyNoteSurfaceQuad(view, BuildHoldHeadSurface(note.Lane, renderSize, screenProgress, height));
                else
                    ApplyNoteSurfaceQuad(view, BuildNoteSurfaceQuad(note.Lane, renderSize, screenProgress, height));
                view.color = Color.white;
                var traceParticle = view.TraceParticle;
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
                var flickArrow = view.FlickArrow;
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
            holdMidMintBatch?.EndFrame();
            holdMidYellowBatch?.EndFrame();

            noteViewReleaseKeys.Clear();
            foreach (var pair in noteViews)
                if (!renderedNoteIds.Contains(pair.Key)) noteViewReleaseKeys.Add(pair.Key);
            foreach (var key in noteViewReleaseKeys)
                if (noteViews.TryGetValue(key, out var oldView)) ReleaseNoteView(key, oldView);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) NotesProfiler.End();
#endif
            sectionEnd = PerformanceTimestamp();
            latestNotesMilliseconds = MillisecondsBetween(sectionStart, sectionEnd);
            sectionStart = sectionEnd;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) HoldsProfiler.Begin();
#endif
            holdVisualRanges.Clear();
            if (gpuRibbonRenderer?.RendersHolds == true)
            {
                chartRenderIndex.QueryHoldRuns(visualFrameContext, 0, ApproachDuration, visibleHoldRuns);
                foreach (var run in visibleHoldRuns) RenderGpuPersistentHoldHead(run, visualTime);
                foreach (var connector in chart.FallbackConnectors)
                    RenderGpuPersistentHoldHead(connector, visualTime);
            }
            else
            {
                holdGreenBatch.BeginFrame();
                holdYellowBatch.BeginFrame();
                missedHoldGreenBatch.BeginFrame();
                missedHoldYellowBatch.BeginFrame();
                chartRenderIndex.QueryHoldRuns(visualFrameContext, 0, ApproachDuration, visibleHoldRuns);
                foreach (var run in visibleHoldRuns)
                    RenderHoldRun(run, visualTime);
                foreach (var connector in chart.FallbackConnectors)
                {
                    if (!CanRenderLegacyConnector(connector)) continue;
                    var startApproach = ApproachProgress(connector.Start, visualTime);
                    var endApproach = ApproachProgress(connector.End, visualTime);
                    var startScreen = PerspectiveProgress(startApproach);
                    var endScreen = PerspectiveProgress(endApproach);
                    var startY = ScreenY(startScreen);
                    var endY = ScreenY(endScreen);
                    var holdMode = ResolveConnectorRenderMode(connector);
                    var show = startY <= TopY + 8 && (holdMode == HoldConnectorRenderMode.AnchorClipped
                        ? endApproach < JudgmentBottomApproach
                        : endY >= NoteExitY);
                    if (!show) continue;
                    var line = LegacyHoldBatch(connector);
                    SetConnectorPath(line, connector, visualTime, startApproach, endApproach, holdMode);
                    if (connector.Start.HoldRootIndex >= 0 && startApproach >= 1f && endApproach <= 1f &&
                        holdRoots.TryGetValue(connector.Start.HoldRootIndex, out var root) &&
                        ShouldRenderPersistentHoldHead(root))
                    {
                        var headT = FindConnectorProgress(connector, visualTime, 1f, startApproach, endApproach);
                        RenderPersistentHoldHead(root, connector, headT);
                    }
                }
                holdGreenBatch.EndFrame();
                holdYellowBatch.EndFrame();
                missedHoldGreenBatch.EndFrame();
                missedHoldYellowBatch.EndFrame();
            }
            persistentHeadReleaseKeys.Clear();
            foreach (var pair in persistentHoldHeadViews)
                if (!renderedPersistentHoldHeads.Contains(pair.Key)) persistentHeadReleaseKeys.Add(pair.Key);
            foreach (var key in persistentHeadReleaseKeys)
                if (persistentHoldHeadViews.TryGetValue(key, out var oldHead)) ReleasePersistentHoldHead(key, oldHead);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled)
            {
                HoldsProfiler.End();
                UpdateVisualsProfiler.End();
            }
#endif
            latestHoldsMilliseconds = MillisecondsBetween(sectionStart, PerformanceTimestamp());
            if (performanceDiagnosticsEnabled)
            {
                hotPathFrameMetrics.Record(HotPathStage.TimeScalePositionAt,
                    visualFrameContext.PositionAtMilliseconds, visualFrameContext.PositionAtCallCount);
                hotPathFrameMetrics.SetTimeScaleSearchSteps(visualFrameContext.PositionAtSearchStepCount);
                latestHotPathFrameSnapshot = hotPathFrameMetrics.Snapshot();
            }
            else latestHotPathFrameSnapshot = default;
        }

        void RenderGpuPersistentHoldHead(HoldRenderRun run, double visualTime)
        {
            var path = run?.Path;
            if (path == null || !holdRoots.TryGetValue(path.RootIndex, out var root) ||
                !ShouldRenderPersistentHoldHead(root)) return;
            var group = string.IsNullOrEmpty(run.Start.TimeScaleGroup)
                ? run.End.TimeScaleGroup : run.Start.TimeScaleGroup;
            var range = HoldVisualRangeFor(group, visualTime);
            var nearTime = range.NearTime;
            if (nearTime < run.Start.Time - 1e-9 || nearTime > run.End.Time + 1e-9) return;
            RenderPersistentHoldHead(root, path.Evaluator.Evaluate(nearTime));
        }

        void RenderGpuPersistentHoldHead(RuntimeConnector connector, double visualTime)
        {
            if (!CanRenderLegacyConnector(connector)) return;
            var rootIndex = connector.Start.HoldRootIndex;
            if (rootIndex < 0 || !holdRoots.TryGetValue(rootIndex, out var root) ||
                !ShouldRenderPersistentHoldHead(root)) return;
            var startApproach = ApproachProgress(connector.Start, visualTime);
            var endApproach = ApproachProgress(connector.End, visualTime);
            if (startApproach < 1f || endApproach > 1f) return;
            var headT = FindConnectorProgress(connector, visualTime, 1f, startApproach, endApproach);
            RenderPersistentHoldHead(root, connector, headT);
        }

        bool RenderHoldRun(HoldRenderRun run, double visualTime)
        {
            var path = run.Path;
            var group = string.IsNullOrEmpty(run.Start.TimeScaleGroup) ? run.End.TimeScaleGroup : run.Start.TimeScaleGroup;
            var range = HoldVisualRangeFor(group, visualTime);
            var firstVisibleTime = Math.Max(run.Start.Time, Math.Min(range.NearTime, range.FarTime));
            var lastVisibleTime = Math.Min(run.End.Time, Math.Max(range.NearTime, range.FarTime));
            if (lastVisibleTime < firstVisibleTime - 1e-9)
                return false;

            projectingHoldPath = path;
            projectingHoldCache = holdRenderCaches[path];
            projectingHoldVisualTime = visualTime;
            var tessellationStart = PerformanceTimestamp();
            holdTessellator.BuildProjected(run, firstVisibleTime, lastVisibleTime, holdPointProjector, holdTessellationPoints);
            RecordHotPathSample(HotPathStage.HoldTessellation, tessellationStart);
            if (holdTessellationPoints.Count < 2)
                return false;
            var line = HoldRunBatch(run);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) HoldMeshSubmissionProfiler.Begin();
#endif
            var meshWriteStart = PerformanceTimestamp();
            line.BeginPath(holdTessellationPoints.Count);
            for (var index = 0; index < holdTessellationPoints.Count; index++)
            {
                var point = holdTessellationPoints[index];
                line.SetPathPoint(index, point.Position, point.Width);
            }
            line.EndPath();
            RecordHotPathSample(HotPathStage.HoldMeshWrite, meshWriteStart, holdTessellationPoints.Count);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (performanceDiagnosticsEnabled) HoldMeshSubmissionProfiler.End();
#endif

            if (firstVisibleTime <= range.NearTime + 1e-9 && range.NearTime <= lastVisibleTime + 1e-9 &&
                holdRoots.TryGetValue(path.RootIndex, out var root) && ShouldRenderPersistentHoldHead(root))
                RenderPersistentHoldHead(root, path.Evaluator.Evaluate(range.NearTime));
            return true;
        }

        HoldVisualRange HoldVisualRangeFor(string group, double visualTime)
        {
            var key = string.IsNullOrEmpty(group) ? chart.DefaultTimeScaleGroup ?? string.Empty : group;
            if (holdVisualRanges.TryGetValue(key, out var range)) return range;
            var position = visualFrameContext.CurrentPosition(key);
            range = new HoldVisualRange(chart.TimeAtVisualPosition(position, key),
                chart.TimeAtVisualPosition(position + ApproachDuration, key));
            holdVisualRanges.Add(key, range);
            return range;
        }

        HoldProjectedPoint ProjectHoldPoint(HoldTessellationPoint point)
        {
            var projectionStart = PerformanceTimestamp();
            var segment = projectingHoldPath.Segments[point.Sample.SegmentIndex];
            var group = string.IsNullOrEmpty(segment.Start.TimeScaleGroup)
                ? segment.End.TimeScaleGroup : segment.Start.TimeScaleGroup;
            var visualPosition = projectingHoldCache.TryVisualPosition(point, out var cachedPosition)
                ? cachedPosition : visualFrameContext.PositionAt(point.Time, group);
            var approach = visualFrameContext.Approach(visualPosition, group, ApproachDuration);
            var screenProgress = Mathf.Clamp(PerspectiveProgress(approach), 0, NearTrackProgress);
            var projected = new Vector2(X(point.Sample.Lane, screenProgress), ScreenY(screenProgress));
            var width = HoldConnectorLaneWidth(LaneWidth(point.Sample.Lane, point.Sample.Size, screenProgress));
            RecordHotPathSample(HotPathStage.HoldProjection, projectionStart);
            return new HoldProjectedPoint(point, projected, width);
        }

        void RenderPersistentHoldHead(RuntimeNote root, RuntimeConnector connector, float progress)
        {
            var laneProgress = EaseConnector(progress, connector.Ease);
            RenderPersistentHoldHead(root, new HoldPathSample(
                Mathf.Lerp(connector.Start.Lane, connector.End.Lane, laneProgress),
                Mathf.Lerp(connector.Start.Size, connector.End.Size, laneProgress), 0, progress));
        }

        void RenderPersistentHoldHead(RuntimeNote root, HoldPathSample sample)
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
                var particle = view.TraceParticle;
                if (particle != null) particle.gameObject.SetActive(false);
                var flickArrow = view.FlickArrow;
                if (flickArrow != null) flickArrow.gameObject.SetActive(false);
            }
            var lane = sample.Lane;
            var size = sample.Size;
            var screenProgress = PerspectiveProgress(1f);
            var height = HoldHeadRenderHeight(NoteSurfaceHeight(screenProgress));
            // Match the descending head's visible body to the same pair of
            // lane boundaries at the judgment line.
            var bodyWidth = LaneWidth(lane, size, screenProgress);
            var renderWidth = HoldHeadRenderQuadWidth(bodyWidth, height, root.Critical);
            var renderSize = size * renderWidth / Mathf.Max(.001f, bodyWidth);
            ApplyNoteSurfaceQuad(view, BuildHoldHeadSurface(lane, renderSize, screenProgress, height));
        }

        bool IsHoldCurrentlyMissed(RuntimeConnector connector)
        {
            if (connector?.Start == null || connector.Start.HoldRootIndex < 0) return false;
            return IsHoldCurrentlyMissedCached(connector.Start.HoldRootIndex);
        }

        // Called once per judgment (root's checkpoint list changes only then),
        // and repeatedly per frame from render code via the cached variant
        // below, so the O(checkpoints) walk here must not run every frame.
        bool IsHoldCurrentlyMissed(int rootIndex)
        {
            if (rootIndex < 0) return false;
            var latestGrade = holdRoots.TryGetValue(rootIndex, out var root)
                ? root.Grade : JudgmentGrade.Pending;
            if (holdCheckpoints.TryGetValue(rootIndex, out var checkpoints))
                foreach (var checkpoint in checkpoints)
                    if (checkpoint.Grade != JudgmentGrade.Pending) latestGrade = checkpoint.Grade;
            return latestGrade == JudgmentGrade.Miss;
        }

        bool IsHoldCurrentlyMissedCached(int rootIndex)
        {
            if (rootIndex < 0) return false;
            if (holdMissedByRoot.TryGetValue(rootIndex, out var missed)) return missed;
            missed = IsHoldCurrentlyMissed(rootIndex);
            holdMissedByRoot[rootIndex] = missed;
            return missed;
        }

        int SetGuidePath(GuideBatchGraphic batch, GuideRenderCache cache, GuideVisualSpan span)
        {
            projectingGuideCache = cache;
            var tessellationStart = PerformanceTimestamp();
            guideTessellator.BuildProjected(cache, span, guideSampleProjector, guideTessellationPoints);
            RecordHotPathSample(HotPathStage.GuideTessellation, tessellationStart);
            var meshWriteStart = PerformanceTimestamp();
            var guideColor = GuideColor(cache.Color);
            guideColor.a = 1;
            batch.BeginPath(guideColor, guideTessellationPoints.Count);
            for (var index = 0; index < guideTessellationPoints.Count; index++)
            {
                var projected = guideTessellationPoints[index];
                batch.SetPathPoint(index, projected.Center, projected.Width,
                    GuideStackOptimizer.CompositeAlpha(projected.Alpha, cache.StackCount));
            }
            batch.EndPath();
            RecordHotPathSample(HotPathStage.GuideMeshWrite, meshWriteStart, guideTessellationPoints.Count);
            return guideTessellationPoints.Count;
        }

        GuideProjectedPoint ProjectGuideSample(GuideRenderSample sample)
        {
            var projectionStart = PerformanceTimestamp();
            var visualPosition = sample.HasVisualPosition ? sample.VisualPosition :
                visualFrameContext.PositionAt(sample.Time, projectingGuideCache.TimeScaleGroup);
            var approach = visualFrameContext.Approach(visualPosition, projectingGuideCache.TimeScaleGroup, ApproachDuration);
            var screenProgress = Mathf.Clamp(PerspectiveProgress(approach), 0, NearTrackProgress);
            var center = new Vector2(X(sample.Lane, screenProgress), ScreenY(screenProgress));
            var projected = new GuideProjectedPoint(sample.Progress, center,
                LaneWidth(sample.Lane, sample.Size, screenProgress), sample.Alpha);
            RecordHotPathSample(HotPathStage.GuideProjection, projectionStart);
            return projected;
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
                var degenerateTessellationStart = PerformanceTimestamp();
                RecordHotPathSample(HotPathStage.HoldTessellation, degenerateTessellationStart, 2);
                var degenerateMeshWriteStart = PerformanceTimestamp();
                line.BeginPath(2);
                SetConnectorPoint(line, 0, connector, 0, startApproach);
                SetConnectorPoint(line, 1, connector, 1, endApproach);
                line.EndPath();
                RecordHotPathSample(HotPathStage.HoldMeshWrite, degenerateMeshWriteStart, 2);
                return;
            }

            var tessellationStart = PerformanceTimestamp();
            var nearApproach = holdMode == HoldConnectorRenderMode.AnchorClipped ? 1f : NearTrackApproach;
            var nearT = FindConnectorProgress(connector, visualTime, nearApproach, startApproach, endApproach);
            var farT = FindConnectorProgress(connector, visualTime, 0f, startApproach, endApproach);
            var sampleCount = BuildStablePathSamples(nearT, farT);
            RecordHotPathSample(HotPathStage.HoldTessellation, tessellationStart, sampleCount);
            var meshWriteStart = PerformanceTimestamp();
            line.BeginPath(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var t = connectorPathSamples[index];
                SetConnectorPoint(line, index, connector, t, ConnectorApproach(connector, visualTime, t));
            }
            line.EndPath();
            RecordHotPathSample(HotPathStage.HoldMeshWrite, meshWriteStart, sampleCount);
        }

        void SetConnectorPath(HoldBatchGraphic batch, RuntimeConnector connector, double visualTime, float startApproach,
            float endApproach, HoldConnectorRenderMode holdMode)
        {
            var approachSpan = startApproach - endApproach;
            var nearT = 0f;
            var farT = 1f;
            var tessellationStart = PerformanceTimestamp();
            if (approachSpan > 1e-5f)
            {
                var nearApproach = holdMode == HoldConnectorRenderMode.AnchorClipped ? 1f : NearTrackApproach;
                nearT = FindConnectorProgress(connector, visualTime, nearApproach, startApproach, endApproach);
                farT = FindConnectorProgress(connector, visualTime, 0f, startApproach, endApproach);
            }
            var sampleCount = approachSpan <= 1e-5f ? 2 : BuildStablePathSamples(nearT, farT);
            RecordHotPathSample(HotPathStage.HoldTessellation, tessellationStart, sampleCount);
            var meshWriteStart = PerformanceTimestamp();
            batch.BeginPath(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var progress = approachSpan <= 1e-5f ? index : connectorPathSamples[index];
                var approach = approachSpan <= 1e-5f ? (index == 0 ? startApproach : endApproach) :
                    ConnectorApproach(connector, visualTime, progress);
                SetConnectorPoint(batch, index, connector, progress, approach);
            }
            batch.EndPath();
            RecordHotPathSample(HotPathStage.HoldMeshWrite, meshWriteStart, sampleCount);
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

        void SetConnectorPoint(TaperedConnectorGraphic line, int index, RuntimeConnector connector, float timeProgress, float approachProgress)
        {
            var projectionStart = PerformanceTimestamp();
            var laneProgress = EaseConnector(timeProgress, connector.Ease);
            var lane = Mathf.Lerp(connector.Start.Lane, connector.End.Lane, laneProgress);
            var size = Mathf.Lerp(connector.Start.Size, connector.End.Size, laneProgress);
            var screenProgress = Mathf.Clamp(PerspectiveProgress(approachProgress), 0, NearTrackProgress);
            var bodyWidth = LaneWidth(lane, size, screenProgress);
            var position = new Vector2(X(lane, screenProgress), ScreenY(screenProgress));
            var width = HoldConnectorLaneWidth(bodyWidth);
            RecordHotPathSample(HotPathStage.HoldProjection, projectionStart);
            line.SetPathPoint(index, position, width);
        }

        void SetConnectorPoint(HoldBatchGraphic batch, int index, RuntimeConnector connector, float timeProgress,
            float approachProgress)
        {
            var projectionStart = PerformanceTimestamp();
            var laneProgress = EaseConnector(timeProgress, connector.Ease);
            var lane = Mathf.Lerp(connector.Start.Lane, connector.End.Lane, laneProgress);
            var size = Mathf.Lerp(connector.Start.Size, connector.End.Size, laneProgress);
            var screenProgress = Mathf.Clamp(PerspectiveProgress(approachProgress), 0, NearTrackProgress);
            var position = new Vector2(X(lane, screenProgress), ScreenY(screenProgress));
            var width = HoldConnectorLaneWidth(LaneWidth(lane, size, screenProgress));
            RecordHotPathSample(HotPathStage.HoldProjection, projectionStart);
            batch.SetPathPoint(index, position, width);
        }

        static float EaseConnector(float progress, int ease) => HoldPathMath.EaseProgress(progress, ease);

        // approach=0 is the far spawn plane; approach=1 is the judgment edge.
        float ApproachProgress(RuntimeNote note, double visualTime) =>
            ApproachProgress(note.Time, visualTime, note.TimeScaleGroup);

        float ApproachProgress(double noteTime, double visualTime, string timeScaleGroup)
        {
            var notePosition = visualFrameContext.PositionAt(noteTime, timeScaleGroup);
            return visualFrameContext.Approach(notePosition, timeScaleGroup, ApproachDuration);
        }

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

        public static double SimLineIndexAhead(double approachDuration, float canvasHeight)
        {
            var visualDepth = HitSourceY / LaneTextureHeight * Mathf.Max(1f, canvasHeight);
            var farPlaneApproachMargin = 8f * PerspectiveDepthRatio / Mathf.Max(1f, visualDepth);
            return Math.Max(0, approachDuration) * (1d + farPlaneApproachMargin);
        }

        public static bool IsInNoteRenderWindow(float screenY, float topY, float exitY) =>
            screenY <= topY + 8f && screenY >= exitY;

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
            return BuildNoteSurfaceQuadRange(lane - size, lane + size, screenProgress, height);
        }

        public static NoteSurfaceQuad BuildHoldHeadSurface(float lane, float renderSize, float screenProgress, float height)
        {
            return BuildNoteSurfaceQuad(lane, renderSize, screenProgress, height);
        }

        static NoteSurfaceQuad BuildNoteSurfaceQuadRange(
            float leftLane, float rightLane, float screenProgress, float height)
        {
            var centerY = ScreenY(screenProgress);
            var upperY = centerY + height * .5f;
            var lowerY = centerY - height * .5f;
            var upperProgress = ScreenProgressAtY(upperY);
            var lowerProgress = ScreenProgressAtY(lowerY);
            return new NoteSurfaceQuad(
                new Vector2(X(leftLane, upperProgress), upperY),
                new Vector2(X(rightLane, upperProgress), upperY),
                new Vector2(X(rightLane, lowerProgress), lowerY),
                new Vector2(X(leftLane, lowerProgress), lowerY));
        }

        static Vector2 NoteSurfaceQuadCenter(NoteSurfaceQuad quad) =>
            (quad.UpperLeft + quad.UpperRight + quad.LowerRight + quad.LowerLeft) * .25f;

        static void ApplyNoteSurfaceQuad(HorizontalSlicedRawImage view, NoteSurfaceQuad quad)
        {
            var center = NoteSurfaceQuadCenter(quad);
            var width = Mathf.Max(quad.UpperRight.x - quad.UpperLeft.x, quad.LowerRight.x - quad.LowerLeft.x);
            var height = Mathf.Max(quad.UpperLeft.y - quad.LowerLeft.y, quad.UpperRight.y - quad.LowerRight.y);
            view.rectTransform.anchoredPosition = center;
            view.rectTransform.sizeDelta = new Vector2(width, height);
            view.SetSurfaceQuad(quad.UpperLeft - center, quad.UpperRight - center, quad.LowerRight - center, quad.LowerLeft - center);
        }

        void RenderHoldMidParticle(RuntimeNote note, float screenProgress)
        {
            var texture = note.Critical ? holdMidYellowTexture : holdMidMintTexture;
            if (texture == null) return;
            var height = NoteSurfaceHeight(screenProgress);
            var bodyWidth = LaneWidth(note.Lane, note.Size, screenProgress);
            var renderWidth = NoteRenderQuadWidth(bodyWidth, height, note);
            var renderSize = note.Size * renderWidth / Mathf.Max(.001f, bodyWidth);
            var center = NoteSurfaceQuadCenter(BuildNoteSurfaceQuad(note.Lane, renderSize, screenProgress, height));
            // Both official tick layouts use the same square as the note's
            // depth-scaled height; their textures distinguish the larger
            // SlideTick from the smaller Trace diamond.
            var aspect = texture.width / (float)Mathf.Max(1, texture.height);
            var size = new Vector2(height * aspect, height);
            (note.Critical ? holdMidYellowBatch : holdMidMintBatch)?.AddQuad(center, size);
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

        public static bool CanRenderLegacyConnector(RuntimeConnector connector) =>
            connector?.Start != null && connector.End != null;

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
        // Trace slide heads/tails use the same slim body and diamond as
        // ordinary Trace notes. They are passive hold checkpoints rather
        // than full mint Tap/Release buttons. Archetype is immutable after
        // import, so this reads RuntimeNote's cached flag instead of
        // re-parsing the string every call.
        static bool IsTrace(RuntimeNote note) => note.IsTraceArchetype;

        static bool IsHoldMid(RuntimeNote note) => note.IsHoldMidArchetype;

        /// <summary>
        /// Both Trace notes and USC Slide tick/attach particles use the child
        /// particle image. A hold mid deliberately has no parent body, so it
        /// must not be gated by the Trace-only visibility rule.
        /// </summary>
        public static bool ShouldShowNoteParticle(RuntimeNote note, bool hasParticleTexture) =>
            hasParticleTexture && note.Visible && (IsTrace(note) || IsHoldMid(note));

        static bool IsDamage(RuntimeNote note) => note.IsDamageArchetype;

        static float NoteOuterPaddingPixels(RuntimeNote note)
        {
            if (IsDamage(note)) return 21f;
            if (IsTrace(note)) return note.Critical ? 30f : 41f;
            return ButtonOuterPaddingPixels(note.Critical);
        }

        static float ButtonOuterPaddingPixels(bool critical) =>
            critical ? 28f : NormalButtonVisibleEdgePaddingPixels;

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
            var padding = height * ButtonOuterPaddingPixels(critical) / NoteTextureHeight;
            return bodyWidth + padding * 2;
        }

        public static float HoldHeadRenderHeight(float height) => height;

        public static float HoldConnectorRenderWidth(float bodyWidth) =>
            bodyWidth * HoldConnectorTextureWidth / HoldConnectorVisibleTextureWidth;

        public static float HoldConnectorLaneWidth(float bodyWidth) => bodyWidth;
        public static float HoldConnectorSourceUvInset => HoldConnectorVisibleUvInset;

        public static float HoldConnectorRenderWidth(float bodyWidth, float lane, float size, float screenProgress)
        {
            _ = lane;
            _ = size;
            _ = screenProgress;
            return HoldConnectorRenderWidth(bodyWidth);
        }

        public static float HoldConnectorVisibleBodyWidth(float renderWidth) =>
            renderWidth * HoldConnectorVisibleTextureWidth / HoldConnectorTextureWidth;

        void FinishGame()
        {
            CancelResumeCountdown();
            presentationClock.Invalidate();
            running = false;
            paused = false;
            music.Stop();
            ClearHoldSound();
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(false);
            ReleaseAllViews();
            RefreshHud();
            var wasInputDiagnostics = InputDiagnosticsSession.IsDebugEntry(currentLibraryEntry);
            EndInputDiagnosticsRun("chart-completed", true);
            if (currentLibraryEntry != null && !wasInputDiagnostics)
                LocalChartLibrary.UpdateBestAccuracy(currentLibraryEntry.Id, (float)scoreState.AccuracyPercent(chart.PlayableCount));
            resultPanel.gameObject.SetActive(true);
            resultText.text = $"ACCURACY  {scoreState.AccuracyPercent(chart.PlayableCount):F4}%\n\nMAX COMBO  {scoreState.MaxCombo:N0}\n\nPERFECT  {scoreState.Perfect:N0}\nGREAT  {scoreState.Great:N0}\nGOOD  {scoreState.Good:N0}\nMISS  {scoreState.Miss:N0}\n\nFAST      LATE\n{judgmentTimingStatistics.Fast:N0}          {judgmentTimingStatistics.Late:N0}";
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
            traceSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/alternative");
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
            gameplayStageCanvas = stage.gameObject.AddComponent<Canvas>();
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
            var laneShader = Shader.Find("Gugarhythm/Black Transparent UI");
            if (laneShader != null)
            {
                laneMaterial = new Material(laneShader);
                lane.GetComponent<RawImage>().material = laneMaterial;
            }
            var missedHoldShader = Shader.Find("Gugarhythm/Desaturate UI");
            if (missedHoldShader != null) missedHoldMaterial = new Material(missedHoldShader);
            guideLayer = Layer("Decoration Guides", stage);
            // GPU ribbon geometry is static after chart load. A child Canvas keeps
            // unrelated note and input changes from rebuilding the Guide batches.
            guideLayer.gameObject.AddComponent<Canvas>();
            var guideBatchObject = new GameObject("Decoration Guide Batch", typeof(RectTransform), typeof(CanvasRenderer), typeof(GuideBatchGraphic));
            var guideBatchRect = guideBatchObject.GetComponent<RectTransform>(); guideBatchRect.SetParent(guideLayer, false); Fill(guideBatchRect);
            guideBatch = guideBatchObject.GetComponent<GuideBatchGraphic>(); guideBatch.raycastTarget = false; guideBatch.color = Color.white;
            connectorLayer = Layer("Hold Connectors", stage);
            connectorUpperHiddenClip = connectorLayer.gameObject.AddComponent<RectMask2D>();
            holdGreenBatch = CreateHoldBatch("Legacy Hold Green Batch", holdGreenConnectorTexture, null);
            holdYellowBatch = CreateHoldBatch("Legacy Hold Yellow Batch", holdYellowConnectorTexture, null);
            missedHoldGreenBatch = CreateHoldBatch("Legacy Missed Hold Green Batch", holdGreenConnectorTexture, missedHoldMaterial);
            missedHoldYellowBatch = CreateHoldBatch("Legacy Missed Hold Yellow Batch", holdYellowConnectorTexture, missedHoldMaterial);
            simLineLayer = Layer("Synchronization Lines", stage);
            persistentHoldHeadLayer = Layer("Persistent Hold Heads", stage);
            persistentHoldHeadUpperHiddenClip = persistentHoldHeadLayer.gameObject.AddComponent<RectMask2D>();
            noteLayer = Layer("Notes", stage);
            noteUpperHiddenClip = noteLayer.gameObject.AddComponent<RectMask2D>();
            // Pooled note views always SetAsLastSibling on acquire, so
            // creating the mid particle batches first keeps them permanently
            // behind every tap/hold-head view without per-frame reordering.
            holdMidMintBatch = CreateNoteParticleBatch("Hold Mid Mint Batch", holdMidMintTexture);
            holdMidYellowBatch = CreateNoteParticleBatch("Hold Mid Yellow Batch", holdMidYellowTexture);
            var upperHiddenMaskObject = new GameObject("Upper Hidden Bar", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var upperHiddenMaskRect = upperHiddenMaskObject.GetComponent<RectTransform>();
            upperHiddenMaskRect.SetParent(stage, false);
            Fill(upperHiddenMaskRect);
            upperHiddenMask = upperHiddenMaskObject.GetComponent<TaperedConnectorGraphic>();
            ConfigureUpperHiddenBarMask(upperHiddenMask);
            SetUpperHiddenBarPercent(upperHiddenBarPercent);
            safeAreaRoot = Layer("Safe Area UI", root);
            BuildHud(safeAreaRoot, root);
            BuildPerformanceHud(safeAreaRoot);
            BuildMenu(safeAreaRoot);
            BuildSettings(safeAreaRoot);
            BuildLatencyCalibration(safeAreaRoot);
            BuildChartEditor(safeAreaRoot);
            BuildImportDecision(safeAreaRoot);
            BuildPauseOverlay(safeAreaRoot);
            BuildResult(safeAreaRoot);
            BuildChartPreview(safeAreaRoot);
            // The dim blue loading veil must cover the physical display,
            // including Android cutout insets, and remain above menu UI.
            BuildGameplayLoadingOverlay(root);
            UpdateSafeAreaLayout(true);
            SetGameplayStageVisible(false);
        }

        void BuildGameplayLoadingOverlay(RectTransform root)
        {
            gameplayLoadingOverlay = Panel("Gameplay Loading Overlay", root, new Color(.015f, .02f, .06f, .82f), Vector2.zero, Vector2.zero, true);
            var card = Panel("Gameplay Loading Card", gameplayLoadingOverlay, new Color(.08f, .12f, .20f, .96f), new Vector2(620, 220), Vector2.zero);
            Outline(card.gameObject, new Color(.25f, .65f, .90f, .85f), 2);
            gameplayLoadingLabel = Label("正在準備譜面…", card, 30);
            gameplayLoadingLabel.alignment = TextAnchor.MiddleCenter;
            gameplayLoadingLabel.rectTransform.sizeDelta = new Vector2(560, 120);
            gameplayLoadingLabel.rectTransform.anchoredPosition = new Vector2(0, 8);
            gameplayLoadingOverlay.gameObject.SetActive(false);
        }

        void BuildPerformanceHud(RectTransform root)
        {
            performanceHudPanel = Panel("Performance HUD", root, new Color(.015f, .03f, .08f, .88f),
                new Vector2(570, 462), Vector2.zero);
            PinToAnchor(performanceHudPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -112));
            Outline(performanceHudPanel.gameObject, new Color(.25f, .85f, 1f, .82f), 2);
            performanceHudPanel.GetComponent<Image>().raycastTarget = false;
            performanceHudLabel = Label("PERFORMANCE\n等待遊戲資料…", performanceHudPanel, 16);
            performanceHudLabel.alignment = TextAnchor.UpperLeft;
            performanceHudLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
            performanceHudLabel.verticalOverflow = VerticalWrapMode.Overflow;
            performanceHudLabel.rectTransform.anchorMin = Vector2.zero;
            performanceHudLabel.rectTransform.anchorMax = Vector2.one;
            performanceHudLabel.rectTransform.offsetMin = new Vector2(14, 10);
            performanceHudLabel.rectTransform.offsetMax = new Vector2(-12, -10);
        }

        public static bool ShouldShowPerformanceDiagnosticsToggle() => false;

        void SetPerformanceDiagnosticsEnabled(bool enabled)
        {
            if (performanceDiagnosticsEnabled == enabled &&
                (enabled ? gcAllocationRecorder.Valid : !gcAllocationRecorder.Valid))
            {
                if (performanceHudPanel != null)
                    performanceHudPanel.gameObject.SetActive(gameplayStageVisible && enabled);
                return;
            }

            performanceDiagnosticsEnabled = enabled;
            if (performanceHudPanel != null)
                performanceHudPanel.gameObject.SetActive(gameplayStageVisible && enabled);

            if (gcAllocationRecorder.Valid) gcAllocationRecorder.Dispose();
            if (enabled)
                gcAllocationRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);

            performanceSamples.Reset();
            gameplayTimingSamples.Reset();
            ResetFramePacingDiagnostics();
            hotPathTimingSamples.Reset();
            frameBudgetCounter.Reset();
            latestHotPathFrameSnapshot = default;
            latestGuideFrameSnapshot = default;
            latestCpuFrameTimeMs = double.NaN;
            latestGpuFrameTimeMs = double.NaN;
            nextPerformanceHudRefresh = 0;
        }

        void UpdatePerformanceHud()
        {
            if (!performanceDiagnosticsEnabled) return;
            if (performanceHudPanel == null || !performanceHudPanel.gameObject.activeInHierarchy) return;
            if (!running || paused || chart == null || judgmentEngine == null) return;

            performanceSamples.AddFrame(Time.unscaledDeltaTime);
            frameBudgetCounter.AddFrame(Time.unscaledDeltaTime);
            if (Time.unscaledTime < nextPerformanceHudRefresh) return;
            nextPerformanceHudRefresh = Time.unscaledTime + .25f;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, frameTimingBuffer) > 0)
            {
                latestCpuFrameTimeMs = frameTimingBuffer[0].cpuFrameTime;
                latestGpuFrameTimeMs = frameTimingBuffer[0].gpuFrameTime;
            }
            var snapshot = performanceSamples.Snapshot();
            if (snapshot.SampleCount == 0) return;
            var timings = gameplayTimingSamples.Snapshot();
            var rawDspDelta = rawDspDeltaSamples.Snapshot();
            var presentationDelta = presentationDeltaSamples.Snapshot();
            var phaseError = presentationPhaseErrorSamples.Snapshot();
            var judgmentDuration = judgmentDurationSamples.Snapshot();
            var inputQueueDelay = inputQueueDelaySamples.Snapshot();
            var hotPathTimings = hotPathTimingSamples.Snapshot();
            var guideFrame = latestGuideFrameSnapshot;
            var hotPathFrame = latestHotPathFrameSnapshot;

            var refreshRate = Screen.currentResolution.refreshRateRatio.value;
            var gcBytes = gcAllocationRecorder.Valid ? gcAllocationRecorder.LastValue : -1;
            var ribbonStatus = gpuRibbonRenderer != null
                ? $"{(gpuRibbonRenderer.RendersGuides ? $"GPU GUIDE {sourceGuidePathCount}>{renderedGuidePathCount}" : "CPU GUIDE")} + " +
                  $"{(gpuRibbonRenderer.RendersHolds ? $"GPU HOLD {gpuRibbonRenderer.HoldPathCount}" : "CPU HOLD")} " +
                  $"C {gpuRibbonRenderer.ChunkCount} V {gpuRibbonRenderer.VertexCount} " +
                  $"B {gpuRibbonRenderer.StaticBuildCount} CACHE {(gpuRibbonRenderer.CacheHit ? "HIT" : "MISS")}"
                : $"CPU GUIDE {sourceGuidePathCount}>{renderedGuidePathCount} + HOLD {gpuRibbonFallbackReason}";
            performanceHudLabel.text =
                $"FPS  {snapshot.CurrentFps:0.0}   AVG  {snapshot.AverageFps:0.0}   MIN  {snapshot.MinimumFps:0.0}\n" +
                $"FRAME  {1000f / snapshot.CurrentFps:0.00} ms   CPU  {FormatMilliseconds(latestCpuFrameTimeMs)}\n" +
                $"GPU  {FormatMilliseconds(latestGpuFrameTimeMs)}   GC  {FormatBytes(gcBytes)}\n" +
                "10S MAX/P95/P99 ms\n" +
                $"NOTE  {FormatTimingTriplet(timings.Notes)}   HOLD  {FormatTimingTriplet(timings.Holds)}\n" +
                $"GUIDE {FormatTimingTriplet(timings.Guides)}   SIM   {FormatTimingTriplet(timings.SimLines)}\n" +
                $"DSP Δ {FormatTimingTriplet(rawDspDelta)}   PRESENT Δ {FormatTimingTriplet(presentationDelta)}\n" +
                $"PHASE {FormatTimingTriplet(phaseError)}   JUDGE {FormatTimingTriplet(judgmentDuration)}\n" +
                $"INPUT {FormatTimingTriplet(inputQueueDelay)}\n" +
                ribbonStatus + "\n" +
                $"G {guideFrame.CandidateCount}/{guideFrame.VisibleCount}  S {guideFrame.SampleCount}  " +
                $"V {guideFrame.VertexCount}  T {guideFrame.TriangleCount}  D {guideFrame.DirtyCount}  M {guideFrame.MeshBuildMilliseconds:0.00}\n" +
                $"TS C/S {hotPathFrame.TimeScalePositionAt.Calls}/{hotPathFrame.TimeScaleSearchSteps} " +
                $"{FormatHotPathTiming(hotPathFrame.TimeScalePositionAt, hotPathTimings.TimeScalePositionAt)}\n" +
                $"G C/T/P/W  {FormatHotPathTiming(hotPathFrame.GuideClipping, hotPathTimings.GuideClipping)}  " +
                $"{FormatHotPathTiming(hotPathFrame.GuideTessellation, hotPathTimings.GuideTessellation)}\n" +
                $"          {FormatHotPathTiming(hotPathFrame.GuideProjection, hotPathTimings.GuideProjection)}  " +
                $"{FormatHotPathTiming(hotPathFrame.GuideMeshWrite, hotPathTimings.GuideMeshWrite)}\n" +
                $"H T/P/W    {FormatHotPathTiming(hotPathFrame.HoldTessellation, hotPathTimings.HoldTessellation)}  " +
                $"{FormatHotPathTiming(hotPathFrame.HoldProjection, hotPathTimings.HoldProjection)}\n" +
                $"          {FormatHotPathTiming(hotPathFrame.HoldMeshWrite, hotPathTimings.HoldMeshWrite)}\n" +
                $"OTHER {FormatTimingTriplet(timings.Other)}   GAME  {FormatTimingTriplet(timings.Total)}\n" +
                $">8.33 {frameBudgetCounter.Over120HzBudget}  >16.67 {frameBudgetCounter.Over60HzBudget}  >33.33 {frameBudgetCounter.Over30HzBudget}\n" +
                $"{Screen.width} × {Screen.height}   @ {refreshRate:0.##} Hz   {snapshot.SampleCount} frames\n" +
                BuildIdentity.Display;
        }

        static string FormatMilliseconds(double value) =>
            double.IsFinite(value) && value > 0 ? $"{value:0.00} ms" : "--";

        static string FormatTimingTriplet(TimingSnapshot value) => value.SampleCount > 0
            ? $"{value.MaximumMilliseconds:0.00}/{value.P95Milliseconds:0.00}/{value.P99Milliseconds:0.00}"
            : "--/--/--";

        static string FormatHotPathTiming(HotPathStageSnapshot current, TimingSnapshot timing) =>
            $"{current.Calls}:{current.ElapsedMilliseconds:0.00}/{timing.P95Milliseconds:0.00}/{timing.P99Milliseconds:0.00}";

        void ResetFramePacingDiagnostics(double rawDspTime = double.NaN,
            double presentationDspTime = double.NaN)
        {
            rawDspDeltaSamples.Reset();
            presentationDeltaSamples.Reset();
            presentationPhaseErrorSamples.Reset();
            judgmentDurationSamples.Reset();
            inputQueueDelaySamples.Reset();
            previousDiagnosticsRawDspTime = rawDspTime;
            previousDiagnosticsPresentationDspTime = presentationDspTime;
        }

        void RecordFramePacingDiagnostics(double rawDspTime, double presentationDspTime,
            float judgmentMilliseconds, float elapsedSeconds)
        {
            if (!performanceDiagnosticsEnabled) return;
            if (double.IsFinite(previousDiagnosticsRawDspTime))
                rawDspDeltaSamples.AddSample(
                    (float)(Math.Abs(rawDspTime - previousDiagnosticsRawDspTime) * 1000d), elapsedSeconds);
            if (double.IsFinite(previousDiagnosticsPresentationDspTime))
                presentationDeltaSamples.AddSample(
                    (float)(Math.Abs(presentationDspTime - previousDiagnosticsPresentationDspTime) * 1000d), elapsedSeconds);
            presentationPhaseErrorSamples.AddSample(
                (float)(Math.Abs(rawDspTime - presentationDspTime) * 1000d), elapsedSeconds);
            judgmentDurationSamples.AddSample(judgmentMilliseconds, elapsedSeconds);
            previousDiagnosticsRawDspTime = rawDspTime;
            previousDiagnosticsPresentationDspTime = presentationDspTime;
        }

        long PerformanceTimestamp() => performanceDiagnosticsEnabled
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0;

        void RecordHotPathSample(HotPathStage stage, long startTimestamp, int callCount = 1)
        {
            if (!performanceDiagnosticsEnabled) return;
            hotPathFrameMetrics.Record(stage,
                MillisecondsBetween(startTimestamp, System.Diagnostics.Stopwatch.GetTimestamp()), callCount);
        }

        static float MillisecondsBetween(long startTimestamp, long endTimestamp) =>
            (float)((endTimestamp - startTimestamp) * 1000d / System.Diagnostics.Stopwatch.Frequency);

        static string FormatBytes(long bytes)
        {
            if (bytes < 0) return "--";
            if (bytes >= 1024 * 1024) return $"{bytes / (1024d * 1024d):0.00} MB";
            if (bytes >= 1024) return $"{bytes / 1024d:0.0} KB";
            return $"{bytes} B";
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
            var judgmentObject = new GameObject("Judgment Graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            judgmentImage = judgmentObject.GetComponent<RawImage>();
            judgmentImage.rectTransform.SetParent(canvasRoot, false);
            judgmentImage.rectTransform.sizeDelta = JudgmentSpriteSize;
            judgmentImage.raycastTarget = false;
            judgmentImage.enabled = false;
            PinToAnchor(judgmentImage.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero);
            foreach (var grade in new[] { JudgmentGrade.Perfect, JudgmentGrade.Great, JudgmentGrade.Good, JudgmentGrade.Miss })
                judgmentSprites[grade] = Resources.Load<Texture2D>(JudgmentSpriteResourcePath(grade));
            var timingObject = new GameObject("Judgment Timing Graphic", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            judgmentTimingImage = timingObject.GetComponent<RawImage>();
            judgmentTimingImage.rectTransform.SetParent(canvasRoot, false);
            judgmentTimingImage.raycastTarget = false;
            judgmentTimingImage.enabled = false;
            PinToAnchor(judgmentTimingImage.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                new Vector2(0, JudgmentTimingSpriteCenterYOffset));
            foreach (var timing in new[] { JudgmentTiming.Fast, JudgmentTiming.Late })
                judgmentTimingSprites[timing] = Resources.Load<Texture2D>(JudgmentTimingSpriteResourcePath(timing));
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
            FitOverlayPanel(calibrationPanel, new Vector2(560, 440), logicalSafeSize);
            FitChartPreviewPanel(chartPreviewPanel, 32f);
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

        static void FitChartPreviewPanel(RectTransform panel, float inset)
        {
            if (panel == null) return;
            inset = Mathf.Max(0f, inset);
            panel.anchorMin = Vector2.zero;
            panel.anchorMax = Vector2.one;
            panel.pivot = new Vector2(.5f, .5f);
            panel.offsetMin = new Vector2(inset, inset);
            panel.offsetMax = new Vector2(-inset, -inset);
            panel.localScale = Vector3.one;
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
            libraryBackdrop.gameObject.SetActive(GugarhythmSceneRouter.IsLibrary);
            menuPanel = Panel("Chart Library", root, new Color(.11f, .11f, .11f, 1f), new Vector2(1500, 820), Vector2.zero);
            Fill(menuPanel);
            menuPanel.localScale = Vector3.one;
            var library = Panel("Library Pane", menuPanel, new Color(.16f, .16f, .16f, 1f), Vector2.zero, Vector2.zero, true);
            library.anchorMin = new Vector2(0, 0); library.anchorMax = new Vector2(.244f, 1); library.offsetMin = Vector2.zero; library.offsetMax = Vector2.zero;
            var divider = Panel("Library Divider", menuPanel, new Color(.27f, .27f, .27f, 1f), Vector2.zero, Vector2.zero, true);
            divider.anchorMin = new Vector2(.244f, 0); divider.anchorMax = new Vector2(.244f, 1); divider.offsetMin = Vector2.zero; divider.offsetMax = new Vector2(PersistentGrayDividerThickness, 0); divider.GetComponent<Image>().raycastTarget = false;
            var detail = Panel("Detail Pane", menuPanel, new Color(.10f, .10f, .10f, 1f), Vector2.zero, Vector2.zero, true);
            detail.anchorMin = new Vector2(.244f, 0); detail.anchorMax = new Vector2(1, 1); detail.offsetMin = new Vector2(PersistentGrayDividerThickness, 0); detail.offsetMax = Vector2.zero;

            var brand = Label("GUGARHYTHM", library, 19); brand.color = new Color(.68f, .68f, .68f); brand.alignment = TextAnchor.MiddleLeft; brand.rectTransform.sizeDelta = new Vector2(260, 36); PinToAnchor(brand.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -34));
            var heading = Label("譜面保管庫", library, 30); heading.alignment = TextAnchor.MiddleLeft; heading.rectTransform.sizeDelta = new Vector2(270, 50); PinToAnchor(heading.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(34, -74));
            localLibrarySourceButton = MakeFlatButton("本機", library, Vector2.zero,
                () => SelectLibrarySource(ChartLibrarySource.Local), new Vector2(146, 42), new Color(.10f, .34f, .50f));
            PinToAnchor(localLibrarySourceButton.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(34, -132));
            onlineLibrarySourceButton = MakeFlatButton("線上", library, Vector2.zero,
                () => SelectLibrarySource(ChartLibrarySource.Online), new Vector2(146, 42), new Color(.20f, .20f, .20f));
            PinToAnchor(onlineLibrarySourceButton.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(190, -132));
            remotePublicScopeButton = MakeFlatButton("公開", library, Vector2.zero,
                () => SelectRemoteCatalogScope(RemoteChartCatalogScope.Public), new Vector2(146, 42), new Color(.10f, .34f, .50f));
            PinToAnchor(remotePublicScopeButton.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(34, -184));
            remotePrivateScopeButton = MakeFlatButton("私人", library, Vector2.zero,
                () => SelectRemoteCatalogScope(RemoteChartCatalogScope.Private), new Vector2(146, 42), new Color(.20f, .20f, .20f));
            PinToAnchor(remotePrivateScopeButton.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(190, -184));
            var countBadge = Panel("Chart Count Badge", library, new Color(.24f, .24f, .24f), new Vector2(42, 42), Vector2.zero);
            PinToAnchor(countBadge, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-38, -42));
            var countBadgeText = Label("", countBadge, 18); Fill(countBadgeText.rectTransform); libraryCountLabel = countBadgeText;
            librarySearchInput = MakeInputField("搜尋", library, Vector2.zero, new Vector2(0, 56));
            var searchRect = librarySearchInput.GetComponent<RectTransform>(); searchRect.anchorMin = new Vector2(0, 1); searchRect.anchorMax = new Vector2(1, 1); searchRect.pivot = new Vector2(.5f, 1); searchRect.offsetMin = new Vector2(34, -304); searchRect.offsetMax = new Vector2(-22, -248);
            librarySearchInput.onValueChanged.AddListener(_ => RefreshLibraryUI());
            const int libraryHeaderFontSize = 22;
            const float librarySortCenterY = -346;
            librarySortLabel = Label("排序", library, libraryHeaderFontSize); librarySortLabel.color = new Color(.62f, .62f, .62f); librarySortLabel.alignment = TextAnchor.MiddleCenter; librarySortLabel.rectTransform.sizeDelta = new Vector2(72, 46); PinToAnchor(librarySortLabel.rectTransform, new Vector2(0, 1), new Vector2(0, .5f), new Vector2(28, librarySortCenterY));
            librarySortModeLabel = Label("準確率", library, libraryHeaderFontSize); librarySortModeLabel.color = new Color(.9f, .9f, .9f); librarySortModeLabel.alignment = TextAnchor.MiddleCenter; librarySortModeLabel.rectTransform.sizeDelta = new Vector2(112, 46); PinToAnchor(librarySortModeLabel.rectTransform, new Vector2(0, 1), new Vector2(0, .5f), new Vector2(112, librarySortCenterY));
            MakeInvisibleButton(librarySortModeLabel.rectTransform, CycleLibrarySort);
            libraryDirectionIcon = Panel("Sort Direction", library, Color.clear, new Vector2(58, 52), Vector2.zero);
            // Rotate around the icon centre so ascending and descending arrows share the same visual X position.
            PinToAnchor(libraryDirectionIcon, new Vector2(0, 1), new Vector2(.5f, .5f), new Vector2(248, librarySortCenterY));
            AddSortArrowIcon(libraryDirectionIcon);
            MakeInvisibleButton(libraryDirectionIcon, () =>
            {
                if (librarySource == ChartLibrarySource.Online)
                    remoteLibrarySortAscending = !remoteLibrarySortAscending;
                else
                {
                    librarySortAscending = !librarySortAscending;
                    LibrarySortPreferences.Save(librarySort, librarySortAscending);
                }
                RefreshLibraryUI();
            });
            libraryListContent = MakeVerticalScroll("Library Scroll", library, Vector2.zero, new Vector2(0, 0));
            var listRoot = libraryListContent.parent.GetComponent<RectTransform>(); listRoot.anchorMin = new Vector2(0, 0); listRoot.anchorMax = new Vector2(1, 1); listRoot.offsetMin = new Vector2(22, 100); listRoot.offsetMax = new Vector2(-2, -396);

            importLibraryButton = MakeOutlinedButton("＋ 匯入 GGR", library, Vector2.zero, RequestImport, new Vector2(0, 64));
            var importRect = importLibraryButton.GetComponent<RectTransform>(); importRect.anchorMin = new Vector2(0, 0); importRect.anchorMax = new Vector2(1, 0); importRect.pivot = new Vector2(.5f, 0); importRect.offsetMin = new Vector2(22, 22); importRect.offsetMax = new Vector2(-22, 86);
            refreshRemoteLibraryButton = MakeOutlinedButton("↻ 重新整理線上譜面", library, Vector2.zero,
                () => StartCoroutine(RefreshRemoteCatalog(true)), new Vector2(0, 64));
            var refreshRemoteRect = refreshRemoteLibraryButton.GetComponent<RectTransform>(); refreshRemoteRect.anchorMin = new Vector2(0, 0); refreshRemoteRect.anchorMax = new Vector2(1, 0); refreshRemoteRect.pivot = new Vector2(.5f, 0); refreshRemoteRect.offsetMin = new Vector2(22, 22); refreshRemoteRect.offsetMax = new Vector2(-22, 86);
            refreshRemoteLibraryButton.gameObject.SetActive(false);

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
            detailCoverFallback = new GameObject("Cover Fallback", typeof(RectTransform)).GetComponent<RectTransform>();
            detailCoverFallback.SetParent(cover, false);
            Fill(detailCoverFallback);
            var coverMagenta = Panel("Cover Magenta", detailCoverFallback, new Color(.61f, .33f, .45f), new Vector2(760, 250), new Vector2(-112, 205));
            coverMagenta.localRotation = Quaternion.Euler(0, 0, -45);
            var coverCyan = Panel("Cover Cyan", detailCoverFallback, new Color(.29f, .55f, .68f), new Vector2(760, 320), new Vector2(0, 15));
            coverCyan.localRotation = Quaternion.Euler(0, 0, -45);
            var coverBlue = Panel("Cover Blue", detailCoverFallback, new Color(.23f, .35f, .77f), new Vector2(760, 245), new Vector2(145, -190));
            coverBlue.localRotation = Quaternion.Euler(0, 0, -45);
            var coverLetter = Label("G", detailCoverFallback, 142); coverLetter.color = new Color(1f, 1f, 1f, .16f); Fill(coverLetter.rectTransform);
            var coverBrand = Label("GUGARHYTHM\nCHART ARCHIVE", detailCoverFallback, 15); coverBrand.alignment = TextAnchor.UpperRight; coverBrand.rectTransform.sizeDelta = new Vector2(210, 70); coverBrand.rectTransform.anchoredPosition = new Vector2(112, 185);
            detailCoverImage = RawPanel("Cover Artwork", cover, null, Color.white, Vector2.zero, Vector2.zero, true).GetComponent<RawImage>();
            var coverAspect = detailCoverImage.gameObject.AddComponent<AspectRatioFitter>();
            coverAspect.aspectMode = CoverPresentationAspectMode();
            var detailKicker = Label("CHART DETAIL", detail, 18); detailKicker.color = new Color(.64f, .64f, .64f); detailKicker.alignment = TextAnchor.MiddleLeft; detailKicker.rectTransform.sizeDelta = new Vector2(320, 34); PinToAnchor(detailKicker.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 305));
            detailTitleLabel = Label("選擇一份譜面", detail, 58); detailTitleLabel.alignment = TextAnchor.MiddleLeft; detailTitleLabel.rectTransform.sizeDelta = new Vector2(620, 92); PinToAnchor(detailTitleLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 183.5f));
            detailTitleMaxFontSize = detailTitleLabel.fontSize;
            detailTitleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            detailTitleLabel.verticalOverflow = VerticalWrapMode.Truncate;
            detailArtistLabel = Label("", detail, 25); detailArtistLabel.color = new Color(.68f, .68f, .68f); detailArtistLabel.alignment = TextAnchor.MiddleLeft; detailArtistLabel.rectTransform.sizeDelta = new Vector2(620, 48); PinToAnchor(detailArtistLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 113.5f));
            var infoDivider = Panel("Detail Divider", detail, new Color(.28f, .28f, .28f), new Vector2(0, PersistentGrayDividerThickness), Vector2.zero); infoDivider.anchorMin = new Vector2(.51f, .5f); infoDivider.anchorMax = new Vector2(.94f, .5f); infoDivider.offsetMin = new Vector2(0, 72); infoDivider.offsetMax = new Vector2(0, 72 + PersistentGrayDividerThickness); infoDivider.GetComponent<Image>().raycastTarget = false;
            detailDifficultyLabel = Label("選擇難度", detail, 17); detailDifficultyLabel.color = new Color(.68f, .68f, .68f); detailDifficultyLabel.alignment = TextAnchor.MiddleLeft; detailDifficultyLabel.rectTransform.sizeDelta = new Vector2(440, 38); PinToAnchor(detailDifficultyLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, 36));
            difficultyButtonContent = new GameObject("Difficulty Buttons", typeof(RectTransform)).GetComponent<RectTransform>();
            difficultyButtonContent.SetParent(detail, false);
            difficultyButtonContent.anchorMin = difficultyButtonContent.anchorMax = new Vector2(0, .5f);
            difficultyButtonContent.pivot = new Vector2(0, .5f);
            difficultyButtonContent.sizeDelta = new Vector2(450, 76);
            difficultyButtonContent.anchoredPosition = new Vector2(0, -26);
            difficultyButtonContent.anchorMin = difficultyButtonContent.anchorMax = new Vector2(.51f, .5f);
            detailAccuracyLabel = Label("BEST ACCURACY\n<size=52>—</size>", detail, 18); detailAccuracyLabel.supportRichText = true; detailAccuracyLabel.alignment = TextAnchor.UpperLeft; detailAccuracyLabel.rectTransform.sizeDelta = new Vector2(460, 100); PinToAnchor(detailAccuracyLabel.rectTransform, new Vector2(.51f, .5f), new Vector2(0, .5f), new Vector2(0, -134));
            loadStatus = Label(string.Empty, detail, 15);
            loadStatus.color = new Color(.68f, .68f, .68f);
            loadStatus.alignment = TextAnchor.MiddleLeft;
            var statusRect = loadStatus.rectTransform; statusRect.anchorMin = new Vector2(.51f, .5f); statusRect.anchorMax = new Vector2(.94f, .5f); statusRect.pivot = new Vector2(.5f, .5f); statusRect.offsetMin = new Vector2(0, -211); statusRect.offsetMax = new Vector2(0, -187);
            startButton = MakeFlatButton("▶  開始遊戲", detail, Vector2.zero, StartGame, new Vector2(0, 82), new Color(.06f, .58f, .96f));
            var startRect = startButton.GetComponent<RectTransform>(); startRect.anchorMin = new Vector2(.51f, .5f); startRect.anchorMax = new Vector2(.94f, .5f); startRect.pivot = new Vector2(.5f, .5f); startRect.offsetMin = new Vector2(0, -300.5f); startRect.offsetMax = new Vector2(0, -218.5f);
            startButton.interactable = false;
            chartPreviewButton = MakeOutlinedButton("預覽", detail, Vector2.zero, OpenChartPreview, new Vector2(0, 52));
            var previewRect = chartPreviewButton.GetComponent<RectTransform>();
            var previewAnchorWidth = ChartPreviewLayout.PrimaryWidth(.94f - .51f);
            var previewAnchorCenter = (.51f + .94f) * .5f;
            previewRect.anchorMin = new Vector2(previewAnchorCenter - previewAnchorWidth * .5f, .5f);
            previewRect.anchorMax = new Vector2(previewAnchorCenter + previewAnchorWidth * .5f, .5f);
            previewRect.pivot = new Vector2(.5f, .5f);
            previewRect.offsetMin = new Vector2(0, -370.5f);
            previewRect.offsetMax = new Vector2(0, -318.5f);
            chartPreviewButton.interactable = false;
            downloadRemoteChartButton = MakeFlatButton("下載到本機", detail, Vector2.zero,
                () => StartCoroutine(DownloadSelectedRemoteChart()), new Vector2(0, 82), new Color(.06f, .58f, .96f));
            var downloadRemoteRect = downloadRemoteChartButton.GetComponent<RectTransform>(); downloadRemoteRect.anchorMin = new Vector2(.51f, .5f); downloadRemoteRect.anchorMax = new Vector2(.94f, .5f); downloadRemoteRect.pivot = new Vector2(.5f, .5f); downloadRemoteRect.offsetMin = new Vector2(0, -300.5f); downloadRemoteRect.offsetMax = new Vector2(0, -218.5f);
            downloadRemoteChartButton.gameObject.SetActive(false);
            RefreshLibrarySourceControls();
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
            settingsAudioNavigationButton = MakeFlatButton("音訊", navigation, new Vector2(0, 285), ShowSettingsAudio, new Vector2(220, 68), new Color(.08f, .28f, .42f));
            settingsGameNavigationButton = MakeFlatButton("遊戲", navigation, new Vector2(0, 205), ShowSettingsGame, new Vector2(220, 68), new Color(.18f, .18f, .18f));
            settingsTagsNavigationButton = MakeFlatButton("標籤", navigation, new Vector2(0, 125), ShowSettingsTags, new Vector2(220, 68), new Color(.18f, .18f, .18f));
            settingsAccountNavigationButton = MakeFlatButton("帳號", navigation, new Vector2(0, 45), ShowSettingsAccount, new Vector2(220, 68), new Color(.18f, .18f, .18f));
            var card = Panel("Settings Audio Panel", settingsPanel, new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));
            settingsAudioPanel = card;

            var delayTitle = Label("音訊延遲", card, 24);
            delayTitle.alignment = TextAnchor.MiddleLeft;
            delayTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            delayTitle.rectTransform.anchoredPosition = new Vector2(0, -70);
            MakeDelayHoldButton("−1 ms", card, new Vector2(-300, -125), () => AdjustSettingsDelay(-SettingsDelayAdjustment.StepSeconds), new Vector2(150, 52), new Color(.06f, .58f, .96f));
            Panel("Delay Value Background", card, new Color(.18f, .18f, .18f), new Vector2(180, 52), new Vector2(-100, -125));
            settingsDelayLabel = Label("", card, 20);
            settingsDelayLabel.alignment = TextAnchor.MiddleCenter;
            settingsDelayLabel.rectTransform.sizeDelta = new Vector2(180, 52);
            settingsDelayLabel.rectTransform.anchoredPosition = new Vector2(-100, -125);
            MakeDelayHoldButton("＋1 ms", card, new Vector2(100, -125), () => AdjustSettingsDelay(SettingsDelayAdjustment.StepSeconds), new Vector2(150, 52), new Color(.06f, .58f, .96f));
            MakeFlatButton("自動調整", card, new Vector2(300, -125), OpenAutoAdjustPanel, new Vector2(150, 52), new Color(.18f, .28f, .38f));
            var delayLateHint = Label(DelayAdjustmentGuidance(-SettingsDelayAdjustment.StepSeconds), card, 17);
            delayLateHint.alignment = TextAnchor.MiddleCenter;
            delayLateHint.color = new Color(.66f, .66f, .66f);
            delayLateHint.rectTransform.sizeDelta = new Vector2(240, 28);
            delayLateHint.rectTransform.anchoredPosition = new Vector2(-300, -170);
            var delayFastHint = Label(DelayAdjustmentGuidance(SettingsDelayAdjustment.StepSeconds), card, 17);
            delayFastHint.alignment = TextAnchor.MiddleCenter;
            delayFastHint.color = new Color(.66f, .66f, .66f);
            delayFastHint.rectTransform.sizeDelta = new Vector2(240, 28);
            delayFastHint.rectTransform.anchoredPosition = new Vector2(100, -170);
            RefreshSettingsDelayLabel();

            var inputDelayTitle = Label("輸入延遲", card, 24);
            inputDelayTitle.alignment = TextAnchor.MiddleLeft;
            inputDelayTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            inputDelayTitle.rectTransform.anchoredPosition = new Vector2(0, -235);
            MakeDelayHoldButton("−1 ms", card, new Vector2(-300, -290),
                () => AdjustSettingsInputDelay(-SettingsDelayAdjustment.StepSeconds),
                new Vector2(150, 52), new Color(.06f, .58f, .96f));
            Panel("Input Delay Value Background", card, new Color(.18f, .18f, .18f),
                new Vector2(180, 52), new Vector2(-100, -290));
            settingsInputDelayLabel = Label("", card, 20);
            settingsInputDelayLabel.alignment = TextAnchor.MiddleCenter;
            settingsInputDelayLabel.rectTransform.sizeDelta = new Vector2(180, 52);
            settingsInputDelayLabel.rectTransform.anchoredPosition = new Vector2(-100, -290);
            MakeDelayHoldButton("＋1 ms", card, new Vector2(100, -290),
                () => AdjustSettingsInputDelay(SettingsDelayAdjustment.StepSeconds),
                new Vector2(150, 52), new Color(.06f, .58f, .96f));
            var inputDelayLateHint = Label(DelayAdjustmentGuidance(-SettingsDelayAdjustment.StepSeconds), card, 17);
            inputDelayLateHint.alignment = TextAnchor.MiddleCenter;
            inputDelayLateHint.color = new Color(.66f, .66f, .66f);
            inputDelayLateHint.rectTransform.sizeDelta = new Vector2(240, 28);
            inputDelayLateHint.rectTransform.anchoredPosition = new Vector2(-300, -335);
            var inputDelayFastHint = Label(DelayAdjustmentGuidance(SettingsDelayAdjustment.StepSeconds), card, 17);
            inputDelayFastHint.alignment = TextAnchor.MiddleCenter;
            inputDelayFastHint.color = new Color(.66f, .66f, .66f);
            inputDelayFastHint.rectTransform.sizeDelta = new Vector2(240, 28);
            inputDelayFastHint.rectTransform.anchoredPosition = new Vector2(100, -335);
            RefreshSettingsInputDelayLabel();

            var musicVolumeTitle = Label("音樂音量", card, 24);
            musicVolumeTitle.alignment = TextAnchor.MiddleLeft;
            musicVolumeTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            musicVolumeTitle.rectTransform.anchoredPosition = new Vector2(0, 280);
            settingsMusicVolumeSlider = MakeSlider(card, new Vector2(0, 225), 0f, 1f, PlayerPrefs.GetFloat("gugarhythm-music-volume", 1f), SetSettingsMusicVolume);
            settingsMusicVolumeSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 18);
            settingsMusicVolumeLabel = Label("100%", card, 20);
            settingsMusicVolumeLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            settingsMusicVolumeLabel.rectTransform.anchoredPosition = new Vector2(0, 175);

            var keyVolumeTitle = Label("按鍵音量", card, 24);
            keyVolumeTitle.alignment = TextAnchor.MiddleLeft;
            keyVolumeTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            keyVolumeTitle.rectTransform.anchoredPosition = new Vector2(0, 105);
            settingsKeyVolumeSlider = MakeSlider(card, new Vector2(0, 50), 0f, 1f, PlayerPrefs.GetFloat("gugarhythm-key-volume", 1f), SetSettingsKeyVolume);
            settingsKeyVolumeSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 18);
            settingsKeyVolumeLabel = Label("100%", card, 20);
            settingsKeyVolumeLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            settingsKeyVolumeLabel.rectTransform.anchoredPosition = new Vector2(0, 0);
            SetSettingsMusicVolume(settingsMusicVolumeSlider.value);
            SetSettingsKeyVolume(settingsKeyVolumeSlider.value);

            settingsGamePanel = Panel("Settings Game Panel", settingsPanel, new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));
            var speedTitle = Label("速度", settingsGamePanel, 24);
            speedTitle.alignment = TextAnchor.MiddleLeft;
            speedTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            speedTitle.rectTransform.anchoredPosition = new Vector2(0, 280);
            speedSlider = MakeSlider(settingsGamePanel, new Vector2(0, 225), 1f, 20f, scrollSpeed, SetScrollSpeed);
            speedSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(700, 18);
            speedLabel = Label("", settingsGamePanel, 20);
            speedLabel.alignment = TextAnchor.MiddleLeft;
            speedLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            speedLabel.rectTransform.anchoredPosition = new Vector2(0, 175);
            SetScrollSpeed(scrollSpeed);

            var upperHiddenBarTitle = Label("上隱條", settingsGamePanel, 24);
            upperHiddenBarTitle.alignment = TextAnchor.MiddleLeft;
            upperHiddenBarTitle.rectTransform.sizeDelta = new Vector2(760, 42);
            upperHiddenBarTitle.rectTransform.anchoredPosition = new Vector2(0, 45);
            upperHiddenBarSlider = MakeSlider(settingsGamePanel, new Vector2(0, -10), 0f, 100f,
                upperHiddenBarPercent, SetUpperHiddenBarPercent);
            upperHiddenBarSlider.GetComponent<RectTransform>().sizeDelta = new Vector2(SettingsSliderWidth, 18);
            upperHiddenBarLabel = Label("", settingsGamePanel, 20);
            upperHiddenBarLabel.alignment = TextAnchor.MiddleLeft;
            upperHiddenBarLabel.rectTransform.sizeDelta = new Vector2(700, 36);
            upperHiddenBarLabel.rectTransform.anchoredPosition = new Vector2(0, -55);
            SetUpperHiddenBarPercent(upperHiddenBarPercent);

            var fastLateTitle = Label("FAST／LATE 顯示", settingsGamePanel, 24);
            fastLateTitle.alignment = TextAnchor.MiddleLeft;
            fastLateTitle.rectTransform.sizeDelta = new Vector2(FastLateDisplayWidth, 42);
            fastLateTitle.rectTransform.anchoredPosition = new Vector2(-SettingsSliderWidth * .5f + FastLateDisplayWidth * .5f, -160);
            fastLateDisplayToggle = MakeFigmaSlidingToggle("顯示", settingsGamePanel,
                new Vector2(-SettingsSliderWidth * .5f + FastLateDisplayWidth * .5f, -215),
                FastLateDisplayWidth, fastLateDisplayEnabled);
            fastLateDisplayToggle.onValueChanged.AddListener(SetFastLateDisplay);
            SetFastLateDisplay(fastLateDisplayEnabled);

            var autoPlayTitle = Label("AUTO PLAY", settingsGamePanel, 24);
            autoPlayTitle.alignment = TextAnchor.MiddleLeft;
            autoPlayTitle.rectTransform.sizeDelta = new Vector2(FastLateDisplayWidth, 42);
            autoPlayTitle.rectTransform.anchoredPosition = new Vector2(FastLateDisplayWidth * .5f, -160);
            autoPlayToggle = MakeFigmaSlidingToggle("啟用", settingsGamePanel,
                new Vector2(FastLateDisplayWidth * .5f, -215),
                FastLateDisplayWidth, autoPlayEnabled);
            autoPlayToggle.onValueChanged.AddListener(SetAutoPlayEnabled);
            SetAutoPlayEnabled(autoPlayEnabled);

            var hitParticleEffectTitle = Label("粒子效果", settingsGamePanel, 24);
            hitParticleEffectTitle.alignment = TextAnchor.MiddleLeft;
            hitParticleEffectTitle.rectTransform.sizeDelta = new Vector2(SettingsSliderWidth, 42);
            hitParticleEffectTitle.rectTransform.anchoredPosition = new Vector2(0, -285);
            const float HitParticleButtonWidth = 220f;
            const float HitParticleButtonSpacing = 20f;
            var hitParticleButtonStep = HitParticleButtonWidth + HitParticleButtonSpacing;
            hitParticleEffectButtons[(int)HitParticleEffectMode.ParticleScatter] = MakeFlatButton(
                "粒子飛散", settingsGamePanel, new Vector2(-hitParticleButtonStep, -335),
                () => SetHitParticleEffectMode(HitParticleEffectMode.ParticleScatter),
                new Vector2(HitParticleButtonWidth, 50), new Color(.18f, .18f, .18f));
            hitParticleEffectButtons[(int)HitParticleEffectMode.ShardBreak] = MakeFlatButton(
                "碎片裂解", settingsGamePanel, new Vector2(0, -335),
                () => SetHitParticleEffectMode(HitParticleEffectMode.ShardBreak),
                new Vector2(HitParticleButtonWidth, 50), new Color(.18f, .18f, .18f));
            hitParticleEffectButtons[(int)HitParticleEffectMode.BrokenRing] = MakeFlatButton(
                "斷環粒子", settingsGamePanel, new Vector2(hitParticleButtonStep, -335),
                () => SetHitParticleEffectMode(HitParticleEffectMode.BrokenRing),
                new Vector2(HitParticleButtonWidth, 50), new Color(.18f, .18f, .18f));
            SetHitParticleEffectMode(hitParticleEffectMode);
            settingsGamePanel.gameObject.SetActive(false);

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
            settingsAccountPanel = Panel("Settings Account Panel", settingsPanel, new Color(.15f, .15f, .15f, 1f), new Vector2(1030, 760), new Vector2(90, -20));
            var accountTitle = Label("帳號", settingsAccountPanel, 32);
            accountTitle.alignment = TextAnchor.MiddleLeft;
            accountTitle.rectTransform.sizeDelta = new Vector2(860, 62);
            accountTitle.rectTransform.anchoredPosition = new Vector2(0, 290);
            var accountDescription = Label("登入後可查看及下載只屬於你的私人譜面。", settingsAccountPanel, 22);
            accountDescription.color = new Color(.72f, .82f, 1f, 1f);
            accountDescription.rectTransform.sizeDelta = new Vector2(860, 46);
            accountDescription.rectTransform.anchoredPosition = new Vector2(0, 220);
            settingsAccountStatusLabel = Label("", settingsAccountPanel, 24);
            settingsAccountStatusLabel.alignment = TextAnchor.MiddleLeft;
            settingsAccountStatusLabel.rectTransform.sizeDelta = new Vector2(860, 56);
            settingsAccountStatusLabel.rectTransform.anchoredPosition = new Vector2(0, 110);
            settingsAccountLoginButton = MakeFlatButton("登入", settingsAccountPanel, new Vector2(-130, -5),
                StartChartVaultLogin, new Vector2(230, 62), new Color(.06f, .58f, .96f));
            settingsAccountLogoutButton = MakeOutlinedButton("登出", settingsAccountPanel, new Vector2(130, -5),
                LogoutChartVault, new Vector2(230, 62));
            settingsAccountManageButton = MakeOutlinedButton("在網站管理帳號", settingsAccountPanel,
                new Vector2(0, -85), OpenChartVaultAccountPage, new Vector2(470, 56));
            settingsAccountPanel.gameObject.SetActive(false);
            RefreshAccountSettings();
            BuildInputDiagnosticsSettingsSection(navigation);
            settingsPanel.gameObject.SetActive(false);
        }

        void RefreshAccountSettings()
        {
            var signedIn = !string.IsNullOrEmpty(chartVaultSessionToken);
            if (settingsAccountStatusLabel != null)
                settingsAccountStatusLabel.text = chartVaultLoginPending
                    ? "正在等待登入完成；若流程已中斷，可重新登入。"
                    : signedIn
                        ? FormatChartVaultAccountStatus()
                        : chartVaultSessionExpired ? "登入已過期，請重新登入。" : "尚未登入";
            if (settingsAccountLoginButton != null)
            {
                settingsAccountLoginButton.gameObject.SetActive(!signedIn);
                settingsAccountLoginButton.interactable = !signedIn;
                var buttonLabel = settingsAccountLoginButton.GetComponentInChildren<Text>();
                if (buttonLabel != null) buttonLabel.text = chartVaultLoginPending ? "重新登入" : "登入";
            }
            if (settingsAccountLogoutButton != null)
                settingsAccountLogoutButton.gameObject.SetActive(signedIn);
            if (settingsAccountManageButton != null)
                settingsAccountManageButton.gameObject.SetActive(signedIn);
        }

        static void OpenChartVaultAccountPage() =>
            Application.OpenURL(ChartVaultApiSettings.ApiOrigin + "/account");

        string FormatChartVaultAccountStatus()
        {
            if (string.IsNullOrEmpty(chartVaultDisplayName)) return "已登入";
            var status = "已登入為 " + chartVaultDisplayName;
            var expires = FormatChartVaultExpiresAt(chartVaultExpiresAt);
            if (!string.IsNullOrEmpty(expires)) status += "（有效至 " + expires + "）";
            if (chartVaultDeviceCount > 0) status += "，已登入 " + chartVaultDeviceCount + "／5 台裝置";
            return status;
        }

        static string FormatChartVaultExpiresAt(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return string.Empty;
            return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : string.Empty;
        }

        void StartChartVaultLogin()
        {
            if (!string.IsNullOrEmpty(chartVaultSessionToken)) return;
            chartVaultSessionExpired = false;
            if (chartVaultLoginPending)
            {
                chartVaultLoginPending = false;
                pendingChartVaultLoginState = null;
                pendingChartVaultCodeVerifier = null;
                ChartVaultSessionStore.ClearPendingLogin();
            }
            var state = NewChartVaultToken();
            var verifier = NewChartVaultToken();
            var challenge = ComputeChartVaultPkceChallenge(verifier);
            var device = SanitizedDeviceLabelToken();
            if (state == null || verifier == null || challenge == null || device == null)
            {
                SetStatus("目前無法開始登入，請稍後再試。");
                return;
            }
            pendingChartVaultLoginState = state;
            pendingChartVaultCodeVerifier = verifier;
            chartVaultLoginPending = true;
            ChartVaultSessionStore.SavePendingLogin(state, verifier);
            RefreshAccountSettings();
            var loginUrl = ChartVaultApiSettings.ApiOrigin + "/app-login?state=" + state +
                "&code_challenge=" + challenge + "&device=" + device + "&platform=" + CurrentChartVaultPlatform();
            Application.OpenURL(loginUrl);
        }

        static string SanitizedDeviceLabelToken()
        {
            var raw = SystemInfo.deviceModel;
            if (string.IsNullOrEmpty(raw) || raw == SystemInfo.unsupportedIdentifier) raw = SystemInfo.deviceName;
            if (string.IsNullOrEmpty(raw)) raw = "Unknown Device";
            var builder = new System.Text.StringBuilder(raw.Length);
            foreach (var character in raw)
            {
                if (char.IsControl(character)) continue;
                builder.Append(character);
                if (builder.Length >= 64) break;
            }
            var sanitized = builder.ToString().Trim();
            if (sanitized.Length == 0) sanitized = "Unknown Device";
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(sanitized);
                return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string CurrentChartVaultPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.Android: return "android";
                case RuntimePlatform.IPhonePlayer: return "ios";
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.LinuxEditor:
                    return "editor";
                default: return "unknown";
            }
        }

        void LogoutChartVault()
        {
            var tokenToRevoke = chartVaultSessionToken;
            chartVaultSessionToken = null;
            chartVaultSessionExpired = false;
            chartVaultDisplayName = null;
            chartVaultExpiresAt = null;
            chartVaultDeviceCount = 0;
            ChartVaultSessionStore.Clear();
            chartVaultLoginPending = false;
            pendingChartVaultLoginState = null;
            pendingChartVaultCodeVerifier = null;
            ChartVaultSessionStore.ClearPendingLogin();
            remoteCatalogScope = RemoteChartCatalogScope.Public;
            remoteCatalog = null;
            selectedRemoteChart = null;
            remoteOperationGeneration++;
            remoteCoverGeneration++;
            ClearRemoteCoverTexture();
            RefreshAccountSettings();
            RefreshLibraryUI();
            if (!string.IsNullOrEmpty(tokenToRevoke))
                StartCoroutine(RevokeChartVaultSession(tokenToRevoke));
            if (librarySource == ChartLibrarySource.Online)
                StartCoroutine(RefreshRemoteCatalog(false));
        }

        IEnumerator RevokeChartVaultSession(string token)
        {
            IEnumerator operation = null;
            try
            {
                operation = chartVaultClient?.LogoutAppSession(token, _ => { });
            }
            catch (Exception) { }
            while (operation != null)
            {
                object current;
                try
                {
                    if (!operation.MoveNext()) break;
                    current = operation.Current;
                }
                catch (Exception) { break; }
                yield return current;
            }
            if (operation is IDisposable disposable)
                try { disposable.Dispose(); } catch (Exception) { }
        }

        void HandleChartVaultDeepLink(string url)
        {
            if (!TryParseChartVaultCallback(url, out var code, out var state) ||
                !chartVaultLoginPending || !FixedTimeEquals(state, pendingChartVaultLoginState) ||
                string.IsNullOrEmpty(pendingChartVaultCodeVerifier))
                return;
            var verifier = pendingChartVaultCodeVerifier;
            pendingChartVaultLoginState = null;
            pendingChartVaultCodeVerifier = null;
            ChartVaultSessionStore.ClearPendingLogin();
            StartCoroutine(ExchangeChartVaultLoginCode(code, verifier));
        }

        IEnumerator ExchangeChartVaultLoginCode(string code, string verifier)
        {
            var completed = false;
            var result = default(ChartVaultSessionResult);
            IEnumerator operation = null;
            try
            {
                operation = chartVaultClient?.ExchangeAppLoginHandoff(code, verifier, value =>
                {
                    if (destroying || completed) return;
                    result = value;
                    completed = true;
                });
            }
            catch (Exception)
            {
                completed = false;
            }
            while (operation != null)
            {
                object current;
                try
                {
                    if (!operation.MoveNext()) break;
                    current = operation.Current;
                }
                catch (Exception)
                {
                    completed = false;
                    break;
                }
                yield return current;
            }
            if (operation is IDisposable disposable)
                try { disposable.Dispose(); } catch (Exception) { }

            chartVaultLoginPending = false;
            pendingChartVaultLoginState = null;
            pendingChartVaultCodeVerifier = null;
            ChartVaultSessionStore.ClearPendingLogin();
            if (completed && result.Success)
            {
                chartVaultSessionToken = result.SessionToken;
                chartVaultSessionExpired = false;
                ChartVaultSessionStore.Save(chartVaultSessionToken);
                SetStatus("帳號已登入。");
                StartCoroutine(RefreshChartVaultProfile());
            }
            else
            {
                SetStatus("登入失敗，請回到設定＞帳號後再試。");
            }
            RefreshAccountSettings();
            RefreshLibraryUI();
        }

        IEnumerator RefreshChartVaultProfile()
        {
            var token = chartVaultSessionToken;
            if (string.IsNullOrEmpty(token) || chartVaultProfileLoading) yield break;
            chartVaultProfileLoading = true;
            var completed = false;
            var result = default(ChartVaultAppSessionResult);
            IEnumerator operation = null;
            try
            {
                operation = chartVaultClient?.GetAppSession(token, value =>
                {
                    if (destroying || completed) return;
                    result = value;
                    completed = true;
                });
            }
            catch (Exception) { }
            while (operation != null)
            {
                object current;
                try
                {
                    if (!operation.MoveNext()) break;
                    current = operation.Current;
                }
                catch (Exception) { break; }
                yield return current;
            }
            if (operation is IDisposable disposable)
                try { disposable.Dispose(); } catch (Exception) { }

            chartVaultProfileLoading = false;
            if (destroying || token != chartVaultSessionToken) yield break;
            if (completed && result.Success)
            {
                chartVaultDisplayName = result.DisplayName;
                chartVaultExpiresAt = result.ExpiresAt;
                chartVaultDeviceCount = result.DeviceCount;
                RefreshAccountSettings();
            }
            else if (completed && result.Unauthorized)
            {
                HandleChartVaultUnauthorized();
            }
        }

        // A 401 from any App-authenticated request means the Bearer token is no
        // longer valid (expired, or revoked from the website's account page).
        // This is the single place that reacts: clear the token and every piece
        // of private-scope state that token unlocked, without touching charts
        // already saved to LocalChartLibrary — those were validated and copied
        // in at download time and do not depend on the session being alive.
        void HandleChartVaultUnauthorized()
        {
            if (string.IsNullOrEmpty(chartVaultSessionToken)) return;
            chartVaultSessionToken = null;
            chartVaultSessionExpired = true;
            chartVaultDisplayName = null;
            chartVaultExpiresAt = null;
            chartVaultDeviceCount = 0;
            ChartVaultSessionStore.Clear();
            var wasPrivateScope = remoteCatalogScope == RemoteChartCatalogScope.Private;
            remoteCatalogScope = RemoteChartCatalogScope.Public;
            remoteCatalog = null;
            selectedRemoteChart = null;
            remoteOperationGeneration++;
            remoteCoverGeneration++;
            ClearRemoteCoverTexture();
            RefreshAccountSettings();
            RefreshLibraryUI();
            if (wasPrivateScope)
            {
                SetStatus("登入已過期，請重新登入。");
                if (librarySource == ChartLibrarySource.Online)
                    StartCoroutine(RefreshRemoteCatalog(false));
            }
        }

        static string NewChartVaultToken()
        {
            try
            {
                var bytes = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            }
            catch (Exception)
            {
                return null;
            }
        }

        static string ComputeChartVaultPkceChallenge(string verifier)
        {
            if (!IsChartVaultToken(verifier)) return null;
            try
            {
                using var sha256 = SHA256.Create();
                var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(verifier));
                return Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            }
            catch (Exception)
            {
                return null;
            }
        }

        static bool TryParseChartVaultCallback(string value, out string code, out string state)
        {
            code = null;
            state = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, "com.luecat.gugarhythm", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(uri.Host) ||
                !string.Equals(uri.AbsolutePath, "/oauth2redirect", StringComparison.Ordinal))
                return false;
            var rawQuery = uri.Query;
            if (string.IsNullOrEmpty(rawQuery) || rawQuery[0] != '?') return false;
            foreach (var pair in rawQuery.Substring(1).Split('&'))
            {
                var separator = pair.IndexOf('=');
                if (separator <= 0 || separator == pair.Length - 1) return false;
                var name = pair.Substring(0, separator);
                var token = pair.Substring(separator + 1);
                if (!IsChartVaultToken(token)) return false;
                if (name == "code" && code == null) code = token;
                else if (name == "state" && state == null) state = token;
                else return false;
            }
            return IsChartVaultToken(code) && IsChartVaultToken(state);
        }

        static bool IsChartVaultToken(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 43) return false;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '-' && character != '_')
                    return false;
            return true;
        }

        static bool FixedTimeEquals(string left, string right)
        {
            if (!IsChartVaultToken(left) || !IsChartVaultToken(right)) return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
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
            input = MakeInputField(label, parent, position + new Vector2(0, -12), new Vector2(620, 56), false);
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
            chartEditorSubtitleLabel.text = string.Empty;
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
            GugarhythmSceneRouter.OpenChartEditor();
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
            GugarhythmSceneRouter.OpenLibrary();
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
            GugarhythmSceneRouter.OpenSettings();
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
            GugarhythmSceneRouter.OpenLibrary();
        }

        void ReturnFromChartEditor() => GugarhythmSceneRouter.OpenLibrary();

        void SelectLibrarySource(ChartLibrarySource source)
        {
            if (source != ChartLibrarySource.Local && source != ChartLibrarySource.Online) return;
            if (librarySource == source)
            {
                RefreshLibraryUI();
                return;
            }

            librarySource = source;
            if (source == ChartLibrarySource.Local)
            {
                remoteCoverGeneration++;
                ClearRemoteCoverTexture();
                RefreshLibraryUI();
                return;
            }

            RefreshDetailCover(null);
            if (remoteCatalogScope == RemoteChartCatalogScope.Public && !remoteCatalogCacheLoaded)
            {
                remoteCatalogCacheLoaded = true;
                if (remoteCatalogCache != null && remoteCatalogCache.TryLoad(out var cachedCatalog))
                {
                    remoteCatalog = cachedCatalog;
                    SetStatus("已載入線上快取（" + FormatRemoteCatalogTimestamp(cachedCatalog.CachedAtUnixMilliseconds) + "）。");
                }
                else
                {
                    SetStatus("尚無線上譜面快取，正在取得公開清單。");
                }
            }

            RefreshLibraryUI();
            if (ShouldFetchRemoteCatalogOnSourceChange(source, remoteCatalogRequested))
                StartCoroutine(RefreshRemoteCatalog(false));
        }

        void RefreshLibrarySourceControls()
        {
            var online = librarySource == ChartLibrarySource.Online;
            if (localLibrarySourceButton != null)
                localLibrarySourceButton.image.color = online ? new Color(.20f, .20f, .20f) : new Color(.10f, .34f, .50f);
            if (onlineLibrarySourceButton != null)
                onlineLibrarySourceButton.image.color = online ? new Color(.10f, .34f, .50f) : new Color(.20f, .20f, .20f);
            if (remotePublicScopeButton != null)
            {
                remotePublicScopeButton.gameObject.SetActive(online);
                remotePublicScopeButton.image.color = remoteCatalogScope == RemoteChartCatalogScope.Public
                    ? new Color(.10f, .34f, .50f) : new Color(.20f, .20f, .20f);
            }
            if (remotePrivateScopeButton != null)
            {
                remotePrivateScopeButton.gameObject.SetActive(online);
                remotePrivateScopeButton.image.color = remoteCatalogScope == RemoteChartCatalogScope.Private
                    ? new Color(.10f, .34f, .50f) : new Color(.20f, .20f, .20f);
            }
            if (importLibraryButton != null) importLibraryButton.gameObject.SetActive(!online);
            if (refreshRemoteLibraryButton != null)
            {
                refreshRemoteLibraryButton.gameObject.SetActive(online);
                refreshRemoteLibraryButton.interactable = online && !remoteCatalogLoading;
            }
            if (difficultyButtonContent != null) difficultyButtonContent.gameObject.SetActive(!online);
            if (startButton != null)
            {
                startButton.gameObject.SetActive(!online);
                startButton.interactable = ShouldEnableLibraryStartButton(librarySource, selectedLibraryEntry != null);
            }
            if (chartPreviewButton != null)
            {
                chartPreviewButton.gameObject.SetActive(!online);
                chartPreviewButton.interactable = ShouldEnableLibraryStartButton(librarySource, selectedLibraryEntry != null);
            }
            if (downloadRemoteChartButton != null)
            {
                downloadRemoteChartButton.gameObject.SetActive(online);
                downloadRemoteChartButton.interactable = online && selectedRemoteChart != null && !remoteChartDownloading;
            }
        }

        void SelectRemoteCatalogScope(RemoteChartCatalogScope scope)
        {
            if (librarySource != ChartLibrarySource.Online) return;
            if (scope == RemoteChartCatalogScope.Private && string.IsNullOrEmpty(chartVaultSessionToken))
            {
                SetStatus("請先到設定＞帳號登入，才能查看私人譜面。");
                return;
            }
            if (remoteCatalogScope == scope)
            {
                RefreshLibraryUI();
                return;
            }
            remoteCatalogScope = scope;
            remoteOperationGeneration++;
            remoteCoverGeneration++;
            ClearRemoteCoverTexture();
            selectedRemoteChart = null;
            remoteCatalog = null;
            remoteCatalogRequested = false;
            RefreshLibraryUI();
            StartCoroutine(RefreshRemoteCatalog(false));
        }

        IEnumerator RefreshRemoteCatalog(bool userInitiated)
        {
            if (librarySource != ChartLibrarySource.Online || remoteCatalogLoading) yield break;
            if (remoteCatalogScope == RemoteChartCatalogScope.Private && string.IsNullOrEmpty(chartVaultSessionToken))
            {
                SetStatus("請先到設定＞帳號登入，才能查看私人譜面。");
                yield break;
            }
            remoteCatalogLoading = true;
            remoteCatalogRequested = true;
            var generation = remoteOperationGeneration;
            RefreshLibrarySourceControls();
            SetStatus(userInitiated ? "正在重新整理線上譜面…" : "正在取得線上譜面…");

            var resultReceived = false;
            var result = default(ChartVaultCatalogResult);
            IEnumerator operation = null;
            try
            {
                operation = chartVaultClient?.FetchCatalog(remoteCatalogScope, value =>
                {
                    if (destroying || generation != remoteOperationGeneration || resultReceived) return;
                    result = value;
                    resultReceived = true;
                }, chartVaultSessionToken);
            }
            catch (Exception)
            {
                operation = null;
            }

            var operationFailed = operation == null;
            var operationCompleted = false;
            try
            {
                while (!operationFailed && !operationCompleted)
                {
                    if (!TryAdvanceRemoteOperation(operation, out var hasNext, out var current))
                    {
                        operationFailed = true;
                        break;
                    }
                    if (!hasNext)
                    {
                        operationCompleted = true;
                        break;
                    }
                    yield return current;
                }
            }
            finally
            {
                DisposeRemoteOperation(operation);
            }

            if (destroying || generation != remoteOperationGeneration) yield break;
            remoteCatalogLoading = false;
            RefreshLibrarySourceControls();
            if (resultReceived && result.Unauthorized)
            {
                HandleChartVaultUnauthorized();
                yield break;
            }
            if (operationFailed || !resultReceived || !result.Success)
            {
                if (librarySource == ChartLibrarySource.Online)
                {
                    RefreshRemoteLibraryUI();
                    SetStatus("線上譜面清單更新失敗，快取內容已保留；請按重新整理後再試。");
                }
                yield break;
            }

            ReconcileRemoteSelection(result.Catalog);
            remoteCatalog = result.Catalog;
            var cacheSaved = remoteCatalogScope != RemoteChartCatalogScope.Public;
            if (remoteCatalogScope == RemoteChartCatalogScope.Public)
            {
                try
                {
                    remoteCatalogCache?.Save(remoteCatalog);
                }
                catch (Exception)
                {
                    cacheSaved = false;
                }
            }
            if (librarySource == ChartLibrarySource.Online)
            {
                RefreshRemoteLibraryUI();
                SetStatus(cacheSaved
                    ? (remoteCatalogScope == RemoteChartCatalogScope.Private
                        ? "私人譜面已更新。"
                        : "線上譜面已更新（" + FormatRemoteCatalogTimestamp(remoteCatalog.CachedAtUnixMilliseconds) + "）。")
                    : "線上譜面已更新，但這次無法寫入本機快取。");
            }
        }

        static bool TryAdvanceRemoteOperation(IEnumerator operation, out bool hasNext, out object current)
        {
            hasNext = false;
            current = null;
            if (operation == null) return false;
            try
            {
                hasNext = operation.MoveNext();
                if (hasNext) current = operation.Current;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void DisposeRemoteOperation(IEnumerator operation)
        {
            if (operation is not IDisposable disposable) return;
            try
            {
                disposable.Dispose();
            }
            catch (Exception)
            {
                // Remote UI failures remain retryable and never surface transport internals.
            }
        }

        static string FormatRemoteCatalogTimestamp(long unixMilliseconds)
        {
            if (unixMilliseconds <= 0) return "時間未知";
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm");
            }
            catch (ArgumentOutOfRangeException)
            {
                return "時間未知";
            }
        }

        void CycleLibrarySort()
        {
            if (librarySource == ChartLibrarySource.Online)
            {
                remoteLibrarySort = remoteLibrarySort == ChartLibrarySort.Title
                    ? ChartLibrarySort.Difficulty
                    : ChartLibrarySort.Title;
                RefreshRemoteLibraryUI();
                return;
            }
            librarySort = librarySort == ChartLibrarySort.Accuracy ? ChartLibrarySort.Difficulty :
                librarySort == ChartLibrarySort.Difficulty ? ChartLibrarySort.Title : ChartLibrarySort.Accuracy;
            LibrarySortPreferences.Save(librarySort, librarySortAscending);
            RefreshLibraryUI();
        }

        void RefreshLibraryUI()
        {
            RefreshLibrarySourceControls();
            if (librarySource == ChartLibrarySource.Online)
                RefreshRemoteLibraryUI();
            else
                RefreshLocalLibraryUI();
        }

        void RefreshLocalLibraryUI()
        {
            if (libraryListContent == null) return;
            var libraryScroll = libraryListContent.parent == null ? null : libraryListContent.parent.GetComponent<ScrollRect>();
            var restoreLibraryScrollPosition = libraryScrollPositionInitialized && libraryScroll != null;
            var preservedLibraryScrollPosition = restoreLibraryScrollPosition ? libraryScroll.verticalNormalizedPosition : 1f;
            var entries = LocalChartLibrary.Load();
            var previousSelectionId = selectedLibraryEntry?.Id;
            selectedLibraryEntry = LibrarySelectionReconciler.Select(entries, selectedLibraryEntry);
            if (selectedLibraryEntry != null)
                currentLibraryEntry = selectedLibraryEntry;
            if (selectedLibraryEntry == null || selectedLibraryEntry.Id != previousSelectionId)
            {
                selectedDifficultyName = selectedLibraryEntry?.DifficultyName ?? string.Empty;
            }
            var groups = ChartLibraryGrouping.Group(entries);
            var filter = librarySearchInput == null ? string.Empty : librarySearchInput.text.Trim();
            if (!string.IsNullOrEmpty(filter))
            {
                groups = groups.Where(group => ContainsIgnoreCase(group.Title, filter) || ContainsIgnoreCase(group.Artist, filter) ||
                    group.Difficulties.Any(entry => ContainsIgnoreCase(entry.Author, filter))).ToList();
            }

            if (selectedLibraryEntry == null && groups.Count > 0)
            {
                selectedLibraryEntry = groups[0].Difficulties[0];
                currentLibraryEntry = selectedLibraryEntry;
                selectedDifficultyName = selectedLibraryEntry.DifficultyName ?? string.Empty;
            }
            if (selectedLibraryEntry != null && string.IsNullOrWhiteSpace(selectedDifficultyName)) selectedDifficultyName = selectedLibraryEntry.DifficultyName ?? string.Empty;

            groups = ChartLibraryGrouping.Sort(groups, librarySort, librarySortAscending, selectedDifficultyName).ToList();
            libraryCountLabel.text = groups.Count.ToString();
            librarySortModeLabel.text = librarySort == ChartLibrarySort.Accuracy ? "準確率" : librarySort == ChartLibrarySort.Difficulty ? "難度" : "曲名";
            libraryDirectionIcon.localRotation = Quaternion.Euler(0, 0, librarySortAscending ? 180 : 0);
            ClearChildren(libraryListContent);
            const float rowHeight = 102f;
            var contentSize = libraryListContent.sizeDelta;
            contentSize.y = Mathf.Max(libraryListContent.parent.GetComponent<RectTransform>().rect.height, groups.Count * rowHeight + 8);
            libraryListContent.sizeDelta = contentSize;
            for (var index = 0; index < groups.Count; index++) BuildLibraryRow(groups[index], index, rowHeight);
            RefreshDetailUI(groups);

            // Selecting a chart rebuilds the rows so the highlight and details
            // stay in sync. Keep the user's current list position instead of
            // implicitly focusing the selected chart or jumping to the top.
            if (restoreLibraryScrollPosition)
            {
                Canvas.ForceUpdateCanvases();
                libraryScroll.verticalNormalizedPosition = preservedLibraryScrollPosition;
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(libraryListContent);
            libraryScrollPositionInitialized = true;
            if (startButton != null)
                startButton.interactable = ShouldEnableLibraryStartButton(librarySource, selectedLibraryEntry != null);
            if (chartPreviewButton != null)
                chartPreviewButton.interactable = ShouldEnableLibraryStartButton(librarySource, selectedLibraryEntry != null);
        }

        void RefreshRemoteLibraryUI()
        {
            if (libraryListContent == null) return;
            RefreshLibrarySourceControls();
            var libraryScroll = libraryListContent.parent == null ? null : libraryListContent.parent.GetComponent<ScrollRect>();
            var restoreScrollPosition = remoteLibraryScrollPositionInitialized && libraryScroll != null;
            var preservedScrollPosition = restoreScrollPosition ? libraryScroll.verticalNormalizedPosition : 1f;
            var charts = remoteCatalog?.Charts == null
                ? new List<RemoteChartSummary>()
                : remoteCatalog.Charts.Where(chart => chart != null).ToList();
            var filter = librarySearchInput == null ? string.Empty : librarySearchInput.text.Trim();
            if (!string.IsNullOrEmpty(filter))
            {
                charts = charts.Where(chart =>
                    ContainsIgnoreCase(chart.Title, filter) ||
                    ContainsIgnoreCase(chart.Artist, filter) ||
                    ContainsIgnoreCase(chart.Author, filter) ||
                    ContainsIgnoreCase(chart.Difficulty, filter)).ToList();
            }

            if (remoteLibrarySort == ChartLibrarySort.Difficulty)
            {
                charts = (remoteLibrarySortAscending
                        ? charts.OrderBy(chart => chart.Rating)
                            .ThenBy(chart => chart.Difficulty, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(chart => chart.Title, StringComparer.OrdinalIgnoreCase)
                        : charts.OrderByDescending(chart => chart.Rating)
                            .ThenByDescending(chart => chart.Difficulty, StringComparer.OrdinalIgnoreCase)
                            .ThenByDescending(chart => chart.Title, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                charts = (remoteLibrarySortAscending
                        ? charts.OrderBy(chart => chart.Title, StringComparer.OrdinalIgnoreCase)
                            .ThenBy(chart => chart.Rating)
                        : charts.OrderByDescending(chart => chart.Title, StringComparer.OrdinalIgnoreCase)
                            .ThenByDescending(chart => chart.Rating))
                    .ToList();
            }

            libraryCountLabel.text = charts.Count.ToString();
            librarySortModeLabel.text = remoteLibrarySort == ChartLibrarySort.Difficulty ? "難度" : "曲名";
            libraryDirectionIcon.localRotation = Quaternion.Euler(0, 0, remoteLibrarySortAscending ? 180 : 0);
            ClearChildren(libraryListContent);
            const float rowHeight = 118f;
            var contentSize = libraryListContent.sizeDelta;
            contentSize.y = Mathf.Max(libraryListContent.parent.GetComponent<RectTransform>().rect.height,
                charts.Count * rowHeight + 8);
            libraryListContent.sizeDelta = contentSize;
            for (var index = 0; index < charts.Count; index++) BuildRemoteLibraryRow(charts[index], index, rowHeight);
            RefreshRemoteDetailUI();

            if (restoreScrollPosition)
            {
                Canvas.ForceUpdateCanvases();
                libraryScroll.verticalNormalizedPosition = preservedScrollPosition;
            }
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(libraryListContent);
            remoteLibraryScrollPositionInitialized = true;
        }

        void BuildRemoteLibraryRow(RemoteChartSummary chart, int index, float rowHeight)
        {
            var selected = SameRemoteChart(selectedRemoteChart, chart);
            var row = Panel("Remote Chart Row", libraryListContent,
                selected ? new Color(.12f, .25f, .36f) : new Color(.16f, .16f, .16f),
                new Vector2(0, rowHeight - 2), Vector2.zero);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(.5f, 1);
            var rowHorizontalInset = selected ? LibraryDividerHorizontalInset : 0f;
            row.offsetMin = new Vector2(rowHorizontalInset, -rowHeight * (index + 1));
            row.offsetMax = new Vector2(-rowHorizontalInset, -rowHeight * index);
            if (index > 0)
            {
                var divider = Panel("Remote Chart Divider", row, new Color(.27f, .27f, .27f, .72f),
                    new Vector2(0, PersistentGrayDividerThickness), Vector2.zero);
                var dividerHorizontalInset = selected ? 0f : LibraryDividerHorizontalInset;
                divider.anchorMin = new Vector2(0, 1);
                divider.anchorMax = new Vector2(1, 1);
                divider.offsetMin = new Vector2(dividerHorizontalInset, -PersistentGrayDividerThickness);
                divider.offsetMax = new Vector2(-dividerHorizontalInset, 0);
                divider.GetComponent<Image>().raycastTarget = false;
            }
            var title = Label(RemoteText(chart.Title), row, 21);
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.anchorMin = new Vector2(0, 1);
            title.rectTransform.anchorMax = new Vector2(1, 1);
            title.rectTransform.pivot = new Vector2(0, 1);
            title.rectTransform.offsetMin = new Vector2(24, -58);
            title.rectTransform.offsetMax = new Vector2(-78, -24);
            var artist = Label(RemoteText(chart.Artist) + " · " + RemoteText(chart.Author), row, 16);
            artist.alignment = TextAnchor.MiddleLeft;
            artist.color = new Color(.67f, .67f, .67f);
            artist.rectTransform.anchorMin = new Vector2(0, 1);
            artist.rectTransform.anchorMax = new Vector2(1, 1);
            artist.rectTransform.pivot = new Vector2(0, 1);
            artist.rectTransform.offsetMin = new Vector2(24, -88);
            artist.rectTransform.offsetMax = new Vector2(-78, -59);
            var level = Label(chart.Rating.ToString("0.##"), row, 20);
            level.color = new Color(.78f, .78f, .78f);
            level.rectTransform.sizeDelta = new Vector2(62, 50);
            PinToAnchor(level.rectTransform, new Vector2(1, .5f), new Vector2(1, .5f), new Vector2(-18, 0));
            MakeInvisibleButton(row, () => SelectRemoteChart(chart));
        }

        void RefreshRemoteDetailUI()
        {
            if (selectedRemoteChart == null)
            {
                SetDetailTitle("選擇一份線上譜面");
                detailArtistLabel.text = string.Empty;
                detailDifficultyLabel.text = "選擇後可下載到本機";
                detailAccuracyLabel.text = string.Empty;
                ShowRemoteCover(null);
            }
            else
            {
                SetDetailTitle(RemoteText(selectedRemoteChart.Title));
                detailArtistLabel.text = RemoteText(selectedRemoteChart.Artist) + " · 譜師 " +
                    RemoteText(selectedRemoteChart.Author);
                detailDifficultyLabel.text = FormatRemoteDifficulty(selectedRemoteChart) + " · v" +
                    selectedRemoteChart.Version + " · " + (selectedRemoteChart.IsPrivate ? "私人" : "公開");
                detailAccuracyLabel.text = string.Empty;
                ShowRemoteCover(remoteCoverTexture);
            }
            if (downloadRemoteChartButton != null)
                downloadRemoteChartButton.interactable = selectedRemoteChart != null && !remoteChartDownloading;
        }

        void SelectRemoteChart(RemoteChartSummary chart)
        {
            if (librarySource != ChartLibrarySource.Online || chart == null || remoteCatalog?.Charts == null) return;
            var current = remoteCatalog.Charts.FirstOrDefault(candidate => SameRemoteChart(candidate, chart));
            if (current == null) return;
            selectedRemoteChart = current;
            remoteCoverGeneration++;
            ClearRemoteCoverTexture();
            RefreshRemoteLibraryUI();
            if (current.CoverUrl != null)
                StartCoroutine(DownloadRemoteCover(current, remoteCoverGeneration));
        }

        IEnumerator DownloadRemoteCover(RemoteChartSummary chart, int coverGeneration)
        {
            Texture2D downloadedTexture = null;
            var resultReceived = false;
            var operationGeneration = remoteOperationGeneration;
            IEnumerator operation = null;
            try
            {
                operation = chartVaultClient?.DownloadCover(chart, (texture, _) =>
                {
                    if (destroying || operationGeneration != remoteOperationGeneration ||
                        coverGeneration != remoteCoverGeneration || !SameRemoteChart(selectedRemoteChart, chart))
                    {
                        if (texture != null) Destroy(texture);
                        return;
                    }
                    if (resultReceived)
                    {
                        if (texture != null) Destroy(texture);
                        return;
                    }
                    downloadedTexture = texture;
                    resultReceived = true;
                }, chartVaultSessionToken);
            }
            catch (Exception)
            {
                operation = null;
            }

            var operationFailed = operation == null;
            var operationCompleted = false;
            try
            {
                while (!operationFailed && !operationCompleted)
                {
                    if (!TryAdvanceRemoteOperation(operation, out var hasNext, out var current))
                    {
                        operationFailed = true;
                        break;
                    }
                    if (!hasNext)
                    {
                        operationCompleted = true;
                        break;
                    }
                    yield return current;
                }
            }
            finally
            {
                DisposeRemoteOperation(operation);
            }

            if (destroying || operationGeneration != remoteOperationGeneration ||
                coverGeneration != remoteCoverGeneration || !SameRemoteChart(selectedRemoteChart, chart))
            {
                if (downloadedTexture != null) Destroy(downloadedTexture);
                yield break;
            }
            if (operationFailed || !resultReceived || downloadedTexture == null) yield break;
            ClearRemoteCoverTexture();
            remoteCoverTexture = downloadedTexture;
            if (librarySource == ChartLibrarySource.Online) ShowRemoteCover(remoteCoverTexture);
        }

        IEnumerator DownloadSelectedRemoteChart()
        {
            if (librarySource != ChartLibrarySource.Online || selectedRemoteChart == null || remoteChartDownloading)
                yield break;
            remoteChartDownloading = true;
            var chartToDownload = selectedRemoteChart;
            var generation = remoteOperationGeneration;
            RefreshLibrarySourceControls();
            SetStatus("正在下載「" + RemoteText(chartToDownload.Title) + "」…");

            var resultReceived = false;
            var result = default(RemoteChartImportResult);
            IEnumerator operation = null;
            try
            {
                operation = remoteChartDownloadService?.DownloadAndImport(chartToDownload, value =>
                {
                    if (destroying || generation != remoteOperationGeneration || resultReceived) return;
                    result = value;
                    resultReceived = true;
                }, chartVaultSessionToken);
            }
            catch (Exception)
            {
                operation = null;
            }

            var operationFailed = operation == null;
            var operationCompleted = false;
            try
            {
                while (!operationFailed && !operationCompleted)
                {
                    if (!TryAdvanceRemoteOperation(operation, out var hasNext, out var current))
                    {
                        operationFailed = true;
                        break;
                    }
                    if (!hasNext)
                    {
                        operationCompleted = true;
                        break;
                    }
                    yield return current;
                }
            }
            finally
            {
                DisposeRemoteOperation(operation);
            }

            if (destroying || generation != remoteOperationGeneration) yield break;
            remoteChartDownloading = false;
            RefreshLibrarySourceControls();
            if (resultReceived && result.Unauthorized)
            {
                HandleChartVaultUnauthorized();
                yield break;
            }
            if (operationFailed || !resultReceived || !result.Success || result.LocalEntry == null)
            {
                if (librarySource == ChartLibrarySource.Online)
                {
                    RefreshRemoteLibraryUI();
                    SetStatus("下載到本機失敗，線上清單與選取已保留；請稍後重試。");
                }
                yield break;
            }

            selectedLibraryEntry = result.LocalEntry;
            currentLibraryEntry = result.LocalEntry;
            selectedDifficultyName = result.LocalEntry.DifficultyName ?? string.Empty;
            SelectLibrarySource(ChartLibrarySource.Local);
            SetStatus("已加入本機");
            if (startButton != null)
                startButton.interactable = ShouldEnableLibraryStartButton(librarySource, selectedLibraryEntry != null);
        }

        void ReconcileRemoteSelection(RemoteChartCatalog catalog)
        {
            if (selectedRemoteChart == null) return;
            var replacement = catalog?.Charts?.FirstOrDefault(chart => SameRemoteChart(chart, selectedRemoteChart));
            if (replacement != null)
            {
                selectedRemoteChart = replacement;
                return;
            }
            selectedRemoteChart = null;
            remoteCoverGeneration++;
            ClearRemoteCoverTexture();
        }

        void ClearRemoteCoverTexture()
        {
            if (remoteCoverTexture != null) Destroy(remoteCoverTexture);
            remoteCoverTexture = null;
            if (librarySource == ChartLibrarySource.Online) ShowRemoteCover(null);
        }

        void ShowRemoteCover(Texture2D texture)
        {
            if (detailCoverImage == null || detailCoverFallback == null) return;
            var hasCover = texture != null;
            detailCoverImage.texture = texture;
            detailCoverImage.uvRect = new Rect(0, 0, 1, 1);
            detailCoverImage.gameObject.SetActive(hasCover);
            detailCoverFallback.gameObject.SetActive(!hasCover);
            if (hasCover && detailCoverImage.TryGetComponent<AspectRatioFitter>(out var aspect))
                aspect.aspectRatio = Mathf.Max(.01f, (float)texture.width / texture.height);
        }

        static bool SameRemoteChart(RemoteChartSummary left, RemoteChartSummary right) =>
            left != null && right != null && left.Version == right.Version &&
            string.Equals(left.ChartId, right.ChartId, StringComparison.Ordinal);

        static string RemoteText(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

        static string FormatRemoteDifficulty(RemoteChartSummary chart)
        {
            if (chart == null) return "未標示";
            var difficulty = string.IsNullOrWhiteSpace(chart.Difficulty) ? "未標示" : chart.Difficulty.Trim();
            return difficulty + " " + chart.Rating.ToString("0.##");
        }

        void BuildLibraryRow(LocalChartGroup group, int index, float rowHeight)
        {
            var hasSelectedDifficulty = group.FindDifficulty(selectedDifficultyName);
            var selected = selectedLibraryEntry != null && group.GroupId == selectedLibraryEntry.GroupId;
            var row = Panel("Chart Row", libraryListContent, selected ? new Color(.12f, .25f, .36f) : new Color(.16f, .16f, .16f), new Vector2(0, rowHeight - 2), Vector2.zero);
            row.anchorMin = new Vector2(0, 1);
            row.anchorMax = new Vector2(1, 1);
            row.pivot = new Vector2(.5f, 1);
            var rowHorizontalInset = selected ? LibraryDividerHorizontalInset : 0f;
            row.offsetMin = new Vector2(rowHorizontalInset, -rowHeight * (index + 1));
            row.offsetMax = new Vector2(-rowHorizontalInset, -rowHeight * index);
            if (index > 0)
            {
                var divider = Panel("Chart Divider", row, new Color(.27f, .27f, .27f, .72f), new Vector2(0, PersistentGrayDividerThickness), Vector2.zero);
                var dividerHorizontalInset = selected ? 0f : LibraryDividerHorizontalInset;
                divider.anchorMin = new Vector2(0, 1); divider.anchorMax = new Vector2(1, 1); divider.offsetMin = new Vector2(dividerHorizontalInset, -PersistentGrayDividerThickness); divider.offsetMax = new Vector2(-dividerHorizontalInset, 0);
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
            if (GugarhythmSceneRouter.IsLibrary)
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
            presentationClock.Invalidate();
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
                SetDetailTitle("選擇一份譜面");
                detailArtistLabel.text = string.Empty;
                detailDifficultyLabel.text = "選擇難度";
                detailAccuracyLabel.text = "BEST ACCURACY\n<size=52>—</size>";
                RefreshDetailCover(null);
                return;
            }
            SetDetailTitle(group.Title);
            detailArtistLabel.text = group.Artist;
            detailDifficultyLabel.text = "選擇難度";
            var current = group.Difficulties.FirstOrDefault(entry => entry.Id == selectedLibraryEntry.Id) ?? group.Difficulties[0];
            RefreshDetailCover(current);
            difficultyButtonContent.sizeDelta = new Vector2(Mathf.Max(450f, group.Difficulties.Count * DifficultyButtonSpacing), 76f);
            for (var index = 0; index < group.Difficulties.Count; index++)
            {
                var entry = group.Difficulties[index];
                var text = DifficultyNameOnly(entry);
                var active = entry.Id == current.Id;
                var button = MakeFlatButton(text, difficultyButtonContent, new Vector2(index * DifficultyButtonSpacing, 0),
                    () => SelectLibraryEntry(entry, true), new Vector2(DifficultyButtonWidthForText(text), 52), active ? new Color(.10f, .20f, .29f) : new Color(.15f, .15f, .15f));
                var buttonRect = button.GetComponent<RectTransform>();
                buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0, .5f);
                buttonRect.pivot = new Vector2(0, .5f);
                buttonRect.anchoredPosition = new Vector2(index * DifficultyButtonSpacing, 0);
                Outline(button.gameObject, active ? new Color(.08f, .62f, 1f) : new Color(.34f, .34f, .34f), active ? 3 : 1);
                button.GetComponentInChildren<Text>().color = active ? new Color(.22f, .68f, 1f) : new Color(.78f, .78f, .78f);
            }
            detailAccuracyLabel.text = current.BestAccuracy < 0 ? "BEST ACCURACY\n<size=52>—</size>" : $"BEST ACCURACY\n<size=52>{current.BestAccuracy:F2}%</size>";
        }

        void SetDetailTitle(string title)
        {
            if (detailTitleLabel == null) return;
            detailTitleLabel.text = title ?? string.Empty;
            detailTitleLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            detailTitleLabel.verticalOverflow = VerticalWrapMode.Truncate;
            detailTitleLabel.resizeTextForBestFit = false;

            var maximumSize = detailTitleMaxFontSize > 0 ? detailTitleMaxFontSize : detailTitleLabel.fontSize;
            var minimumSize = Mathf.Min(maximumSize, Mathf.Max(1, Mathf.RoundToInt(maximumSize * (28f / 58f))));
            var availableWidth = detailTitleLabel.rectTransform.rect.width;
            if (availableWidth <= 0f) availableWidth = detailTitleLabel.rectTransform.sizeDelta.x;

            detailTitleLabel.fontSize = maximumSize;
            while (detailTitleLabel.fontSize > minimumSize && detailTitleLabel.preferredWidth > availableWidth)
                detailTitleLabel.fontSize--;
        }

        void RefreshDetailCover(LocalChartEntry entry)
        {
            if (detailCoverImage == null || detailCoverFallback == null) return;
            if (entry == null)
            {
                detailCoverEntryId = null;
                if (detailCoverTexture != null) Destroy(detailCoverTexture);
                detailCoverTexture = null;
            }
            else if (detailCoverEntryId != entry.Id)
            {
                detailCoverEntryId = entry.Id;
                if (detailCoverTexture != null) Destroy(detailCoverTexture);
                detailCoverTexture = LoadDetailCover(entry);
            }

            var hasCover = detailCoverTexture != null;
            detailCoverImage.texture = detailCoverTexture;
            detailCoverImage.uvRect = new Rect(0, 0, 1, 1);
            detailCoverImage.gameObject.SetActive(hasCover);
            detailCoverFallback.gameObject.SetActive(!hasCover);
            if (hasCover && detailCoverImage.TryGetComponent<AspectRatioFitter>(out var aspect))
                aspect.aspectRatio = Mathf.Max(.01f, (float)detailCoverTexture.width / detailCoverTexture.height);
        }

        static Texture2D LoadDetailCover(LocalChartEntry entry)
        {
            if (entry == null || !LocalChartLibrary.TryReadSource(entry, out var bytes)) return null;
            try
            {
                var package = GgrPackageReader.Read(bytes);
                return GgrChartImporter.DecodeCoverTexture(package.CoverBytes, false);
            }
            catch (Exception) { return null; }
        }

        static AspectRatioFitter.AspectMode CoverPresentationAspectMode() => AspectRatioFitter.AspectMode.EnvelopeParent;

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

        static string DifficultyNameOnly(LocalChartEntry entry) =>
            string.IsNullOrWhiteSpace(entry.DifficultyName) ? "未標示難度" : entry.DifficultyName;

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
            GugarhythmSceneRouter.OpenSettings();
        }

        void ReturnFromSettings()
        {
            calibrationActive = false;
            StopCalibrationTickAudio();
            if (ChartSelectionSession.Ensure().TryGetEditorDraft(out _, out _, out _, out _)) GugarhythmSceneRouter.OpenChartEditor();
            else GugarhythmSceneRouter.OpenLibrary();
        }

        void BuildLatencyCalibration(RectTransform root)
        {
            calibrationBackdrop = Panel("Latency Calibration Backdrop", root, new Color(0, 0, 0, .56f), Vector2.zero, Vector2.zero, true);
            MakeInvisibleButton(calibrationBackdrop, ReturnFromLatencyCalibration);
            calibrationPanel = Panel("Latency Calibration Dialog", root, new Color(.12f, .12f, .13f, .99f), new Vector2(560, 440), Vector2.zero);
            Outline(calibrationPanel.gameObject, new Color(.34f, .35f, .38f, .95f), 1);
            var title = Label("自動調整延遲", calibrationPanel, 28);
            title.alignment = TextAnchor.MiddleLeft;
            title.rectTransform.sizeDelta = new Vector2(430, 46);
            title.rectTransform.anchoredPosition = new Vector2(-20, 184);
            var divider = Panel("Calibration Divider", calibrationPanel, new Color(.28f, .29f, .31f), new Vector2(470, 1), new Vector2(0, 145));
            divider.GetComponent<Image>().raycastTarget = false;
            calibrationLabel = Label("", calibrationPanel, 22);
            calibrationLabel.rectTransform.sizeDelta = new Vector2(430, 36);
            calibrationLabel.rectTransform.anchoredPosition = new Vector2(0, 108);
            for (var index = 0; index < calibrationProgressDots.Length; index++)
            {
                calibrationProgressDots[index] = Panel($"Calibration Progress {index + 1}", calibrationPanel,
                    new Color(.30f, .32f, .35f), new Vector2(10, 10), new Vector2((index - 1.5f) * 36f, 68));
                calibrationProgressDots[index].GetComponent<Image>().raycastTarget = false;
            }
            calibrationTapButton = MakePressFlatButton("TAP", calibrationPanel, new Vector2(0, -12), RegisterCalibrationTapFromButton, new Vector2(230, 98), new Color(.08f, .43f, .76f));
            var tapText = calibrationTapButton.GetComponentInChildren<Text>();
            tapText.alignment = TextAnchor.MiddleCenter;
            tapText.fontSize = 30;
            var calibrationActionRow = new GameObject("Calibration Actions", typeof(RectTransform)).GetComponent<RectTransform>();
            calibrationActionRow.SetParent(calibrationPanel, false);
            calibrationActionRow.anchorMin = calibrationActionRow.anchorMax = new Vector2(.5f, .5f);
            calibrationActionRow.pivot = new Vector2(.5f, .5f);
            calibrationActionRow.sizeDelta = new Vector2(320, 44);
            // Keep the restart/close actions at the approved lower position.
            calibrationActionRow.anchoredPosition = new Vector2(0, -134.5f);
            calibrationRestartButton = MakeFlatButton("重新開始", calibrationActionRow, new Vector2(-85, 0), RestartLatencyCalibration, new Vector2(150, 44), new Color(.06f, .58f, .96f));
            calibrationRestartButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            calibrationCloseButton = MakeOutlinedButton("關閉", calibrationActionRow, new Vector2(85, 0), ReturnFromLatencyCalibration, new Vector2(150, 44));
            calibrationCloseButton.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            RefreshManualAudioOffsetControls();
            RefreshCalibrationProgress();
            calibrationBackdrop.gameObject.SetActive(false);
            calibrationPanel.gameObject.SetActive(false);
        }

        void BuildResult(RectTransform root)
        {
            resultPanel = Panel("Result", root, new Color(.04f, .06f, .14f, .96f), new Vector2(620, 650), Vector2.zero); Outline(resultPanel.gameObject, new Color(.9f, .5f, 1f, .75f), 3);
            var title = Label("RESULT", resultPanel, 38); title.rectTransform.sizeDelta = new Vector2(580, 70); title.rectTransform.anchoredPosition = new Vector2(0, 260);
            resultText = Label("", resultPanel, 27); resultText.rectTransform.sizeDelta = new Vector2(540, 440); resultText.rectTransform.anchoredPosition = new Vector2(0, 25);
            MakeButton("返回曲庫", resultPanel, new Vector2(0, -270), GugarhythmSceneRouter.OpenLibrary);
            resultPanel.gameObject.SetActive(false);
        }

        void BuildChartPreview(RectTransform root)
        {
            const float previewInset = 32f;
            chartPreviewBackdrop = Panel("Chart Preview Backdrop", root, new Color(0, 0, 0, .68f), Vector2.zero, Vector2.zero, true);
            MakeInvisibleButton(chartPreviewBackdrop, CloseChartPreview);
            chartPreviewPanel = Panel("Chart Preview Dialog", root, new Color(.11f, .12f, .15f, .99f), new Vector2(1120, 820), Vector2.zero);
            Outline(chartPreviewPanel.gameObject, new Color(.30f, .65f, .94f, .9f), 2);
            chartPreviewTitle = Label("譜面預覽", chartPreviewPanel, 30);
            chartPreviewTitle.alignment = TextAnchor.MiddleLeft;
            chartPreviewTitle.rectTransform.anchorMin = new Vector2(0, 1);
            chartPreviewTitle.rectTransform.anchorMax = new Vector2(1, 1);
            chartPreviewTitle.rectTransform.pivot = new Vector2(0, 1);
            chartPreviewTitle.rectTransform.offsetMin = new Vector2(previewInset, -80);
            chartPreviewTitle.rectTransform.offsetMax = new Vector2(-192, -24);
            var close = MakeOutlinedButton("關閉", chartPreviewPanel, Vector2.zero, CloseChartPreview, new Vector2(120, 48));
            var closeRect = close.GetComponent<RectTransform>();
            PinToAnchor(closeRect, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-previewInset, -24));
            close.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
            var divider = Panel("Chart Preview Divider", chartPreviewPanel, new Color(.30f, .34f, .40f), Vector2.zero, Vector2.zero);
            divider.anchorMin = new Vector2(0, 1);
            divider.anchorMax = new Vector2(1, 1);
            divider.offsetMin = new Vector2(previewInset, -96);
            divider.offsetMax = new Vector2(-previewInset, -94);
            divider.GetComponent<Image>().raycastTarget = false;
            chartPreviewContent = MakeHorizontalScroll("Chart Preview Scroll", chartPreviewPanel, Vector2.zero, Vector2.zero);
            var scrollRoot = chartPreviewContent.parent.GetComponent<RectTransform>();
            scrollRoot.anchorMin = Vector2.zero;
            scrollRoot.anchorMax = Vector2.one;
            scrollRoot.offsetMin = new Vector2(previewInset, previewInset);
            scrollRoot.offsetMax = new Vector2(-previewInset, -112);
            var documentObject = new GameObject("Chart Document", typeof(RectTransform), typeof(CanvasRenderer), typeof(ChartDocumentPreviewGraphic));
            var document = documentObject.GetComponent<RectTransform>();
            document.SetParent(chartPreviewContent, false);
            document.anchorMin = Vector2.zero;
            document.anchorMax = Vector2.one;
            document.pivot = new Vector2(.5f, .5f);
            document.offsetMin = Vector2.zero;
            document.offsetMax = Vector2.zero;
            chartPreviewGraphic = documentObject.GetComponent<ChartDocumentPreviewGraphic>();
            chartPreviewGraphic.color = Color.white;
            chartPreviewGraphic.raycastTarget = false;
            chartPreviewBackdrop.gameObject.SetActive(false);
            chartPreviewPanel.gameObject.SetActive(false);
        }

        void OpenChartPreview()
        {
            if (librarySource != ChartLibrarySource.Local || selectedLibraryEntry == null || chartPreviewGraphic == null) return;
            if (!LocalChartLibrary.TryReadSource(selectedLibraryEntry, out var bytes))
            {
                SetStatus("找不到已儲存的 GGR 檔案。請重新匯入。");
                return;
            }
            var result = new GgrChartImporter().Import(selectedLibraryEntry.SourceFile, bytes, null);
            if (!result.Success || result.Chart == null)
            {
                SetStatus("譜面預覽載入失敗：" + (result.Error ?? "不支援的譜面內容。"));
                return;
            }
            chartPreviewBackdrop.gameObject.SetActive(true);
            chartPreviewPanel.gameObject.SetActive(true);
            chartPreviewTitle.text = string.IsNullOrWhiteSpace(result.Chart.Title) ? selectedLibraryEntry.Title : result.Chart.Title;
            chartPreviewGraphic.SetChart(result.Chart);
            Canvas.ForceUpdateCanvases();
            var scroll = chartPreviewContent == null ? null : chartPreviewContent.parent.GetComponent<ScrollRect>();
            var viewportWidth = scroll?.viewport == null ? 0f : scroll.viewport.rect.width;
            var previewContentWidth = Mathf.Max(chartPreviewGraphic.ContentWidth, viewportWidth);
            var previewContentSize = chartPreviewContent.sizeDelta;
            previewContentSize.x = previewContentWidth;
            chartPreviewContent.sizeDelta = previewContentSize;
            chartPreviewGraphic.rectTransform.offsetMin = Vector2.zero;
            chartPreviewGraphic.rectTransform.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();
            chartPreviewGraphic.RefreshArtwork();
            Canvas.ForceUpdateCanvases();
            if (scroll != null) scroll.horizontalNormalizedPosition = 0f;
        }

        void CloseChartPreview()
        {
            if (chartPreviewBackdrop != null) chartPreviewBackdrop.gameObject.SetActive(false);
            if (chartPreviewPanel != null) chartPreviewPanel.gameObject.SetActive(false);
            chartPreviewGraphic?.ClearChart();
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
#elif UNITY_ANDROID || UNITY_IOS
            NativeChartPicker.OpenFile();
            SetStatus("請在系統檔案選擇器選取 GGR…");
#else
            SetStatus("目前請將譜面放入 StreamingAssets，或使用 Android／iOS 匯入。");
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
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            if (loading) return;
            var result = NativeChartPicker.ConsumeResult();
            if (!string.IsNullOrEmpty(result)) StartCoroutine(ImportPaths(NativeChartPicker.SplitResultPaths(result)));
#endif
        }

        IEnumerator ImportPaths(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0) yield break;
            for (var index = 0; index < paths.Count; index++)
            {
                yield return ImportPath(paths[index]);
                if (importDecisionPanel != null && importDecisionPanel.gameObject.activeSelf)
                    yield return new WaitUntil(() => importDecisionPanel == null || !importDecisionPanel.gameObject.activeSelf);
            }
            SetStatus($"批量匯入完成：{paths.Count} 份");
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
            view.TraceParticle = particle;
            var flickArrow = RawPanel("Flick Arrow", view.rectTransform, null, Color.white, new Vector2(72, 58), new Vector2(0, 32)).GetComponent<RawImage>();
            flickArrow.raycastTarget = false;
            flickArrow.gameObject.SetActive(false);
            view.FlickArrow = flickArrow;
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
            var particle = view.TraceParticle;
            if (particle != null)
            {
                particle.texture = holdMid
                    ? note.Critical ? holdMidYellowTexture : holdMidMintTexture
                    : traceKey == "yellow" ? traceDiamondYellowTexture :
                    traceKey == "pink" ? traceDiamondPinkTexture : traceDiamondMintTexture;
                particle.gameObject.SetActive(ShouldShowNoteParticle(note, particle.texture != null));
            }
            var flickArrow = view.FlickArrow;
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
        void ReleaseHoldRun(HoldRenderRun run, TaperedConnectorGraphic line) { holdRunViews.Remove(run); line.gameObject.SetActive(false); connectorPool.Push(line); }
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

        HoldBatchGraphic CreateHoldBatch(string name, Texture2D texture, Material material)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(HoldBatchGraphic));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(connectorLayer, false); Fill(rect);
            var batch = go.GetComponent<HoldBatchGraphic>();
            batch.raycastTarget = false;
            batch.texture = texture;
            batch.material = material;
            batch.color = new Color(1, 1, 1, .62f);
            batch.sourceUvInset = HoldConnectorVisibleUvInset;
            return batch;
        }

        NoteParticleBatchGraphic CreateNoteParticleBatch(string name, Texture2D texture)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(NoteParticleBatchGraphic));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(noteLayer, false); Fill(rect);
            var batch = go.GetComponent<NoteParticleBatchGraphic>();
            batch.raycastTarget = false;
            batch.texture = texture;
            batch.color = Color.white;
            return batch;
        }

        HoldBatchGraphic LegacyHoldBatch(RuntimeConnector connector)
        {
            var missed = IsHoldCurrentlyMissed(connector);
            if (connector.Critical) return missed ? missedHoldYellowBatch : holdYellowBatch;
            return missed ? missedHoldGreenBatch : holdGreenBatch;
        }

        HoldBatchGraphic HoldRunBatch(HoldRenderRun run)
        {
            var missed = IsHoldCurrentlyMissedCached(run.Path.RootIndex);
            if (run.Critical) return missed ? missedHoldYellowBatch : holdYellowBatch;
            return missed ? missedHoldGreenBatch : holdGreenBatch;
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
        void ReleaseAllViews()
        {
            persistentHeadReleaseKeys.Clear();
            foreach (var pair in persistentHoldHeadViews) persistentHeadReleaseKeys.Add(pair.Key);
            foreach (var key in persistentHeadReleaseKeys)
                if (persistentHoldHeadViews.TryGetValue(key, out var head)) ReleasePersistentHoldHead(key, head);

            noteViewReleaseKeys.Clear();
            foreach (var pair in noteViews) noteViewReleaseKeys.Add(pair.Key);
            foreach (var key in noteViewReleaseKeys)
                if (noteViews.TryGetValue(key, out var note)) ReleaseNoteView(key, note);

            holdRunReleaseKeys.Clear();
            foreach (var pair in holdRunViews) holdRunReleaseKeys.Add(pair.Key);
            foreach (var run in holdRunReleaseKeys)
                if (holdRunViews.TryGetValue(run, out var hold)) ReleaseHoldRun(run, hold);

            connectorReleaseKeys.Clear();
            foreach (var pair in connectorViews) connectorReleaseKeys.Add(pair.Key);
            foreach (var connector in connectorReleaseKeys)
                if (connectorViews.TryGetValue(connector, out var hold)) ReleaseConnector(connector, hold);

            simLineReleaseKeys.Clear();
            foreach (var pair in simLineViews) simLineReleaseKeys.Add(pair.Key);
            foreach (var simLine in simLineReleaseKeys)
                if (simLineViews.TryGetValue(simLine, out var line)) ReleaseSimLine(simLine, line);

            guideReleaseKeys.Clear();
            foreach (var pair in guideViews) guideReleaseKeys.Add(pair.Key);
            foreach (var guide in guideReleaseKeys)
                if (guideViews.TryGetValue(guide, out var line)) ReleaseGuide(guide, line);
        }

        void SpawnHitParticle(JudgmentEvent judgment)
        {
            var note = judgment.Note;
            var tint = ResolveHitEffectColor(note);
            var x = X(ResolveHitEffectLane(judgment), 1f);
            var noteWidth = Mathf.Clamp(LaneWidth(note.Lane, note.Size, 1f), 64f, 154f);
            var particleRoot = new GameObject("Judgment Pulse", typeof(RectTransform), typeof(CanvasRenderer), typeof(HitBurstGraphic)).GetComponent<RectTransform>();
            particleRoot.SetParent(stage, false); particleRoot.sizeDelta = new Vector2(360, 600); particleRoot.anchoredPosition = new Vector2(x, HitY);
            particleRoot.SetAsLastSibling();
            var burst = particleRoot.GetComponent<HitBurstGraphic>();
            burst.raycastTarget = false;
            burst.color = tint;
            burst.upperWidth = noteWidth;
            burst.effectMode = hitParticleEffectMode;
            burst.SetProgress(0);
            StartCoroutine(AnimateHitEffect(particleRoot, burst));
        }

        public static float ResolveHitEffectLane(JudgmentEvent judgment) =>
            judgment.HitLane ?? judgment.Note?.Lane ?? 0f;

        public static Color ResolveHitEffectColor(RuntimeNote note)
        {
            if (note.Critical) return new Color(1f, .82f, .12f, .9f);
            if (IsTrace(note) || note.Kind == RuntimeNoteKind.Sustain) return new Color(.12f, 1f, .58f, .84f);
            return note.Kind == RuntimeNoteKind.Flick
                ? new Color(1f, .2f, .67f, .86f)
                : new Color(.28f, .82f, 1f, .84f);
        }

        public static string JudgmentSpriteResourcePath(JudgmentGrade grade) => grade switch
        {
            JudgmentGrade.Perfect => "JudgmentSprites/perfect",
            JudgmentGrade.Great => "JudgmentSprites/great",
            JudgmentGrade.Good => "JudgmentSprites/good",
            JudgmentGrade.Miss => "JudgmentSprites/miss",
            _ => string.Empty,
        };

        public static string JudgmentTimingSpriteResourcePath(JudgmentTiming timing) => timing switch
        {
            JudgmentTiming.Fast => "JudgmentSprites/fast",
            JudgmentTiming.Late => "JudgmentSprites/late",
            _ => string.Empty,
        };

        IEnumerator AnimateHitEffect(RectTransform particleRoot, HitBurstGraphic burst)
        {
            for (var elapsed = 0f; elapsed < HitBurstGraphic.DurationSeconds; elapsed += Time.unscaledDeltaTime)
            {
                burst.SetProgress(elapsed / HitBurstGraphic.DurationSeconds);
                yield return null;
            }
            Destroy(particleRoot.gameObject);
        }

        void RefreshHud()
        {
            var totalNotes = chart?.PlayableCount ?? 0;
            if (hudState.ShouldUpdateAccuracy(scoreState.AccuracyNumerator, totalNotes))
                accuracyLabel.text = $"ACCURACY  {scoreState.AccuracyPercent(totalNotes):F4}%";
            var comboVisible = running && scoreState.Combo > 0;
            if (!hudState.ShouldUpdateCombo(scoreState.Combo, comboVisible)) return;
            comboLabel.text = "COMBO\n" + scoreState.Combo;
            comboLabel.gameObject.SetActive(comboVisible);
        }
        void SetStatus(string message) { if (loadStatus != null) loadStatus.text = message; }
        void ShowJudgment(JudgmentGrade grade)
        {
            if (judgmentImage == null || !judgmentSprites.TryGetValue(grade, out var sprite) || sprite == null) return;
            SetJudgmentSprite(judgmentImage, sprite);
            judgmentHideAt = Time.unscaledTime + JudgmentDisplayDuration;
        }

        void ShowJudgmentTiming(JudgmentTiming timing)
        {
            if (!fastLateDisplayEnabled || judgmentTimingImage == null || timing == JudgmentTiming.None ||
                !judgmentTimingSprites.TryGetValue(timing, out var sprite) || sprite == null)
            {
                SetJudgmentSprite(judgmentTimingImage, null);
                return;
            }
            var width = JudgmentTimingSpriteHeight * sprite.width / Mathf.Max(1, sprite.height);
            judgmentTimingImage.rectTransform.sizeDelta = new Vector2(width, JudgmentTimingSpriteHeight);
            SetJudgmentSprite(judgmentTimingImage, sprite);
        }

        void ClearJudgment()
        {
            SetJudgmentSprite(judgmentImage, null);
            SetJudgmentSprite(judgmentTimingImage, null);
            judgmentHideAt = -1f;
        }

        public static void SetJudgmentSprite(RawImage image, Texture texture)
        {
            if (image == null) return;
            image.texture = texture;
            image.enabled = texture != null;
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

        static Button MakePressFlatButton(string text, RectTransform parent, Vector2 position, Action action, Vector2 size, Color color)
        {
            var button = MakeFlatButton(text, parent, position, action, size, color);
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
            entry.callback.AddListener(_ => action());
            trigger.triggers = new List<EventTrigger.Entry> { entry };
            return button;
        }

        static Button MakeDelayHoldButton(string text, RectTransform parent, Vector2 position, Action action,
            Vector2 size, Color color)
        {
            var button = MakeFlatButton(text, parent, position, action, size, color);
            button.onClick.RemoveAllListeners();
            button.gameObject.AddComponent<SettingsDelayHoldButton>().Configure(action);
            return button;
        }

        static Button MakeOutlinedButton(string text, RectTransform parent, Vector2 position, Action action, Vector2 size)
        {
            var panel = Panel(text, parent, new Color(.16f, .16f, .16f), size, position);
            var image = panel.GetComponent<Image>();
            image.raycastTarget = true;
            Outline(panel.gameObject, new Color(.42f, .42f, .42f), 2);
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

        static InputField MakeInputField(string placeholder, RectTransform parent, Vector2 position, Vector2 size, bool showOutline = true)
        {
            var panel = Panel("Search", parent, new Color(.12f, .12f, .12f), size, position);
            if (showOutline) Outline(panel.gameObject, new Color(.30f, .30f, .30f), 1);
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
            // Reserve a wider gutter for the scrollbar so row hit areas and
            // selected outlines never extend underneath the draggable handle.
            content.offsetMax = new Vector2(-38, 0);
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
            var handle = Panel("Scroll Handle", track, new Color(1f, 1f, 1f, .9f), new Vector2(6, 40), Vector2.zero);
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
            // Hide the scrollbar when the list fits in the viewport; otherwise
            // Unity expands the handle to the full track and it looks like a
            // non-draggable decoration rather than a scroll thumb.
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        static RectTransform MakeHorizontalScroll(string name, RectTransform parent, Vector2 position, Vector2 size)
        {
            var root = Panel(name, parent, new Color(.12f, .12f, .12f), size, position);
            var mask = root.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(root, false);
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(0, 1);
            content.pivot = new Vector2(0, .5f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(size.x, 0);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = root;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = .135f;
            scroll.scrollSensitivity = 28;
            var track = Panel("Scroll Track", root, new Color(.35f, .35f, .35f, .34f), Vector2.zero, Vector2.zero);
            track.anchorMin = new Vector2(0, 0);
            track.anchorMax = new Vector2(1, 0);
            track.pivot = new Vector2(.5f, 0);
            track.offsetMin = new Vector2(10, 7);
            track.offsetMax = new Vector2(-10, 13);
            var handle = Panel("Scroll Handle", track, new Color(1f, 1f, 1f, .9f), new Vector2(40, 6), Vector2.zero);
            handle.anchorMin = new Vector2(0, 0);
            handle.anchorMax = new Vector2(0, 1);
            handle.pivot = new Vector2(0, .5f);
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = new Vector2(40, 0);
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.direction = Scrollbar.Direction.LeftToRight;
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
            scroll.horizontalScrollbar = scrollbar;
            scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
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

        static Toggle MakeFigmaSlidingToggle(string labelText, RectTransform parent, Vector2 position, float width, bool enabled)
        {
            var rootObject = new GameObject($"{labelText} Sliding Toggle", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(FigmaRoundedRectangleGraphic), typeof(Toggle), typeof(FigmaSlidingToggleVisual));
            var root = rootObject.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.sizeDelta = new Vector2(width, 56f);
            root.anchoredPosition = position;
            var background = rootObject.GetComponent<FigmaRoundedRectangleGraphic>();
            background.Configure(new Color(.11f, .13f, .17f, 1f), 14f);
            var label = Label(labelText, root, 20);
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.anchorMin = new Vector2(0f, .5f);
            label.rectTransform.anchorMax = new Vector2(0f, .5f);
            label.rectTransform.pivot = new Vector2(0f, .5f);
            label.rectTransform.sizeDelta = new Vector2(width - 118f, 44f);
            label.rectTransform.anchoredPosition = new Vector2(20f, 0f);

            var trackObject = new GameObject("Sliding Track", typeof(RectTransform), typeof(CanvasRenderer), typeof(FigmaRoundedRectangleGraphic));
            var trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.SetParent(root, false);
            trackRect.sizeDelta = new Vector2(88f, 44f);
            trackRect.anchoredPosition = new Vector2(width * .5f - 64f, 0f);
            var track = trackObject.GetComponent<FigmaRoundedRectangleGraphic>();
            track.raycastTarget = false;

            var handleObject = new GameObject("Sliding Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(FigmaRoundedRectangleGraphic));
            var handle = handleObject.GetComponent<RectTransform>();
            handle.SetParent(trackRect, false);
            handle.sizeDelta = new Vector2(32f, 32f);
            var handleGraphic = handleObject.GetComponent<FigmaRoundedRectangleGraphic>();
            handleGraphic.Configure(new Color(.97f, .99f, 1f, 1f), 16f);
            handleGraphic.raycastTarget = false;

            var toggle = rootObject.GetComponent<Toggle>();
            toggle.targetGraphic = background;
            // Toggle.graphic is treated as a checkmark and fades out while
            // disabled. The Figma handle is persistent and is moved by
            // FigmaSlidingToggleVisual instead.
            toggle.graphic = null;
            toggle.isOn = enabled;
            rootObject.GetComponent<FigmaSlidingToggleVisual>().Initialize(toggle, handle, track, 22f);
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
        struct TouchMemory { public float Lane; public Vector2 ScreenPosition; public double EventTime; public double StartTime; public double LastInputRecordTime; }
    }
}
