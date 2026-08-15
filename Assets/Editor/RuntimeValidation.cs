using System;
using System.Collections.Generic;
using System.IO;
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
        foreach (var sound in new[] { "perfect", "great", "good", "alternative", "hold", "stage" })
            Require(Resources.Load<AudioClip>($"NeonRhythm/package/audio/{sound}") != null,
                $"SCP-derived judgment sound is missing: {sound}");
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
        Require(holdTerminalNotes.Length > 0 && holdTerminalNotes.Where(note => note.HoldCheckpointSource != HoldCheckpointSource.Mid).All(note => !note.Judged),
            "Normal Hold tails must be visual endpoints without sustained, Flick, or release judgment");
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
        Debug.Log($"GUGARYTHM_VALIDATION_OK title={chart.Title} playable={chart.PlayableCount} connectors={chart.Connectors.Count} simLines={chart.SimLines.Count} guides={chart.Guides.Count} " +
                  $"normal={chart.Connectors.Count(value => !value.Critical)} critical={chart.Connectors.Count(value => value.Critical)} " +
                  $"warnings={chart.Warnings.Count} bgmBytes={chart.BgmBytes.Length}");
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
        Require(SonolusLandscapePrototype.UsesHoldJudgmentSound(holdMid) && SonolusLandscapePrototype.UsesHoldJudgmentSound(autoCheckpoints[0]) &&
                !SonolusLandscapePrototype.UsesHoldJudgmentSound(holdHead),
            "Only Hold checkpoints must select the Hold judgment sound");
        Require(!holdTail.Judged,
            "Hold tails must not create a judgment checkpoint");

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
        Require(tailMid.Judged && tailMid.HoldCheckpointSource == HoldCheckpointSource.Mid,
            "An authored mid at a Hold tail must remain an independent judged checkpoint");

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
