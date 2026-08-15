using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        // Mapping measured directly from the original 1280x732 lane artwork.
        // CanvasScaler matches width, so Free Aspect/editor windows can be taller
        // than 1080 logical units. Derive Y from the live viewport instead of
        // assuming 16:9; this keeps note edges on the gray texture guides.
        const float ReferenceWidth = 1920f;
        const float LaneTextureWidth = 1280f;
        const float LaneTextureHeight = 732f;
        const float HitSourceY = 502f;
        const float CentralHalfLanes = 6f;
        const float PerspectiveDepthRatio = 3.2f;
        // Curves are sampled on fixed chart-time boundaries. A denser grid
        // keeps curved ribbons smooth, while stable boundaries prevent the
        // entire tessellation from shifting whenever the visible end is clipped.
        const int ConnectorPathSegments = 128;
        // The texture contains seven actual gray guides at chart lanes
        // -6,-4,-2,0,2,4,6. These straight-line fits are in source-image pixels
        // (x = intercept + slope * y); the six lanes between them are interpolated.
        static readonly float[] LaneGuideIntercepts = { 613.9411f, 623.2157f, 631.3852f, 639.0410f, 647.6391f, 655.7550f, 665.0466f };
        static readonly float[] LaneGuideSlopes = { -.8341789f, -.5564627f, -.2783288f, -.0000775f, .2781847f, .5565205f, .8342110f };
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
        const int MouseContactId = int.MinValue;

        readonly Dictionary<string, Texture2D> buttonTextures = new(StringComparer.Ordinal);
        readonly Dictionary<string, Texture2D> traceTextures = new(StringComparer.Ordinal);
        readonly List<Texture2D> particleTextures = new();
        readonly Dictionary<int, HorizontalSlicedRawImage> noteViews = new();
        readonly Dictionary<RuntimeConnector, TaperedConnectorGraphic> connectorViews = new();
        readonly Dictionary<RuntimeSimLine, SimLineGraphic> simLineViews = new();
        readonly Dictionary<RuntimeGuide, TaperedConnectorGraphic> guideViews = new();
        readonly Stack<HorizontalSlicedRawImage> notePool = new();
        readonly Stack<TaperedConnectorGraphic> connectorPool = new();
        readonly Stack<SimLineGraphic> simLinePool = new();
        readonly Stack<TaperedConnectorGraphic> guidePool = new();
        readonly Dictionary<int, TouchMemory> touches = new();
        readonly List<InputToken> inputBatch = new();
        readonly List<ActiveContact> contacts = new();
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
        Texture2D holdGreenConnectorTexture;
        Texture2D holdYellowConnectorTexture;
        Texture2D holdMidMintTexture;
        Texture2D holdMidYellowTexture;
        Texture2D traceDiamondMintTexture;
        Texture2D traceDiamondPinkTexture;
        Texture2D traceDiamondYellowTexture;
        RuntimeChart chart;
        JudgmentEngine judgmentEngine;
        AudioSource music;
        RectTransform stage;
        RectTransform guideLayer;
        RectTransform connectorLayer;
        RectTransform simLineLayer;
        RectTransform noteLayer;
        RectTransform menuPanel;
        RectTransform resultPanel;
        Text accuracyLabel;
        Text comboLabel;
        Text judgmentLabel;
        Text loadStatus;
        Text resultText;
        Text speedLabel;
        Button startButton;
        Slider speedSlider;
        Material laneMaterial;
        bool running;
        bool loading;
        bool paused;
        double scheduledDsp;
        double pauseDsp;
        double accumulatedPause;
        double inputOffsetSeconds;
        double visualOffsetSeconds;
        float scrollSpeed = 8f;

        static float CanvasHeight => ReferenceWidth * Screen.height / Math.Max(1, Screen.width);
        static float TopY => CanvasHeight * .5f;
        static float HitY => TopY - HitSourceY / LaneTextureHeight * CanvasHeight;

        // Speed controls the time spent approaching the judgment edge. Screen Y,
        // lane position, and width are all derived from the same perspective
        // projection below, so distant notes converge at the vanishing point.
        float ApproachDuration => (TopY - HitY) / (210f + scrollSpeed * 52f);

        void Awake()
        {
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
            UpdateDesktopSpeedControls();
            if (!running || paused || chart == null || judgmentEngine == null) return;
            var songTime = CurrentSongTime();
            CollectInput();
            var events = judgmentEngine.Process(songTime, inputBatch, contacts);
            foreach (var judgment in events) OnJudgment(judgment);
            UpdateVisuals(songTime + visualOffsetSeconds);
            RefreshHud();
            if (songTime > chart.LastNoteTime + .75 && chart.Notes.All(note => note.Grade != JudgmentGrade.Pending)) FinishGame();
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
            var type = extension?.ToLowerInvariant() switch { ".ogg" => AudioType.OGGVORBIS, ".wav" => AudioType.WAV, _ => AudioType.MPEG };
            using var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type);
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { SetStatus("音樂解碼失敗：" + request.error); yield break; }
            music.clip = DownloadHandlerAudioClip.GetContent(request);
        }

        void StartGame()
        {
            if (loading || chart == null || music.clip == null) return;
            ResetRuntime();
            menuPanel.gameObject.SetActive(false);
            resultPanel.gameObject.SetActive(false);
            running = true;
            paused = false;
            accumulatedPause = 0;
            scheduledDsp = AudioSettings.dspTime + .25;
            music.time = 0;
            music.PlayScheduled(scheduledDsp);
            ShowJudgment("READY", Color.white);
        }

        void ResetRuntime()
        {
            foreach (var note in chart.Notes) note.Grade = JudgmentGrade.Pending;
            scoreState.Reset();
            judgmentEngine = new JudgmentEngine(chart.Notes, scoreState);
            touches.Clear();
            ReleaseAllViews();
            RefreshHud();
        }

        double CurrentSongTime() => AudioSettings.dspTime - scheduledDsp - accumulatedPause - chart.BgmOffset;

        void CollectInput()
        {
            inputBatch.Clear();
            contacts.Clear();
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
                        inputBatch.Add(new InputToken(id, RuntimeNoteKind.Tap, eventTime - inputOffsetSeconds, lane));
                    else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved && Vector2.SqrMagnitude(touch.screenPosition - memory.ScreenPosition) > .01f)
                        inputBatch.Add(new InputToken(id, RuntimeNoteKind.Flick, eventTime - inputOffsetSeconds, lane, memory.Lane, memory.EventTime - inputOffsetSeconds));
                    else if (touch.phase is UnityEngine.InputSystem.TouchPhase.Ended or UnityEngine.InputSystem.TouchPhase.Canceled)
                        inputBatch.Add(new InputToken(id, RuntimeNoteKind.Release, eventTime - inputOffsetSeconds, lane));
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
            foreach (var id in touches.Keys.Where(id => !seen.Contains(id)).ToArray()) touches.Remove(id);
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
            // The legacy mouse backend remains available in Unity Editor even
            // when the active Android target exposes no InputSystem Mouse
            // device. Convert its button state into the same touch records used
            // by mobile input.
            var pressed = UnityEngine.Input.GetMouseButton(0);
            var beganThisFrame = UnityEngine.Input.GetMouseButtonDown(0);
            var endedThisFrame = UnityEngine.Input.GetMouseButtonUp(0);
            if (!pressed && !beganThisFrame && !endedThisFrame) return;

            var position = (Vector2)UnityEngine.Input.mousePosition;
            var lane = ScreenToLane(position);
            var eventTime = CurrentSongTime();
            var began = !touches.TryGetValue(MouseContactId, out var memory);
            if (endedThisFrame && !began)
            {
                inputBatch.Add(new InputToken(MouseContactId, RuntimeNoteKind.Release,
                    eventTime - inputOffsetSeconds, lane));
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
                inputBatch.Add(new InputToken(MouseContactId, RuntimeNoteKind.Tap, eventTime - inputOffsetSeconds, lane));
            }
            else if (Vector2.SqrMagnitude(position - memory.ScreenPosition) > .01f)
            {
                inputBatch.Add(new InputToken(MouseContactId, RuntimeNoteKind.Flick, eventTime - inputOffsetSeconds,
                    lane, memory.Lane, memory.EventTime - inputOffsetSeconds));
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
            if (noteViews.TryGetValue(judgment.Note.Index, out var view)) ReleaseNoteView(judgment.Note.Index, view);
            var color = judgment.Grade switch
            {
                JudgmentGrade.Perfect => new Color(.65f, 1f, 1f),
                JudgmentGrade.Great => new Color(1f, .84f, .38f),
                JudgmentGrade.Good => new Color(.52f, 1f, .66f),
                _ => new Color(1f, .34f, .55f),
            };
            var timing = judgment.Grade == JudgmentGrade.Miss || Math.Abs(judgment.Delta) < .01 ? "" : judgment.Delta < 0 ? "  EARLY" : "  LATE";
            ShowJudgment(judgment.Grade.ToString().ToUpperInvariant() + timing, color);
            if (judgment.Grade != JudgmentGrade.Miss)
                SpawnHitParticle(X(judgment.Note.Lane, 1f), judgment.Note.Critical ? "yellow" : IsTrace(judgment.Note) || judgment.Note.Kind == RuntimeNoteKind.Sustain ? "green" : judgment.Note.Kind == RuntimeNoteKind.Flick ? "pink" : "blue");
        }

        void UpdateVisuals(double visualTime)
        {
            foreach (var guide in chart.Guides)
            {
                var headApproach = ApproachProgress(guide.Head.Time, visualTime);
                var tailApproach = ApproachProgress(guide.Tail.Time, visualTime);
                var show = ScreenY(PerspectiveProgress(tailApproach)) >= HitY - 90 &&
                    ScreenY(PerspectiveProgress(headApproach)) <= TopY + 8;
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
                SetGuidePath(guideLine, guide, headApproach, tailApproach);
            }

            foreach (var simLine in chart.SimLines)
            {
                var aApproach = ApproachProgress(simLine.A.Time, visualTime);
                var bApproach = ApproachProgress(simLine.B.Time, visualTime);
                var aScreen = PerspectiveProgress(aApproach);
                var bScreen = PerspectiveProgress(bApproach);
                var aY = ScreenY(aScreen);
                var bY = ScreenY(bScreen);
                var visible = Mathf.Max(aY, bY) >= HitY - 90 && Mathf.Min(aY, bY) <= TopY + 8;
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
                var depth = Mathf.Clamp01((aScreen + bScreen) * .5f);
                line.SetGeometry(
                    new Vector2(X(simLine.A.Lane, aScreen), aY),
                    new Vector2(X(simLine.B.Lane, bScreen), bY),
                    Mathf.Lerp(.65f, 2.25f, depth));
            }

            foreach (var note in chart.Notes)
            {
                var approachProgress = ApproachProgress(note.Time, visualTime);
                var screenProgress = PerspectiveProgress(approachProgress);
                var y = ScreenY(screenProgress);
                var visible = note.Grade == JudgmentGrade.Pending && y <= TopY + 8 && y >= HitY - 90;
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
                var width = LaneWidth(note.Lane, note.Size, screenProgress);
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
                var traceParticle = view.transform.Find("Trace Particle")?.GetComponent<RawImage>();
                if (traceParticle != null)
                {
                    // Both official tick layouts use the same square as the
                    // note's depth-scaled height. Their textures distinguish
                    // the larger SlideTick from the smaller Trace diamond.
                    var particleAspect = traceParticle.texture == null ? 1f :
                        traceParticle.texture.width / (float)Mathf.Max(1, traceParticle.texture.height);
                    traceParticle.rectTransform.sizeDelta = new Vector2(height * particleAspect, height);
                }
                var flickArrow = view.transform.Find("Flick Arrow")?.GetComponent<RawImage>();
                if (flickArrow != null && flickArrow.gameObject.activeSelf && flickArrow.texture != null)
                {
                    var spriteIndex = FlickSpriteIndex(note.Size);
                    var arrowBaseWidth = LaneWidth(note.Lane, Mathf.Min(note.Size, 3f) * .5f, screenProgress);
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
                var startApproach = ApproachProgress(connector.Start.Time, visualTime);
                var endApproach = ApproachProgress(connector.End.Time, visualTime);
                var startScreen = PerspectiveProgress(startApproach);
                var endScreen = PerspectiveProgress(endApproach);
                var show = ScreenY(endScreen) >= HitY - 90 && ScreenY(startScreen) <= TopY + 8;
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
                SetConnectorPath(line, connector, startApproach, endApproach);
            }
        }

        void SetGuidePath(TaperedConnectorGraphic line, RuntimeGuide guide, float headApproach, float tailApproach)
        {
            var approachSpan = headApproach - tailApproach;
            var nearT = approachSpan <= 1e-5f ? 0 : Mathf.Clamp01((headApproach - 1f) / approachSpan);
            var farT = approachSpan <= 1e-5f ? 1 : Mathf.Clamp01(headApproach / approachSpan);
            var sampleCount = BuildStablePathSamples(nearT, farT);
            line.BeginPath(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var t = connectorPathSamples[index];
                var lane = InterpolateGuide(guide, t, point => point.Lane);
                var size = Mathf.Max(.01f, InterpolateGuide(guide, t, point => point.Size));
                var approach = Mathf.Lerp(headApproach, tailApproach, t);
                var screenProgress = Mathf.Clamp01(PerspectiveProgress(approach));
                var alpha = Mathf.Lerp(guide.HeadOpacity, guide.TailOpacity, t);
                line.SetPathPoint(index, new Vector2(X(lane, screenProgress), ScreenY(screenProgress)), LaneWidth(lane, size, screenProgress), alpha);
            }
            line.EndPath();
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
            1 => new Color(214 / 255f, 115 / 255f, 205 / 255f, .32f),
            _ => new Color(115 / 255f, 214 / 255f, 157 / 255f, .32f),
        };

        void SetConnectorPath(TaperedConnectorGraphic line, RuntimeConnector connector, float startApproach, float endApproach)
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

            // Clip in approach space, then interpolate the lane at the clipped
            // time. This keeps an active hold attached to the judgment edge
            // instead of leaving it at the already-passed start lane.
            var nearT = Mathf.Clamp01((startApproach - 1f) / approachSpan);
            var farT = Mathf.Clamp01(startApproach / approachSpan);
            var sampleCount = BuildStablePathSamples(nearT, farT);
            line.BeginPath(sampleCount);
            for (var index = 0; index < sampleCount; index++)
            {
                var t = connectorPathSamples[index];
                SetConnectorPoint(line, index, connector, t, Mathf.Lerp(startApproach, endApproach, t));
            }
            line.EndPath();
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
            var screenProgress = Mathf.Clamp01(PerspectiveProgress(approachProgress));
            // The official connector sprite expands its target quad by 1.275,
            // while its baked transparent shoulders bring the visible fill
            // back to the note's exact left/right edges.
            line.SetPathPoint(index, new Vector2(X(lane, screenProgress), ScreenY(screenProgress)), LaneWidth(lane, size, screenProgress) * 1.275f);
        }

        static float EaseConnector(float progress, int ease) => ease switch
        {
            1 => 1f - Mathf.Cos(progress * Mathf.PI * .5f),
            2 => Mathf.Sin(progress * Mathf.PI * .5f),
            _ => progress,
        };

        // approach=0 is the far spawn plane; approach=1 is the judgment edge.
        float ApproachProgress(double noteTime, double visualTime) =>
            1f - (float)((noteTime - visualTime) / ApproachDuration);

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
        static float X(float lane, float screenProgress)
        {
            var sourceY = (TopY - ScreenY(screenProgress)) * LaneTextureHeight / CanvasHeight;
            var guide = Mathf.Clamp(Mathf.FloorToInt((lane + CentralHalfLanes) * .5f), 0, LaneGuideIntercepts.Length - 2);
            var guideLane = -CentralHalfLanes + guide * 2f;
            var t = (lane - guideLane) * .5f;
            var left = LaneGuideIntercepts[guide] + LaneGuideSlopes[guide] * sourceY;
            var right = LaneGuideIntercepts[guide + 1] + LaneGuideSlopes[guide + 1] * sourceY;
            var sourceX = Mathf.LerpUnclamped(left, right, t);
            return (sourceX / LaneTextureWidth - .5f) * ReferenceWidth;
        }

        static float LaneWidth(float lane, float size, float screenProgress) =>
            Mathf.Max(12, X(lane + size, screenProgress) - X(lane - size, screenProgress));

        static float ScreenXToLane(float canvasX, float screenProgress)
        {
            if (canvasX <= X(-CentralHalfLanes, screenProgress))
            {
                var left = X(-CentralHalfLanes, screenProgress);
                var span = X(-CentralHalfLanes + 2, screenProgress) - left;
                return -CentralHalfLanes + (canvasX - left) / span * 2f;
            }
            for (var guide = 0; guide < LaneGuideIntercepts.Length - 1; guide++)
            {
                var leftLane = -CentralHalfLanes + guide * 2f;
                var left = X(leftLane, screenProgress);
                var right = X(leftLane + 2, screenProgress);
                if (canvasX <= right) return leftLane + (canvasX - left) / (right - left) * 2f;
            }
            var finalLeft = X(CentralHalfLanes - 2, screenProgress);
            var finalSpan = X(CentralHalfLanes, screenProgress) - finalLeft;
            return CentralHalfLanes - 2 + (canvasX - finalLeft) / finalSpan * 2f;
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
            running = false;
            music.Stop();
            ReleaseAllViews();
            resultPanel.gameObject.SetActive(true);
            resultText.text = $"ACCURACY  {scoreState.AccuracyPercent(chart.PlayableCount):F4}%\n\nMAX COMBO  {scoreState.MaxCombo:N0}\n\nPERFECT  {scoreState.Perfect:N0}\nGREAT  {scoreState.Great:N0}\nGOOD  {scoreState.Good:N0}\nMISS  {scoreState.Miss:N0}";
        }

        void LoadArtwork()
        {
            backgroundTexture = Resources.Load<Texture2D>("NeonRhythm/background/neon-city-background");
            laneTexture = Resources.Load<Texture2D>("NeonRhythm/lane/neon-rhythm-lane");
            foreach (var name in new[] { "purple", "cyan", "mint", "white", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("NeonRhythm/official/buttons/button-" + name) ??
                    Resources.Load<Texture2D>("NeonRhythm/buttons/button-" + name);
                if (texture != null) buttonTextures[name] = texture;
            }
            foreach (var name in new[] { "mint", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("NeonRhythm/official/traces/trace-" + name) ??
                    Resources.Load<Texture2D>("NeonRhythm/traces/trace-" + name);
                if (texture != null) traceTextures[name] = texture;
            }
            foreach (var name in new[] { "blue", "green", "pink", "yellow" })
            {
                var texture = Resources.Load<Texture2D>("NeonRhythm/particles/spark-" + name);
                if (texture != null) particleTextures.Add(texture);
            }
            damageTexture = Resources.Load<Texture2D>("NeonRhythm/official/damage/damage-purple") ??
                Resources.Load<Texture2D>("NeonRhythm/damage/damage-purple");
            for (var index = 0; index < 6; index++)
            {
                var suffix = (index + 1).ToString();
                flickNormalCenterTextures[index] = Resources.Load<Texture2D>("NeonRhythm/flicks/flick-normal-center-" + suffix);
                flickNormalSideTextures[index] = Resources.Load<Texture2D>("NeonRhythm/flicks/flick-normal-side-" + suffix);
                flickCriticalCenterTextures[index] = Resources.Load<Texture2D>("NeonRhythm/flicks/flick-critical-center-" + suffix);
                flickCriticalSideTextures[index] = Resources.Load<Texture2D>("NeonRhythm/flicks/flick-critical-side-" + suffix);
            }
            holdGreenConnectorTexture = Resources.Load<Texture2D>("NeonRhythm/connectors/hold-green");
            holdYellowConnectorTexture = Resources.Load<Texture2D>("NeonRhythm/connectors/hold-yellow");
            holdMidMintTexture = Resources.Load<Texture2D>("NeonRhythm/official/particles/slide-tick-mint") ??
                Resources.Load<Texture2D>("NeonRhythm/particles/hold-mid-mint");
            holdMidYellowTexture = Resources.Load<Texture2D>("NeonRhythm/official/particles/slide-tick-yellow") ??
                Resources.Load<Texture2D>("NeonRhythm/particles/hold-mid-yellow");
            traceDiamondMintTexture = Resources.Load<Texture2D>("NeonRhythm/official/particles/trace-diamond-mint");
            traceDiamondPinkTexture = Resources.Load<Texture2D>("NeonRhythm/official/particles/trace-diamond-pink");
            traceDiamondYellowTexture = Resources.Load<Texture2D>("NeonRhythm/official/particles/trace-diamond-yellow");
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
            var canvasObject = new GameObject("Rhythm Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var eventSystemObject = new GameObject("Event System", typeof(EventSystem));
#if UNITY_EDITOR || UNITY_STANDALONE
            // Desktop UI must not depend on InputSystem Mouse.current: it may be
            // absent entirely when the active editor build target is Android.
            eventSystemObject.AddComponent<StandaloneInputModule>();
#else
            // A module created entirely at runtime has no UI action asset until
            // defaults are assigned explicitly. Keep the unified Input System so
            // mouse, pen and touchscreen all drive the same controls.
            var inputModule = eventSystemObject.AddComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
#endif
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920, 1080);
            var root = canvasObject.GetComponent<RectTransform>();
            Panel("Base", root, new Color(.015f, .02f, .06f), Vector2.zero, Vector2.zero, true);
            RawPanel("Background", root, backgroundTexture, new Color(1, 1, 1, .72f), Vector2.zero, Vector2.zero, true);
            stage = Panel("Rhythm Stage", root, new Color(0, 0, 0, .05f), Vector2.zero, Vector2.zero, true);
            var trackObject = new GameObject("Track Depth", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var trackRect = trackObject.GetComponent<RectTransform>(); trackRect.SetParent(stage, false); Fill(trackRect);
            var trackGraphic = trackObject.GetComponent<TaperedConnectorGraphic>(); trackGraphic.raycastTarget = false; trackGraphic.color = new Color(0, 0, .035f, .72f);
            trackGraphic.SetGeometry(
                new Vector2((X(-CentralHalfLanes, 0) + X(CentralHalfLanes, 0)) * .5f, TopY),
                new Vector2((X(-CentralHalfLanes, 1) + X(CentralHalfLanes, 1)) * .5f, HitY),
                X(CentralHalfLanes, 0) - X(-CentralHalfLanes, 0),
                X(CentralHalfLanes, 1) - X(-CentralHalfLanes, 1));
            var lane = RawPanel("Perspective Lane", stage, laneTexture, new Color(1, 1, 1, .92f), Vector2.zero, Vector2.zero, true);
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
            BuildHud(root);
            BuildMenu(root);
            BuildResult(root);
        }

        void BuildHud(RectTransform root)
        {
            var accuracy = Panel("Accuracy", root, new Color(.04f, .08f, .20f, .72f), new Vector2(280, 72), new Vector2(-790, 480)); Outline(accuracy.gameObject, new Color(.55f, .75f, 1f, .75f), 2);
            accuracyLabel = Label("ACCURACY  0.0000%", accuracy, 22); Fill(accuracyLabel.rectTransform);
            comboLabel = Label("COMBO\n0", root, 38); comboLabel.rectTransform.sizeDelta = new Vector2(300, 130); comboLabel.rectTransform.anchoredPosition = new Vector2(650, 125);
            judgmentLabel = Label("", root, 48); judgmentLabel.rectTransform.sizeDelta = new Vector2(620, 80); judgmentLabel.rectTransform.anchoredPosition = new Vector2(0, -30);
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
            MakeButton("匯入檔案／ZIP", menuPanel, new Vector2(-175, -168), RequestImport);
            MakeButton("匯入資料夾", menuPanel, new Vector2(175, -168), RequestImportFolder);
        }

        void BuildResult(RectTransform root)
        {
            resultPanel = Panel("Result", root, new Color(.04f, .06f, .14f, .96f), new Vector2(620, 650), Vector2.zero); Outline(resultPanel.gameObject, new Color(.9f, .5f, 1f, .75f), 3);
            var title = Label("RESULT", resultPanel, 38); title.rectTransform.sizeDelta = new Vector2(580, 70); title.rectTransform.anchoredPosition = new Vector2(0, 260);
            resultText = Label("", resultPanel, 27); resultText.rectTransform.sizeDelta = new Vector2(540, 440); resultText.rectTransform.anchoredPosition = new Vector2(0, 25);
            MakeButton("返回曲庫", resultPanel, new Vector2(0, -270), () => { resultPanel.gameObject.SetActive(false); menuPanel.gameObject.SetActive(true); });
            resultPanel.gameObject.SetActive(false);
        }

        void RequestImport()
        {
#if UNITY_EDITOR
            var path = UnityEditor.EditorUtility.OpenFilePanel("匯入譜面", "", "scp,sus,usc,json,gz,zip");
            if (!string.IsNullOrEmpty(path)) StartCoroutine(ImportPath(path));
#elif UNITY_ANDROID
            NativeChartPicker.OpenFile();
            SetStatus("請在系統檔案選擇器選取譜面或 ZIP…");
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
                particle.gameObject.SetActive((trace || holdMid) && particle.texture != null);
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
        void ReleaseAllViews() { foreach (var pair in noteViews.ToArray()) ReleaseNoteView(pair.Key, pair.Value); foreach (var pair in connectorViews.ToArray()) ReleaseConnector(pair.Key, pair.Value); foreach (var pair in simLineViews.ToArray()) ReleaseSimLine(pair.Key, pair.Value); foreach (var pair in guideViews.ToArray()) ReleaseGuide(pair.Key, pair.Value); }

        void SpawnHitParticle(float x, string color)
        {
            var tint = color switch
            {
                "yellow" => new Color(1f, .82f, .12f, .9f),
                "pink" => new Color(1f, .2f, .67f, .86f),
                "green" => new Color(.12f, 1f, .58f, .84f),
                _ => new Color(.28f, .82f, 1f, .84f),
            };
            var texture = color switch
            {
                "yellow" => holdMidYellowTexture,
                "pink" => traceDiamondPinkTexture,
                _ => holdMidMintTexture,
            } ?? particleTextures.FirstOrDefault();

            var flashObject = new GameObject("Judgment Lane Flash", typeof(RectTransform), typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
            var flashRect = flashObject.GetComponent<RectTransform>(); flashRect.SetParent(stage, false); Fill(flashRect);
            flashRect.SetSiblingIndex(Mathf.Max(0, noteLayer.GetSiblingIndex()));
            var flash = flashObject.GetComponent<TaperedConnectorGraphic>(); flash.raycastTarget = false;
            flash.drawGlow = false; flash.drawEdges = false; flash.fillAlphaScale = 1; flash.fillAlphaLimit = 1;
            var hitWidth = LaneWidth(0, 1f, 1f);
            flash.BeginPath(2);
            flash.SetPathPoint(0, new Vector2(x, HitY + 2), hitWidth * 1.08f, 1);
            flash.SetPathPoint(1, new Vector2(x, -TopY - 20), hitWidth * 1.85f, .34f);
            flash.EndPath();

            var burstObject = new GameObject("Judgment Diamond Burst", typeof(RectTransform), typeof(CanvasRenderer), typeof(HitBurstGraphic));
            var burstRect = burstObject.GetComponent<RectTransform>(); burstRect.SetParent(stage, false); burstRect.sizeDelta = new Vector2(520, 330); burstRect.anchoredPosition = new Vector2(x, HitY);
            var burst = burstObject.GetComponent<HitBurstGraphic>(); burst.raycastTarget = false; burst.color = tint;
            RawImage particle = null;
            if (texture != null)
            {
                particle = RawPanel("Judgment Core", burstRect, texture, Color.white, new Vector2(58, 58), Vector2.zero).GetComponent<RawImage>();
                particle.raycastTarget = false;
            }
            StartCoroutine(AnimateHitEffect(burst, flash, particle, tint));
        }

        IEnumerator AnimateHitEffect(HitBurstGraphic burst, TaperedConnectorGraphic flash, RawImage particle, Color tint)
        {
            const float Duration = .42f;
            for (var elapsed = 0f; elapsed < Duration; elapsed += Time.unscaledDeltaTime)
            {
                var t = elapsed / Duration;
                burst.SetProgress(t);
                var fade = 1 - Mathf.SmoothStep(0, 1, t);
                flash.color = new Color(tint.r, tint.g, tint.b, .2f * fade);
                if (particle != null)
                {
                    particle.rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(44, 92, t);
                    particle.color = new Color(1, 1, 1, fade);
                }
                yield return null;
            }
            Destroy(flash.gameObject);
            Destroy(burst.gameObject);
        }

        void RefreshHud() { accuracyLabel.text = $"ACCURACY  {scoreState.AccuracyPercent(chart?.PlayableCount ?? 0):F4}%"; comboLabel.text = "COMBO\n" + scoreState.Combo; }
        void SetStatus(string message) { if (loadStatus != null) loadStatus.text = message; }
        void ShowJudgment(string value, Color color) { judgmentLabel.text = value; judgmentLabel.color = color; }

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
