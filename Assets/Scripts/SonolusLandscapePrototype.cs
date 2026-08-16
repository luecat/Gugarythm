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
        public enum HoldConnectorRenderMode { AnchorClipped, NaturalPassThrough }
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
        const float PerspectiveDepthRatio = 3.2f;
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
        // Note height is independent of note.Size, but follows the same depth
        // scale as lane width. This preserves the source video's perspective
        // without making wide notes uniformly taller.
        // At the judgment edge the lane artwork's purple judgment strip is
        // about 45 px tall while a size-1 note spans about 147.5 px. The atlas
        // note sprites include transparent glow padding; Next SEKAI expands
        // their render quad by 2.325 so the visible body (not the padded image)
        // shares the judgment strip's height.
        const float ButtonSpriteTransform = 2.325f;
        const float ButtonHeightRatio = 45f / 147.5f * ButtonSpriteTransform;
        const float NoteCapRatio = 93f / 354f;
        const float NoteTextureHeight = 186f;
        const float HoldHeadWidthScale = 1.275f;
        const int MouseContactId = int.MinValue;
        // Missed notes keep travelling beyond the judgment line until their
        // sprite leaves the viewport. Successful hits return to the pool at once.
        const float NoteExitMargin = 140f;
        const float JudgmentDisplayDuration = .35f;

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
        readonly Stack<HorizontalSlicedRawImage> notePool = new();
        readonly Stack<TaperedConnectorGraphic> connectorPool = new();
        readonly Stack<SimLineGraphic> simLinePool = new();
        readonly Stack<TaperedConnectorGraphic> guidePool = new();
        readonly Dictionary<int, TouchMemory> touches = new();
        readonly List<InputToken> inputBatch = new();
        readonly List<ActiveContact> contacts = new();
        readonly List<ContactPathSegment> contactPaths = new();
        readonly VirtualSliderInput virtualSlider = new();
        readonly float[] connectorPathSamples = new float[ConnectorPathSegments + 3];
        readonly ScoreState scoreState = new();
        readonly List<IChartImporter> importers = new() { new ScpChartImporter(), new SusChartImporter(), new UscChartImporter(), new LevelDataImporter() };

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
        Texture2D pixelParticleAtlas;
        AudioClip perfectSound;
        AudioClip greatSound;
        AudioClip goodSound;
        AudioClip holdSound;
        AudioClip flickSound;
        AudioClip criticalFlickSound;
        AudioClip stageSound;
        RuntimeChart chart;
        JudgmentEngine judgmentEngine;
        AudioSource music;
        AudioSource effects;
        RectTransform canvasRoot;
        RectTransform stage;
        RectTransform safeAreaRoot;
        RectTransform guideLayer;
        RectTransform connectorLayer;
        RectTransform simLineLayer;
        RectTransform noteLayer;
        RectTransform menuPanel;
        RectTransform pauseOverlay;
        RectTransform pauseMenuContent;
        RectTransform resultPanel;
        Text accuracyLabel;
        Text comboLabel;
        Text judgmentLabel;
        Text loadStatus;
        Text resultText;
        Text speedLabel;
        Text resumeCountdownLabel;
        Text pauseTitle;
        Button startButton;
        Button pauseButton;
        Slider speedSlider;
        Material laneMaterial;
        bool running;
        bool loading;
        bool paused;
        int audioDeviceChangePending;
        bool resumeNeedsAudioReschedule;
        Coroutine resumeCoroutine;
        Rect appliedSafeArea = new(-1, -1, -1, -1);
        double scheduledDsp;
        double pauseDsp;
        double accumulatedPause;
        double lastObservedSongTime;
        double interruptedSongTime;
        double inputOffsetSeconds;
        double visualOffsetSeconds;
        float scrollSpeed = 8f;
        float judgmentHideAt = -1f;

        static float CanvasHeight => ReferenceWidth * Screen.height / Math.Max(1, Screen.width);
        static float TopY => CanvasHeight * .5f;
        static float HitY => TopY - HitSourceY / LaneTextureHeight * CanvasHeight;
        static float NoteExitY => -TopY - NoteExitMargin;
        static float NearTrackProgress => (TopY - NoteExitY) / Mathf.Max(1, TopY - HitY);
        static float NearTrackApproach => 1f + (NearTrackProgress - 1f) / PerspectiveDepthRatio;
        // HitSourceY is the centre of the 45px judgment strip. Notes and
        // gameplay connectors leave only after reaching its lower edge.
        static float JudgmentBottomApproach => 1f + (JudgmentStripSourceHeight * .5f / HitSourceY) / PerspectiveDepthRatio;

        // Speed controls the time spent approaching the judgment edge. Screen Y,
        // lane position, and width are all derived from the same perspective
        // projection below, so distant notes converge at the vanishing point.
        float ApproachDuration => (TopY - HitY) / (210f + scrollSpeed * 52f);

        void Awake()
        {
            AudioSettings.OnAudioConfigurationChanged += HandleAudioConfigurationChanged;
            Application.targetFrameRate = 120;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            QualitySettings.vSyncCount = 0;
            scrollSpeed = PlayerPrefs.GetFloat("gugarythm-scroll-speed", 8f);
#if UNITY_EDITOR || UNITY_STANDALONE
            // TouchSimulation can leave the real Mouse device disabled across
            // editor play sessions. Desktop input is adapted explicitly below.
            EnsureDesktopMouseAvailable();
#endif
            EnhancedTouchSupport.Enable();
            LoadArtwork();
            BuildInterface();
            StartCoroutine(LoadDefaultChart());
        }

        void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= HandleAudioConfigurationChanged;
#if UNITY_EDITOR || UNITY_STANDALONE
            TouchSimulation.Disable();
#endif
            if (EnhancedTouchSupport.enabled) EnhancedTouchSupport.Disable();
            if (laneMaterial != null) Destroy(laneMaterial);
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
            UpdateDesktopSpeedControls();
            if (judgmentHideAt >= 0 && Time.unscaledTime >= judgmentHideAt)
                ShowJudgment("", Color.white);
            if (Interlocked.Exchange(ref audioDeviceChangePending, 0) != 0 &&
                ShouldPauseForAudioConfigurationChange(true, running, paused))
                PauseForAudioDeviceChange();
            if (!running || paused || chart == null || judgmentEngine == null) return;
            var songTime = CurrentSongTime();
            lastObservedSongTime = songTime;
            CollectInput();
            var events = judgmentEngine.Process(songTime, inputBatch, contacts, contactPaths);
            if (events.Count > 0)
            {
                foreach (var judgment in events) OnJudgment(judgment);
            }
            UpdateVisuals(songTime + visualOffsetSeconds);
            RefreshHud();
            if (songTime > chart.LastNoteTime + .75 && chart.Notes.All(note => !note.Judged || note.Grade != JudgmentGrade.Pending)) FinishGame();
        }

        IEnumerator LoadDefaultChart()
        {
            loading = true;
            SetStatus("正在解析預設 SCP…");
            var path = Path.Combine(Application.streamingAssetsPath, "Charts/default.scp");
            byte[] bytes;
            if (path.Contains("://") || path.Contains(":///"))
            {
                using var request = UnityWebRequest.Get(path);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { SetStatus("預設 SCP 載入失敗：" + request.error); loading = false; yield break; }
                bytes = request.downloadHandler.data;
            }
            else bytes = File.ReadAllBytes(path);
            yield return ImportBytes("default.scp", bytes, null, true);
        }

        IEnumerator ImportBytes(string fileName, byte[] bytes, IReadOnlyDictionary<string, byte[]> companions, bool isDefault = false)
        {
            loading = true;
            startButton.interactable = false;
            SetStatus("正在匯入 " + fileName + "…");
            yield return null;
            var header = bytes.Length <= 16 ? bytes : bytes[..16];
            ImportResult result = null;
            foreach (var importer in importers)
            {
                if (!importer.CanImport(fileName, header)) continue;
                result = importer.Import(fileName, bytes, companions);
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
                yield return LoadMusic(chart.BgmBytes, chart.BgmExtension);
                if (music.clip == null) { loading = false; yield break; }
            }
            else SetStatus("譜面已解析，但缺少音樂。請用 ZIP／資料夾連同音訊匯入。");

            SaveToLocalLibrary(fileName, bytes, chart);
            startButton.interactable = music.clip != null;
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
                speedLabel.text = $"流速  {value:F1}  ·  按鈕／← → 每次 0.1";
            PlayerPrefs.SetFloat("gugarythm-scroll-speed", value);
        }

        IEnumerator LoadMusic(byte[] bytes, string extension)
        {
            var cache = Path.Combine(Application.persistentDataPath, "AudioCache");
            Directory.CreateDirectory(cache);
            var hash = LocalChartLibrary.Sha256(bytes);
            var path = Path.Combine(cache, hash + (string.IsNullOrEmpty(extension) ? ".mp3" : extension));
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            var type = extension?.ToLowerInvariant() switch
            {
                ".ogg" => AudioType.OGGVORBIS,
                ".wav" => AudioType.WAV,
                ".m4a" or ".aac" => AudioType.ACC,
                _ => AudioType.MPEG,
            };
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { SetStatus("音樂解碼失敗：" + request.error); yield break; }
            music.clip = DownloadHandlerAudioClip.GetContent(request);
        }

        void StartGame()
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
            lastObservedSongTime = -chart.BgmOffset;
            effects.UnPause();
            scheduledDsp = AudioSettings.dspTime + .25;
            music.time = 0;
            music.PlayScheduled(scheduledDsp);
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
                music.Stop();
                music.time = AudioDeviceRecovery.ClipTimeForChartTime(interruptedSongTime, chart.BgmOffset, music.clip.length);
                scheduledDsp = AudioDeviceRecovery.ScheduledDspForChartTime(nextDsp, interruptedSongTime, chart.BgmOffset);
                accumulatedPause = 0;
                music.PlayScheduled(nextDsp);
                resumeNeedsAudioReschedule = false;
            }
            else
            {
                accumulatedPause += AudioSettings.dspTime - pauseDsp;
                music.UnPause();
            }
            effects.UnPause();
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
            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
            ReleaseAllViews();
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);
            menuPanel.gameObject.SetActive(true);
            RefreshHud();
            ShowJudgment("", Color.white);
        }

        void CancelResumeCountdown()
        {
            if (resumeCoroutine == null) return;
            StopCoroutine(resumeCoroutine);
            resumeCoroutine = null;
        }

        void ResetRuntime()
        {
            foreach (var note in chart.Notes) note.Grade = JudgmentGrade.Pending;
            BuildHoldRenderState();
            scoreState.Reset();
            judgmentEngine = new JudgmentEngine(chart.Notes, scoreState);
            touches.Clear();
            contactPaths.Clear();
            virtualSlider.Reset();
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
                seen.Add(id);
                var eventTime = InputEventSongTime(touch.time);
                var lane = ScreenToLane(touch.screenPosition);
                if (!touches.TryGetValue(id, out var memory))
                {
                    memory = new TouchMemory { Lane = lane, EventTime = eventTime, StartTime = eventTime, LastInputRecordTime = double.NegativeInfinity };
                    touches[id] = memory;
                }
                if (touch.time > memory.LastInputRecordTime + 1e-7)
                {
                    if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                        virtualSlider.Begin(id, eventTime - inputOffsetSeconds, lane, inputBatch);
                    else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved && Vector2.SqrMagnitude(touch.screenPosition - memory.ScreenPosition) > .01f)
                    {
                        virtualSlider.Move(id, eventTime - inputOffsetSeconds, lane, inputBatch);
                        contactPaths.Add(new ContactPathSegment(id, memory.EventTime - inputOffsetSeconds, eventTime - inputOffsetSeconds,
                            memory.Lane, lane, false));
                    }
                    else if (touch.phase is UnityEngine.InputSystem.TouchPhase.Ended or UnityEngine.InputSystem.TouchPhase.Canceled)
                    {
                        contactPaths.Add(new ContactPathSegment(id, memory.EventTime - inputOffsetSeconds, eventTime - inputOffsetSeconds,
                            memory.Lane, lane, true));
                        virtualSlider.End(id, eventTime - inputOffsetSeconds, lane, inputBatch);
                    }
                    memory.LastInputRecordTime = touch.time;
                    memory.EventTime = eventTime;
                    memory.Lane = lane;
                    memory.ScreenPosition = touch.screenPosition;
                    touches[id] = memory;
                }
                if (touch.phase is not UnityEngine.InputSystem.TouchPhase.Ended and not UnityEngine.InputSystem.TouchPhase.Canceled)
                    contacts.Add(new ActiveContact(id, lane, memory.StartTime - inputOffsetSeconds));
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
            var lane = ScreenToLane(position);
            var eventTime = CurrentSongTime();
            var began = !touches.TryGetValue(MouseContactId, out var memory);
            if (endedThisFrame && !began)
            {
                contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime - inputOffsetSeconds, eventTime - inputOffsetSeconds,
                    memory.Lane, lane, true));
                virtualSlider.End(MouseContactId, eventTime - inputOffsetSeconds, lane, inputBatch);
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
                virtualSlider.Begin(MouseContactId, eventTime - inputOffsetSeconds, lane, inputBatch);
            }
            else if (Vector2.SqrMagnitude(position - memory.ScreenPosition) > .01f)
            {
                virtualSlider.Move(MouseContactId, eventTime - inputOffsetSeconds, lane, inputBatch);
                contactPaths.Add(new ContactPathSegment(MouseContactId, memory.EventTime - inputOffsetSeconds, eventTime - inputOffsetSeconds,
                    memory.Lane, lane, false));
                memory.Lane = lane;
                memory.ScreenPosition = position;
                memory.EventTime = eventTime;
                memory.LastInputRecordTime = eventTime;
            }

            touches[MouseContactId] = memory;
            if (!pressed) return;
            seen.Add(MouseContactId);
            contacts.Add(new ActiveContact(MouseContactId, lane, memory.StartTime - inputOffsetSeconds));
        }
#endif

        double InputEventSongTime(double inputTime)
        {
            var eventDsp = AudioSettings.dspTime - (InputState.currentTime - inputTime);
            return eventDsp - scheduledDsp - accumulatedPause - chart.BgmOffset;
        }

        float ScreenToLane(Vector2 screenPosition)
        {
            // Invert the same background-derived geometry used for rendering.
            var canvasX = (screenPosition.x / Math.Max(1, Screen.width) - .5f) * ReferenceWidth;
            return ScreenXToLane(canvasX, 1f);
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
                SpawnHitParticle(X(judgment.Note.Lane, 1f), judgment.Note.Critical ? "yellow" : IsTrace(judgment.Note) || judgment.Note.Kind == RuntimeNoteKind.Sustain ? "green" : judgment.Note.Kind == RuntimeNoteKind.Flick ? "pink" : "blue");
            }
        }

        void PlayJudgmentSound(JudgmentEvent judgment)
        {
            if (judgment.Grade == JudgmentGrade.Miss || effects == null) return;
            var clip = UsesHoldJudgmentSound(judgment.Note) && holdSound != null
                ? holdSound
                : judgment.Note.Kind == RuntimeNoteKind.Flick
                ? judgment.Note.Critical && criticalFlickSound != null ? criticalFlickSound : flickSound
                : judgment.Grade switch
            {
                JudgmentGrade.Perfect => perfectSound,
                JudgmentGrade.Great => greatSound,
                JudgmentGrade.Good => goodSound,
                _ => null,
            };
            if (clip != null) effects.PlayOneShot(clip, .78f);
        }

        public static bool UsesHoldJudgmentSound(RuntimeNote note) =>
            note != null && note.HoldCheckpointSource is HoldCheckpointSource.Auto or HoldCheckpointSource.Mid;

        void UpdateVisuals(double visualTime)
        {
            renderedPersistentHoldHeads.Clear();
            foreach (var guide in chart.Guides)
            {
                var headApproach = ApproachProgress(guide.Head.Time, visualTime, guide.Head.TimeScaleGroup);
                var tailApproach = ApproachProgress(guide.Tail.Time, visualTime, guide.Tail.TimeScaleGroup);
                var headY = ScreenY(PerspectiveProgress(headApproach));
                var show = headY <= TopY + 8 && HasVisibleDecorationSegment(headApproach, tailApproach);
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
                var visible = Mathf.Min(aY, bY) <= TopY + 8 &&
                    HasVisibleDecorationSegment(leadingApproach, trailingApproach);
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
                var visible = note.Visible && y <= TopY + 8 && y >= NoteExitY &&
                    !ShouldHideHoldHead(note, approachProgress);
                if (!visible)
                {
                    if (noteViews.TryGetValue(note.Index, out var oldView)) ReleaseNoteView(note.Index, oldView);
                    continue;
                }
                if (!noteViews.TryGetValue(note.Index, out var view))
                {
                    view = AcquireNoteView();
                    noteViews[note.Index] = view;
                    ApplyNoteTexture(view, note);
                }
                var width = LaneWidth(note.Lane, note.Size, screenProgress) *
                    (note.HoldRootIndex == note.Index ? HoldHeadWidthScale : 1f);
                view.rectTransform.anchoredPosition = new Vector2(X(note.Lane, screenProgress), y);
                // The source engine applies the same perspective depth to both
                // axes. Slim Trace/Damage bodies use a full-height atlas quad;
                // their texture's transparent rows create the thin visible line.
                var depthLaneWidth = LaneWidth(0, 1f, screenProgress);
                var height = depthLaneWidth * ButtonHeightRatio;
                // HorizontalSlicedRawImage preserves each cap's pixel aspect,
                // so compensate the atlas's transparent outer pixels in screen
                // space. The visible note edges—not the PNG bounds—then meet
                // the exact same lane edges as Hold and Guide geometry.
                var horizontalPadding = height * NoteOuterPaddingPixels(note) / NoteTextureHeight;
                view.rectTransform.sizeDelta = new Vector2(width + horizontalPadding * 2, height);
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
                var show = startY <= TopY + 8 && (holdMode == HoldConnectorRenderMode.AnchorClipped
                    ? endApproach < JudgmentBottomApproach
                    : endY >= NoteExitY);
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
                SetConnectorPath(line, connector, visualTime, startApproach, endApproach, holdMode);
                if (connector.Start.HoldRootIndex >= 0 && startApproach >= 1f && endApproach <= 1f &&
                    holdRoots.TryGetValue(connector.Start.HoldRootIndex, out var root))
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
                view = AcquireNoteView();
                persistentHoldHeadViews[rootIndex] = view;
                view.texture = buttonTextures.TryGetValue(root.Critical ? "yellow" : "mint", out var texture) ? texture : null;
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
            var height = LaneWidth(0, 1f, screenProgress) * ButtonHeightRatio;
            var padding = height * (root.Critical ? 28f : 40f) / NoteTextureHeight;
            view.rectTransform.anchoredPosition = new Vector2(X(lane, screenProgress), HitY);
            view.rectTransform.sizeDelta = new Vector2(LaneWidth(lane, size, screenProgress) * HoldHeadWidthScale + padding * 2, height);
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
            // The official connector sprite expands its target quad by 1.275,
            // while its baked transparent shoulders bring the visible fill
            // back to the note's exact left/right edges.
            line.SetPathPoint(index, new Vector2(X(lane, screenProgress), ScreenY(screenProgress)), LaneWidth(lane, size, screenProgress) * 1.275f);
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
        public static bool HasVisibleDecorationSegment(float leadingApproach, float trailingApproach) => trailingApproach < 1f;
        public static bool ShouldHideJudgedVisual(JudgmentGrade grade, float approachProgress) =>
            approachProgress >= JudgmentBottomApproach && grade != JudgmentGrade.Pending;
        static bool ShouldHideHoldHead(RuntimeNote note, float approachProgress) =>
            note.IsHoldTerminal
                ? approachProgress >= 1f
                : note.HoldRootIndex == note.Index
                ? approachProgress >= 1f
                : ShouldHideJudgedVisual(note.Grade, approachProgress);
        public static HoldConnectorRenderMode ResolveHoldConnectorRenderMode(bool rootSucceeded, JudgmentGrade nextCheckpointGrade) =>
            HoldConnectorRenderMode.AnchorClipped;
        public static bool ShouldClipHoldConnector(RuntimeConnector connector) =>
            connector?.Start != null && connector.Start.HoldRootIndex >= 0;

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

        static bool IsDamage(RuntimeNote note) =>
            (note.Archetype ?? string.Empty).IndexOf("Damage", StringComparison.OrdinalIgnoreCase) >= 0;

        static float NoteOuterPaddingPixels(RuntimeNote note)
        {
            if (IsDamage(note)) return 21f;
            if (IsTrace(note)) return note.Critical ? 30f : 41f;
            return note.Critical ? 28f : 40f;
        }

        void FinishGame()
        {
            CancelResumeCountdown();
            running = false;
            paused = false;
            music.Stop();
            pauseOverlay.gameObject.SetActive(false);
            pauseButton.gameObject.SetActive(false);
            ReleaseAllViews();
            RefreshHud();
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
            pixelParticleAtlas = Resources.Load<Texture2D>("Gugarhythm/package/particles/pixel-atlas");
            perfectSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/perfect");
            greatSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/great");
            goodSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/good");
            holdSound = Resources.Load<AudioClip>("Gugarhythm/package/audio/hold");
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
            RawPanel("Background", root, backgroundTexture, new Color(1, 1, 1, .72f), Vector2.zero, Vector2.zero, true);
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
            guideLayer = Layer("Decoration Guides", stage);
            connectorLayer = Layer("Hold Connectors", stage);
            simLineLayer = Layer("Synchronization Lines", stage);
            noteLayer = Layer("Notes", stage);
            safeAreaRoot = Layer("Safe Area UI", root);
            BuildHud(safeAreaRoot, root);
            BuildMenu(safeAreaRoot);
            BuildPauseOverlay(safeAreaRoot);
            BuildResult(safeAreaRoot);
            UpdateSafeAreaLayout(true);
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
            FitOverlayPanel(menuPanel, new Vector2(760, 570), logicalSafeSize);
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
            menuPanel = Panel("Library Menu", root, new Color(.04f, .06f, .14f, .94f), new Vector2(760, 570), Vector2.zero); Outline(menuPanel.gameObject, new Color(.4f, .8f, 1f, .75f), 3);
            var title = Label("GUGARYTHM  LOCAL PLAYER", menuPanel, 36); title.rectTransform.sizeDelta = new Vector2(700, 70); title.rectTransform.anchoredPosition = new Vector2(0, 220);
            loadStatus = Label("準備中…", menuPanel, 20); loadStatus.rectTransform.sizeDelta = new Vector2(680, 80); loadStatus.rectTransform.anchoredPosition = new Vector2(0, 145);
            speedLabel = Label("流速  8.0  ·  按鈕／← → 每次 0.1", menuPanel, 23); speedLabel.rectTransform.sizeDelta = new Vector2(600, 44); speedLabel.rectTransform.anchoredPosition = new Vector2(0, 82);
            speedLabel.text = $"流速  {scrollSpeed:F1}  ·  按鈕／← → 每次 0.1";
            speedSlider = MakeSlider(menuPanel, new Vector2(0, 42), 1, 20, scrollSpeed, value =>
            {
                SetScrollSpeed(value);
            });
            MakeButton("−", menuPanel, new Vector2(-320, 42), () => AdjustScrollSpeed(-.1f), new Vector2(88, 58));
            MakeButton("＋", menuPanel, new Vector2(320, 42), () => AdjustScrollSpeed(.1f), new Vector2(88, 58));
            startButton = MakeButton("開始遊玩", menuPanel, new Vector2(0, -62), StartGame); startButton.interactable = false;
            MakeButton("匯入 ZIP／SCP", menuPanel, new Vector2(0, -168), RequestImport);
        }

        void BuildResult(RectTransform root)
        {
            resultPanel = Panel("Result", root, new Color(.04f, .06f, .14f, .96f), new Vector2(620, 650), Vector2.zero); Outline(resultPanel.gameObject, new Color(.9f, .5f, 1f, .75f), 3);
            var title = Label("RESULT", resultPanel, 38); title.rectTransform.sizeDelta = new Vector2(580, 70); title.rectTransform.anchoredPosition = new Vector2(0, 260);
            resultText = Label("", resultPanel, 27); resultText.rectTransform.sizeDelta = new Vector2(540, 440); resultText.rectTransform.anchoredPosition = new Vector2(0, 25);
            MakeButton("返回曲庫", resultPanel, new Vector2(0, -270), () => { resultPanel.gameObject.SetActive(false); menuPanel.gameObject.SetActive(true); });
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
            var path = UnityEditor.EditorUtility.OpenFilePanel("匯入 ZIP／SCP", "", "zip,scp");
            if (!string.IsNullOrEmpty(path)) StartCoroutine(ImportPath(path));
#elif UNITY_ANDROID
            NativeChartPicker.OpenFile();
            SetStatus("請在系統檔案選擇器選取 ZIP 或 SCP…");
#else
            SetStatus("目前請將譜面放入 StreamingAssets，或使用 Android 匯入。");
#endif
        }

        void RequestImportFolder()
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFolderPanel("匯入譜面資料夾", "", "");
            if (!string.IsNullOrEmpty(path)) StartCoroutine(ImportPath(path));
#elif UNITY_ANDROID
            NativeChartPicker.OpenFolder();
            SetStatus("請在系統檔案選擇器選取譜面資料夾…");
#else
            SetStatus("目前請使用 ZIP 匯入譜面與伴隨檔案。");
#endif
        }

        IEnumerator ImportPath(string path)
        {
            if (path.StartsWith("ERROR:", StringComparison.Ordinal)) { SetStatus("匯入失敗：" + path[6..]); yield break; }
            if (!File.Exists(path) && !Directory.Exists(path)) { SetStatus("匯入檔案不存在。"); yield break; }
            if (File.Exists(path))
            {
                var extension = Path.GetExtension(path);
                if (!extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".scp", StringComparison.OrdinalIgnoreCase))
                {
                    SetStatus("請選擇內含 USC 與 BGM 的 ZIP，或 SCP 檔案。");
                    yield break;
                }
            }
            byte[] bytes;
            IReadOnlyDictionary<string, byte[]> companions = null;
            if (Directory.Exists(path))
            {
                var package = ChartPackageReader.ReadFolder(path);
                path = package.ChartName; bytes = package.ChartBytes; companions = package.Files;
            }
            else
            {
                bytes = File.ReadAllBytes(path);
                if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    var package = ChartPackageReader.ReadZip(bytes);
                    if (package.ChartName != null) { path = package.ChartName; bytes = package.ChartBytes; companions = package.Files; }
                }
            }
            yield return ImportBytes(Path.GetFileName(path), bytes, companions);
        }

        void PollNativeImport()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var path = NativeChartPicker.ConsumeResult();
            if (!string.IsNullOrEmpty(path) && !loading) StartCoroutine(ImportPath(path));
#endif
        }

        void SaveToLocalLibrary(string fileName, byte[] bytes, RuntimeChart importedChart)
        {
            try { LocalChartLibrary.Save(fileName, bytes, importedChart); }
            catch (Exception exception) { importedChart.Warnings.Add("本機曲庫保存失敗：" + exception.Message); }
        }

        HorizontalSlicedRawImage AcquireNoteView()
        {
            if (notePool.Count > 0)
            {
                var pooled = notePool.Pop();
                pooled.gameObject.SetActive(true);
                pooled.transform.SetAsLastSibling();
                return pooled;
            }
            var go = new GameObject("Runtime Note", typeof(RectTransform), typeof(CanvasRenderer), typeof(HorizontalSlicedRawImage));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(noteLayer, false); rect.sizeDelta = new Vector2(100, 30);
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
                particle.gameObject.SetActive(trace && !holdMid && particle.texture != null);
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
                var pooled = guidePool.Pop(); pooled.gameObject.SetActive(true); ConfigureGuideGraphic(pooled); return pooled;
            }
            var go = new GameObject("Decoration Guide", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(guideLayer, false); Fill(rect);
            var graphic = go.GetComponent<TaperedConnectorGraphic>(); graphic.raycastTarget = false; ConfigureGuideGraphic(graphic); return graphic;
        }
        static void ConfigureHoldGraphic(TaperedConnectorGraphic graphic)
        {
            // Color, edge softness, and alpha are baked into the official
            // 306x4 seamless Hold texture. Drawing extra procedural bands was
            // what made overlapping Holds turn cloudy and merge with Guides.
            graphic.drawGlow = false; graphic.drawEdges = false;
            graphic.fillAlphaScale = 1; graphic.fillAlphaLimit = 1;
        }
        static void ConfigureGuideGraphic(TaperedConnectorGraphic graphic)
        {
            // Guides are a single flat-color pass, with no Hold-style glow or
            // bright edge. Their reference-video opacity is applied by color.
            graphic.texture = null;
            graphic.drawGlow = false; graphic.drawEdges = false;
            graphic.fillAlphaScale = 1; graphic.fillAlphaLimit = 1;
        }
        void ReleaseGuide(RuntimeGuide guide, TaperedConnectorGraphic line) { guideViews.Remove(guide); line.gameObject.SetActive(false); guidePool.Push(line); }
        void ReleaseAllViews() { foreach (var pair in persistentHoldHeadViews.ToArray()) ReleasePersistentHoldHead(pair.Key, pair.Value); foreach (var pair in noteViews.ToArray()) ReleaseNoteView(pair.Key, pair.Value); foreach (var pair in connectorViews.ToArray()) ReleaseConnector(pair.Key, pair.Value); foreach (var pair in simLineViews.ToArray()) ReleaseSimLine(pair.Key, pair.Value); foreach (var pair in guideViews.ToArray()) ReleaseGuide(pair.Key, pair.Value); }

        void SpawnHitParticle(float x, string color)
        {
            var tint = color switch
            {
                "yellow" => new Color(1f, .82f, .12f, .9f),
                "pink" => new Color(1f, .2f, .67f, .86f),
                "green" => new Color(.12f, 1f, .58f, .84f),
                _ => new Color(.28f, .82f, 1f, .84f),
            };
            var particleRoot = new GameObject("Pixel Judgment Burst", typeof(RectTransform)).GetComponent<RectTransform>();
            particleRoot.SetParent(stage, false); particleRoot.sizeDelta = new Vector2(240, 240); particleRoot.anchoredPosition = new Vector2(x, HitY);
            particleRoot.SetAsLastSibling();
            RawImage core = null;
            RawImage ring = null;
            if (pixelParticleAtlas != null)
            {
                var sprite = color switch { "yellow" => 4, "pink" => 5, "green" => 2, _ => 3 };
                core = RawPanel("Pixel Core", particleRoot, pixelParticleAtlas, Color.white, new Vector2(52, 52), Vector2.zero).GetComponent<RawImage>();
                ring = RawPanel("Pixel Ring", particleRoot, pixelParticleAtlas, Color.white, new Vector2(62, 62), Vector2.zero).GetComponent<RawImage>();
                core.uvRect = PixelParticleUv(sprite);
                ring.uvRect = PixelParticleUv(sprite + 7);
                core.raycastTarget = false;
                ring.raycastTarget = false;
            }
            StartCoroutine(AnimateHitEffect(particleRoot, core, ring, tint));
        }

        static Rect PixelParticleUv(int sprite)
        {
            var position = sprite switch
            {
                0 => new Vector2(391, 261), 1 => new Vector2(1, 1), 2 => new Vector2(131, 1),
                3 => new Vector2(261, 261), 4 => new Vector2(1, 521), 5 => new Vector2(651, 1),
                6 => new Vector2(521, 131), 7 => new Vector2(1, 261), 8 => new Vector2(261, 391),
                9 => new Vector2(131, 521), 10 => new Vector2(1, 651), 11 => new Vector2(1, 131),
                12 => new Vector2(261, 1), 13 => new Vector2(131, 131), _ => Vector2.zero,
            };
            const float atlasSize = 1024f;
            const float spriteSize = 128f;
            return new Rect(position.x / atlasSize, (atlasSize - position.y - spriteSize) / atlasSize,
                spriteSize / atlasSize, spriteSize / atlasSize);
        }

        IEnumerator AnimateHitEffect(RectTransform particleRoot, RawImage core, RawImage ring, Color tint)
        {
            const float Duration = .38f;
            for (var elapsed = 0f; elapsed < Duration; elapsed += Time.unscaledDeltaTime)
            {
                var t = elapsed / Duration;
                var fade = 1 - Mathf.SmoothStep(0, 1, t);
                if (core != null)
                {
                    core.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(48, 108, t);
                    core.color = new Color(1, 1, 1, fade);
                    ring.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(56, 196, Mathf.SmoothStep(0, 1, t));
                    ring.color = new Color(1, 1, 1, fade * .9f);
                }
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
            text.text = content; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.fontStyle = FontStyle.Bold; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white;
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
