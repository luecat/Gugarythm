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
        ValidateGgrUscHoldRoots();
        ValidateAttachedGgrPlayableCount();
        ValidateUscSlideRoleClassification();
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
            Require(Resources.Load<Texture2D>($"NeonRhythm/official/buttons/button-{tone}") != null,
                $"Official button texture is missing: {tone}");
        foreach (var tone in new[] { "mint", "pink", "yellow" })
            Require(Resources.Load<Texture2D>($"NeonRhythm/official/traces/trace-{tone}") != null,
                $"Official trace texture is missing: {tone}");
        Require(Resources.Load<Texture2D>("NeonRhythm/official/damage/damage-purple") != null, "Official Damage texture is missing");
        foreach (var tone in new[] { "normal", "critical" })
        foreach (var direction in new[] { "center", "side" })
        foreach (var size in Enumerable.Range(1, 6))
            Require(Resources.Load<Texture2D>($"NeonRhythm/flicks/flick-{tone}-{direction}-{size}") != null,
                $"Flick texture is missing: {tone}-{direction}-{size}");
        Require(Resources.Load<Texture2D>("NeonRhythm/connectors/hold-green") != null, "Normal Hold connector texture is missing");
        Require(Resources.Load<Texture2D>("NeonRhythm/connectors/hold-yellow") != null, "Critical Hold connector texture is missing");
        Require(Resources.Load<Texture2D>("NeonRhythm/official/particles/slide-tick-mint") != null, "Official normal hold-mid particle is missing");
        Require(Resources.Load<Texture2D>("NeonRhythm/official/particles/slide-tick-yellow") != null, "Official critical hold-mid particle is missing");
        foreach (var tone in new[] { "mint", "pink", "yellow" })
            Require(Resources.Load<Texture2D>($"NeonRhythm/official/particles/trace-diamond-{tone}") != null,
                $"Official Trace diamond is missing: {tone}");
        Require(Resources.Load<Texture2D>("NeonRhythm/package/particles/pixel-atlas") != null,
            "SCP-derived Pixel judgment atlas is missing");
        foreach (var sound in new[] { "perfect", "great", "good", "alternative", "hold-loop", "stage" })
            Require(Resources.Load<AudioClip>($"NeonRhythm/package/audio/{sound}") != null,
                $"SCP-derived judgment sound is missing: {sound}");
        var holdLoop = Resources.Load<AudioClip>("NeonRhythm/package/audio/hold-loop");
        Require(holdLoop != null && holdLoop.channels == 1 && holdLoop.frequency == 44100,
            "Hold loop must be a mono 44.1 kHz resource for gapless Android playback");
        Require(Resources.Load<AudioClip>("NeonRhythm/package/audio/flick") != null,
            "Normal Flick sound is missing");
        Require(Resources.Load<AudioClip>("NeonRhythm/package/audio/critical-flick") != null,
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
        Debug.Log($"GUGARYTHM_VALIDATION_OK title={chart.Title} playable={chart.PlayableCount} connectors={chart.Connectors.Count} simLines={chart.SimLines.Count} guides={chart.Guides.Count} " +
                  $"normal={chart.Connectors.Count(value => !value.Critical)} critical={chart.Connectors.Count(value => value.Critical)} " +
                  $"warnings={chart.Warnings.Count} bgmBytes={chart.BgmBytes.Length}");
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
        RuntimeNote At(double beat) => nodes.Single(note => Math.Abs(note.Beat - beat) < 1e-9);

        var normalHead = At(0);
        var normalMid = At(.125);
        var normalTail = At(.25);
        var traceHead = At(1);
        var traceMid = At(1.125);
        var traceTail = At(1.25);
        var noneNodes = new[] { At(2), At(2.125), At(2.25) };
        var flickTail = At(3.25);
        var noneDirectionTail = At(4.25);
        var noneHead = At(5);
        var firstJudgedAfterNoneHead = At(5.125);
        var noneHeadTail = At(5.25);
        var terminalFirstJudgedAfterNoneHead = At(6.25);

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
            new ContactPathSegment(1, 5.8, 5.9, -2, 2, false),
        });
        Require(earlyPath.Grade == JudgmentGrade.Pending,
            "A Hold path that finishes before the checkpoint time must not be consumed early");

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
        // Forgiveness alone must not author a protection pair.
        var left = Note(730, 1.000, 0); left.Size = 1;
        var right = Note(731, 1.120, 3.1f); right.Size = 1;
        var engine = new JudgmentEngine(new[] { left, right }, new ScoreState());
        engine.Process(1.075,
            new[] { new InputToken(1, RuntimeNoteKind.Tap, 1.075, 0) },
            Array.Empty<ActiveContact>());
        Require(left.Grade == JudgmentGrade.Good,
            "Lane forgiveness alone must not form a protection pair");

        // The same authored gap must remain unprotected even where forgiveness overlaps both hitboxes.
        left = Note(744, 1.300, 0); left.Size = 1;
        right = Note(745, 1.420, 3.1f); right.Size = 1;
        engine = new JudgmentEngine(new[] { left, right }, new ScoreState());
        engine.Process(1.375,
            new[] { new InputToken(8, RuntimeNoteKind.Tap, 1.375, 1.3f) },
            Array.Empty<ActiveContact>());
        Require(left.Grade == JudgmentGrade.Good,
            "Forgiveness overlap without authored span overlap must not protect the earlier Tap");

        var first = Note(732, 2.000, 0);
        var second = Note(733, 2.140, 0);
        engine = new JudgmentEngine(new[] { first, second }, new ScoreState());
        engine.Process(2.190,
            new[] { new InputToken(2, RuntimeNoteKind.Tap, 2.190, 0) },
            Array.Empty<ActiveContact>());
        Require(first.Grade == JudgmentGrade.Pending && second.Grade == JudgmentGrade.Good,
            "Post-midpoint Attack must route to later protected Tap");

        // A wide Tap's unique lane must preserve its own Perfect/Great/Good windows.
        var wide = Note(734, 3.000, 0); wide.Size = 2;
        var separate = Note(735, 3.120, 3.5f); separate.Size = 1;
        engine = new JudgmentEngine(new[] { wide, separate }, new ScoreState());
        engine.Process(3.090,
            new[] { new InputToken(3, RuntimeNoteKind.Tap, 3.090, 2f) },
            Array.Empty<ActiveContact>());
        Require(wide.Grade == JudgmentGrade.Good && separate.Grade == JudgmentGrade.Pending,
            "A lane unique to a wide Tap must preserve its original Good window");

        // Shared geometry resolves Perfect to the nearer note on either side of midpoint.
        var sharedEarly = Note(736, 4.000, 0); sharedEarly.Size = 2;
        var sharedLate = Note(737, 4.120, 1.5f); sharedLate.Size = 1;
        engine = new JudgmentEngine(new[] { sharedEarly, sharedLate }, new ScoreState());
        engine.Process(4.020,
            new[] { new InputToken(4, RuntimeNoteKind.Tap, 4.020, 1.5f) },
            Array.Empty<ActiveContact>());
        Require(sharedEarly.Grade == JudgmentGrade.Perfect && sharedLate.Grade == JudgmentGrade.Pending,
            "Shared-span Perfect before midpoint must route to earlier Tap");

        sharedEarly = Note(738, 5.000, 0); sharedEarly.Size = 2;
        sharedLate = Note(739, 5.120, 1.5f); sharedLate.Size = 1;
        engine = new JudgmentEngine(new[] { sharedEarly, sharedLate }, new ScoreState());
        engine.Process(5.100,
            new[] { new InputToken(5, RuntimeNoteKind.Tap, 5.100, 1.5f) },
            Array.Empty<ActiveContact>());
        Require(sharedEarly.Grade == JudgmentGrade.Pending && sharedLate.Grade == JudgmentGrade.Perfect,
            "Shared-span Perfect after midpoint must route to later Tap");

        // A protection chain must route each activation to its adjacent note only.
        var chainA = Note(740, 6.000, 0);
        var chainB = Note(741, 6.100, 0);
        var chainC = Note(742, 6.200, 0);
        engine = new JudgmentEngine(new[] { chainA, chainB, chainC }, new ScoreState());
        engine.Process(6.060,
            new[] { new InputToken(6, RuntimeNoteKind.Tap, 6.060, 0) },
            Array.Empty<ActiveContact>());
        Require(chainA.Grade == JudgmentGrade.Pending && chainB.Grade == JudgmentGrade.Great && chainC.Grade == JudgmentGrade.Pending,
            "Three-note same-lane protection must route the first activation to the middle Tap");
        engine.Process(6.160,
            new[] { new InputToken(7, RuntimeNoteKind.Tap, 6.160, 0) },
            Array.Empty<ActiveContact>());
        Require(chainB.Grade == JudgmentGrade.Great && chainC.Grade == JudgmentGrade.Great,
            "Three-note same-lane protection must preserve the next adjacent route");

        var critical = Note(743, 7.000, 0); critical.Critical = true;
        Require(JudgmentEngine.GradeFor(critical, -6.0 / 60.0) == JudgmentGrade.Perfect &&
                JudgmentEngine.GradeFor(critical, 6.0 / 60.0) == JudgmentGrade.Perfect &&
                JudgmentEngine.GradeFor(critical, -6.0 / 60.0 - .0001) == JudgmentGrade.Pending &&
                JudgmentEngine.GradeFor(critical, 6.0 / 60.0 + .0001) == JudgmentGrade.Pending,
            "Critical Tap must never return Great or Good");
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
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(12.5, .3, 60) - 12.8) < .0001,
            "Audio recovery must seek to chart time plus BGM offset");
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(-.4, .3, 60)) < .0001,
            "Audio recovery must not seek before the start of a clip");
        Require(Math.Abs(AudioDeviceRecovery.ClipTimeForChartTime(100, 0, 60) - 60) < .0001,
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
        Require(Math.Abs(AudioDeviceRecovery.PlaybackDspForChartTime(400, -.4, .3) - 400.1) < .0001,
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
        slider.Begin(1, 1, -5.5f, inputs);
        Require(inputs.Count == 1 && Math.Abs(inputs[0].Lane + 5.5f) < .0001,
            "Initial slider contact must emit one Tap activation");

        slider.Move(1, 1.005, -5.4f, inputs);
        Require(inputs.Count == 1, "Motion inside one slider cell must not retrigger Tap");

        slider.Move(1, 1.02, -2.5f, inputs);
        var crossedTaps = inputs.Where(input => input.Kind == RuntimeNoteKind.Tap).ToArray();
        Require(crossedTaps.Length == 4 && crossedTaps.Skip(1).Select(input => input.Lane).SequenceEqual(new[] { -4.5f, -3.5f, -2.5f }),
            "A rub must activate every newly entered slider cell exactly once");

        slider.Move(1, 1.1, -3.5f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 5 && Math.Abs(inputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane + 3.5f) < .0001,
            "Leaving and re-entering a slider cell after debounce must reactivate it");

        slider.End(1, 1.11, -3.5f, inputs);
        slider.Begin(1, 2, 0, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 6, "A new contact must activate its initial slider cell");

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
        Require(jitterInputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 2,
            "A boundary jitter return inside 25 ms must not rearm its starting slider cell");

        var reentryInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(5, 3, -5.5f, reentryInputs);
        slider.Move(5, 3.005, -4.85f, reentryInputs);
        slider.Move(5, 3.010, -5.01f, reentryInputs);
        Require(reentryInputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 3 && Math.Abs(reentryInputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane + 5.5f) < .0001,
            "Departing a slider cell by 0.15 lanes must permit its early reentry activation");

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
        slider.Begin(3, 2.1, -7, outsideSweep);
        slider.Move(3, 2.2, 7, outsideSweep);
        Require(outsideSweep.Count(input => input.Kind == RuntimeNoteKind.Tap) == VirtualSliderInput.CellCount,
            "A low-frame-rate sweep across the whole slider must not skip cells");

        var rubNotes = new[]
        {
            Note(10, 3.05, -4.5f),
            Note(11, 3.10, -3.5f),
            Note(12, 3.15, -2.5f),
        };
        var rubInputs = new List<InputToken>();
        slider.Reset();
        slider.Begin(2, 3, -5.5f, rubInputs);
        slider.Move(2, 3.2, -2.5f, rubInputs);
        var rubEngine = new JudgmentEngine(rubNotes, new ScoreState());
        rubEngine.Process(3.2, rubInputs, Array.Empty<ActiveContact>());
        Require(rubNotes.All(note => note.Grade == JudgmentGrade.Perfect),
            "A timed rub must match Tap notes in every crossed slider cell");
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
