using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Gugarythm;
using UnityEditor;
using UnityEngine;

public static class RuntimeValidation
{
    [MenuItem("Gugarythm/Start Loaded Chart _F8", true)]
    static bool CanStartLoadedChart() => EditorApplication.isPlaying;

    [MenuItem("Gugarythm/Start Loaded Chart _F8")]
    public static void StartLoadedChart()
    {
        var controller = UnityEngine.Object.FindFirstObjectByType<SonolusLandscapePrototype>();
        if (controller == null) throw new InvalidOperationException("Runtime controller is not active.");
        controller.StartLoadedChartForEditor();
    }

    [MenuItem("Gugarythm/Validate Runtime")]
    public static void ValidateRuntime()
    {
        ValidateGgrPackageReader();
        Require(Math.Abs(SonolusLandscapePrototype.NoteApproachDurationSeconds - 2f) < .0001f,
            "Notes must use a fixed two-second approach duration");
        ValidateUscLeadingMeasurePadding();
        ValidateInitialWaterfallTiming();
        ValidateGgrUscHoldRoots();
        ValidateAttachedGgrPlayableCount();
        ValidateUscSlideRoleClassification();
        ValidateUscSlideMidpointRoles();
        ValidateHeadlessCriticalSlideStart();
        ValidateNoteRenderWidths();
        ValidateNoteRenderVisibilityWindow();
        ValidateLibrarySelectionFrameGeometry();
        ValidateLibraryDataRefreshContracts();
        ValidateCoverPresentationContracts();
        ValidateNoteSurfaceProjection();
        ValidateHeadlessHoldRendering();
        ValidatePersistentHoldVisualRouting();
        ValidateHoldSoundGate();
        ValidateHoldJudgmentAudioRouting();
        ValidateHitEffectColorRouting();
        var path = Path.Combine(Application.dataPath, "StreamingAssets/Charts/default.scp");
        if (!File.Exists(path)) throw new FileNotFoundException("Default SCP is missing", path);
        var bytes = File.ReadAllBytes(path);
        var result = new ScpChartImporter().Import("default.scp", bytes);
        if (!result.Success) throw new InvalidDataException(result.Error);
        var chart = result.Chart;
        // Hidden and attached slide-control entities belong to connector geometry,
        // not the playable/judged note set.
        Require(chart.Notes.Any(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto && !note.Visible),
            "Imported Holds must add invisible eighth-note checkpoints");
        Require(chart.Connectors.Count == 1175, $"Expected 1175 connectors, got {chart.Connectors.Count}");
        Require(chart.Connectors.Any(value => value.Start.SourceId == "6" && value.End.SourceId == "8"),
            "Hold connector geometry must stop at its first particle/control point");
        Require(!chart.Connectors.Any(value => value.Start.SourceId == "6" && value.End.SourceId == "7"),
            "Hold connector geometry must not skip particles and flatten logical start/end into one ribbon");
        var hiddenRootConnector = chart.Connectors.FirstOrDefault(value => value.Start.Archetype == "HiddenSlideStartNote");
        Require(hiddenRootConnector != null && SonolusLandscapePrototype.ShouldClipHoldConnector(hiddenRootConnector),
            "Hidden-head Hold connectors must remain clipped at the judgment line");
        Require(chart.Connectors.Where(value => value.Start.HoldRootIndex >= 0 || value.End.HoldRootIndex >= 0)
                    .All(SonolusLandscapePrototype.ShouldClipHoldConnector),
            "Every segment attached to a Hold must stop at its head instead of continuing below it");
        Require(chart.TimeScaleGroups.Count == 3, $"Expected 3 time-scale layers, got {chart.TimeScaleGroups.Count}");
        Require(chart.Notes.Any(note => note.TimeScaleGroup == "tsg:1") && chart.Notes.Any(note => note.TimeScaleGroup == "tsg:2"),
            "Notes from secondary time-scale layers were not preserved");
        Require(chart.TimeScaleGroups.Values.Select(value => value.PositionAt(100)).Distinct().Count() > 1,
            "Independent time-scale layers must produce distinct visual positions");
        Require(chart.SimLines.Count == 579, $"Expected 579 synchronization lines, got {chart.SimLines.Count}");
        Require(chart.Guides.Count == 154, $"Expected 154 decoration guides, got {chart.Guides.Count}");
        Require(chart.Guides.Count(guide => guide.FadeOut) == 39,
            $"Expected 39 decoration guide chain endings, got {chart.Guides.Count(guide => guide.FadeOut)}");
        var chartNotes = new HashSet<RuntimeNote>(chart.Notes);
        var connectorOnlyNodes = chart.Connectors
            .SelectMany(connector => new[] { connector.Start, connector.End })
            .Where(note => !chartNotes.Contains(note))
            .Distinct()
            .ToArray();
        Require(connectorOnlyNodes.Length > 0 && connectorOnlyNodes.All(note => !note.Judged && !note.Visible),
            "Connector-only geometry nodes must not be classified as playable Hold checkpoints or visible notes");
        Require(chart.Notes.Count(note => (note.Archetype ?? string.Empty).EndsWith("SlideTickNote", StringComparison.OrdinalIgnoreCase)) == 46,
            "Expected 46 particle-only hold mids");
        foreach (var tone in new[] { "cyan", "mint", "pink", "yellow" })
            Require(Resources.Load<Texture2D>($"Gugarhythm/official/buttons/button-{tone}") != null,
                $"Official button texture is missing: {tone}");
        foreach (var tone in new[] { "mint", "pink", "yellow" })
            Require(Resources.Load<Texture2D>($"Gugarhythm/official/traces/trace-{tone}") != null,
                $"Official trace texture is missing: {tone}");
        Require(Resources.Load<Texture2D>("Gugarhythm/official/damage/damage-purple") != null, "Official Damage texture is missing");
        foreach (var tone in new[] { "normal", "critical" })
        foreach (var direction in new[] { "center", "side" })
        foreach (var size in Enumerable.Range(1, 6))
            Require(Resources.Load<Texture2D>($"Gugarhythm/flicks/flick-{tone}-{direction}-{size}") != null,
                $"Flick texture is missing: {tone}-{direction}-{size}");
        Require(Resources.Load<Texture2D>("Gugarhythm/connectors/hold-green") != null, "Normal Hold connector texture is missing");
        Require(Resources.Load<Texture2D>("Gugarhythm/connectors/hold-yellow") != null, "Critical Hold connector texture is missing");
        Require(Resources.Load<Texture2D>("Gugarhythm/official/particles/slide-tick-mint") != null, "Official normal hold-mid particle is missing");
        Require(Resources.Load<Texture2D>("Gugarhythm/official/particles/slide-tick-yellow") != null, "Official critical hold-mid particle is missing");
        foreach (var tone in new[] { "mint", "pink", "yellow" })
            Require(Resources.Load<Texture2D>($"Gugarhythm/official/particles/trace-diamond-{tone}") != null,
                $"Official Trace diamond is missing: {tone}");
        Require(Resources.Load<Texture2D>("Gugarhythm/package/particles/pixel-atlas") != null,
            "SCP-derived Pixel judgment atlas is missing");
        foreach (var sound in new[] { "perfect", "great", "good", "alternative", "hold-loop", "stage" })
            Require(Resources.Load<AudioClip>($"Gugarhythm/package/audio/{sound}") != null,
                $"SCP-derived judgment sound is missing: {sound}");
        var holdLoop = Resources.Load<AudioClip>("Gugarhythm/package/audio/hold-loop");
        Require(holdLoop != null && holdLoop.channels == 1 && holdLoop.frequency == 44100,
            "Hold loop must be a mono 44.1 kHz resource for gapless Android playback");
        Require(Resources.Load<AudioClip>("Gugarhythm/package/audio/flick") != null,
            "Normal Flick sound is missing");
        Require(Resources.Load<AudioClip>("Gugarhythm/package/audio/critical-flick") != null,
            "Critical Flick sound is missing");
        var flicks = chart.Notes.Where(note => note.Kind == RuntimeNoteKind.Flick).ToArray();
        Require(flicks.Length == 243, $"Expected 243 flick notes, got {flicks.Length}");
        Require(flicks.Count(note => note.Direction < 0) == 117 && flicks.Count(note => note.Direction == 0) == 21 && flicks.Count(note => note.Direction > 0) == 105,
            "Flick left/center/right directions were not preserved");
        var holdTerminalNotes = chart.Connectors.Select(connector => connector.End)
            .Where(note => !chart.Connectors.Any(connector => ReferenceEquals(connector.Start, note)))
            .Distinct()
            .ToArray();
        Require(holdTerminalNotes.Length > 0 && holdTerminalNotes.Where(note => note.Judged).All(note =>
                    note.IsHoldTerminal && note.HoldCheckpointSource == HoldCheckpointSource.Tail),
            "Judged Hold terminals must retain Tail checkpoint metadata while unjudged geometry stays non-playable");
        Require(chart.Guides.Any(guide => guide.TailOpacity < guide.HeadOpacity) && chart.Guides.Min(guide => guide.TailOpacity) <= .081f,
            "Guide chains must fade continuously toward their ending");
        Require(chart.Guides.Any(guide => guide.Start.Lane - guide.Start.Size < -6 || guide.Start.Lane + guide.Start.Size > 6 ||
            guide.Head.Lane - guide.Head.Size < -6 || guide.Head.Lane + guide.Head.Size > 6 ||
            guide.Tail.Lane - guide.Tail.Size < -6 || guide.Tail.Lane + guide.Tail.Size > 6 ||
            guide.End.Lane - guide.End.Size < -6 || guide.End.Lane + guide.End.Size > 6),
            "Expected at least one decoration guide outside the central lane range");
        Require(chart.BgmBytes?.Length > 0, "Default SCP BGM was not extracted");
        Require(chart.Notes.SequenceEqual(chart.Notes.OrderBy(note => note.Time).ThenBy(note => note.Index)), "Notes are not time sorted");

        ValidateJudgedVisualMasking();
        ValidateJudgmentRules();
        ValidateAutoPlay();
        ValidateAudioDeviceRecovery();
        ValidateLatencyCalibrationMath();
        Debug.Log($"GUGARYTHM_VALIDATION_OK title={chart.Title} playable={chart.PlayableCount} connectors={chart.Connectors.Count} simLines={chart.SimLines.Count} guides={chart.Guides.Count} " +
                  $"normal={chart.Connectors.Count(value => !value.Critical)} critical={chart.Connectors.Count(value => value.Critical)} " +
                  $"warnings={chart.Warnings.Count} bgmBytes={chart.BgmBytes.Length}");
    }

    static void ValidateLibrarySelectionFrameGeometry()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static;
        var insetField = typeof(SonolusLandscapePrototype).GetField("LibrarySelectedRowInset", flags);
        var widthField = typeof(SonolusLandscapePrototype).GetField("LibrarySelectionFrameWidth", flags);
        var frameGraphicType = typeof(SonolusLandscapePrototype).GetNestedType("SelectionFrameGraphic", flags);
        Require(insetField != null && Math.Abs((float)insetField.GetRawConstantValue() - 2f) < .0001f,
            "Selected library rows must keep their original two-unit horizontal inset");
        Require(widthField != null && Math.Abs((float)widthField.GetRawConstantValue() - 4f) < .0001f,
            "The library selection frame must remain four units wide for mobile visibility");
        Require(frameGraphicType != null && typeof(UnityEngine.UI.MaskableGraphic).IsAssignableFrom(frameGraphicType),
            "The library selection frame must render all four sides as one mask-safe graphic");
    }

    static void ValidateLatencyCalibrationMath()
    {
        var samples = new[] { .010d, .020d, .030d, .040d };
        Require(LatencyCalibrationMath.TryGetCalibrationAverage(samples, out var average) && Math.Abs(average - .025d) < .000001d,
            "Four calibration rounds must average their valid fourth-beat taps");
        Require(!LatencyCalibrationMath.TryGetCalibrationAverage(new[] { .010d, double.NaN, .030d, .040d }, out _),
            "Calibration must reject a round containing an invalid tap");
        Require(!LatencyCalibrationMath.TryGetCalibrationAverage(new[] { .010d, .020d, .030d }, out _),
            "Calibration must reject an incomplete set of rounds");
    }

    static void ValidateLibraryDataRefreshContracts()
    {
        var entryType = typeof(LocalChartEntry).Assembly.GetType("Gugarythm.LibrarySelectionReconciler");
        var selectMethod = entryType?.GetMethod("Select", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Require(selectMethod != null, "Library refresh must expose a selection reconciliation rule");

        var entries = new[]
        {
            new LocalChartEntry { Id = "first" },
            new LocalChartEntry { Id = "second" },
        };
        var refreshed = (LocalChartEntry)selectMethod.Invoke(null, new object[] { entries, entries[1] });
        Require(ReferenceEquals(refreshed, entries[1]),
            "Library refresh must preserve a selected entry that still exists");
        var fallback = (LocalChartEntry)selectMethod.Invoke(null, new object[] { entries, new LocalChartEntry { Id = "deleted" } });
        Require(ReferenceEquals(fallback, entries[0]),
            "Library refresh must select the first remaining entry after deletion");

        var canonicalizeMethod = typeof(LocalChartLibrary).GetMethod("CanonicalizeDifficultyTags",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(canonicalizeMethod != null, "Difficulty tags must expose a canonical merge rule");
        var canonical = (IReadOnlyList<string>)canonicalizeMethod.Invoke(null, new object[] {
            new[] { " APPEND ", "append", "Append", "EXPERT", "" }
        });
        Require(canonical.SequenceEqual(new[] { "APPEND", "EXPERT" }),
            "Difficulty tags with the same normalized name must merge automatically");

        var splitMethod = typeof(NativeChartPicker).GetMethod("SplitResultPaths",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(splitMethod != null, "Native picker must expose a batch result parser");
        var paths = (string[])splitMethod.Invoke(null, new object[] { "/cache/a.ggr\n/cache/b.ggr\n" });
        Require(paths.SequenceEqual(new[] { "/cache/a.ggr", "/cache/b.ggr" }),
            "Native picker must preserve every selected file path");
    }

    static void ValidateCoverPresentationContracts()
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        source.SetPixels(new[] { Color.red, Color.green, Color.blue, Color.white });
        source.Apply();
        var jpg = source.EncodeToJPG(90);
        UnityEngine.Object.DestroyImmediate(source);

        var decodeMethod = typeof(GgrChartImporter).GetMethod("DecodeCoverTexture",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(decodeMethod != null, "GGR cover bytes must expose a display decoder");
        var decoded = (Texture2D)decodeMethod.Invoke(null, new object[] { jpg, false });
        Require(decoded != null && decoded.width == 2 && decoded.height == 2,
            "A valid GGR JPEG cover must decode into a display texture");
        UnityEngine.Object.DestroyImmediate(decoded);

        var aspectMethod = typeof(SonolusLandscapePrototype).GetMethod("CoverPresentationAspectMode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(aspectMethod != null && string.Equals(aspectMethod.Invoke(null, null)?.ToString(), "EnvelopeParent", StringComparison.Ordinal),
            "Cover artwork must crop into a unified square presentation");
    }

    static void ValidateUscLeadingMeasurePadding()
    {
        var result = new UscChartImporter().Import("leading-note.usc", System.Text.Encoding.UTF8.GetBytes(
            "{\"usc\":{\"objects\":[{\"type\":\"bpm\",\"beat\":0,\"bpm\":120},{\"type\":\"single\",\"beat\":0,\"lane\":0}]}}"));
        Require(result.Success, "USC leading-note fixture must import successfully");
        Require(result.Chart.Notes.Count == 1 && Math.Abs(result.Chart.Notes[0].Beat - 4) < .0001 &&
                Math.Abs(result.Chart.Notes[0].Time - 2) < .0001,
            "A USC chart whose first note starts before beat 4 must gain one empty measure");
        Require(Math.Abs(result.Chart.BgmStartDelaySeconds - 2) < .0001,
            "The BGM must be delayed by the same duration as the inserted empty measure");

        var alreadyStarted = new UscChartImporter().Import("already-started.usc", System.Text.Encoding.UTF8.GetBytes(
            "{\"usc\":{\"objects\":[{\"type\":\"bpm\",\"beat\":0,\"bpm\":120},{\"type\":\"single\",\"beat\":4,\"lane\":0}]}}"));
        Require(alreadyStarted.Success && Math.Abs(alreadyStarted.Chart.Notes[0].Beat - 4) < .0001 &&
                Math.Abs(alreadyStarted.Chart.BgmStartDelaySeconds) < .0001,
            "A USC chart already starting after the first measure must not be shifted");

    }

    public static void ValidateInitialWaterfallTiming()
    {
        var initialWaterfallTimeMethod = typeof(SonolusLandscapePrototype).GetMethod(
            "InitialWaterfallSongTime", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Require(initialWaterfallTimeMethod != null,
            "Gameplay must expose the initial waterfall song-time calculation");
        var firstVisualTime = 4d * 60d / 193d;
        var initialWaterfallTime = (double)initialWaterfallTimeMethod.Invoke(null,
            new object[] { firstVisualTime, -1.097d, 0d, 2d, .25d });
        Require(Math.Abs(initialWaterfallTime - (firstVisualTime - 2.25d)) < .0001,
            "A 193 BPM chart with negative offset must begin off-screen before its first beat-4 object");

        var scaledChart = new RuntimeChart { DefaultTimeScaleGroup = "scaled" };
        scaledChart.TimeScaleGroups["scaled"] = new RuntimeTimeScaleGroup("scaled", new[] { (time: 0d, scale: .5d) });
        scaledChart.Notes.Add(new RuntimeNote { Time = 4d, TimeScaleGroup = "scaled", Visible = true });
        var firstVisualTimeMethod = typeof(SonolusLandscapePrototype).GetMethod(
            "FirstWaterfallVisualTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(firstVisualTimeMethod != null, "Gameplay must expose its first visual-time calculation");
        var scaledFirstVisualTime = (double)firstVisualTimeMethod.Invoke(null, new object[] { scaledChart });
        Require(Math.Abs(scaledFirstVisualTime - 2d) < .0001,
            "The first waterfall time must use the note's time-scale visual position");
        var firstSongTimeMethod = typeof(SonolusLandscapePrototype).GetMethod(
            "FirstWaterfallSongTime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(firstSongTimeMethod != null, "Gameplay must expose its first waterfall song-time calculation");
        var scaledFirstSongTime = (double)firstSongTimeMethod.Invoke(null, new object[] { scaledChart });
        Require(Math.Abs(scaledFirstSongTime + .5d) < .0001,
            "The first waterfall song time must invert the time-scale visual position");

        Require(Math.Abs(SonolusLandscapePrototype.DifficultyButtonWidthForText("未標示難度") - 170f) < .0001,
            "The unmarked difficulty button must reserve enough width for mobile text");
        Debug.Log("GUGARYTHM_WATERFALL_VALIDATION_OK");
    }

    static void ValidateGgrPackageReader()
    {
        var package = GgrPackageReader.Read(GgrZipFixture.Create(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"gugarythm-package\",\"version\":1,\"chart\":\"chart.usc\",\"audio\":\"audio.mp3\"}"),
            ["chart.usc"] = System.Text.Encoding.UTF8.GetBytes("{\"usc\":{\"objects\":[]}}"),
            ["audio.mp3"] = new byte[] { 1, 2, 3 },
        }));
        Require(package.ChartBytes.Length > 0 && package.AudioName == "audio.mp3",
            "A canonical stored GGR package must expose its chart and audio resources");
        var disguisedAudio = new GgrChartImporter().Import("disguised.ggr", GgrZipFixture.Create(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"gugarythm-package\",\"version\":1,\"chart\":\"chart.usc\",\"audio\":\"audio.mp3\"}"),
            ["chart.usc"] = System.Text.Encoding.UTF8.GetBytes("{\"usc\":{\"objects\":[]}}"),
            ["audio.mp3"] = new byte[] { 0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'d', (byte)'a', (byte)'s', (byte)'h' },
        }));
        Require(disguisedAudio.Success && disguisedAudio.Chart.BgmExtension == ".m4a",
            "GGR MP4/AAC audio disguised as MP3 must use the AAC decoder");
        var unknownLengthWav = new GgrChartImporter().Import("unknown-length-wav.ggr", GgrZipFixture.Create(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"gugarythm-package\",\"version\":1,\"chart\":\"chart.usc\",\"audio\":\"audio.wav\"}"),
            ["chart.usc"] = System.Text.Encoding.UTF8.GetBytes("{\"usc\":{\"objects\":[]}}"),
            ["audio.wav"] = new byte[]
            {
                (byte)'R', (byte)'I', (byte)'F', (byte)'F', 255, 255, 255, 255, (byte)'W', (byte)'A', (byte)'V', (byte)'E',
                (byte)'f', (byte)'m', (byte)'t', (byte)' ', 16, 0, 0, 0, 1, 0, 2, 0, 68, 172, 0, 0, 16, 177, 2, 0, 4, 0, 16, 0,
                (byte)'d', (byte)'a', (byte)'t', (byte)'a', 255, 255, 255, 255, 0, 0, 0, 0,
            },
        }));
        Require(unknownLengthWav.Success && unknownLengthWav.Chart.BgmBytes[4] == 40 && unknownLengthWav.Chart.BgmBytes[5] == 0 &&
                unknownLengthWav.Chart.BgmBytes[40] == 4 && unknownLengthWav.Chart.BgmBytes[41] == 0,
            "GGR PCM WAV files with unknown chunk lengths must be normalized before decoding");
        RequireGgrFailure(GgrZipFixture.Create(new Dictionary<string, byte[]>
        {
            ["payload.exe"] = new byte[] { 1 },
        }), "GGR 包含不安全的檔案路徑。");
    }

    static void ValidateGgrUscHoldRoots()
    {
        var result = new GgrChartImporter().Import("hold.ggr", GgrZipFixture.Create(new Dictionary<string, byte[]>
        {
            ["manifest.json"] = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"gugarythm-package\",\"version\":1,\"chart\":\"chart.usc\",\"audio\":\"audio.wav\"}"),
            ["chart.usc"] = System.Text.Encoding.UTF8.GetBytes("{\"usc\":{\"objects\":[{\"beat\":0,\"bpm\":120,\"type\":\"bpm\"},{\"type\":\"slide\",\"connections\":[{\"beat\":0,\"judgeType\":\"normal\",\"lane\":0,\"size\":1,\"type\":\"start\"},{\"beat\":2,\"judgeType\":\"none\",\"lane\":0,\"size\":1,\"type\":\"end\"}]}]}}"),
            ["audio.wav"] = new byte[] { 0 },
        }));
        Require(result.Success && result.Chart.Connectors.Count == 1 &&
                result.Chart.Connectors.All(connector => connector.Start.HoldRootIndex >= 0 && connector.End.HoldRootIndex >= 0),
            "GGR USC Hold connectors must retain Hold root metadata for clipped rendering");
    }

    static void ValidateAttachedGgrPlayableCount()
    {
        var path = Environment.GetEnvironmentVariable("GUGARYTHM_VALIDATION_GGR");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        var result = new GgrChartImporter().Import(Path.GetFileName(path), File.ReadAllBytes(path));
        Require(result.Success, "Attached GGR must import successfully: " + result.Error);
        Require(result.Chart.PlayableCount == 5579,
            $"Attached GGR must contain 5579 playable notes after judged Slide tails are restored, got {result.Chart.PlayableCount}");
    }

    static void ValidateNoteRenderWidths()
    {
        var normalTap = new RuntimeNote { Kind = RuntimeNoteKind.Tap };
        var tapQuadWidth = SonolusLandscapePrototype.NoteRenderQuadWidth(147.5f, 104.7f, normalTap);
        Require(Math.Abs(SonolusLandscapePrototype.NoteBodyWidth(tapQuadWidth, 104.7f, normalTap) - 147.5f) < .0001f,
            "A normal Tap's visible body must span exactly one authored note track");
        var holdHeadQuadWidth = SonolusLandscapePrototype.HoldHeadRenderQuadWidth(147.5f, 104.7f, false);
        var criticalHoldHeadQuadWidth = SonolusLandscapePrototype.HoldHeadRenderQuadWidth(147.5f, 104.7f, true);
        Require(Math.Abs(SonolusLandscapePrototype.HoldHeadVisibleCoreWidth(holdHeadQuadWidth) - 147.5f) < .0001f &&
                Math.Abs(SonolusLandscapePrototype.HoldHeadVisibleCoreWidth(criticalHoldHeadQuadWidth) - 147.5f) < .0001f,
            "Mint and yellow Hold heads' solid cores must each span one authored note track");
        var descendingHoldHead = new RuntimeNote { Index = 7, HoldRootIndex = 7, Kind = RuntimeNoteKind.Sustain };
        Require(Math.Abs(SonolusLandscapePrototype.NoteRenderQuadWidth(147.5f, 104.7f, descendingHoldHead) - holdHeadQuadWidth) < .0001f,
            "A descending Hold head must use the same quad width as its persistent judgment-line head");
        var connectorVisibleWidth = SonolusLandscapePrototype.HoldConnectorVisibleBodyWidth(
            SonolusLandscapePrototype.HoldConnectorRenderWidth(147.5f));
        Require(Math.Abs(connectorVisibleWidth - 147.5f) < .0001f,
            "A Hold ribbon's visible fill must align with its USC-authored head width");
        Require(Math.Abs(SonolusLandscapePrototype.HoldConnectorLaneWidth(147.5f) - 147.5f) < .0001f,
            "A rendered Hold ribbon must stay within its authored lane span instead of expanding into a neighboring lane");
        Require(Math.Abs((1 - SonolusLandscapePrototype.HoldConnectorSourceUvInset * 2) * 306 - 240) < .0001f,
            "A lane-confined Hold ribbon must map only the texture's visible core across its authored lane span");

        var clippedConnectorWidth = typeof(SonolusLandscapePrototype).GetMethod(
            "HoldConnectorRenderWidth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null, new[] { typeof(float), typeof(float), typeof(float), typeof(float) }, null);
        Require(clippedConnectorWidth != null,
            "Hold connectors must expose the same edge-clamped width calculation used by Hold heads");
        var clippedRenderWidth = (float)clippedConnectorWidth.Invoke(null, new object[] { 1000f, 0f, 6f, 1f });
        var clippedVisibleWidth = SonolusLandscapePrototype.HoldConnectorVisibleBodyWidth(clippedRenderWidth);
        Require(clippedVisibleWidth < 1000f,
            "An edge-clamped Hold connector must shrink with its head instead of retaining its authored full width");
    }

    static void ValidateNoteRenderVisibilityWindow()
    {
        var method = typeof(SonolusLandscapePrototype).GetMethod(
            "IsInNoteRenderWindow", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Require(method != null, "Gameplay must expose its note render-window rule");

        var inside = (bool)method.Invoke(null, new object[] { 0f, 100f, -100f });
        var tooFar = (bool)method.Invoke(null, new object[] { 150f, 100f, -100f });
        var alreadyExited = (bool)method.Invoke(null, new object[] { -150f, 100f, -100f });
        Require(inside && !tooFar && !alreadyExited,
            "Only notes between the upper render boundary and exit boundary may stay in the active UI pool");
    }

    static void ValidateNoteSurfaceProjection()
    {
        // A projected note stays a fixed-height sticker on the lane surface:
        // its top and bottom edges sample the lane separately, instead of
        // scaling an axis-aligned UI rectangle by depth.
        var quad = SonolusLandscapePrototype.BuildNoteSurfaceQuad(3f, 1f, .5f, 48f);
        Require(Math.Abs(quad.UpperLeft.y - quad.LowerLeft.y - 48f) < .0001f &&
                Math.Abs(quad.UpperRight.y - quad.LowerRight.y - 48f) < .0001f,
            "A surface-projected note must retain its authored screen-space height");
        Require(Math.Abs(quad.UpperLeft.x - quad.LowerLeft.x) > .01f &&
                Math.Abs(quad.UpperRight.x - quad.LowerRight.x) > .01f,
            "A surface-projected note must follow the sloped lane edges instead of remaining axis aligned");
        Require(quad.UpperRight.x > quad.UpperLeft.x && quad.LowerRight.x > quad.LowerLeft.x,
            "A surface-projected note must keep its left-to-right lane ordering at both edges");

        var judgmentHeight = SonolusLandscapePrototype.NoteSurfaceHeight(1f);
        var midHeight = SonolusLandscapePrototype.NoteSurfaceHeight(.75f);
        var farHeight = SonolusLandscapePrototype.NoteSurfaceHeight(.1f);
        Require(farHeight > 0 && farHeight < midHeight && midHeight < judgmentHeight,
            "Note height must grow continuously from the vanishing point to the judgment edge");
    }

    static void ValidateHeadlessHoldRendering()
    {
        var judgedRoot = new RuntimeNote { Visible = true, Judged = true };
        var headlessRoot = new RuntimeNote { Visible = false, Judged = false };
        Require(SonolusLandscapePrototype.ShouldRenderPersistentHoldHead(judgedRoot),
            "A judged Hold root must retain its judgment-line head");
        Require(!SonolusLandscapePrototype.ShouldRenderPersistentHoldHead(headlessRoot),
            "A no-head-judgment Hold must not synthesize a normal Hold head at the judgment line");
    }

    static void ValidatePersistentHoldVisualRouting()
    {
        var traceRoot = new RuntimeNote { Archetype = "USC Trace Slide start", Visible = true, Judged = true };
        var normalRoot = new RuntimeNote { Archetype = "USC Slide start", Visible = true, Judged = true };
        Require(SonolusLandscapePrototype.ShouldUseTracePersistentHoldVisual(traceRoot),
            "A Trace Hold root must retain its Trace visual at the judgment line");
        Require(!SonolusLandscapePrototype.ShouldUseTracePersistentHoldVisual(normalRoot),
            "A normal Hold root must retain its Hold-head visual at the judgment line");
    }

    // This fixture deliberately has no .5-beat interior spans, so PlayableCount
    // describes only authored Slide nodes. Auto-checkpoint cadence is covered by
    // the existing HoldCheckpointBuilder validation below.
    static void ValidateUscSlideRoleClassification()
    {
        const string usc = @"{
            ""usc"": {
                ""objects"": [
                    { ""type"": ""bpm"", ""beat"": 0, ""bpm"": 120 },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 0, ""judgeType"": ""normal"", ""lane"": -4, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 0.125, ""judgeType"": ""normal"", ""lane"": -3, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 0.25, ""judgeType"": ""normal"", ""lane"": -2, ""size"": 1, ""type"": ""end"" }
                    ] },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 1, ""judgeType"": ""trace"", ""lane"": -1, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 1.125, ""judgeType"": ""trace"", ""lane"": 0, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 1.25, ""judgeType"": ""trace"", ""lane"": 1, ""size"": 1, ""type"": ""end"" }
                    ] },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 2, ""judgeType"": ""none"", ""lane"": -1, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 2.125, ""judgeType"": ""none"", ""lane"": 0, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 2.25, ""judgeType"": ""none"", ""lane"": 1, ""size"": 1, ""type"": ""end"" }
                    ] },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 3, ""judgeType"": ""normal"", ""lane"": 2, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 3.125, ""judgeType"": ""normal"", ""lane"": 3, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 3.25, ""judgeType"": ""normal"", ""lane"": 4, ""size"": 1, ""type"": ""end"", ""direction"": ""right"" }
                    ] },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 4, ""judgeType"": ""normal"", ""lane"": 2, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 4.125, ""judgeType"": ""normal"", ""lane"": 3, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 4.25, ""judgeType"": ""none"", ""lane"": 4, ""size"": 1, ""type"": ""end"", ""direction"": ""right"" }
                    ] },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 5, ""judgeType"": ""none"", ""lane"": -4, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 5.125, ""judgeType"": ""normal"", ""lane"": -3, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 5.25, ""judgeType"": ""none"", ""lane"": -2, ""size"": 1, ""type"": ""end"" }
                    ] },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 6, ""judgeType"": ""none"", ""lane"": -4, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 6.25, ""judgeType"": ""normal"", ""lane"": -3, ""size"": 1, ""type"": ""end"" }
                    ] }
                ]
            }
        }";
        var result = new UscChartImporter().Import("slide-roles.usc", System.Text.Encoding.UTF8.GetBytes(usc));
        Require(result.Success, "Synthetic USC Slide-role fixture must import: " + result.Error);
        var chart = result.Chart;
        var nodes = chart.Notes.Concat(chart.Connectors.SelectMany(connector => new[] { connector.Start, connector.End }))
            .Distinct().ToArray();
        RuntimeNote At(int connectionIndex) => nodes.Single(note => note.SourceId == "usc-slide:" + connectionIndex);

        var normalHead = At(1);
        var normalMid = At(2);
        var normalTail = At(3);
        var traceHead = At(4);
        var traceMid = At(5);
        var traceTail = At(6);
        var noneNodes = new[] { At(7), At(8), At(9) };
        var flickTail = At(12);
        var noneDirectionTail = At(15);
        var noneHead = At(16);
        var firstJudgedAfterNoneHead = At(17);
        var noneHeadTail = At(18);
        var terminalFirstJudgedAfterNoneHead = At(20);

        Require(Enum.TryParse("Tail", out HoldCheckpointSource tailSource),
            "HoldCheckpointSource must define Tail for judged Slide terminals");
        Require(normalHead.Judged && normalHead.Kind == RuntimeNoteKind.Tap && traceHead.Judged && traceHead.Kind == RuntimeNoteKind.Tap,
            "Normal and trace Slide heads must be discrete Tap judgments");
        Require(normalMid.Judged && normalMid.Kind == RuntimeNoteKind.Sustain && traceMid.Judged && traceMid.Kind == RuntimeNoteKind.Sustain,
            "Judged authored Slide mids must be Sustain checkpoints regardless of normal or trace judgeType");
        Require(normalTail.Judged && normalTail.Kind == RuntimeNoteKind.Sustain && normalTail.IsHoldTerminal && normalTail.HoldCheckpointSource == tailSource &&
                traceTail.Judged && traceTail.Kind == RuntimeNoteKind.Sustain && traceTail.IsHoldTerminal && traceTail.HoldCheckpointSource == tailSource,
            "Normal and trace Slide terminals must be judged Sustain Tail checkpoints");
        Require(flickTail.Judged && flickTail.Kind == RuntimeNoteKind.Flick && flickTail.IsHoldTerminal && flickTail.HoldCheckpointSource == tailSource,
            "Directional Slide terminals must remain judged Flick Tail checkpoints");
        Require(noneNodes.All(note => !note.Judged) && !noneDirectionTail.Judged && !noneHeadTail.Judged && chart.PlayableCount == 13,
            "Slide judgeType:none nodes must stay out of judgment and PlayableCount");
        Require(!noneHead.Judged && firstJudgedAfterNoneHead.Judged && firstJudgedAfterNoneHead.Kind == RuntimeNoteKind.Tap,
            "The first judged Slide connection after a none head must become the discrete Tap head");
        Require(terminalFirstJudgedAfterNoneHead.Judged && terminalFirstJudgedAfterNoneHead.Kind == RuntimeNoteKind.Tap &&
                terminalFirstJudgedAfterNoneHead.IsHoldTerminal && terminalFirstJudgedAfterNoneHead.HoldCheckpointSource == tailSource,
            "A first judged Slide terminal after a none head must remain a Tap with Tail metadata");

        var headEngine = new JudgmentEngine(new[] { normalHead }, new ScoreState());
        headEngine.Process(normalHead.Time, Array.Empty<InputToken>(), new[] { new ActiveContact(1, normalHead.Lane, normalHead.Time - .1) });
        Require(normalHead.Grade == JudgmentGrade.Pending,
            "A static pre-held contact must not hit a Slide Tap head");
        headEngine.Process(normalHead.Time, new[] { new InputToken(1, RuntimeNoteKind.Tap, normalHead.Time, normalHead.Lane) }, Array.Empty<ActiveContact>());
        Require(normalHead.Grade == JudgmentGrade.Perfect,
            "A Slide Tap head must resolve from a discrete Tap token");

        var terminalHeadEngine = new JudgmentEngine(new[] { terminalFirstJudgedAfterNoneHead }, new ScoreState());
        terminalHeadEngine.Process(terminalFirstJudgedAfterNoneHead.Time, Array.Empty<InputToken>(),
            new[] { new ActiveContact(1, terminalFirstJudgedAfterNoneHead.Lane, terminalFirstJudgedAfterNoneHead.Time - .1) });
        Require(terminalFirstJudgedAfterNoneHead.Grade == JudgmentGrade.Pending,
            "A pre-held contact must not hit a first judged Slide terminal Tap");

        var midEngine = new JudgmentEngine(new[] { normalMid }, new ScoreState());
        midEngine.Process(normalMid.Time, Array.Empty<InputToken>(), new[] { new ActiveContact(1, normalMid.Lane, normalMid.Time - .1) });
        Require(normalMid.Grade == JudgmentGrade.Perfect,
            "A judged Slide mid must resolve from sustained contact");
        var tailEngine = new JudgmentEngine(new[] { normalTail }, new ScoreState());
        tailEngine.Process(normalTail.Time, Array.Empty<InputToken>(), new[] { new ActiveContact(1, normalTail.Lane, normalTail.Time - .1) });
        Require(normalTail.Grade == JudgmentGrade.Perfect,
            "A non-Flick Slide tail must resolve Perfect from sustained contact without release");

        var flickEngine = new JudgmentEngine(new[] { flickTail }, new ScoreState());
        flickEngine.Process(flickTail.Time, new[] { new InputToken(1, RuntimeNoteKind.Tap, flickTail.Time, flickTail.Lane) }, Array.Empty<ActiveContact>());
        Require(flickTail.Grade == JudgmentGrade.Pending,
            "A Slide Flick tail must not resolve from a Tap token");
        flickEngine.Process(flickTail.Time, new[] { new InputToken(1, RuntimeNoteKind.Flick, flickTail.Time, flickTail.Lane, flickTail.Lane, flickTail.Time) }, Array.Empty<ActiveContact>());
        Require(flickTail.Grade == JudgmentGrade.Perfect,
            "A Slide Flick tail must resolve from a Flick token");

        var noneDirectionScore = new ScoreState();
        var noneDirectionEngine = new JudgmentEngine(new[] { noneDirectionTail }, noneDirectionScore);
        noneDirectionEngine.Process(noneDirectionTail.Time,
            new[] { new InputToken(1, RuntimeNoteKind.Flick, noneDirectionTail.Time, noneDirectionTail.Lane, noneDirectionTail.Lane, noneDirectionTail.Time) },
            Array.Empty<ActiveContact>());
        Require(noneDirectionTail.Grade == JudgmentGrade.Pending && noneDirectionScore.Judged == 0 && noneDirectionScore.Combo == 0,
            "A directional judgeType:none Slide terminal must not be judged or add Combo");
    }

    static void ValidateUscSlideMidpointRoles()
    {
        const string usc = @"{
            ""usc"": {
                ""objects"": [
                    { ""type"": ""bpm"", ""beat"": 0, ""bpm"": 120 },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 10, ""judgeType"": ""normal"", ""lane"": 0, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 10.1, ""lane"": 1, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 10.2, ""critical"": false, ""lane"": 4, ""size"": 1, ""type"": ""attach"" },
                        { ""beat"": 10.3, ""critical"": false, ""lane"": 2, ""size"": 1, ""type"": ""tick"" },
                        { ""beat"": 10.4, ""judgeType"": ""trace"", ""lane"": 3, ""size"": 1, ""type"": ""end"" }
                    ] }
                ]
            }
        }";
        var result = new UscChartImporter().Import("midpoint-roles.usc", System.Text.Encoding.UTF8.GetBytes(usc));
        Require(result.Success, "Synthetic USC midpoint-role fixture must import: " + result.Error);
        var chart = result.Chart;
        var nodes = chart.Notes.Concat(chart.Connectors.SelectMany(connector => new[] { connector.Start, connector.End }))
            .Distinct().ToArray();
        RuntimeNote At(double beat) => nodes.Single(note => Math.Abs(note.Beat - beat) < 1e-9);
        var bendOnly = At(10.1);
        var particleOnly = At(10.2);
        var bendAndParticle = At(10.3);

        Require(!bendOnly.Visible && !bendOnly.Judged && !chart.Notes.Contains(bendOnly),
            "A tick without critical must bend the Hold path without creating a particle or judgment");
        Require(particleOnly.Visible && !particleOnly.Judged && chart.Notes.Contains(particleOnly),
            "An attach must create a visible particle without becoming a judgment");
        Require(Math.Abs(particleOnly.Lane - 1.5f) < .0001f,
            "An attach particle must use the interpolated Hold trajectory instead of its own raw lane coordinate");
        Require(particleOnly.HoldRootIndex == At(10).Index &&
                !SonolusLandscapePrototype.ShouldHideAttachedHoldParticle(particleOnly, .999f) &&
                SonolusLandscapePrototype.ShouldHideAttachedHoldParticle(particleOnly, 1f),
            "An attach particle must belong to its Hold and retract at the same judgment-line threshold");
        Require(bendAndParticle.Visible && !bendAndParticle.Judged && chart.Notes.Contains(bendAndParticle),
            "A tick with critical must both bend the Hold path and create a visible particle");
        Require(chart.Connectors.Any(connector => ReferenceEquals(connector.End, bendOnly)) &&
                chart.Connectors.Any(connector => ReferenceEquals(connector.Start, bendOnly)),
            "A bend-only tick must remain in the connector path");
        Require(!chart.Connectors.Any(connector => ReferenceEquals(connector.Start, particleOnly) || ReferenceEquals(connector.End, particleOnly)),
            "An attach particle must not create a Hold-path bend");
        Require(chart.Connectors.Any(connector => ReferenceEquals(connector.End, bendAndParticle)) &&
                chart.Connectors.Any(connector => ReferenceEquals(connector.Start, bendAndParticle)),
            "A tick with critical must remain in the connector path");
        Require(SonolusLandscapePrototype.ShouldShowNoteParticle(particleOnly, true) &&
                SonolusLandscapePrototype.ShouldShowNoteParticle(bendAndParticle, true),
            "USC attach and critical tick particles must be visible even though they are not Trace notes");
        Require(!SonolusLandscapePrototype.ShouldShowNoteParticle(bendOnly, true) &&
                !SonolusLandscapePrototype.ShouldShowNoteParticle(particleOnly, false),
            "Bend-only ticks and missing particle textures must remain hidden");
    }

    static void ValidateHeadlessCriticalSlideStart()
    {
        const string usc = @"{
            ""usc"": {
                ""objects"": [
                    { ""type"": ""bpm"", ""beat"": 0, ""bpm"": 120 },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 0, ""judgeType"": ""none"", ""critical"": true, ""lane"": 0, ""size"": 1, ""type"": ""start"" },
                        { ""beat"": 1, ""judgeType"": ""trace"", ""critical"": true, ""lane"": 2, ""size"": 1, ""type"": ""end"" }
                    ] }
                ]
            }
        }";
        var result = new UscChartImporter().Import("headless-critical.usc", System.Text.Encoding.UTF8.GetBytes(usc));
        Require(result.Success, "A headless critical USC Slide fixture must import: " + result.Error);
        var nodes = result.Chart.Notes.Concat(result.Chart.Connectors.SelectMany(connector => new[] { connector.Start, connector.End }))
            .Distinct().ToArray();
        var head = nodes.Single(note => Math.Abs(note.Beat) < 1e-9 || Math.Abs(note.Beat - 4) < 1e-9);
        Require(!head.Visible && !head.Judged && !result.Chart.Notes.Contains(head),
            "A critical judgeType:none Slide start must remain a headless path anchor, not render as a yellow button");
    }

    static void ValidateHoldSoundGate()
    {
        var gate = new HoldSoundGate();
        Require(!gate.ShouldPlay && gate.ActiveCount == 0,
            "An empty HoldSoundGate must keep the shared Hold loop stopped");

        gate.Deactivate(999);
        Require(!gate.ShouldPlay && gate.ActiveCount == 0,
            "Deactivating an unknown Hold root must leave an empty gate stopped");

        gate.Activate(10);
        gate.Activate(10);
        Require(gate.ShouldPlay && gate.ActiveCount == 1,
            "Duplicate HoldSoundGate activation must retain one active root");

        gate.Activate(20);
        Require(gate.ShouldPlay && gate.ActiveCount == 2,
            "Two active Hold roots must share one playing gate with count two");

        gate.Deactivate(10);
        Require(gate.ShouldPlay && gate.ActiveCount == 1,
            "Ending one of multiple active Holds must keep the shared loop active");

        gate.Deactivate(20);
        Require(!gate.ShouldPlay && gate.ActiveCount == 0,
            "Ending the last active Hold must stop the shared loop gate");

        gate.Deactivate(20);
        Require(!gate.ShouldPlay && gate.ActiveCount == 0,
            "Repeated Hold deactivation must leave the stopped gate unchanged");

        gate.Activate(10);
        Require(gate.ShouldPlay && gate.ActiveCount == 1,
            "A Hold must reactivate the gate after Miss-style deactivation");

        gate.Clear();
        Require(!gate.ShouldPlay && gate.ActiveCount == 0,
            "Clearing HoldSoundGate must remove every active root and stop the loop");
        gate.Clear();
        Require(!gate.ShouldPlay && gate.ActiveCount == 0,
            "Repeated HoldSoundGate clearing must leave the gate stopped");
    }

    static void ValidateHitEffectColorRouting()
    {
        static bool Same(Color actual, Color expected) =>
            Mathf.Approximately(actual.r, expected.r) && Mathf.Approximately(actual.g, expected.g) &&
            Mathf.Approximately(actual.b, expected.b) && Mathf.Approximately(actual.a, expected.a);

        Require(Same(SonolusLandscapePrototype.ResolveHitEffectColor(new RuntimeNote { Kind = RuntimeNoteKind.Tap }),
                new Color(.28f, .82f, 1f, .84f)),
            "Normal Tap hit effect must use the cyan button color");
        Require(Same(SonolusLandscapePrototype.ResolveHitEffectColor(new RuntimeNote { Kind = RuntimeNoteKind.Flick }),
                new Color(1f, .2f, .67f, .86f)),
            "Normal Flick hit effect must use the pink button color");
        Require(Same(SonolusLandscapePrototype.ResolveHitEffectColor(new RuntimeNote { Kind = RuntimeNoteKind.Sustain }),
                new Color(.12f, 1f, .58f, .84f)),
            "Normal Hold hit effect must use the mint button color");
        Require(Same(SonolusLandscapePrototype.ResolveHitEffectColor(new RuntimeNote { Kind = RuntimeNoteKind.Tap, Archetype = "TraceNote" }),
                new Color(.12f, 1f, .58f, .84f)),
            "Trace hit effect must use the mint button color");
        Require(Same(SonolusLandscapePrototype.ResolveHitEffectColor(new RuntimeNote { Kind = RuntimeNoteKind.Flick, Critical = true }),
                new Color(1f, .82f, .12f, .9f)),
            "Critical hit effect must use the yellow button color regardless of note kind");
    }

    static void ValidateHoldJudgmentAudioRouting()
    {
        RuntimeNote HoldPart(int index, int root, HoldCheckpointSource source, RuntimeNoteKind kind = RuntimeNoteKind.Sustain)
        {
            var note = Note(index, index, 0);
            note.Kind = kind;
            note.HoldRootIndex = root;
            note.HoldCheckpointSource = source;
            note.IsHoldTerminal = source == HoldCheckpointSource.Tail;
            return note;
        }

        var state = new SonolusLandscapePrototype.HoldJudgmentAudioState();
        var head = HoldPart(100, 100, HoldCheckpointSource.None, RuntimeNoteKind.Tap);
        var headRoute = state.Route(new JudgmentEvent(head, JudgmentGrade.Perfect, 0));
        Require(headRoute == SonolusLandscapePrototype.JudgmentAudioRoute.GradeOneShot &&
                !state.ShouldPlay && state.ActiveCount == 0,
            "A successful Slide head must route only to its ordinary judgment one-shot and never activate the Hold loop");

        var mid = HoldPart(101, 100, HoldCheckpointSource.Mid);
        var midRoute = state.Route(new JudgmentEvent(mid, JudgmentGrade.Great, 0));
        Require(midRoute == SonolusLandscapePrototype.JudgmentAudioRoute.ActivateHoldLoop &&
                state.ShouldPlay && state.ActiveCount == 1,
            "A successful authored Hold mid must activate its root without routing a one-shot");

        var otherMid = HoldPart(201, 200, HoldCheckpointSource.Mid);
        state.Route(new JudgmentEvent(otherMid, JudgmentGrade.Perfect, 0));
        Require(state.ActiveCount == 2,
            "Successful checkpoints from overlapping Holds must add roots to the shared gate");

        var midMissRoute = state.Route(new JudgmentEvent(mid, JudgmentGrade.Miss, 0));
        Require(midMissRoute == SonolusLandscapePrototype.JudgmentAudioRoute.DeactivateHoldLoop &&
                state.ShouldPlay && state.ActiveCount == 1,
            "A missed Hold mid must deactivate only its own root while another active Hold keeps the loop enabled");

        var auto = HoldPart(102, 100, HoldCheckpointSource.Auto);
        var autoRoute = state.Route(new JudgmentEvent(auto, JudgmentGrade.Good, 0));
        Require(autoRoute == SonolusLandscapePrototype.JudgmentAudioRoute.ActivateHoldLoop && state.ActiveCount == 2,
            "A later successful Auto checkpoint must reactivate a Hold after an authored-mid Miss");

        var missedTail = HoldPart(103, 100, HoldCheckpointSource.Tail);
        var missedTailRoute = state.Route(new JudgmentEvent(missedTail, JudgmentGrade.Miss, 0));
        Require(missedTailRoute == SonolusLandscapePrototype.JudgmentAudioRoute.DeactivateHoldLoop &&
                state.ShouldPlay && state.ActiveCount == 1,
            "A missed Hold tail must permanently deactivate its root without a success one-shot");
        Require(state.Route(new JudgmentEvent(auto, JudgmentGrade.Perfect, 0)) == SonolusLandscapePrototype.JudgmentAudioRoute.None &&
                state.ActiveCount == 1,
            "A Hold root must never reactivate after its terminal judgment");

        var normalAuto = HoldPart(301, 300, HoldCheckpointSource.Auto);
        state.Route(new JudgmentEvent(normalAuto, JudgmentGrade.Perfect, 0));
        var normalTail = HoldPart(302, 300, HoldCheckpointSource.Tail);
        var normalTailRoute = state.Route(new JudgmentEvent(normalTail, JudgmentGrade.Perfect, 0));
        Require(normalTailRoute == (SonolusLandscapePrototype.JudgmentAudioRoute.PerfectOneShot |
                                    SonolusLandscapePrototype.JudgmentAudioRoute.DeactivateHoldLoop) &&
                state.ActiveCount == 1,
            "A successful non-Flick Hold tail must deactivate its root and retain the Perfect one-shot route");

        var flickTail = HoldPart(202, 200, HoldCheckpointSource.Tail, RuntimeNoteKind.Flick);
        var flickTailRoute = state.Route(new JudgmentEvent(flickTail, JudgmentGrade.Perfect, 0));
        Require(flickTailRoute == (SonolusLandscapePrototype.JudgmentAudioRoute.FlickOneShot |
                                   SonolusLandscapePrototype.JudgmentAudioRoute.DeactivateHoldLoop) &&
                !state.ShouldPlay && state.ActiveCount == 0,
            "A successful Flick Hold tail must deactivate its root and retain the Flick one-shot route");

        state.Clear();
        Require(!state.ShouldPlay && state.ActiveCount == 0,
            "Clearing Hold judgment audio state must reset both active and permanently-ended roots for a new chart");
    }

    static void RequireGgrFailure(byte[] bytes, string expected)
    {
        try
        {
            GgrPackageReader.Read(bytes);
            throw new InvalidOperationException("Expected GGR import failure: " + expected);
        }
        catch (GgrPackageException exception)
        {
            Require(exception.Message == expected, "Unexpected GGR error: " + exception.Message);
        }
    }

    static void ValidateJudgedVisualMasking()
    {
        Require(SonolusLandscapePrototype.ResolveHoldConnectorRenderMode(true, JudgmentGrade.Pending) == SonolusLandscapePrototype.HoldConnectorRenderMode.AnchorClipped &&
                SonolusLandscapePrototype.ResolveHoldConnectorRenderMode(true, JudgmentGrade.Perfect) == SonolusLandscapePrototype.HoldConnectorRenderMode.AnchorClipped &&
                SonolusLandscapePrototype.ResolveHoldConnectorRenderMode(true, JudgmentGrade.Miss) == SonolusLandscapePrototype.HoldConnectorRenderMode.AnchorClipped &&
                SonolusLandscapePrototype.ResolveHoldConnectorRenderMode(false, JudgmentGrade.Perfect) == SonolusLandscapePrototype.HoldConnectorRenderMode.AnchorClipped,
            "Hold connectors must always stop at their Head, regardless of judgment result");
        Require(!SonolusLandscapePrototype.ShouldHideJudgedVisual(JudgmentGrade.Perfect, 1f),
            "A successful note must remain visible until it reaches the lower edge of the judgment strip");
        Require(!SonolusLandscapePrototype.ShouldHideJudgedVisual(JudgmentGrade.Miss, 1.01f),
            "A missed note must remain visible while travelling through the judgment strip");
        Require(SonolusLandscapePrototype.ShouldHideJudgedVisual(JudgmentGrade.Perfect, 1.02f),
            "A successful note must disappear after reaching the lower edge of the judgment strip");
        Require(!SonolusLandscapePrototype.ShouldHideJudgedVisual(JudgmentGrade.Pending, 1.1f),
            "An unresolved note must not be hidden by the judgment mask");
    }

    static void ValidateJudgmentRules()
    {
        ValidateChunithmJudgmentWindows();
        ValidateTapJackProtection();
        Validate225BpmEighthProtection();
        ValidateLatencyCalibration();

        var score = new ScoreState();
        score.Register(JudgmentGrade.Good);
        Require(score.Combo == 1 && Math.Abs(score.AccuracyPercent(1) - 50) < .0001, "Good must preserve combo and score 50%");
        score.Register(JudgmentGrade.Miss);
        Require(score.Combo == 0, "Only Miss should break combo");
        score.Reset();
        score.Register(JudgmentGrade.Perfect);
        Require(Math.Abs(score.AccuracyPercent(1) - 101) < .0001, "Perfect must score 101%");

        var closeA = Note(1, 1.000, 0);
        var closeB = Note(2, 1.160, 0);
        var engine = new JudgmentEngine(new[] { closeA, closeB }, new ScoreState());
        engine.Process(1.09, new[] { new InputToken(1, RuntimeNoteKind.Tap, 1.09, 0) }, Array.Empty<ActiveContact>());
        Require(closeB.Grade != JudgmentGrade.Pending && closeA.Grade == JudgmentGrade.Pending,
            "Judgment protection must route an outer-window input to the closer overlapping note");

        var left = Note(3, 2, -2);
        var right = Note(4, 2, 2);
        engine = new JudgmentEngine(new[] { left, right }, new ScoreState());
        engine.Process(2, new[] { new InputToken(1, RuntimeNoteKind.Tap, 2, -2), new InputToken(2, RuntimeNoteKind.Tap, 2, 2) }, Array.Empty<ActiveContact>());
        Require(left.Grade == JudgmentGrade.Perfect && right.Grade == JudgmentGrade.Perfect, "Batched multi-touch matching failed");

        var overlapA = Note(20, 2.5, 1);
        var overlapB = Note(21, 2.5, 1);
        engine = new JudgmentEngine(new[] { overlapA, overlapB }, new ScoreState());
        engine.Process(2.5, new[] { new InputToken(1, RuntimeNoteKind.Tap, 2.5, 1) }, Array.Empty<ActiveContact>());
        Require((overlapA.Grade == JudgmentGrade.Perfect) != (overlapB.Grade == JudgmentGrade.Perfect),
            "One discrete activation must not consume two geometrically overlapping notes");

        overlapA = Note(22, 2.6, 1);
        overlapB = Note(23, 2.6, 1);
        engine = new JudgmentEngine(new[] { overlapA, overlapB }, new ScoreState());
        engine.Process(2.6, new[]
        {
            new InputToken(1, RuntimeNoteKind.Tap, 2.6, 1),
            new InputToken(2, RuntimeNoteKind.Tap, 2.6, 1),
        }, Array.Empty<ActiveContact>());
        Require(overlapA.Grade == JudgmentGrade.Perfect && overlapB.Grade == JudgmentGrade.Perfect,
            "Two contacts must be able to consume two geometrically overlapping notes");

        var overlapSustainA = Note(24, 2.7, 1);
        overlapSustainA.Kind = RuntimeNoteKind.Sustain;
        var overlapSustainB = Note(25, 2.7, 1);
        overlapSustainB.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { overlapSustainA, overlapSustainB }, new ScoreState());
        engine.Process(2.7, Array.Empty<InputToken>(), new[] { new ActiveContact(1, 1, 2.5) });
        Require(overlapSustainA.Grade == JudgmentGrade.Perfect && overlapSustainB.Grade == JudgmentGrade.Perfect,
            "One continuous contact must satisfy overlapping Hold checkpoints without discrete matching limits");

        var release = Note(5, 3, 0);
        release.Kind = RuntimeNoteKind.Release;
        engine = new JudgmentEngine(new[] { release }, new ScoreState());
        engine.Process(3, new[] { new InputToken(1, RuntimeNoteKind.Tap, 3, 0) }, Array.Empty<ActiveContact>());
        Require(release.Grade == JudgmentGrade.Pending, "A Hold release tail must not consume a tap input");
        engine.Process(3, Array.Empty<InputToken>(), new[] { new ActiveContact(1, 0, 2.5) });
        Require(release.Grade == JudgmentGrade.Perfect, "A Hold release tail must complete from sustained coverage without a release input");

        var traceTail = Note(6, 4, 0);
        traceTail.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { traceTail }, new ScoreState());
        engine.Process(4, Array.Empty<InputToken>(), new[] { new ActiveContact(1, 0, 3.5) });
        Require(traceTail.Grade == JudgmentGrade.Perfect, "A Trace Hold tail must complete from sustained coverage without a release input");

        var pathTail = Note(7, 5, 0);
        pathTail.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { pathTail }, new ScoreState());
        engine.Process(5.05, Array.Empty<InputToken>(), Array.Empty<ActiveContact>(), new[]
        {
            new ContactPathSegment(1, 5, 5.05, -2, 2, false),
        });
        Require(pathTail.Grade == JudgmentGrade.Perfect,
            "A Hold checkpoint crossed by a post-note contact path must complete at low frame rate");

        var earlyPath = Note(8, 6, 0);
        earlyPath.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { earlyPath }, new ScoreState());
        engine.Process(6, Array.Empty<InputToken>(), Array.Empty<ActiveContact>(), new[]
        {
            new ContactPathSegment(1, 5.7, 5.8, -2, 2, false),
        });
        Require(earlyPath.Grade == JudgmentGrade.Pending,
            "A Hold path that finishes before the checkpoint time must not be consumed early");

        var recentRelease = Note(81, 6.5, 0);
        recentRelease.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { recentRelease }, new ScoreState());
        engine.Process(6.47, Array.Empty<InputToken>(), Array.Empty<ActiveContact>(), new[]
        {
            new ContactPathSegment(1, 6.34, 6.47, 0, 0, true),
        });
        engine.Process(6.5, Array.Empty<InputToken>(), Array.Empty<ActiveContact>());
        Require(recentRelease.Grade == JudgmentGrade.Perfect,
            "A Hold checkpoint must retain recent pre-tick coverage after the contact releases");

        var recoveryA = Note(9, 7, 0);
        recoveryA.Kind = RuntimeNoteKind.Sustain;
        var recoveryB = Note(10, 7.2, 0);
        recoveryB.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { recoveryA, recoveryB }, new ScoreState());
        engine.Process(7.12, Array.Empty<InputToken>(), Array.Empty<ActiveContact>());
        engine.Process(7.2, Array.Empty<InputToken>(), new[] { new ActiveContact(1, 0, 7.2) });
        Require(recoveryA.Grade == JudgmentGrade.Miss && recoveryB.Grade == JudgmentGrade.Perfect,
            "A newly pressed contact must recover a later Hold checkpoint after an earlier miss");

        var holdChart = new RuntimeChart();
        var holdHead = Note(30, 0, -1);
        holdHead.Beat = 0;
        var holdMid = Note(31, 1, 0);
        holdMid.Beat = 1;
        holdMid.Kind = RuntimeNoteKind.Sustain;
        holdMid.Archetype = "SlideTickNote";
        var holdTail = Note(32, 2, 1);
        holdTail.Beat = 2;
        holdTail.Kind = RuntimeNoteKind.Release;
        holdChart.Notes.AddRange(new[] { holdHead, holdMid, holdTail });
        holdChart.Connectors.Add(new RuntimeConnector { Start = holdHead, End = holdMid });
        holdChart.Connectors.Add(new RuntimeConnector { Start = holdMid, End = holdTail });
        HoldCheckpointBuilder.Apply(holdChart, beat => beat);
        var autoCheckpoints = holdChart.Notes.Where(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto).OrderBy(note => note.Beat).ToArray();
        Require(autoCheckpoints.Length == 3 && autoCheckpoints.Select(note => note.Beat).SequenceEqual(new[] { .5, 1d, 1.5 }),
            "Every Hold must create one invisible Auto checkpoint per eighth note before its tail");
        Require(holdMid.HoldCheckpointSource == HoldCheckpointSource.Mid && holdMid.Judged,
            "Each authored Hold mid must remain an independent judged checkpoint");
        Require(holdTail.Judged && holdTail.Kind == RuntimeNoteKind.Sustain && holdTail.IsHoldTerminal &&
                holdTail.HoldCheckpointSource == HoldCheckpointSource.Tail,
            "A judged non-Flick Hold tail, including a legacy Release, must become a Sustain Tail checkpoint");

        var tempoChangingHold = new RuntimeChart();
        var tempoChangingHead = Note(40, 3, 0); tempoChangingHead.Beat = 3;
        var tempoChangingTail = Note(41, 5, 0); tempoChangingTail.Beat = 5;
        tempoChangingHold.Notes.AddRange(new[] { tempoChangingHead, tempoChangingTail });
        tempoChangingHold.Connectors.Add(new RuntimeConnector { Start = tempoChangingHead, End = tempoChangingTail });
        var changingTempo = new BeatTimeMap(new[] { new BeatBpm(0, 180), new BeatBpm(4, 240) });
        HoldCheckpointBuilder.Apply(tempoChangingHold, changingTempo.SecondsAt);
        var changingCheckpoints = tempoChangingHold.Notes.Where(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto).OrderBy(note => note.Beat).ToArray();
        Require(changingCheckpoints.Select(note => note.Beat).SequenceEqual(new[] { 3.5d, 4d, 4.5d }) &&
                Math.Abs(changingCheckpoints[1].Time - changingCheckpoints[0].Time - (60d / 180 / 2)) < 1e-7 &&
                Math.Abs(changingCheckpoints[2].Time - changingCheckpoints[1].Time - (60d / 240 / 2)) < 1e-7,
            "Hold eighth-note checkpoints must use the BPM in effect at each chart segment");

        var tailMidChart = new RuntimeChart();
        var tailMidHead = Note(33, 0, 0);
        tailMidHead.Beat = 0;
        var tailMid = Note(34, 1, 1);
        tailMid.Beat = 1;
        tailMid.Kind = RuntimeNoteKind.Sustain;
        tailMid.Archetype = "SlideTickNote";
        tailMidChart.Notes.AddRange(new[] { tailMidHead, tailMid });
        tailMidChart.Connectors.Add(new RuntimeConnector { Start = tailMidHead, End = tailMid });
        HoldCheckpointBuilder.Apply(tailMidChart, beat => beat);
        Require(tailMid.Judged && tailMid.IsHoldTerminal && tailMid.HoldCheckpointSource == HoldCheckpointSource.Tail,
            "An authored node at a Hold tail must retain Tail rather than Mid checkpoint metadata");

        engine = new JudgmentEngine(holdChart.Notes, new ScoreState());
        engine.Process(.5, Array.Empty<InputToken>(), new[] { new ActiveContact(1, -.5f, .5) });
        engine.Process(1, Array.Empty<InputToken>(), new[] { new ActiveContact(1, 0, .8) });
        Require(autoCheckpoints[0].Grade == JudgmentGrade.Perfect && autoCheckpoints[1].Grade == JudgmentGrade.Perfect &&
                holdMid.Grade == JudgmentGrade.Perfect && holdHead.Grade == JudgmentGrade.Miss,
            "A middle press must independently hit auto and authored-mid Hold checkpoints after a missed head");

        var flickTail = Note(11, 8, 0);
        flickTail.Kind = RuntimeNoteKind.Flick;
        var flickTailInputs = new List<InputToken>();
        var flickSlider = new VirtualSliderInput();
        flickSlider.Begin(1, 7.95, -.2f, flickTailInputs);
        flickSlider.Move(1, 8, .15f, flickTailInputs);
        engine = new JudgmentEngine(new[] { flickTail }, new ScoreState());
        engine.Process(8, flickTailInputs, Array.Empty<ActiveContact>());
        Require(flickTail.Grade == JudgmentGrade.Perfect,
            "A Flick Hold tail must resolve from a 0.35-lane Flick activation");

        ValidateVirtualSlider();
        ValidateJudgmentDebugGrid();
    }

    static void ValidateChunithmJudgmentWindows()
    {
        var tap = Note(720, 1, 0);
        Require(JudgmentEngine.GradeFor(tap, -2.0 / 60.0) == JudgmentGrade.Perfect, "Tap JC early edge");
        Require(JudgmentEngine.GradeFor(tap, 2.0 / 60.0) == JudgmentGrade.Perfect, "Tap JC late edge");
        Require(JudgmentEngine.GradeFor(tap, -2.0 / 60.0 - .0001) == JudgmentGrade.Great, "Tap Justice begins early");
        Require(JudgmentEngine.GradeFor(tap, 2.0 / 60.0 + .0001) == JudgmentGrade.Great, "Tap Justice begins late");
        Require(JudgmentEngine.GradeFor(tap, -4.0 / 60.0) == JudgmentGrade.Great, "Tap Justice edge early");
        Require(JudgmentEngine.GradeFor(tap, 4.0 / 60.0) == JudgmentGrade.Great, "Tap Justice edge late");
        Require(JudgmentEngine.GradeFor(tap, -4.0 / 60.0 - .0001) == JudgmentGrade.Good, "Tap Attack begins early");
        Require(JudgmentEngine.GradeFor(tap, 4.0 / 60.0 + .0001) == JudgmentGrade.Good, "Tap Attack begins late");
        Require(JudgmentEngine.GradeFor(tap, -6.0 / 60.0) == JudgmentGrade.Good, "Tap Attack edge early");
        Require(JudgmentEngine.GradeFor(tap, 6.0 / 60.0) == JudgmentGrade.Good, "Tap Attack edge late");
        Require(JudgmentEngine.GradeFor(tap, -6.0 / 60.0 - .0001) == JudgmentGrade.Pending, "Tap outside Attack early");
        Require(JudgmentEngine.GradeFor(tap, 6.0 / 60.0 + .0001) == JudgmentGrade.Pending, "Tap outside Attack late");

        var critical = Note(721, 2, 0);
        critical.Critical = true;
        Require(JudgmentEngine.GradeFor(critical, -6.0 / 60.0) == JudgmentGrade.Perfect, "Critical Tap early edge");
        Require(JudgmentEngine.GradeFor(critical, 6.0 / 60.0) == JudgmentGrade.Perfect, "Critical Tap late edge");
        Require(JudgmentEngine.GradeFor(critical, -6.0 / 60.0 - .0001) == JudgmentGrade.Pending, "Critical Tap outside early");
        Require(JudgmentEngine.GradeFor(critical, 6.0 / 60.0 + .0001) == JudgmentGrade.Pending, "Critical Tap outside late");

        var flick = Note(722, 3, 0);
        flick.Kind = RuntimeNoteKind.Flick;
        Require(JudgmentEngine.GradeFor(flick, -6.0 / 60.0) == JudgmentGrade.Perfect, "Flick early edge");
        Require(JudgmentEngine.GradeFor(flick, 2.0 / 60.0) == JudgmentGrade.Perfect, "Flick JC edge");
        Require(JudgmentEngine.GradeFor(flick, 4.0 / 60.0) == JudgmentGrade.Great, "Flick Justice edge");
        Require(JudgmentEngine.GradeFor(flick, 6.0 / 60.0) == JudgmentGrade.Good, "Flick Attack edge");
        Require(JudgmentEngine.GradeFor(flick, 6.0 / 60.0 + .0001) == JudgmentGrade.Pending, "Flick outside Attack");
    }

    static void ValidateTapJackProtection()
    {
        var intervalFirst = Note(740, 3.000, 0);
        var intervalMiddle = Note(741, 3.100, 0);
        var intervalLast = Note(742, 3.200, 0);
        var engine = new JudgmentEngine(new[] { intervalFirst, intervalMiddle, intervalLast }, new ScoreState());
        engine.Process(3.040, new[] { new InputToken(6, RuntimeNoteKind.Tap, 3.040, 0) }, Array.Empty<ActiveContact>());
        engine.Process(3.100, new[] { new InputToken(7, RuntimeNoteKind.Tap, 3.100, .6f) }, Array.Empty<ActiveContact>());
        engine.Process(3.160, new[] { new InputToken(8, RuntimeNoteKind.Tap, 3.160, 0) }, Array.Empty<ActiveContact>());
        Require(intervalFirst.Grade == JudgmentGrade.Great && intervalMiddle.Grade == JudgmentGrade.Perfect && intervalLast.Grade == JudgmentGrade.Great,
            "Three-note midpoint intervals must consume the first, middle, and last Tap exactly once");

        var left = Note(730, 1.000, 0); left.Size = 1;
        var right = Note(731, 1.120, 4f); right.Size = 1;
        engine = new JudgmentEngine(new[] { left, right }, new ScoreState());
        engine.Process(1.075, new[] { new InputToken(1, RuntimeNoteKind.Tap, 1.075, 0) }, Array.Empty<ActiveContact>());
        Require(left.Grade == JudgmentGrade.Good, "Lane forgiveness alone must not form a protection pair");

        left = Note(744, 1.300, 0); left.Size = 1;
        right = Note(745, 1.420, 3.1f); right.Size = 1;
        engine = new JudgmentEngine(new[] { left, right }, new ScoreState());
        engine.Process(1.375, new[] { new InputToken(8, RuntimeNoteKind.Tap, 1.375, 1.3f) }, Array.Empty<ActiveContact>());
        Require(left.Grade == JudgmentGrade.Pending && right.Grade == JudgmentGrade.Great,
            "A rubbed candidate on the correct side must still resolve to the closer Tap");

        var rubFirst = Note(756, 9.000, 0); rubFirst.Size = 1;
        var rubLater = Note(757, 9.120, 3.1f); rubLater.Size = 1;
        engine = new JudgmentEngine(new[] { rubFirst, rubLater }, new ScoreState());
        engine.Process(8.910, new[] { new InputToken(14, RuntimeNoteKind.Tap, 8.910, 0) }, Array.Empty<ActiveContact>());
        engine.Process(9.030, new[] { new InputToken(14, RuntimeNoteKind.Tap, 9.030, 3.1f) }, Array.Empty<ActiveContact>());
        Require(rubFirst.Grade == JudgmentGrade.Good && rubLater.Grade == JudgmentGrade.Pending,
            "A Good at the start of a rubbed Tap train must keep the next forgiveness-overlap Tap protected");

        var edgeLeft = Note(732, 2.000, 0); edgeLeft.Size = 1;
        var edgeRight = Note(733, 2.120, 2); edgeRight.Size = 1;
        engine = new JudgmentEngine(new[] { edgeLeft, edgeRight }, new ScoreState());
        engine.Process(2.055, new[] { new InputToken(2, RuntimeNoteKind.Tap, 2.055, 2.5f) }, Array.Empty<ActiveContact>());
        Require(edgeLeft.Grade == JudgmentGrade.Pending && edgeRight.Grade == JudgmentGrade.Pending,
            "Touching playable input regions must form a protection pair");

        var wideEarly = Note(734, 3.000, 0); wideEarly.Size = 2;
        var narrowLate = Note(735, 3.120, 2.5f); narrowLate.Size = 1;
        engine = new JudgmentEngine(new[] { wideEarly, narrowLate }, new ScoreState());
        engine.Process(3.055, new[] { new InputToken(3, RuntimeNoteKind.Tap, 3.055, 3.1f) }, Array.Empty<ActiveContact>());
        Require(wideEarly.Grade == JudgmentGrade.Pending && narrowLate.Grade == JudgmentGrade.Pending,
            "Justice before midpoint must be protected across the whole later Tap width");

        wideEarly = Note(746, 3.500, 0); wideEarly.Size = 2;
        narrowLate = Note(747, 3.660, 2.5f); narrowLate.Size = 1;
        engine = new JudgmentEngine(new[] { wideEarly, narrowLate }, new ScoreState());
        engine.Process(3.575, new[] { new InputToken(9, RuntimeNoteKind.Tap, 3.575, 3.1f) }, Array.Empty<ActiveContact>());
        Require(wideEarly.Grade == JudgmentGrade.Pending && narrowLate.Grade == JudgmentGrade.Pending,
            "Attack before midpoint must be protected across the whole later Tap width");

        var jcEarly = Note(736, 4.000, 0); jcEarly.Size = 2;
        var jcLate = Note(737, 4.040, 2.5f); jcLate.Size = 1;
        engine = new JudgmentEngine(new[] { jcEarly, jcLate }, new ScoreState());
        engine.Process(4.015, new[] { new InputToken(4, RuntimeNoteKind.Tap, 4.015, 3.1f) }, Array.Empty<ActiveContact>());
        Require(jcEarly.Grade == JudgmentGrade.Pending && jcLate.Grade == JudgmentGrade.Perfect,
            "JC outside the shared span must preserve the later Tap's full interval");

        var sharedEarly = Note(738, 5.000, 0); sharedEarly.Size = 2;
        var sharedLate = Note(739, 5.040, 2.5f); sharedLate.Size = 1;
        engine = new JudgmentEngine(new[] { sharedEarly, sharedLate }, new ScoreState());
        engine.Process(5.015, new[] { new InputToken(5, RuntimeNoteKind.Tap, 5.015, 1.75f) }, Array.Empty<ActiveContact>());
        Require(sharedEarly.Grade == JudgmentGrade.Perfect && sharedLate.Grade == JudgmentGrade.Pending,
            "JC inside the shared span must be trimmed at the midpoint");

        var criticalEarly = Note(750, 6.000, 0); criticalEarly.Size = 2;
        var criticalLate = Note(751, 6.120, 2.5f); criticalLate.Size = 1; criticalLate.Critical = true;
        engine = new JudgmentEngine(new[] { criticalEarly, criticalLate }, new ScoreState());
        engine.Process(6.055, new[] { new InputToken(10, RuntimeNoteKind.Tap, 6.055, 3.1f) }, Array.Empty<ActiveContact>());
        Require(criticalEarly.Grade == JudgmentGrade.Pending && criticalLate.Grade == JudgmentGrade.Pending,
            "Critical Tap's hidden Justice band must be protected across its whole width");

        var flickEarly = Note(752, 7.000, 0); flickEarly.Size = 2;
        var flickLate = Note(753, 7.120, 2.5f); flickLate.Size = 1; flickLate.Kind = RuntimeNoteKind.Flick;
        engine = new JudgmentEngine(new[] { flickEarly, flickLate }, new ScoreState());
        engine.Process(7.055, new[] { new InputToken(11, RuntimeNoteKind.Flick, 7.055, 3.1f, 2.8f, 7.055) }, Array.Empty<ActiveContact>());
        Require(flickEarly.Grade == JudgmentGrade.Pending && flickLate.Grade == JudgmentGrade.Pending,
            "Early Flick's hidden Justice band must be protected across its whole width");

        var resolvedEarly = Note(754, 8.000, 0); resolvedEarly.Size = 1;
        var resolvedLate = Note(755, 8.120, 0); resolvedLate.Size = 1;
        engine = new JudgmentEngine(new[] { resolvedEarly, resolvedLate }, new ScoreState());
        engine.Process(8.000, new[] { new InputToken(12, RuntimeNoteKind.Tap, 8.000, 0) }, Array.Empty<ActiveContact>());
        engine.Process(8.055, new[] { new InputToken(13, RuntimeNoteKind.Tap, 8.055, 0) }, Array.Empty<ActiveContact>());
        Require(resolvedEarly.Grade == JudgmentGrade.Perfect && resolvedLate.Grade == JudgmentGrade.Pending,
            "A resolved earlier Tap must keep protecting the later Tap's early Justice interval");
    }

    static void ValidateLatencyCalibration()
    {
        var audioOffset = SonolusLandscapePrototype.CalibrationAudioOffsetForTap(4.906077, 4.8);
        Require(Math.Abs(audioOffset + .106077) < .000001,
            "A late tap must be compared with its assigned fourth audible beat");
        var earlyOffset = SonolusLandscapePrototype.CalibrationAudioOffsetForTap(4.694, 4.8);
        Require(Math.Abs(earlyOffset - .106) < .000001,
            "An early tap must delay audio by its measured difference from the assigned beat");
        Require(SonolusLandscapePrototype.SanitizeAudioOffset(4.906077) == 0,
            "An impossible persisted audio offset must reset to zero");
        Require(Math.Abs(SonolusLandscapePrototype.SanitizeAudioOffset(.106077) - .106077) < .000001,
            "A plausible persisted audio offset must be preserved");
        Require(SonolusLandscapePrototype.SanitizeAudioOffset(.31) == 0,
            "Manual audio offset must not exceed the 300 ms safety limit");
        Require(!SonolusLandscapePrototype.CanAdjustAudioOffsetManually(true),
            "Manual audio offset controls must be disabled while calibration ticks are scheduled");
        Require(SonolusLandscapePrototype.CanAdjustAudioOffsetManually(false),
            "Manual audio offset controls must be available outside calibration");
    }

    static void ValidateAutoPlay()
    {
        var tap = Note(500, 1, 0);
        var flick = Note(501, 1, 2); flick.Kind = RuntimeNoteKind.Flick;
        var sustain = Note(502, 1, -2); sustain.Kind = RuntimeNoteKind.Sustain;
        var release = Note(503, 1, 4); release.Kind = RuntimeNoteKind.Release;
        var future = Note(504, 2, 0);
        var engine = new JudgmentEngine(new[] { tap, flick, sustain, release, future }, new ScoreState());
        engine.Process(1, new[] { new InputToken(1, RuntimeNoteKind.Tap, 1, 99) }, Array.Empty<ActiveContact>(), Array.Empty<ContactPathSegment>(), true);
        Require(tap.Grade == JudgmentGrade.Perfect && flick.Grade == JudgmentGrade.Perfect &&
                sustain.Grade == JudgmentGrade.Perfect && release.Grade == JudgmentGrade.Perfect && future.Grade == JudgmentGrade.Pending,
            "Auto Play 必須在音符時間以 Perfect 結算所有可判定音符，並忽略玩家輸入");
    }

    static void ValidateAudioDeviceRecovery()
    {
        Require(Math.Abs(AudioDeviceRecovery.ChartAnchorDspForAudioOffset(400.25, -.1) - 400.35) < .0001,
            "An early audio offset must add chart lead-in before playback can be scheduled");
        Require(Math.Abs(AudioDeviceRecovery.ChartAnchorDspForAudioOffset(400.25, .1) - 400.25) < .0001,
            "A late audio offset must not delay the chart clock");
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(12.5, .3, .1, 60) - 12.7) < .0001,
            "A positive audio offset must seek the BGM earlier at the same chart time");
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(12.5, .3, -.1, 60) - 12.9) < .0001,
            "A negative audio offset must seek the BGM later at the same chart time");
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(-.4, .3, 0, 60)) < .0001,
            "Audio recovery must not seek before the start of a clip");
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(100, 0, 0, 60) - 60) < .0001,
            "Audio recovery must not seek past the end of a clip");
        Require(Math.Abs(AudioDeviceRecovery.ScheduledDspForChartTime(400.25, 12.5, .3) - 387.45) < .0001,
            "Audio recovery must rebuild a DSP schedule that preserves chart time");
        Require(SonolusLandscapePrototype.ShouldPauseForAudioConfigurationChange(true, true, false),
            "An active unpaused game must pause after an output-device change");
        Require(!SonolusLandscapePrototype.ShouldPauseForAudioConfigurationChange(true, false, false),
            "An idle game must ignore an output-device change");
        Require(!SonolusLandscapePrototype.ShouldPauseForAudioConfigurationChange(true, true, true),
            "An already paused game must not restart its pause flow");
        Require(!SonolusLandscapePrototype.ShouldPauseForAudioConfigurationChange(false, true, false),
            "Non-device audio configuration changes must not interrupt gameplay");
        Require(AudioDeviceRecovery.ShouldRescheduleAfterAudioInterruption(true),
            "An audio interruption must rebuild its schedule instead of unpausing the old one");
        Require(!AudioDeviceRecovery.ShouldRescheduleAfterAudioInterruption(false),
            "A normal manual pause must retain the existing unpause path");
        Require(Math.Abs(AudioDeviceRecovery.PlaybackDspForChartTime(400, -.4, .3, .1) - 400.2) < .0001,
            "Audio recovery must delay playback until a pre-roll chart time reaches the BGM start");
        Require(Math.Abs(AudioDeviceRecovery.ScheduledDspForPlayback(400.1, 0) - 400.1) < .0001,
            "Pre-roll recovery must keep the chart clock silent until clip playback begins");
        Require(Math.Abs(AudioDeviceRecovery.ScheduledDspForRecovery(400, 100, 0) - 300) < .0001,
            "Audio recovery must preserve chart time even after clip playback reaches its end");
    }

    static void ValidateVirtualSlider()
    {
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();
        Require(VirtualSliderInput.CellCount == 24 && VirtualSliderInput.CellAt(VirtualSliderInput.MinimumLane) == 0 &&
                VirtualSliderInput.CellAt(VirtualSliderInput.MaximumLane) == VirtualSliderInput.CellCount - 1 &&
                VirtualSliderInput.CellAt(-5.5f) == 1 &&
                Math.Abs(VirtualSliderInput.CellCenter(1) + 5.25f) < .0001,
            "The 12-lane slider must split each lane into two 0.5-lane cells including both outer edges");
        slider.Begin(1, 1, -5.5f, inputs);
        Require(inputs.Count == 1 && Math.Abs(inputs[0].Lane + 5.25f) < .0001,
            "Initial slider contact must emit one Tap activation");

        slider.Move(1, 1.005, -5.4f, inputs);
        Require(inputs.Count == 1, "Motion inside one slider cell must not retrigger Tap");

        slider.Move(1, 1.02, -2.5f, inputs);
        var crossedTaps = inputs.Where(input => input.Kind == RuntimeNoteKind.Tap).ToArray();
        Require(crossedTaps.Length == 7 && crossedTaps.Skip(1).Select(input => input.Lane).SequenceEqual(new[] { -4.75f, -4.25f, -3.75f, -3.25f, -2.75f, -2.25f }),
            "A rub must activate every newly entered slider cell exactly once");

        slider.Move(1, 1.1, -3.5f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 9 && Math.Abs(inputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane + 3.25f) < .0001,
            "Leaving and re-entering a half-lane slider cell must reactivate it");

        slider.End(1, 1.11, -3.5f, inputs);
        slider.Begin(1, 2, 0, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 10, "A new contact must activate its initial slider cell");

        var extendedInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(13, 2.1, -7f, extendedInputs);
        Require(extendedInputs.Count == 1 && Math.Abs(extendedInputs[0].Lane + 6.75f) < .0001f,
            "The virtual slider must emit taps for perspective lanes beyond the visible central track");

        var releaseInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(12, 2.2, -1.5f, releaseInputs);
        releaseInputs.Clear();
        slider.End(12, 2.21, 1.5f, releaseInputs);
        Require(releaseInputs.Count == 0,
            "TouchUp must only release contact ownership and must not emit Tap or Flick while the finger leaves");
        slider.Begin(12, 2.22, 1.5f, releaseInputs);
        Require(releaseInputs.Count == 1 && releaseInputs[0].Kind == RuntimeNoteKind.Tap,
            "A new TouchDown after release must activate normally");

        var jitterInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(4, 2.5, -5.5f, jitterInputs);
        slider.Move(4, 2.505, -4.99f, jitterInputs);
        slider.Move(4, 2.510, -5.01f, jitterInputs);
        slider.Move(4, 2.550, -4.99f, jitterInputs);
        slider.Move(4, 2.560, -5.01f, jitterInputs);
        Require(jitterInputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 5,
            "Every slider-cell re-entry must emit a Tap without time or departure throttling");

        var reentryInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(5, 3, -5.5f, reentryInputs);
        slider.Move(5, 3.005, -4.85f, reentryInputs);
        slider.Move(5, 3.010, -5.01f, reentryInputs);
        Require(reentryInputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 3 && Math.Abs(reentryInputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane + 5.25f) < .0001,
            "A half-lane slider cell re-entry must emit a Tap");

        var flickThreshold = new List<InputToken>();
        slider.Reset();
        slider.Begin(6, 4, -5.5f, flickThreshold);
        slider.Move(6, 4.01, -5.151f, flickThreshold);
        Require(!flickThreshold.Any(input => input.Kind == RuntimeNoteKind.Flick),
            "Moving 0.349 lanes must not activate Flick");
        slider.Move(6, 4.02, -5.15f, flickThreshold);
        Require(flickThreshold.Count(input => input.Kind == RuntimeNoteKind.Flick) == 1,
            "Moving 0.35 lanes from the Flick anchor must activate exactly one Flick");

        var oppositeFlick = new List<InputToken>();
        slider.Reset();
        slider.Begin(7, 4.1, -5.15f, oppositeFlick);
        slider.Move(7, 4.2, -5.5f, oppositeFlick);
        Require(oppositeFlick.Count(input => input.Kind == RuntimeNoteKind.Flick) == 1,
            "A leftward 0.35-lane motion must activate Flick without direction filtering");

        var longFlick = new List<InputToken>();
        slider.Reset();
        slider.Begin(8, 4.3, -5.5f, longFlick);
        slider.Move(8, 4.44, -4.1f, longFlick);
        var longFlicks = longFlick.Where(input => input.Kind == RuntimeNoteKind.Flick).ToArray();
        Require(longFlicks.Length == 4 && longFlicks.Zip(longFlicks.Skip(1), (a, b) => a.Time < b.Time).All(value => value),
            "A long motion must emit every interpolated 0.35-lane Flick threshold");

        var flickJitter = new List<InputToken>();
        slider.Reset();
        slider.Begin(9, 4.5, -5.5f, flickJitter);
        slider.Move(9, 4.6, -5.3f, flickJitter);
        slider.Move(9, 4.7, -5.5f, flickJitter);
        slider.Move(9, 4.8, -5.3f, flickJitter);
        Require(!flickJitter.Any(input => input.Kind == RuntimeNoteKind.Flick),
            "Sub-threshold oscillation must not accumulate into Flick activation");

        var outsideSweep = new List<InputToken>();
        slider.Reset();
        slider.Begin(3, 2.1, -6, outsideSweep);
        slider.Move(3, 2.2, 6, outsideSweep);
        Require(outsideSweep.Count(input => input.Kind == RuntimeNoteKind.Tap) == VirtualSliderInput.CellCount,
            "A low-frame-rate sweep across the whole slider must not skip cells");

        var rubNotes = new[]
        {
            Note(10, 3 + 1d / 30d, -4.75f),
            Note(11, 3.10, -3.75f),
            Note(12, 3 + 1d / 6d, -2.75f),
        };
        var rubInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(2, 3, -5.5f, rubInputs);
        slider.Move(2, 3.2, -2.5f, rubInputs);
        var rubEngine = new JudgmentEngine(rubNotes, new ScoreState());
        rubEngine.Process(3.2, rubInputs, Array.Empty<ActiveContact>());
        Require(rubNotes.All(note => note.Grade == JudgmentGrade.Perfect),
            "A timed rub must match Tap notes in every crossed slider cell");

        var forgivenessPriority = Note(13, 5, 0);
        var priorityEngine = new JudgmentEngine(new[] { forgivenessPriority }, new ScoreState());
        priorityEngine.Process(5.070,
            new[]
            {
                new InputToken(10, RuntimeNoteKind.Tap, 5.000, 1.2f),
                new InputToken(11, RuntimeNoteKind.Tap, 5.070, 0),
            },
            Array.Empty<ActiveContact>());
        Require(forgivenessPriority.Grade == JudgmentGrade.Perfect,
            "A better forgiveness candidate must not be discarded for a worse authored-span candidate in the same batch");

        var multiFingerA = Note(14, 6, 0);
        var multiFingerB = Note(15, 6, 0);
        var multiFingerEngine = new JudgmentEngine(new[] { multiFingerA, multiFingerB }, new ScoreState());
        multiFingerEngine.Process(6,
            new[]
            {
                new InputToken(20, RuntimeNoteKind.Tap, 6, 0),
                new InputToken(21, RuntimeNoteKind.Tap, 6, 1.2f),
            },
            Array.Empty<ActiveContact>());
        Require(multiFingerA.Grade == JudgmentGrade.Perfect && multiFingerB.Grade == JudgmentGrade.Perfect,
            "An authored candidate from one finger must not remove another finger's forgiveness candidate");
    }

    static void Validate225BpmEighthProtection()
    {
        var eighthInterval = 60d / 225d / 2d;
        foreach (var inputOffset in new[] { .040d, .090d })
        {
            var first = Note(780, 10, 0);
            var second = Note(781, 10 + eighthInterval, 0);
            var engine = new JudgmentEngine(new[] { first, second }, new ScoreState());
            var results = engine.Process(10 + inputOffset,
                new[] { new InputToken(1, RuntimeNoteKind.Tap, 10 + inputOffset, 0) }, Array.Empty<ActiveContact>());
            Require(results.All(result => result.Grade != JudgmentGrade.Good) &&
                    first.Grade != JudgmentGrade.Good && second.Grade != JudgmentGrade.Good,
                "225 BPM eighth-note Tap protection must not produce Good inside the overlapping same-lane windows");
        }
    }

    static void ValidateJudgmentDebugGrid()
    {
        Require(SonolusLandscapePrototype.JudgmentDebugCellCount == 24,
            "Judgment debug grid must expose one cell for every half lane");
        Require(Math.Abs(SonolusLandscapePrototype.JudgmentDebugCellWidth - .5f) < .0001f,
            "Judgment debug grid cells must remain half a lane wide");
        foreach (var lane in new[] { -6f, -3f, 0f, 3f, 6f })
            Require(Math.Abs(SonolusLandscapePrototype.InputLaneAtCanvasX(
                    SonolusLandscapePrototype.JudgmentLaneCanvasX(lane)) - lane) < .0001f,
                "Input lane conversion must invert the rendered perspective lane geometry");
        Require(Math.Abs(SonolusLandscapePrototype.InputLaneAtCanvasX(-960f) + 6f) < .0001f &&
                Math.Abs(SonolusLandscapePrototype.InputLaneAtCanvasX(960f) - 6f) < .0001f,
            "The far left and right of the touch region must clamp to the -6 and +6 outer keys");
        Require(SonolusLandscapePrototype.ShouldContinueTrackedContact(true, false) &&
                SonolusLandscapePrototype.ShouldContinueTrackedContact(true, true) &&
                !SonolusLandscapePrototype.ShouldContinueTrackedContact(false, false),
            "A started Hold contact must continue outside the input band until it ends, while a new contact must begin inside it");
        Require(Math.Abs(SonolusLandscapePrototype.JudgmentInputBandHeight(732f) - 45f) < .0001f,
            "Each judgment input band must match the 45-pixel judgement strip");
        Require(SonolusLandscapePrototype.JudgmentInputGridRow(-111.5f, 732f) == 0 &&
                SonolusLandscapePrototype.JudgmentInputGridRow(-111.6f, 732f) == -1 &&
                SonolusLandscapePrototype.JudgmentInputGridRow(-156.6f, 732f) == -2,
            "Virtual touch rows must advance once per purple judgment-strip height");
        var inputTop = SonolusLandscapePrototype.JudgmentInputTop(732f);
        Require(Math.Abs(inputTop - 1f) < .001f &&
                SonolusLandscapePrototype.IsJudgmentInputBand(-366f, 732f) &&
                SonolusLandscapePrototype.IsJudgmentInputBand(inputTop, 732f) &&
                !SonolusLandscapePrototype.IsJudgmentInputBand(-366.1f, 732f) &&
                !SonolusLandscapePrototype.IsJudgmentInputBand(inputTop + .1f, 732f),
            "The input region must run from canvas bottom to three band heights above the judgment line");
        Require(SonolusLandscapePrototype.CanvasXAtInputLane(-6f) < -600f &&
                SonolusLandscapePrototype.CanvasXAtInputLane(6f) > 600f &&
                Math.Abs(SonolusLandscapePrototype.InputLaneAtCanvasX(
                    SonolusLandscapePrototype.CanvasXAtInputLane(-6f)) + 6f) < .0001f &&
                Math.Abs(SonolusLandscapePrototype.InputLaneAtCanvasX(
                    SonolusLandscapePrototype.CanvasXAtInputLane(6f)) - 6f) < .0001f,
            "The visible input region must align with the rendered outer lanes");
        Require(Math.Abs(SonolusLandscapePrototype.JudgmentDebugCanvasXAtLane(-6f) + 960f) < .0001f &&
                Math.Abs(SonolusLandscapePrototype.JudgmentDebugCanvasXAtLane(6f) - 960f) < .0001f,
            "The purple judgment debug region must retain the full canvas width");
        Require(Math.Abs(SonolusLandscapePrototype.InputLaneFeedbackDuration - .12f) < .0001f &&
                Math.Abs(SonolusLandscapePrototype.InputLaneFeedbackWidth - 1f) < .0001f &&
                Math.Abs(SonolusLandscapePrototype.InputLaneFeedbackTop(732f) -
                         SonolusLandscapePrototype.InputLaneFeedbackBottom(732f) -
                         SonolusLandscapePrototype.JudgmentInputBandHeight(732f)) < .0001f &&
                Math.Abs((SonolusLandscapePrototype.InputLaneFeedbackTop(732f) +
                          SonolusLandscapePrototype.InputLaneFeedbackBottom(732f)) * .5f -
                         (732f * .5f - 500f)) < .0001f &&
                SonolusLandscapePrototype.InputLaneFeedbackCell(-6f) == 0 &&
                SonolusLandscapePrototype.InputLaneFeedbackCell(6f) == VirtualSliderInput.CellCount - 1 &&
                SonolusLandscapePrototype.InputLaneFeedbackGridCell(0) == 0 &&
                SonolusLandscapePrototype.InputLaneFeedbackGridCell(1) == 0 &&
                SonolusLandscapePrototype.InputLaneFeedbackGridCell(2) == 1 &&
                SonolusLandscapePrototype.InputLaneFeedbackGridCell(VirtualSliderInput.CellCount - 1) == 11,
            "Input feedback must flash one perspective-aligned button grid for each pair of input half-cells");
    }

    static RuntimeNote Note(int index, double time, float lane) => new()
    {
        Index = index, Time = time, Lane = lane, Size = .5f, Kind = RuntimeNoteKind.Tap,
    };

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GUGARYTHM_VALIDATION_FAILED: " + message);
    }
}

static class GgrZipFixture
{
    public static byte[] Create(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var pair in entries)
            {
                var entry = archive.CreateEntry(pair.Key, System.IO.Compression.CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(pair.Value, 0, pair.Value.Length);
            }
        }
        return output.ToArray();
    }
}
