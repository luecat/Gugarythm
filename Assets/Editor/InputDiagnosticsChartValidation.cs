using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Gugarhythm;
using UnityEditor;
using UnityEngine;

public static class InputDiagnosticsChartValidation
{
    const string RelativePath = "StreamingAssets/DebugCharts/Input-Diagnostics.ggr";

    static readonly (double Beat, float Lane)[] ExpectedNotes =
    {
        (4.00, -4f),
        (6.00, 0f),
        (8.00, 4f),
        (10.00, 0f),
        (10.44, 3.1f),
        (13.00, 0f),
        (13.24, 4f),
        (16.00, 0f),
        (16.24, 3.1f),
        (19.00, 3.1f),
        (19.24, 0f),
        (22.00, 0f),
        (22.24, 3.1f),
        (25.00, 0f),
        (25.24, 3.1f),
        (28.00, 3.1f),
        (28.24, 0f),
        (31.00, 0f),
        (31.24, 3.1f),
    };

    [MenuItem("Gugarhythm/Validate Input Diagnostics Chart")]
    public static void Validate()
    {
        var path = Path.Combine(Application.dataPath, RelativePath);
        Require(File.Exists(path), "Input-Diagnostics.ggr is missing from StreamingAssets/DebugCharts");

        var bytes = File.ReadAllBytes(path);
        var imported = new GgrChartImporter().Import("Input-Diagnostics.ggr", bytes);
        Require(imported.Success && imported.Chart != null,
            "Input-Diagnostics.ggr must import through the production GGR importer: " + imported.Error);

        var chart = imported.Chart;
        Require(chart.SourceFormat == "GGR" && chart.Title == "Input Diagnostics" &&
                chart.Artist == "GUGArhythm" && chart.Author == "GUGArhythm Debug" &&
                chart.DifficultyName == "DEBUG" && chart.DifficultyLevel == "1",
            "Input diagnostics metadata must identify the hidden DEBUG chart");
        Require(Math.Abs(chart.BgmOffset) < .000001d && Math.Abs(chart.BgmStartDelaySeconds) < .000001d,
            "Input diagnostics chart and package offsets must remain zero");
        Require(chart.BgmExtension == ".wav" && chart.Warnings.Count == 0,
            "Input diagnostics package must contain a warning-free WAV payload");

        var notes = chart.Notes.OrderBy(note => note.Beat).ThenBy(note => note.Index).ToArray();
        Require(notes.Length == ExpectedNotes.Length && chart.PlayableCount == ExpectedNotes.Length,
            $"Input diagnostics chart must contain exactly {ExpectedNotes.Length} playable notes");
        for (var index = 0; index < ExpectedNotes.Length; index++)
        {
            var expected = ExpectedNotes[index];
            var note = notes[index];
            Require(Math.Abs(note.Beat - expected.Beat) < .000001d &&
                    Math.Abs(note.Time - expected.Beat * .5d) < .000001d &&
                    Math.Abs(note.Lane - expected.Lane) < .0001f,
                $"Input diagnostics note {index + 1} must stay at beat {expected.Beat:0.00}, lane {expected.Lane:0.0}");
            Require(note.Kind == RuntimeNoteKind.Tap && note.Judged && !note.Critical &&
                    Math.Abs(note.Size - 1f) < .0001f && note.HoldRootIndex < 0,
                $"Input diagnostics note {index + 1} must remain a normal size-1 Tap");
            Require(Math.Abs(chart.VisualPosition(note.Time, note.TimeScaleGroup) - note.Time) < .000001d,
                $"Input diagnostics note {index + 1} must remain on a 1.0x visual time scale");
        }

        Require(chart.Connectors.Count == 0 && chart.HoldPaths.Count == 0 &&
                chart.SimLines.Count == 0 && chart.Guides.Count == 0,
            "Input diagnostics chart must not contain Hold, SimLine, or Guide interference");
        Require(Math.Abs(chart.LastNoteTime - 15.62d) < .000001d,
            "Input diagnostics final note must remain at 15.62 seconds");
        var audioDuration = WavDurationSeconds(chart.BgmBytes);
        Require(audioDuration >= chart.LastNoteTime + .5d,
            $"Input diagnostics audio must extend beyond the final note; audio={audioDuration:0.00}s note={chart.LastNoteTime:0.00}s");

        ValidateIsolatedTap(bytes, 4d);
        ValidateIsolatedTap(bytes, 6d);
        ValidateIsolatedTap(bytes, 8d);
        ValidateTapDriftSuppression();
        ValidateGridRowDriftSuppression();
        ValidateDirectTapPair(bytes, 10d, 10.44d, true, JudgmentGrade.Good, 1, "220 ms time control");
        ValidateDirectTapPair(bytes, 13d, 13.24d, true, JudgmentGrade.Good, 1, "120 ms spatial control");
        ValidateDirectTapPair(bytes, 16d, 16.24d, true, JudgmentGrade.Pending, 0, "120 ms protected target 1");
        ValidateDirectTapPair(bytes, 19d, 19.24d, true, JudgmentGrade.Pending, 0, "120 ms protected target 2");
        ValidateDirectTapPair(bytes, 22d, 22.24d, true, JudgmentGrade.Pending, 0, "120 ms protected target 3");
        ValidateDirectTapPair(bytes, 16d, 16.24d, false, JudgmentGrade.Good, 1, "Protection OFF target");
        ValidateStationaryPair(bytes, 25d, 25.24d);
        ValidateSwipePair(bytes, 25d, 25.24d);
        ValidateSwipePair(bytes, 28d, 28.24d);
        ValidateSwipePair(bytes, 31d, 31.24d);
        ValidateDiagnosticDispositions(bytes);
        ValidateStreamingAssetUrls();
        Require(typeof(SonolusLandscapePrototype).GetMethod("StartInputDiagnosticsChart",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null &&
                typeof(SonolusLandscapePrototype).GetMethod("BuildInputDiagnosticsSettingsSection",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) != null,
            "Runtime prototype must expose the DEBUG tab and chart launch workflow");
        Debug.Log("GUGARHYTHM_INPUT_DIAGNOSTICS_CHART_VALIDATION_OK notes=19 targetDeltaMs=120");
    }

    static void ValidateDiagnosticDispositions(byte[] bytes)
    {
        var protectedPair = ImportPair(bytes, 16d, 16.24d);
        var protectedEngine = new JudgmentEngine(protectedPair, new ScoreState())
        {
            JudgmentProtectionEnabled = true,
        };
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();
        var events = new List<JudgmentEvent>();
        var diagnostics = new List<JudgmentInputDiagnostic>();
        var firstTime = protectedPair[0].Time - .09d;
        slider.Begin(1, firstTime, protectedPair[0].Lane, inputs);
        protectedEngine.ProcessInto(firstTime, inputs, Array.Empty<ActiveContact>(),
            Array.Empty<ContactPathSegment>(), false, events, diagnostics);
        inputs.Clear();
        slider.End(1, firstTime + .01d, protectedPair[0].Lane, inputs);
        var secondTime = protectedPair[1].Time - .09d;
        slider.Begin(1, secondTime, protectedPair[1].Lane, inputs);
        protectedEngine.ProcessInto(secondTime, inputs, Array.Empty<ActiveContact>(),
            Array.Empty<ContactPathSegment>(), false, events, diagnostics);
        Require(events.Count == 0 && diagnostics.Any(item =>
                item.Disposition == JudgmentInputDisposition.ProtectionBlocked &&
                item.Note == protectedPair[1]),
            "A protected physical Tap must be reported as ProtectionBlocked instead of disappearing silently");

        var swipePair = ImportPair(bytes, 25d, 25.24d);
        var swipeEngine = new JudgmentEngine(swipePair, new ScoreState());
        slider.Reset();
        inputs.Clear();
        var swipeFirstTime = swipePair[0].Time - .09d;
        slider.Begin(2, swipeFirstTime, swipePair[0].Lane, inputs);
        swipeEngine.ProcessInto(swipeFirstTime, inputs, Array.Empty<ActiveContact>(),
            Array.Empty<ContactPathSegment>(), false, events, diagnostics);
        inputs.Clear();
        var swipeSecondTime = swipePair[1].Time - .03d;
        slider.Move(2, swipeSecondTime, swipePair[1].Lane, inputs);
        swipeEngine.ProcessInto(swipeSecondTime, inputs, Array.Empty<ActiveContact>(),
            Array.Empty<ContactPathSegment>(), false, events, diagnostics);
        Require(events.Count == 1 && diagnostics.Any(item =>
                item.Disposition == JudgmentInputDisposition.Matched && item.Note == swipePair[1]),
            "A same-finger swipe must report which crossing token matched the second Tap");
    }

    static void ValidateStreamingAssetUrls()
    {
        var android = InputDiagnosticsChartLoader.BuildStreamingAssetUrl(
            "jar:file:///data/app/base.apk!/assets");
        Require(android == "jar:file:///data/app/base.apk!/assets/DebugCharts/Input-Diagnostics.ggr",
            "Android StreamingAssets URL must keep the jar:file root");
        var ios = InputDiagnosticsChartLoader.BuildStreamingAssetUrl("/var/mobile/My Game/Data/Raw");
        Require(ios.StartsWith("file://", StringComparison.Ordinal) &&
                ios.EndsWith("/DebugCharts/Input-Diagnostics.ggr", StringComparison.Ordinal) &&
                ios.Contains("My%20Game", StringComparison.Ordinal),
            "iOS StreamingAssets URL must be a correctly escaped file URL");
    }

    static void ValidateIsolatedTap(byte[] bytes, double beat)
    {
        var imported = new GgrChartImporter().Import("Input-Diagnostics.ggr", bytes);
        Require(imported.Success && imported.Chart != null,
            "Input diagnostics isolated-Tap fixture must re-import cleanly");
        var note = imported.Chart.Notes.Single(candidate => Math.Abs(candidate.Beat - beat) < .000001d);
        var engine = new JudgmentEngine(new[] { note }, new ScoreState());
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();
        slider.Begin(1, note.Time, note.Lane, inputs);
        var events = engine.Process(note.Time, inputs, Array.Empty<ActiveContact>());
        Require(note.Grade == JudgmentGrade.Perfect && events.Count == 1,
            $"Isolated Tap at beat {beat:0.00} must resolve from a physical Begin token");
    }

    static void ValidateTapDriftSuppression()
    {
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();

        slider.Begin(1, 1d, .735867f, inputs);
        slider.Move(1, 1.01d, .858512f, inputs);
        slider.Move(1, 1.02d, .909012f, inputs);
        slider.Move(1, 1.03d, .988370f, inputs);
        slider.Move(1, 1.04d, 1.111025f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 1,
            "A physical Tap's observed 0.38-lane centroid drift must not synthesize a crossing Tap");

        slider.Reset();
        inputs.Clear();
        slider.Begin(2, 2d, 2.946556f, inputs);
        slider.Move(2, 2.01d, 3.005698f, inputs);
        slider.Move(2, 2.02d, 2.983592f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 1,
            "Boundary jitter from an ordinary Tap must not synthesize cell re-entry Taps");

        slider.Move(2, 2.05d, 3.446556f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 2 &&
                Math.Abs(inputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane - 3.25f) < .0001f,
            "One full cell of intentional travel must activate rub and replay the crossed Tap cell");
    }

    static void ValidateGridRowDriftSuppression()
    {
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();

        slider.Begin(1, 1d, -.48f, .98f, inputs);
        slider.Move(1, 1.01d, -.42f, 1.12f, inputs);
        slider.Move(1, 1.02d, -.46f, .91f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 1,
            "Crossing a row boundary with sub-row Tap jitter must not synthesize another Tap");

        slider.Move(1, 1.05d, -.44f, 1.99f, inputs);
        slider.Move(1, 1.06d, -.43f, 2.02f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 3 &&
                Math.Abs(inputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane + .43f) < .0001f,
            "One full row of vertical travel must activate rub and preserve later row crossings");

        slider.Reset();
        inputs.Clear();
        slider.Begin(2, 1d, .1f, .1f, inputs);
        slider.Move(2, 1.01d, .1f, 1.05f, inputs);
        slider.Move(2, 1.02d, .1f, 1.11f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 2,
            "Vertical rub must replay its first row crossing when full-row intent becomes clear inside that row");

        slider.Reset();
        inputs.Clear();
        slider.Begin(3, 2d, .24f, .99f, inputs);
        slider.Move(3, 2.05d, .76f, 2f, inputs);
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 2 &&
                Math.Abs(inputs.Last(input => input.Kind == RuntimeNoteKind.Tap).Lane - .75f) < .0001f,
            "A diagonal sample crossing both a slider cell and a row must emit only one moved Tap");
    }

    static void ValidateDirectTapPair(byte[] bytes, double firstBeat, double secondBeat, bool protectionEnabled,
        JudgmentGrade expectedSecondGrade, int expectedSecondEventCount, string label)
    {
        var target = ImportPair(bytes, firstBeat, secondBeat);
        var engine = new JudgmentEngine(target, new ScoreState())
        {
            JudgmentProtectionEnabled = protectionEnabled,
        };
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();
        var firstTime = target[0].Time - .09d;
        slider.Begin(1, firstTime, target[0].Lane, inputs);
        engine.Process(firstTime, inputs, Array.Empty<ActiveContact>());

        inputs.Clear();
        slider.End(1, firstTime + .01d, target[0].Lane, inputs);
        var secondTime = target[1].Time - .09d;
        slider.Begin(1, secondTime, target[1].Lane, inputs);
        var secondEvents = engine.Process(secondTime, inputs, Array.Empty<ActiveContact>());

        Require(target[0].Grade == JudgmentGrade.Good && target[1].Grade == expectedSecondGrade &&
                secondEvents.Count == expectedSecondEventCount,
            $"{label} must produce the intended separate-Tap result");
    }

    static void ValidateStationaryPair(byte[] bytes, double firstBeat, double secondBeat)
    {
        var target = ImportPair(bytes, firstBeat, secondBeat);
        var engine = new JudgmentEngine(target, new ScoreState());
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();
        var firstTime = target[0].Time - .09d;
        slider.Begin(1, firstTime, target[0].Lane, inputs);
        engine.Process(firstTime, inputs, Array.Empty<ActiveContact>());

        inputs.Clear();
        var secondTime = target[1].Time - .03d;
        slider.Move(1, secondTime, target[0].Lane, inputs);
        var secondEvents = engine.Process(secondTime, inputs, Array.Empty<ActiveContact>());
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) == 0 &&
                target[0].Grade == JudgmentGrade.Good && target[1].Grade == JudgmentGrade.Pending &&
                secondEvents.Count == 0,
            "A stationary finger must not synthesize a second Tap at the swipe comparison time");
    }

    static void ValidateSwipePair(byte[] bytes, double firstBeat, double secondBeat)
    {
        var target = ImportPair(bytes, firstBeat, secondBeat);
        var engine = new JudgmentEngine(target, new ScoreState());
        var slider = new VirtualSliderInput();
        var inputs = new List<InputToken>();
        var firstTime = target[0].Time - .09d;
        slider.Begin(1, firstTime, target[0].Lane, inputs);
        engine.Process(firstTime, inputs, Array.Empty<ActiveContact>());

        inputs.Clear();
        var secondTime = target[1].Time - .03d;
        slider.Move(1, secondTime, target[1].Lane, inputs);
        var secondEvents = engine.Process(secondTime, inputs, Array.Empty<ActiveContact>());
        Require(inputs.Count(input => input.Kind == RuntimeNoteKind.Tap) >= 6 &&
                target[0].Grade == JudgmentGrade.Good && target[1].Grade == JudgmentGrade.Great &&
                secondEvents.Count == 1,
            "A same-finger swipe must emit crossing Tap tokens and resolve the protected second note after the midpoint");
    }

    static RuntimeNote[] ImportPair(byte[] bytes, double firstBeat, double secondBeat)
    {
        var imported = new GgrChartImporter().Import("Input-Diagnostics.ggr", bytes);
        Require(imported.Success && imported.Chart != null,
            "Input diagnostics behavior fixture must re-import cleanly");
        var pair = imported.Chart.Notes
            .Where(note => Math.Abs(note.Beat - firstBeat) < .000001d ||
                           Math.Abs(note.Beat - secondBeat) < .000001d)
            .OrderBy(note => note.Beat)
            .ToArray();
        Require(pair.Length == 2,
            $"Input diagnostics pair must exist at beats {firstBeat:0.00} and {secondBeat:0.00}");
        return pair;
    }

    static double WavDurationSeconds(byte[] bytes)
    {
        Require(bytes != null && bytes.Length >= 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
                Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE",
            "Input diagnostics audio must be a PCM WAV file");
        var byteRate = 0;
        var dataSize = -1;
        var audioFormat = 0;
        var channels = 0;
        var sampleRate = 0;
        var bitsPerSample = 0;
        for (var offset = 12; offset + 8 <= bytes.Length;)
        {
            var name = Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, offset + 4);
            Require(chunkSize >= 0 && offset + 8L + chunkSize <= bytes.Length,
                "Input diagnostics WAV chunks must stay within the audio payload");
            if (name == "fmt " && chunkSize >= 16)
            {
                audioFormat = BitConverter.ToUInt16(bytes, offset + 8);
                channels = BitConverter.ToUInt16(bytes, offset + 10);
                sampleRate = BitConverter.ToInt32(bytes, offset + 12);
                byteRate = BitConverter.ToInt32(bytes, offset + 16);
                bitsPerSample = BitConverter.ToUInt16(bytes, offset + 22);
            }
            if (name == "data") dataSize = chunkSize;
            offset += 8 + chunkSize + (chunkSize & 1);
        }
        Require(audioFormat == 1 && channels == 1 && sampleRate == 44100 && bitsPerSample == 16 &&
                byteRate == sampleRate * channels * bitsPerSample / 8 && dataSize >= 0,
            "Input diagnostics audio must remain 44.1 kHz mono 16-bit PCM WAV");
        return dataSize / (double)byteRate;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
