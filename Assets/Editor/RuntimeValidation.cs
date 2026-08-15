using System;
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
        Require(chart.PlayableCount == 2063, $"Expected 2063 playable notes, got {chart.PlayableCount}");
        Require(chart.Connectors.Count == 1175, $"Expected 1175 connectors, got {chart.Connectors.Count}");
        Require(chart.SimLines.Count == 579, $"Expected 579 synchronization lines, got {chart.SimLines.Count}");
        Require(chart.Guides.Count == 154, $"Expected 154 decoration guides, got {chart.Guides.Count}");
        Require(chart.Guides.Count(guide => guide.FadeOut) == 39,
            $"Expected 39 decoration guide chain endings, got {chart.Guides.Count(guide => guide.FadeOut)}");
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
        var flicks = chart.Notes.Where(note => note.Kind == RuntimeNoteKind.Flick).ToArray();
        Require(flicks.Length == 243, $"Expected 243 flick notes, got {flicks.Length}");
        Require(flicks.Count(note => note.Direction < 0) == 117 && flicks.Count(note => note.Direction == 0) == 21 && flicks.Count(note => note.Direction > 0) == 105,
            "Flick left/center/right directions were not preserved");
        Require(chart.Notes.Count(note => (note.Archetype ?? string.Empty).Contains("TraceSlideEnd") && note.Kind == RuntimeNoteKind.Sustain) == 143,
            "Trace Hold tails must complete from sustained coverage without a separate release input");
        Require(chart.Notes.Count(note => (note.Archetype ?? string.Empty).Contains("SlideEndFlick") && note.Kind == RuntimeNoteKind.Flick) == 37,
            "Flick Hold tails must retain flick input");
        Require(chart.Guides.Any(guide => guide.TailOpacity < guide.HeadOpacity) && chart.Guides.Min(guide => guide.TailOpacity) <= .081f,
            "Guide chains must fade continuously toward their ending");
        Require(chart.Guides.Any(guide => guide.Start.Lane - guide.Start.Size < -6 || guide.Start.Lane + guide.Start.Size > 6 ||
            guide.Head.Lane - guide.Head.Size < -6 || guide.Head.Lane + guide.Head.Size > 6 ||
            guide.Tail.Lane - guide.Tail.Size < -6 || guide.Tail.Lane + guide.Tail.Size > 6 ||
            guide.End.Lane - guide.End.Size < -6 || guide.End.Lane + guide.End.Size > 6),
            "Expected at least one decoration guide outside the central lane range");
        Require(chart.BgmBytes?.Length > 0, "Default SCP BGM was not extracted");
        Require(chart.Notes.SequenceEqual(chart.Notes.OrderBy(note => note.Time).ThenBy(note => note.Index)), "Notes are not time sorted");

        ValidateJudgmentRules();
        Debug.Log($"GUGARYTHM_VALIDATION_OK title={chart.Title} playable={chart.PlayableCount} connectors={chart.Connectors.Count} simLines={chart.SimLines.Count} guides={chart.Guides.Count} " +
                  $"normal={chart.Connectors.Count(value => !value.Critical)} critical={chart.Connectors.Count(value => value.Critical)} " +
                  $"warnings={chart.Warnings.Count} bgmBytes={chart.BgmBytes.Length}");
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

        var release = Note(5, 3, 0);
        release.Kind = RuntimeNoteKind.Release;
        engine = new JudgmentEngine(new[] { release }, new ScoreState());
        engine.Process(3, new[] { new InputToken(1, RuntimeNoteKind.Tap, 3, 0) }, Array.Empty<ActiveContact>());
        Require(release.Grade == JudgmentGrade.Pending, "A Hold release tail must not consume a tap input");
        engine.Process(3, new[] { new InputToken(1, RuntimeNoteKind.Release, 3, 0) }, Array.Empty<ActiveContact>());
        Require(release.Grade == JudgmentGrade.Perfect, "A Hold release tail must consume a release input");

        var traceTail = Note(6, 4, 0);
        traceTail.Kind = RuntimeNoteKind.Sustain;
        engine = new JudgmentEngine(new[] { traceTail }, new ScoreState());
        engine.Process(4, Array.Empty<InputToken>(), new[] { new ActiveContact(1, 0, 3.5) });
        Require(traceTail.Grade == JudgmentGrade.Perfect, "A Trace Hold tail must complete from sustained coverage without a release input");
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
