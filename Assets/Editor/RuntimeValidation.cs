using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Gugarythm;
using UnityEditor;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

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
        ValidateScrollSpeedMath();
        ValidateUscLeadingMeasurePadding();
        ValidateInitialWaterfallTiming();
        ValidateTimingAndHotPathReuse();
        ValidateGameplayUpdateStateRestoration();
        ValidateGgrUscHoldRoots();
        ValidateAttachedGgrPlayableCount();
        ValidateLibrarySelectionRestore();
        ValidateStartupSplashConfiguration();
        ValidateStartupBuildSceneOrder();
        ValidateBundledChartManifest();
        ValidateUscSlideRoleClassification();
        ValidateUscSlideMidpointRoles();
        ValidateHeadlessCriticalSlideStart();
        ValidateRuntimeHoldPaths();
        ValidateValidPathAttachEvaluatorAlignment();
        ValidateHoldEaseParity();
        ValidateHoldPlayableRangeCheckpoints();
        ValidateChartRenderIndex();
        ValidateNoteRenderWidths();
        ValidateTaperedConnectorGeometry();
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
        var holdRenderRunCount = chart.HoldPaths.Sum(path => path.RenderRuns.Count);
        Require(chart.HoldPaths.Count > 0 && chart.FallbackConnectors.Count == 0,
            "The DOMiNUS regression chart must build complete Hold paths without legacy fallback");
        Require(holdRenderRunCount < chart.Connectors.Count,
            $"Hold render runs must reduce Graphic ownership below connector count ({holdRenderRunCount} vs {chart.Connectors.Count})");
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
        ValidateLatencyCalibrationMath();
        Debug.Log($"GUGARYTHM_VALIDATION_OK title={chart.Title} playable={chart.PlayableCount} auto={chart.Notes.Count(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto)} connectors={chart.Connectors.Count} holdPaths={chart.HoldPaths.Count} holdRuns={holdRenderRunCount} simLines={chart.SimLines.Count} guides={chart.Guides.Count} " +
                  $"normal={chart.Connectors.Count(value => !value.Critical)} critical={chart.Connectors.Count(value => value.Critical)} " +
                  $"warnings={chart.Warnings.Count} bgmBytes={chart.BgmBytes.Length}");
    }

    static void ValidateRuntimeHoldPaths()
    {
        RuntimeNote Point(int index, double time, float lane, float size = 1) => new()
        {
            Index = index,
            SourceId = $"hold-path:{index}",
            Time = time,
            Beat = time,
            Lane = lane,
            Size = size,
            Kind = RuntimeNoteKind.Sustain,
            TimeScaleGroup = "main",
        };

        var chart = new RuntimeChart();
        var a = Point(1, 0, 0);
        var b = Point(2, 1, 0);
        var c = Point(3, 2, 2);
        var d = Point(4, 3, 1, .1f);
        chart.Connectors.Add(new RuntimeConnector { Start = a, End = b, Ease = 0, Critical = false });
        chart.Connectors.Add(new RuntimeConnector { Start = b, End = c, Ease = 0, Critical = false });
        chart.Connectors.Add(new RuntimeConnector { Start = c, End = d, Ease = 0, Critical = true });

        var result = HoldPathBuilder.Build(chart);
        Require(result.Paths.Count == 1 && result.FallbackConnectors.Count == 0,
            "A non-branching Hold connector chain must build one runtime path");
        var path = result.Paths[0];
        Require(path.RenderRuns.Count == 2 && !path.RenderRuns[0].Critical && path.RenderRuns[1].Critical,
            "A Hold path must split render runs only when its Critical material class changes");

        var atB = path.Evaluator.Evaluate(1);
        var atC = path.Evaluator.Evaluate(2);
        Require(Math.Abs(atB.Lane - b.Lane) < 1e-6 && Math.Abs(atC.Lane - c.Lane) < 1e-6,
            "The Hold evaluator must pass through every authored path node");
        var epsilon = 1e-3;
        var leftDerivative = (path.Evaluator.Evaluate(1).Lane - path.Evaluator.Evaluate(1 - epsilon).Lane) / epsilon;
        var rightDerivative = (path.Evaluator.Evaluate(1 + epsilon).Lane - path.Evaluator.Evaluate(1).Lane) / epsilon;
        Require(Math.Abs(leftDerivative - rightDerivative) < .02,
            "A vertical-to-diagonal Hold join must have matching left and right derivatives");

        for (var time = 0d; time <= 3; time += .01)
        {
            var sample = path.Evaluator.Evaluate(time);
            var segment = path.Segments[sample.SegmentIndex];
            var minLane = Math.Min(segment.Start.Lane, segment.End.Lane) - 1e-5;
            var maxLane = Math.Max(segment.Start.Lane, segment.End.Lane) + 1e-5;
            Require(sample.Lane >= minLane && sample.Lane <= maxLane,
                "Hold interpolation must not overshoot the current segment's lane bounds");
            Require(sample.Size >= .25f, "Hold interpolation must clamp Size to at least 0.25");
        }

        var sameTimeChart = new RuntimeChart();
        var sameA = Point(10, 0, -1);
        var sameB = Point(11, 1, -1);
        var sameC = Point(12, 1, 1);
        var sameD = Point(13, 2, 0);
        sameTimeChart.Connectors.Add(new RuntimeConnector { Start = sameA, End = sameB });
        sameTimeChart.Connectors.Add(new RuntimeConnector { Start = sameB, End = sameC });
        sameTimeChart.Connectors.Add(new RuntimeConnector { Start = sameC, End = sameD });
        var sameTimeResult = HoldPathBuilder.Build(sameTimeChart);
        Require(sameTimeResult.Paths.Count == 1 && sameTimeResult.Paths[0].Segments[1].HardCorner,
            "A same-time horizontal Hold movement must remain a finite explicit hard corner");

        var branchChart = new RuntimeChart();
        var branchA = Point(20, 0, 0);
        var branchB = Point(21, 1, -1);
        var branchC = Point(22, 1, 1);
        branchChart.Connectors.Add(new RuntimeConnector { Start = branchA, End = branchB });
        branchChart.Connectors.Add(new RuntimeConnector { Start = branchA, End = branchC });
        var branchResult = HoldPathBuilder.Build(branchChart);
        Require(branchResult.Paths.Count == 0 && branchResult.FallbackConnectors.Count == 2 && branchResult.Warnings.Count > 0,
            "A branched Hold must warn and preserve every source connector for fallback rendering");

        var nullChart = new RuntimeChart();
        var nullConnector = new RuntimeConnector { Start = Point(25, 0, 0), End = null };
        nullChart.Connectors.Add(nullConnector);
        var nullResult = HoldPathBuilder.Build(nullChart);
        Require(nullResult.FallbackConnectors.Count == 1 && nullResult.Warnings.Count > 0 &&
                !SonolusLandscapePrototype.CanRenderLegacyConnector(nullConnector),
            "A null-endpoint connector must warn but never enter the dereferencing legacy renderer");

        var cycleChart = new RuntimeChart();
        var cycleA = Point(30, 0, 0);
        var cycleB = Point(31, 1, 1);
        cycleChart.Connectors.Add(new RuntimeConnector { Start = cycleA, End = cycleB });
        cycleChart.Connectors.Add(new RuntimeConnector { Start = cycleB, End = cycleA });
        var cycleResult = HoldPathBuilder.Build(cycleChart);
        Require(cycleResult.Paths.Count == 0 && cycleResult.FallbackConnectors.Count == 2 && cycleResult.Warnings.Count > 0,
            "A cyclic Hold must warn and preserve its connectors for fallback rendering");

        var mixedGroupChart = new RuntimeChart { DefaultTimeScaleGroup = "main" };
        var mixedA = Point(35, 0, 0); mixedA.TimeScaleGroup = "main";
        var mixedB = Point(36, 1, 1); mixedB.TimeScaleGroup = "fast";
        mixedGroupChart.Connectors.Add(new RuntimeConnector { Start = mixedA, End = mixedB });
        var mixedGroupResult = HoldPathBuilder.Build(mixedGroupChart);
        Require(mixedGroupResult.Paths.Count == 0 && mixedGroupResult.FallbackConnectors.Count == 1,
            "A Hold that changes TimeScaleGroup mid-path must retain legacy per-segment rendering");

        var reverseGroupChart = new RuntimeChart { DefaultTimeScaleGroup = "reverse" };
        reverseGroupChart.TimeScaleGroups["reverse"] = new RuntimeTimeScaleGroup("reverse", new[] { (0d, -1d) });
        var reverseA = Point(37, 0, 0); reverseA.TimeScaleGroup = "reverse";
        var reverseB = Point(38, 1, 1); reverseB.TimeScaleGroup = "reverse";
        reverseGroupChart.Connectors.Add(new RuntimeConnector { Start = reverseA, End = reverseB });
        var reverseGroupResult = HoldPathBuilder.Build(reverseGroupChart);
        Require(reverseGroupResult.Paths.Count == 0 && reverseGroupResult.FallbackConnectors.Count == 1,
            "A Hold with a non-invertible reverse TimeScaleGroup must retain legacy clipping and rendering");

        var checkpointChart = new RuntimeChart();
        var checkpointA = Point(40, 0, 0);
        var checkpointB = Point(41, 1, 2);
        var checkpointC = Point(42, 2, 3);
        checkpointChart.Notes.AddRange(new[] { checkpointA, checkpointB, checkpointC });
        checkpointChart.Connectors.Add(new RuntimeConnector { Start = checkpointA, End = checkpointB });
        checkpointChart.Connectors.Add(new RuntimeConnector { Start = checkpointB, End = checkpointC });
        HoldCheckpointBuilder.Apply(checkpointChart, beat => beat);
        Require(checkpointChart.HoldPaths.Count == 1,
            "Hold checkpoint construction must retain the complete runtime path on the chart");
        var curvedCheckpoint = checkpointChart.Notes.Single(note =>
            note.HoldCheckpointSource == HoldCheckpointSource.Auto && Math.Abs(note.Beat - .5) < 1e-9);
        var evaluatedCheckpoint = checkpointChart.HoldPaths[0].Evaluator.Evaluate(curvedCheckpoint.Time);
        Require(Math.Abs(curvedCheckpoint.Lane - evaluatedCheckpoint.Lane) < 1e-6 &&
                Math.Abs(curvedCheckpoint.Size - evaluatedCheckpoint.Size) < 1e-6,
            "Automatic Hold checkpoints must use the same curved evaluator as rendering");
        Require(Math.Abs(curvedCheckpoint.Lane - 1f) > .01f,
            "The curved checkpoint regression fixture must differ from old linear interpolation");

        var straightChart = new RuntimeChart();
        var straightA = Point(50, 0, 0);
        var straightB = Point(51, 1, 2);
        straightChart.Connectors.Add(new RuntimeConnector { Start = straightA, End = straightB });
        var straightPath = HoldPathBuilder.Build(straightChart).Paths[0];
        var tessellator = new AdaptiveHoldTessellator();
        var tessellation = new List<HoldTessellationPoint>(AdaptiveHoldTessellator.MaxPointsPerRun);
        Vector2 Project(HoldTessellationPoint point) => new(point.Sample.Lane * 100, (float)point.Time * 100);
        tessellator.BuildVisibleRun(straightPath.RenderRuns[0], 0, 1, Project, tessellation);
        Require(tessellation.Count == 2,
            "A straight Hold run must tessellate to only its two endpoints");

        tessellator.BuildVisibleRun(path.RenderRuns[0], .25, 1.75, Project, tessellation);
        Require(tessellation.Count > 2 && tessellation.Count <= AdaptiveHoldTessellator.MaxPointsPerRun,
            "A curved Hold run must subdivide by screen error without exceeding the run cap");
        Require(Math.Abs(tessellation[0].Time - .25) < 1e-9 && Math.Abs(tessellation[^1].Time - 1.75) < 1e-9,
            "Adaptive Hold tessellation must preserve exact visible clip times");
        var pointsInFirstSegment = tessellation.Count(point => point.Sample.SegmentIndex == 0);
        Require(pointsInFirstSegment <= AdaptiveHoldTessellator.MaxPointsPerSegment,
            "Adaptive Hold tessellation must respect its per-source-segment point cap");

        tessellator.BuildVisibleRun(path.RenderRuns[0], 1.75, .25, Project, tessellation);
        Require(Math.Abs(tessellation[0].Time - .25) < 1e-9 && Math.Abs(tessellation[^1].Time - 1.75) < 1e-9,
            "Adaptive Hold tessellation must normalize a reversed visible-time interval");

        var stressChart = new RuntimeChart();
        RuntimeNote stressPrevious = null;
        for (var stressIndex = 0; stressIndex <= 300; stressIndex++)
        {
            var stressPoint = Point(100 + stressIndex, stressIndex * .05, stressIndex % 2 == 0 ? -4 : 4);
            if (stressPrevious != null)
                stressChart.Connectors.Add(new RuntimeConnector { Start = stressPrevious, End = stressPoint });
            stressPrevious = stressPoint;
        }
        var stressRun = HoldPathBuilder.Build(stressChart).Paths[0].RenderRuns[0];
        Vector2 StressProject(HoldTessellationPoint point) => new(point.Sample.Lane * 1000, (float)point.Time * 1000);
        tessellator.BuildVisibleRun(stressRun, stressRun.Start.Time, stressRun.End.Time, StressProject, tessellation);
        Require(tessellation.Count <= AdaptiveHoldTessellator.MaxPointsPerRun &&
                Math.Abs(tessellation[0].Time - stressRun.Start.Time) < 1e-9 &&
                Math.Abs(tessellation[^1].Time - stressRun.End.Time) < 1e-9,
            "A high-curvature Hold must preserve both endpoints while respecting the per-run safety cap");

        var graphicObject = new GameObject("Hold geometry dirtiness validation", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
        var graphic = graphicObject.GetComponent<TaperedConnectorGraphic>();
        graphic.SetGeometry(Vector2.zero, Vector2.one, 10, 20);
        var firstRevision = graphic.GeometryRevision;
        graphic.SetGeometry(Vector2.zero, Vector2.one, 10, 20);
        Require(graphic.GeometryRevision == firstRevision,
            "Submitting identical Hold geometry must not dirty the uGUI mesh again");
        graphic.SetGeometry(Vector2.zero, Vector2.one * 2, 10, 20);
        Require(graphic.GeometryRevision == firstRevision + 1,
            "Changing Hold geometry must dirty the uGUI mesh exactly once");
        UnityEngine.Object.DestroyImmediate(graphicObject);
    }

    static void ValidateHoldPlayableRangeCheckpoints()
    {
        RuntimeNote Node(int index, double beat, SlideNodeRole role, SlideJudgeMode judgeMode, bool judged, float lane = 0) => new()
        {
            Index = index,
            SourceId = $"playable-range:{index}",
            Archetype = role == SlideNodeRole.Unspecified ? "LegacySlideNote" : $"USC Slide {role}",
            Time = beat,
            Beat = beat,
            Lane = lane,
            Size = 1,
            Kind = role == SlideNodeRole.Start && judgeMode == SlideJudgeMode.Normal
                ? RuntimeNoteKind.Tap : RuntimeNoteKind.Sustain,
            Visible = true,
            Judged = judged,
            SlideNodeRole = role,
            SlideJudgeMode = judgeMode,
        };

        RuntimeChart Chain(params RuntimeNote[] nodes)
        {
            var chart = new RuntimeChart();
            chart.Notes.AddRange(nodes);
            for (var index = 0; index < nodes.Length - 1; index++)
                chart.Connectors.Add(new RuntimeConnector { Start = nodes[index], End = nodes[index + 1] });
            return chart;
        }

        double[] AutoBeats(RuntimeChart chart) => chart.Notes
            .Where(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto)
            .Select(note => note.Beat)
            .OrderBy(beat => beat)
            .ToArray();

        bool TryReadBool(RuntimeHoldPath path, string propertyName, out bool value)
        {
            var property = typeof(RuntimeHoldPath).GetProperty(propertyName);
            if (property?.GetValue(path) is bool propertyValue)
            {
                value = propertyValue;
                return true;
            }
            value = false;
            return false;
        }

        bool TryReadDouble(RuntimeHoldPath path, string propertyName, out double value)
        {
            var property = typeof(RuntimeHoldPath).GetProperty(propertyName);
            if (property?.GetValue(path) is double propertyValue)
            {
                value = propertyValue;
                return true;
            }
            value = 0;
            return false;
        }

        bool HasNullDouble(RuntimeHoldPath path, string propertyName)
        {
            var property = typeof(RuntimeHoldPath).GetProperty(propertyName);
            return property != null && property.GetValue(path) == null;
        }

        var allNone = Chain(
            Node(2000, 0, SlideNodeRole.Start, SlideJudgeMode.None, false, -2),
            Node(2001, 8, SlideNodeRole.End, SlideJudgeMode.None, false, 2));
        HoldCheckpointBuilder.Apply(allNone, beat => beat);
        var allNoneAutos = AutoBeats(allNone);

        var noneLead = Chain(
            Node(2010, 0, SlideNodeRole.Start, SlideJudgeMode.None, false, -2),
            Node(2011, 2, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 0),
            Node(2012, 4, SlideNodeRole.End, SlideJudgeMode.Trace, true, 2));
        HoldCheckpointBuilder.Apply(noneLead, beat => beat);
        var noneLeadAutos = AutoBeats(noneLead);

        var sameBeat = Chain(
            Node(2020, 0, SlideNodeRole.Start, SlideJudgeMode.Normal, true, -1),
            Node(2021, 1, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 0),
            Node(2022, 2, SlideNodeRole.End, SlideJudgeMode.Trace, true, 1));
        HoldCheckpointBuilder.Apply(sameBeat, beat => beat);
        HoldCheckpointBuilder.Apply(sameBeat, beat => beat);
        var repeatedAutos = AutoBeats(sameBeat);
        var repeatedAutoDuplicates = repeatedAutos.GroupBy(beat => beat).Count(group => group.Count() > 1);
        var authoredCollisionCount = repeatedAutos.Count(beat => sameBeat.Connectors
            .SelectMany(connector => new[] { connector.Start, connector.End })
            .Distinct()
            .Any(note => note.Judged && Math.Abs(note.Beat - beat) < 1e-9));

        var legacy = Chain(
            Node(2030, 0, SlideNodeRole.Unspecified, SlideJudgeMode.Unspecified, true, -1),
            Node(2031, 1, SlideNodeRole.Unspecified, SlideJudgeMode.Unspecified, false, 0),
            Node(2032, 2, SlideNodeRole.Unspecified, SlideJudgeMode.Unspecified, true, 1));
        legacy.Notes[^1].Kind = RuntimeNoteKind.Release;
        HoldCheckpointBuilder.Apply(legacy, beat => beat);
        var legacyAutos = AutoBeats(legacy);

        var singleJudged = Chain(
            Node(2040, 0, SlideNodeRole.Start, SlideJudgeMode.None, false, -2),
            Node(2041, 2, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 0),
            Node(2042, 4, SlideNodeRole.End, SlideJudgeMode.None, false, 2));
        HoldCheckpointBuilder.Apply(singleJudged, beat => beat);
        var singleJudgedAutos = AutoBeats(singleJudged);

        var fallback = new RuntimeChart();
        var fallbackHead = Node(2050, 0, SlideNodeRole.Start, SlideJudgeMode.Normal, true);
        var fallbackLeft = Node(2051, 1, SlideNodeRole.End, SlideJudgeMode.Trace, true, -1);
        var fallbackRight = Node(2052, 1, SlideNodeRole.End, SlideJudgeMode.Trace, true, 1);
        fallback.Notes.AddRange(new[] { fallbackHead, fallbackLeft, fallbackRight });
        fallback.Connectors.Add(new RuntimeConnector { Start = fallbackHead, End = fallbackLeft });
        fallback.Connectors.Add(new RuntimeConnector { Start = fallbackHead, End = fallbackRight });
        HoldCheckpointBuilder.Apply(fallback, beat => beat);

        Debug.Log($"GUGARYTHM_TASK2_CHECKPOINT_COUNTS " +
                  $"allNonePlayable={allNone.PlayableCount} allNoneAuto={allNoneAutos.Length} " +
                  $"noneLeadPlayable={noneLead.PlayableCount} noneLeadAuto={noneLeadAutos.Length} " +
                  $"repeatedPlayable={sameBeat.PlayableCount} repeatedAuto={repeatedAutos.Length} " +
                  $"duplicateAutoBeats={repeatedAutoDuplicates} authoredCollisions={authoredCollisionCount} " +
                  $"legacyPlayable={legacy.PlayableCount} legacyAuto={legacyAutos.Length} " +
                  $"singleJudgedPlayable={singleJudged.PlayableCount} singleJudgedAuto={singleJudgedAutos.Length}");

        Require(allNoneAutos.Length == 0 && allNone.PlayableCount == 0,
            $"An explicit all-none Hold must have no playable range or Auto checkpoints, got Auto={allNoneAutos.Length}");
        Require(noneLeadAutos.SequenceEqual(new[] { 2.5d, 3d, 3.5d }) && noneLead.PlayableCount == 5,
            $"A none lead-in must create Auto checkpoints only inside its first/last judged bounds, got {string.Join(",", noneLeadAutos)}");
        Require(repeatedAutos.SequenceEqual(new[] { .5d, 1.5d }) && repeatedAutoDuplicates == 0 && authoredCollisionCount == 0,
            $"Repeated Apply must rebuild exactly one Auto per eligible beat and skip authored judged beats, got {string.Join(",", repeatedAutos)}");
        var legacyTail = legacy.Connectors[^1].End;
        Require(legacyAutos.SequenceEqual(new[] { .5d, 1d, 1.5d }) && legacyTail.IsHoldTerminal &&
                legacyTail.HoldCheckpointSource == HoldCheckpointSource.Tail,
            "An all-Unspecified legacy/SCP path must retain geometry-head-to-tail checkpoints and legacy Tail metadata");
        Require(singleJudgedAutos.Length == 0 && singleJudged.PlayableCount == 1 &&
                !singleJudged.Notes[1].IsHoldTerminal && singleJudged.Notes[1].HoldCheckpointSource != HoldCheckpointSource.Tail &&
                !singleJudged.Notes[2].IsHoldTerminal && singleJudged.Notes[2].HoldCheckpointSource != HoldCheckpointSource.Tail,
            "A one-judged-node path has no interior Auto and only an explicit judged structural End may become Tail");
        var noneLeadTail = noneLead.Connectors[^1].End;
        Require(noneLeadTail.IsHoldTerminal && noneLeadTail.HoldCheckpointSource == HoldCheckpointSource.Tail,
            "An explicit judged structural End must retain Tail metadata");
        Require(fallback.FallbackConnectors.Count == 2 && fallbackHead.Judged && fallbackLeft.Judged && fallbackRight.Judged,
            "A failed Hold topology must preserve legacy connectors and authored judgments");

        var allNonePath = allNone.HoldPaths.Single();
        var noneLeadPath = noneLead.HoldPaths.Single();
        var legacyPath = legacy.HoldPaths.Single();
        Require(TryReadBool(allNonePath, "HasPlayableRange", out var allNoneHasRange) && !allNoneHasRange &&
                HasNullDouble(allNonePath, "PlayableStartBeat") && HasNullDouble(allNonePath, "PlayableEndBeat") &&
                HasNullDouble(allNonePath, "PlayableStartTime") && HasNullDouble(allNonePath, "PlayableEndTime") &&
                TryReadBool(noneLeadPath, "HasPlayableRange", out var noneLeadHasRange) && noneLeadHasRange &&
                TryReadDouble(noneLeadPath, "VisualStartBeat", out var visualStartBeat) && Math.Abs(visualStartBeat) < 1e-9 &&
                TryReadDouble(noneLeadPath, "VisualEndBeat", out var visualEndBeat) && Math.Abs(visualEndBeat - 4) < 1e-9 &&
                TryReadDouble(noneLeadPath, "VisualStartTime", out var visualStartTime) && Math.Abs(visualStartTime) < 1e-9 &&
                TryReadDouble(noneLeadPath, "VisualEndTime", out var visualEndTime) && Math.Abs(visualEndTime - 4) < 1e-9 &&
                TryReadDouble(noneLeadPath, "PlayableStartBeat", out var playableStartBeat) && Math.Abs(playableStartBeat - 2) < 1e-9 &&
                TryReadDouble(noneLeadPath, "PlayableEndBeat", out var playableEndBeat) && Math.Abs(playableEndBeat - 4) < 1e-9 &&
                TryReadDouble(noneLeadPath, "PlayableStartTime", out var playableStartTime) && Math.Abs(playableStartTime - 2) < 1e-9 &&
                TryReadDouble(noneLeadPath, "PlayableEndTime", out var playableEndTime) && Math.Abs(playableEndTime - 4) < 1e-9 &&
                TryReadBool(legacyPath, "HasPlayableRange", out var legacyHasRange) && legacyHasRange &&
                TryReadDouble(legacyPath, "PlayableStartBeat", out var legacyStartBeat) && Math.Abs(legacyStartBeat) < 1e-9 &&
                TryReadDouble(legacyPath, "PlayableEndBeat", out var legacyEndBeat) && Math.Abs(legacyEndBeat - 2) < 1e-9,
            "RuntimeHoldPath must expose visual/playable beat/time bounds and legacy-compatible HasPlayableRange");

        var attachBounds = Chain(
            Node(2060, 0, SlideNodeRole.Start, SlideJudgeMode.None, false, -2),
            Node(2061, 2, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 0),
            Node(2062, 4, SlideNodeRole.End, SlideJudgeMode.None, false, 2));
        var earlyAttach = Node(2063, 1, SlideNodeRole.Attach, SlideJudgeMode.Trace, true, -1);
        var lateAttach = Node(2064, 3, SlideNodeRole.Attach, SlideJudgeMode.Trace, true, 1);
        earlyAttach.HoldRootIndex = attachBounds.Notes[0].Index;
        lateAttach.HoldRootIndex = attachBounds.Notes[0].Index;
        attachBounds.Notes.AddRange(new[] { earlyAttach, lateAttach });
        HoldCheckpointBuilder.Apply(attachBounds, beat => beat);
        var attachPath = attachBounds.HoldPaths.Single();
        var attachAutos = AutoBeats(attachBounds);

        var shifted = Chain(
            Node(2070, 0, SlideNodeRole.Start, SlideJudgeMode.None, false, -2),
            Node(2071, 1, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, -1),
            Node(2072, 3, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 1),
            Node(2073, 4, SlideNodeRole.End, SlideJudgeMode.None, false, 2));
        HoldCheckpointBuilder.Apply(shifted, beat => beat);
        var shiftedPath = shifted.HoldPaths.Single();
        var shiftedAuthored = shifted.Connectors.SelectMany(connector => new[] { connector.Start, connector.End })
            .Distinct().ToArray();
        var beatsBeforeShift = shiftedAuthored.Select(note => note.Beat).ToArray();
        var timesBeforeShift = shiftedAuthored.Select(note => note.Time).ToArray();
        shifted.ShiftTiming(4, 2);
        var shiftedNodesOnce = shiftedAuthored.Select((note, index) =>
            Math.Abs(note.Beat - beatsBeforeShift[index] - 4) < 1e-9 &&
            Math.Abs(note.Time - timesBeforeShift[index] - 2) < 1e-9).All(value => value);

        var invalidAllNone = new RuntimeChart();
        var invalidNoneHead = Node(2080, 0, SlideNodeRole.Start, SlideJudgeMode.None, false);
        var invalidNoneLeft = Node(2081, 2, SlideNodeRole.End, SlideJudgeMode.None, false, -1);
        var invalidNoneRight = Node(2082, 4, SlideNodeRole.End, SlideJudgeMode.None, false, 1);
        invalidAllNone.Notes.AddRange(new[] { invalidNoneHead, invalidNoneLeft, invalidNoneRight });
        invalidAllNone.Connectors.Add(new RuntimeConnector { Start = invalidNoneHead, End = invalidNoneLeft });
        invalidAllNone.Connectors.Add(new RuntimeConnector { Start = invalidNoneHead, End = invalidNoneRight });
        HoldCheckpointBuilder.Apply(invalidAllNone, beat => beat);
        var invalidAllNoneAutos = AutoBeats(invalidAllNone);

        var invalidNoneLead = new RuntimeChart();
        var invalidLeadHead = Node(2090, 0, SlideNodeRole.Start, SlideJudgeMode.None, false);
        var invalidLeadTick = Node(2091, 2, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, -1);
        var invalidLeadEnd = Node(2092, 4, SlideNodeRole.End, SlideJudgeMode.Trace, true, 1);
        invalidNoneLead.Notes.AddRange(new[] { invalidLeadHead, invalidLeadTick, invalidLeadEnd });
        invalidNoneLead.Connectors.Add(new RuntimeConnector { Start = invalidLeadHead, End = invalidLeadTick });
        invalidNoneLead.Connectors.Add(new RuntimeConnector { Start = invalidLeadHead, End = invalidLeadEnd });
        HoldCheckpointBuilder.Apply(invalidNoneLead, beat => beat);
        var invalidNoneLeadAutos = AutoBeats(invalidNoneLead);

        var invalidNonEnd = new RuntimeChart();
        var invalidNonEndHead = Node(2100, 0, SlideNodeRole.Start, SlideJudgeMode.Normal, true);
        var invalidNonEndLeft = Node(2101, 2, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, -1);
        var invalidNonEndRight = Node(2102, 4, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 1);
        invalidNonEnd.Notes.AddRange(new[] { invalidNonEndHead, invalidNonEndLeft, invalidNonEndRight });
        invalidNonEnd.Connectors.Add(new RuntimeConnector { Start = invalidNonEndHead, End = invalidNonEndLeft });
        invalidNonEnd.Connectors.Add(new RuntimeConnector { Start = invalidNonEndHead, End = invalidNonEndRight });
        HoldCheckpointBuilder.Apply(invalidNonEnd, beat => beat);
        var invalidNonEndAutos = AutoBeats(invalidNonEnd);

        var nonInvertibleLinear = Chain(
            Node(2120, 0, SlideNodeRole.Start, SlideJudgeMode.Normal, true, 0),
            Node(2121, 1, SlideNodeRole.Tick, SlideJudgeMode.Trace, true, 2),
            Node(2122, 2, SlideNodeRole.End, SlideJudgeMode.Trace, true, 4));
        nonInvertibleLinear.DefaultTimeScaleGroup = "reverse";
        nonInvertibleLinear.TimeScaleGroups["reverse"] = new RuntimeTimeScaleGroup("reverse", new[] { (0d, -1d) });
        foreach (var note in nonInvertibleLinear.Notes) note.TimeScaleGroup = "reverse";
        nonInvertibleLinear.Connectors[0].Ease = 1;
        HoldCheckpointBuilder.Apply(nonInvertibleLinear, beat => beat);
        var nonInvertibleAutos = nonInvertibleLinear.Notes
            .Where(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto)
            .OrderBy(note => note.Beat).ToArray();

        var invalidLegacy = new RuntimeChart();
        var invalidLegacyHead = Node(2110, 0, SlideNodeRole.Unspecified, SlideJudgeMode.Unspecified, true);
        var invalidLegacyLeft = Node(2111, 2, SlideNodeRole.Unspecified, SlideJudgeMode.Unspecified, true, -1);
        var invalidLegacyRight = Node(2112, 4, SlideNodeRole.Unspecified, SlideJudgeMode.Unspecified, true, 1);
        invalidLegacy.Notes.AddRange(new[] { invalidLegacyHead, invalidLegacyLeft, invalidLegacyRight });
        invalidLegacy.Connectors.Add(new RuntimeConnector { Start = invalidLegacyHead, End = invalidLegacyLeft });
        invalidLegacy.Connectors.Add(new RuntimeConnector { Start = invalidLegacyHead, End = invalidLegacyRight });
        HoldCheckpointBuilder.Apply(invalidLegacy, beat => beat);
        var invalidLegacyAutos = AutoBeats(invalidLegacy);

        Debug.Log($"GUGARYTHM_TASK2_REVIEW_COUNTS " +
                  $"attachStart={attachPath.PlayableStartBeat} attachEnd={attachPath.PlayableEndBeat} attachAuto={attachAutos.Length} " +
                  $"shiftVisualBeat={shiftedPath.VisualStartBeat}:{shiftedPath.VisualEndBeat} " +
                  $"shiftVisualTime={shiftedPath.VisualStartTime}:{shiftedPath.VisualEndTime} " +
                  $"shiftPlayableBeat={shiftedPath.PlayableStartBeat}:{shiftedPath.PlayableEndBeat} " +
                  $"shiftPlayableTime={shiftedPath.PlayableStartTime}:{shiftedPath.PlayableEndTime} " +
                  $"invalidAllNoneAuto={invalidAllNoneAutos.Length} invalidNoneLeadAuto={invalidNoneLeadAutos.Length} " +
                  $"invalidNonEndAuto={invalidNonEndAutos.Length} nonInvertibleAuto={nonInvertibleAutos.Length} " +
                  $"invalidLegacyAuto={invalidLegacyAutos.Length} " +
                  $"invalidLeadTickTail={invalidLeadTick.IsHoldTerminal} invalidLeadEndTail={invalidLeadEnd.IsHoldTerminal} " +
                  $"invalidNonEndTail={invalidNonEndLeft.IsHoldTerminal || invalidNonEndRight.IsHoldTerminal}");

        Require(Math.Abs(attachPath.PlayableStartBeat.Value - 1) < 1e-9 &&
                Math.Abs(attachPath.PlayableEndBeat.Value - 3) < 1e-9 &&
                Math.Abs(attachPath.PlayableStartTime.Value - 1) < 1e-9 &&
                Math.Abs(attachPath.PlayableEndTime.Value - 3) < 1e-9 &&
                attachAutos.SequenceEqual(new[] { 1.5d, 2.5d }) &&
                earlyAttach.HoldCheckpointSource == HoldCheckpointSource.Mid &&
                lateAttach.HoldCheckpointSource == HoldCheckpointSource.Mid,
            "Judged Attach nodes outside connector geometry membership must define the earliest/latest playable bounds");
        Require(shiftedNodesOnce &&
                Math.Abs(shiftedPath.VisualStartBeat - 4) < 1e-9 && Math.Abs(shiftedPath.VisualEndBeat - 8) < 1e-9 &&
                Math.Abs(shiftedPath.VisualStartTime - 2) < 1e-9 && Math.Abs(shiftedPath.VisualEndTime - 6) < 1e-9 &&
                Math.Abs(shiftedPath.PlayableStartBeat.Value - 5) < 1e-9 && Math.Abs(shiftedPath.PlayableEndBeat.Value - 7) < 1e-9 &&
                Math.Abs(shiftedPath.PlayableStartTime.Value - 3) < 1e-9 && Math.Abs(shiftedPath.PlayableEndTime.Value - 5) < 1e-9,
            "RuntimeChart.ShiftTiming must refresh all visual/playable path bounds without shifting authored nodes twice");
        Require(invalidAllNoneAutos.Length == 0 && invalidAllNone.PlayableCount == 0,
            "An invalid explicit all-none topology must not fall back to legacy Auto judgments");
        Require(invalidNoneLeadAutos.Length == 0 && !invalidLeadTick.IsHoldTerminal &&
                invalidLeadTick.HoldCheckpointSource == HoldCheckpointSource.Mid &&
                invalidLeadEnd.IsHoldTerminal && invalidLeadEnd.HoldCheckpointSource == HoldCheckpointSource.Tail,
            "An invalid explicit none lead-in must retain authored structural semantics without synthesizing unsafe Auto judgments");
        Require(invalidNonEndAutos.Length == 0 && !invalidNonEndLeft.IsHoldTerminal && !invalidNonEndRight.IsHoldTerminal &&
                invalidNonEndLeft.HoldCheckpointSource == HoldCheckpointSource.Mid &&
                invalidNonEndRight.HoldCheckpointSource == HoldCheckpointSource.Mid,
            "An invalid explicit path ending at a non-End node must not infer Tail or synthesize Auto judgments");
        var expectedEaseInLane = 2 * (1 - Math.Cos(Math.PI * .25));
        Require(nonInvertibleAutos.Select(note => note.Beat).SequenceEqual(new[] { .5d, 1.5d }) &&
                Math.Abs(nonInvertibleAutos[0].Lane - expectedEaseInLane) < 1e-6 &&
                nonInvertibleLinear.Notes[1].HoldCheckpointSource == HoldCheckpointSource.Mid &&
                nonInvertibleLinear.Notes[2].IsHoldTerminal &&
                nonInvertibleLinear.Notes[2].HoldCheckpointSource == HoldCheckpointSource.Tail,
            "A unique time-monotonic explicit chain must retain authored-bound Auto checkpoints and per-segment ease even when visual time is non-invertible");
        Require(invalidLegacyAutos.SequenceEqual(new[] { .5d, 1d, 1.5d }),
            "An invalid all-Unspecified topology must retain legacy checkpoint fallback behavior");
    }

    public static void ValidateValidPathAttachEvaluatorAlignment()
    {
        const string usc = @"{
            ""usc"": {
                ""objects"": [
                    { ""type"": ""bpm"", ""beat"": 0, ""bpm"": 120 },
                    { ""type"": ""slide"", ""connections"": [
                        { ""beat"": 4, ""judgeType"": ""normal"", ""lane"": 0, ""size"": 0.25, ""type"": ""start"", ""ease"": ""in"" },
                        { ""beat"": 4.5, ""judgeType"": ""trace"", ""lane"": 99, ""size"": 1, ""type"": ""attach"" },
                        { ""beat"": 6, ""judgeType"": ""trace"", ""lane"": 80, ""size"": 2, ""type"": ""end"" }
                    ] }
                ]
            }
        }";
        var imported = new UscChartImporter().Import("valid-eased-judged-attach.usc",
            System.Text.Encoding.UTF8.GetBytes(usc));
        Require(imported.Success, "Valid eased judged-Attach fixture must import: " + imported.Error);
        var path = imported.Chart.HoldPaths.Single();
        var attach = imported.Chart.Notes.Single(note => note.SlideNodeRole == SlideNodeRole.Attach);
        var evaluated = path.Evaluator.Evaluate(attach.Time);
        var score = new ScoreState();
        var engine = new JudgmentEngine(new[] { attach }, score);
        engine.Process(attach.Time, Array.Empty<InputToken>(),
            new[] { new ActiveContact(1, evaluated.Lane, attach.Time - .1) });
        var autoCount = imported.Chart.Notes.Count(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto);

        Debug.Log($"GUGARYTHM_FINAL_ATTACH_EVALUATOR " +
                  $"lane={attach.Lane:0.######} size={attach.Size:0.######} " +
                  $"evaluatorLane={evaluated.Lane:0.######} evaluatorSize={evaluated.Size:0.######} " +
                  $"judgment={attach.Grade} playable={imported.Chart.PlayableCount} auto={autoCount}");
        Require(Math.Abs(attach.Lane - evaluated.Lane) < 1e-6 &&
                Math.Abs(attach.Size - evaluated.Size) < 1e-6,
            "A judged Attach on a valid Hold path must use the shared evaluator Lane and Size");
        Require(attach.Grade == JudgmentGrade.Perfect && score.Perfect == 1,
            "A judged Attach must resolve from sustained contact at the visible evaluator lane");
        Require(imported.Chart.PlayableCount == 5 && autoCount == 2,
            "Aligning valid-path Attach geometry must not change authored or Auto checkpoint counts");
    }

    static void ValidateHoldEaseParity()
    {
        var easeNames = new[] { "linear", "in", "out", "inout" };
        var sampleProgress = new[] { .25f, .75f };
        var sharedMathType = typeof(RuntimeHoldPath).Assembly.GetType("Gugarythm.HoldPathMath");
        var sharedEaseMethod = sharedMathType?.GetMethod("EaseProgress",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var markerValues = new List<string>();

        float ExpectedProgress(float progress, int ease) => ease switch
        {
            1 => 1f - (float)Math.Cos(progress * Math.PI * .5),
            2 => (float)Math.Sin(progress * Math.PI * .5),
            3 => progress < .5f ? 2 * progress * progress :
                1 - (float)Math.Pow(-2 * progress + 2, 2) * .5f,
            _ => progress,
        };

        for (var ease = 0; ease < easeNames.Length; ease++)
        {
            var usc = $@"{{
                ""usc"": {{
                    ""objects"": [
                        {{ ""type"": ""bpm"", ""beat"": 0, ""bpm"": 120 }},
                        {{ ""type"": ""slide"", ""connections"": [
                            {{ ""beat"": 4, ""judgeType"": ""normal"", ""lane"": 0, ""size"": 1, ""type"": ""start"", ""ease"": ""{easeNames[ease]}"" }},
                            {{ ""beat"": 4.5, ""judgeType"": ""trace"", ""lane"": 99, ""size"": 1, ""type"": ""attach"" }},
                            {{ ""beat"": 5.5, ""judgeType"": ""trace"", ""lane"": 99, ""size"": 1, ""type"": ""attach"" }},
                            {{ ""beat"": 6, ""judgeType"": ""trace"", ""lane"": 4, ""size"": 1, ""type"": ""end"" }}
                        ] }}
                    ]
                }}
            }}";
            var imported = new UscChartImporter().Import($"ease-{ease}.usc",
                System.Text.Encoding.UTF8.GetBytes(usc));
            Require(imported.Success, $"Ease {ease} USC fixture must import: {imported.Error}");
            var attachNotes = imported.Chart.Notes
                .Where(note => note.SlideNodeRole == SlideNodeRole.Attach)
                .OrderBy(note => note.Beat)
                .ToArray();
            var validPath = imported.Chart.HoldPaths.Single();

            var fallback = new RuntimeChart { DefaultTimeScaleGroup = "reverse" };
            fallback.TimeScaleGroups["reverse"] = new RuntimeTimeScaleGroup("reverse", new[] { (0d, -1d) });
            var start = new RuntimeNote
            {
                Index = 2200 + ease * 2,
                SourceId = $"ease-fallback:{ease}:start",
                Archetype = "USC Slide start",
                Beat = 4,
                Time = 4,
                Lane = 0,
                Size = 1,
                Kind = RuntimeNoteKind.Tap,
                Visible = true,
                Judged = true,
                SlideNodeRole = SlideNodeRole.Start,
                SlideJudgeMode = SlideJudgeMode.Normal,
                TimeScaleGroup = "reverse",
            };
            var end = new RuntimeNote
            {
                Index = start.Index + 1,
                SourceId = $"ease-fallback:{ease}:end",
                Archetype = "USC Trace Slide end",
                Beat = 6,
                Time = 6,
                Lane = 4,
                Size = 1,
                Kind = RuntimeNoteKind.Sustain,
                Visible = true,
                Judged = true,
                SlideNodeRole = SlideNodeRole.End,
                SlideJudgeMode = SlideJudgeMode.Trace,
                TimeScaleGroup = "reverse",
            };
            fallback.Notes.AddRange(new[] { start, end });
            fallback.Connectors.Add(new RuntimeConnector { Start = start, End = end, Ease = ease });
            HoldCheckpointBuilder.Apply(fallback, beat => beat);
            var fallbackLanes = fallback.Notes
                .Where(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto &&
                    (Math.Abs(note.Beat - 4.5) < 1e-9 || Math.Abs(note.Beat - 5.5) < 1e-9))
                .OrderBy(note => note.Beat)
                .Select(note => note.Lane)
                .ToArray();

            Require(attachNotes.Length == sampleProgress.Length && fallbackLanes.Length == sampleProgress.Length,
                $"Ease {ease} parity fixture must produce two Attach and two sampled fallback Auto nodes");
            for (var sample = 0; sample < sampleProgress.Length; sample++)
            {
                var expectedLane = 4 * ExpectedProgress(sampleProgress[sample], ease);
                var validPathLane = validPath.Evaluator.Evaluate(attachNotes[sample].Time).Lane;
                Require(Math.Abs(attachNotes[sample].Lane - validPathLane) < 1e-6,
                    $"Valid-path USC Attach Ease {ease} drifted from its shared evaluator at progress {sampleProgress[sample]}");
                Require(Math.Abs(fallbackLanes[sample] - expectedLane) < 1e-6,
                    $"Safe fallback Ease {ease} drifted at progress {sampleProgress[sample]}");
            }
            markerValues.Add($"ease{ease}=valid:{attachNotes[0].Lane:0.######}:{attachNotes[1].Lane:0.######}" +
                $"/fallback:{fallbackLanes[0]:0.######}:{fallbackLanes[1]:0.######}");
        }

        Debug.Log("GUGARYTHM_TASK2_EASE_PARITY " + string.Join(" ", markerValues));
        Require(sharedEaseMethod != null,
            "Hold interpolation must expose one shared pure HoldPathMath.EaseProgress evaluator");
        for (var ease = 0; ease < easeNames.Length; ease++)
        foreach (var progress in sampleProgress)
        {
            var actual = (float)sharedEaseMethod.Invoke(null, new object[] { progress, ease });
            Require(Math.Abs(actual - ExpectedProgress(progress, ease)) < 1e-6,
                $"Shared Hold Ease {ease} drifted at progress {progress}");
        }
    }

    static void ValidateChartRenderIndex()
    {
        var chart = new RuntimeChart { DefaultTimeScaleGroup = "main" };
        chart.TimeScaleGroups["main"] = new RuntimeTimeScaleGroup("main", new[] { (0d, 1d) });
        chart.TimeScaleGroups["fast"] = new RuntimeTimeScaleGroup("fast", new[] { (0d, 2d) });
        chart.TimeScaleGroups["reverse"] = new RuntimeTimeScaleGroup("reverse", new[] { (0d, -1d) });
        RuntimeNote NoteAt(int index, double time, string group) => new()
        {
            Index = index,
            Time = time,
            Beat = time,
            Lane = index,
            Size = 1,
            Visible = true,
            TimeScaleGroup = group,
        };
        var late = NoteAt(2, 1.5, "main");
        var early = NoteAt(1, .5, "main");
        var fastOutside = NoteAt(3, 1.5, "fast");
        var reverse = NoteAt(4, 1.5, "reverse");
        chart.Notes.AddRange(new[] { late, fastOutside, reverse, early });

        var holdA = NoteAt(10, .25, "main");
        var holdB = NoteAt(11, 2, "main");
        chart.Connectors.Add(new RuntimeConnector { Start = holdA, End = holdB });
        HoldCheckpointBuilder.Apply(chart, beat => beat);
        var index = new ChartRenderIndex(chart);
        var notes = new List<RuntimeNote>();
        index.QueryNotes(0, 0, 2, notes);
        Require(notes.SequenceEqual(new[] { early, late }),
            "ChartRenderIndex must query each TimeScaleGroup in visual-position order with stable note ordering");
        index.QueryNotes(0, .5, .5, notes);
        Require(notes.Count == 1 && ReferenceEquals(notes[0], early),
            "ChartRenderIndex note queries must include exact visual-window boundaries");
        index.QueryNotes(1, .5, .5, notes);
        Require(notes.Contains(reverse),
            "ChartRenderIndex must query reverse TimeScaleGroups in visual-position space");

        var runs = new List<HoldRenderRun>();
        index.QueryHoldRuns(1, 0, .25, runs);
        Require(runs.Count == 1 && ReferenceEquals(runs[0], chart.HoldPaths[0].RenderRuns[0]),
            "ChartRenderIndex must return a Hold run whose visual interval overlaps the query window");
        index.QueryHoldRuns(4, 0, .25, runs);
        Require(runs.Count == 0, "ChartRenderIndex must exclude Hold runs outside the visual window");

        var emptyIndex = new ChartRenderIndex(new RuntimeChart());
        emptyIndex.QueryNotes(0, 1, 1, notes);
        emptyIndex.QueryHoldRuns(0, 1, 1, runs);
        Require(notes.Count == 0 && runs.Count == 0,
            "ChartRenderIndex must return empty reusable buffers for an empty chart");
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

    static void ValidateStartupSplashConfiguration()
    {
        var startupType = typeof(SonolusLandscapePrototype).Assembly.GetType("Gugarythm.GugarythmStartupSplash");
        Require(startupType != null, "The startup splash controller must exist");
        var durationField = startupType.GetField("DefaultDisplaySeconds",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Require(durationField != null, "The startup splash duration must be exposed for validation");
        Require(Math.Abs((float)durationField.GetRawConstantValue() - 1.5f) < .0001f,
            "The GUGARYTHM startup page must remain visible for 1.5 seconds");
        Debug.Log("GUGARYTHM_STARTUP_SPLASH_VALIDATION_OK duration=1.5");
    }

    static void ValidateStartupBuildSceneOrder()
    {
        var method = typeof(CreatePrototypeScene).GetMethod("PlayerBuildScenePaths",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(method != null, "Android builds must expose their ordered player scene paths");
        var scenes = (string[])method.Invoke(null, null);
        Require(scenes.Length == 5 && scenes[0] == "Assets/Scenes/StartupScene.unity",
            "Android builds must start with StartupScene before the library scene");
        Debug.Log("GUGARYTHM_STARTUP_BUILD_SCENE_VALIDATION_OK first=StartupScene");
    }

    static void ValidateBundledChartManifest()
    {
        var manifestPath = Path.Combine(Application.dataPath, "StreamingAssets/BundledCharts/bundled-ggr.txt");
        Require(File.Exists(manifestPath), "Bundled GGR manifest is missing");
        var names = File.ReadAllLines(manifestPath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .ToArray();
        Require(names.Length == 14, $"Expected 14 bundled GGR charts, got {names.Length}");
        Require(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length,
            "Bundled GGR manifest must not contain duplicate files");
        foreach (var name in names)
        {
            Require(name.EndsWith(".ggr", StringComparison.OrdinalIgnoreCase),
                "Bundled chart manifest entries must use the GGR extension");
            Require(File.Exists(Path.Combine(Application.dataPath, "StreamingAssets/BundledCharts", name)),
                "Bundled GGR file is missing: " + name);
        }
        Debug.Log("GUGARYTHM_BUNDLED_CHARTS_VALIDATION_OK count=" + names.Length);
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

    static void ValidateScrollSpeedMath()
    {
        Require(Math.Abs(SonolusLandscapePrototype.DefaultScrollSpeed - 4f) < .0001f,
            "The default scroll speed must be 4");
        Require(Math.Abs(SonolusLandscapePrototype.NoteApproachDurationForScrollSpeed(4f) - 2f) < .0001f,
            "Scroll speed 4 must preserve the current two-second approach duration");
        Require(Math.Abs(SonolusLandscapePrototype.NoteApproachDurationForScrollSpeed(8f) - 1f) < .0001f,
            "Doubling scroll speed must halve the approach duration");
        Require(Math.Abs(SonolusLandscapePrototype.NoteApproachDurationForScrollSpeed(2f) - 4f) < .0001f,
            "Halving scroll speed must double the approach duration");
        Debug.Log("GUGARYTHM_SCROLL_SPEED_VALIDATION_OK");
    }

    static void ValidateLibrarySelectionRestore()
    {
        var method = typeof(SonolusLandscapePrototype).GetMethod(
            "ShouldEnableLibraryStartButton", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Require(method != null, "Library selection restore must expose its start-button state rule");
        Require((bool)method.Invoke(null, new object[] { true }),
            "A restored library selection must enable the start button");
        Require(!(bool)method.Invoke(null, new object[] { false }),
            "The start button must remain disabled without a restored selection");
        Debug.Log("GUGARYTHM_LIBRARY_SELECTION_RESTORE_VALIDATION_OK");
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
        Debug.Log($"GUGARYTHM_TASK2_ATTACHED_GGR_COUNTS playable={result.Chart.PlayableCount} " +
                  $"auto={result.Chart.Notes.Count(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto)} " +
                  $"autoRoots={result.Chart.Notes.Where(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto).Select(note => note.HoldRootIndex).Distinct().Count()} " +
                  $"holdPaths={result.Chart.HoldPaths.Count} fallback={result.Chart.FallbackConnectors.Count} " +
                  $"playableRanges={result.Chart.HoldPaths.Count(holdPath => holdPath.HasPlayableRange)} " +
                  $"semanticJudged={result.Chart.HoldPaths.Sum(holdPath => holdPath.SemanticNodes.Count(note => note.Judged))} " +
                  $"warnings={string.Join(" | ", result.Chart.Warnings)}");
        Require(result.Chart.PlayableCount == 3710 &&
                result.Chart.Notes.Count(note => note.HoldCheckpointSource == HoldCheckpointSource.Auto) == 554,
            $"Attached GGR must retain 3156 authored judgments plus 554 semantic-safe fallback Auto checkpoints, got {result.Chart.PlayableCount}");
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

    public static void ValidateTaperedConnectorGeometry()
    {
        var populateMesh = typeof(TaperedConnectorGraphic).GetMethod(
            "OnPopulateMesh", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, new[] { typeof(VertexHelper) }, null);
        Require(populateMesh != null, "Hold ribbon geometry must expose its uGUI mesh population path");

        var graphicObject = new GameObject("Hold ribbon geometry validation", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TaperedConnectorGraphic));
        var graphic = graphicObject.GetComponent<TaperedConnectorGraphic>();
        graphic.drawGlow = false;
        graphic.drawEdges = false;
        graphic.color = new Color(.2f, .8f, .4f, .8f);
        graphic.sourceUvInset = .1f;
        var helper = new VertexHelper();
        var mesh = new Mesh { name = "Hold ribbon validation mesh" };

        void Populate(Vector2[] points, float[] pointWidths, float[] pointAlphas = null)
        {
            graphic.BeginPath(points.Length);
            for (var index = 0; index < points.Length; index++)
                graphic.SetPathPoint(index, points[index], pointWidths[index], pointAlphas?[index] ?? 1);
            graphic.EndPath();
            helper.Clear();
            populateMesh.Invoke(graphic, new object[] { helper });
            mesh.Clear();
            helper.FillMesh(mesh);
        }

        int EdgeUseCount(int a, int b)
        {
            var triangles = mesh.triangles;
            var count = 0;
            for (var index = 0; index < triangles.Length; index += 3)
            {
                for (var edge = 0; edge < 3; edge++)
                {
                    var first = triangles[index + edge];
                    var second = triangles[index + (edge + 1) % 3];
                    if ((first == a && second == b) || (first == b && second == a)) count++;
                }
            }
            return count;
        }

        bool HasZeroAreaTriangle()
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (var index = 0; index < triangles.Length; index += 3)
            {
                var a = vertices[triangles[index]];
                var b = vertices[triangles[index + 1]];
                var c = vertices[triangles[index + 2]];
                if (Vector3.Cross(b - a, c - a).sqrMagnitude <= .00000001f) return true;
            }
            return false;
        }

        bool HasConsistentPositiveWinding()
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (var index = 0; index < triangles.Length; index += 3)
            {
                var a = vertices[triangles[index]];
                var b = vertices[triangles[index + 1]];
                var c = vertices[triangles[index + 2]];
                if (Vector3.Cross(b - a, c - a).z <= .00001f) return false;
            }
            return true;
        }

        bool HasPositiveAreaOverlap(
            Vector2 a0, Vector2 a1, Vector2 a2,
            Vector2 b0, Vector2 b1, Vector2 b2)
        {
            bool Separates(Vector2 first, Vector2 second)
            {
                var edge = second - first;
                var axis = new Vector2(-edge.y, edge.x);
                var aMin = Mathf.Min(Vector2.Dot(a0, axis), Vector2.Dot(a1, axis), Vector2.Dot(a2, axis));
                var aMax = Mathf.Max(Vector2.Dot(a0, axis), Vector2.Dot(a1, axis), Vector2.Dot(a2, axis));
                var bMin = Mathf.Min(Vector2.Dot(b0, axis), Vector2.Dot(b1, axis), Vector2.Dot(b2, axis));
                var bMax = Mathf.Max(Vector2.Dot(b0, axis), Vector2.Dot(b1, axis), Vector2.Dot(b2, axis));
                return Mathf.Min(aMax, bMax) - Mathf.Max(aMin, bMin) <= .00001f;
            }

            return !Separates(a0, a1) && !Separates(a1, a2) && !Separates(a2, a0) &&
                   !Separates(b0, b1) && !Separates(b1, b2) && !Separates(b2, b0);
        }

        bool HasOverlappingTriangleInteriors()
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            for (var first = 0; first < triangles.Length; first += 3)
            {
                var a0 = (Vector2)vertices[triangles[first]];
                var a1 = (Vector2)vertices[triangles[first + 1]];
                var a2 = (Vector2)vertices[triangles[first + 2]];
                for (var second = first + 3; second < triangles.Length; second += 3)
                {
                    var b0 = (Vector2)vertices[triangles[second]];
                    var b1 = (Vector2)vertices[triangles[second + 1]];
                    var b2 = (Vector2)vertices[triangles[second + 2]];
                    if (HasPositiveAreaOverlap(a0, a1, a2, b0, b1, b2)) return true;
                }
            }
            return false;
        }

        var straightPoints = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(20, 0) };
        Populate(straightPoints, new[] { 4f, 6f, 8f }, new[] { 1f, .5f, .25f });
        Debug.Log($"GUGARYTHM_CONNECTOR_GEOMETRY_TOPOLOGY vertices={mesh.vertexCount} triangles={mesh.triangles.Length / 3} expectedVertices=6 expectedTriangles=4");
        Require(mesh.vertexCount == 6 && mesh.triangles.Length == 12,
            "A three-point Hold ribbon must reuse one pair of vertices at its interior cross-section");
        Require(EdgeUseCount(0, 1) == 1 && EdgeUseCount(2, 3) == 2 && EdgeUseCount(4, 5) == 1,
            "Only the true start and end of a Hold ribbon may remain capped");
        Require(!HasZeroAreaTriangle(), "A straight Hold ribbon must not emit zero-area triangles");

        var straightVertices = mesh.vertices;
        var startCenter = (straightVertices[0] + straightVertices[1]) * .5f;
        var endCenter = (straightVertices[4] + straightVertices[5]) * .5f;
        Require(((Vector2)startCenter - straightPoints[0]).sqrMagnitude < .000001f &&
                ((Vector2)endCenter - straightPoints[2]).sqrMagnitude < .000001f &&
                Math.Abs(Vector3.Distance(straightVertices[0], straightVertices[1]) - 4f) < .0001f &&
                Math.Abs(Vector3.Distance(straightVertices[4], straightVertices[5]) - 8f) < .0001f,
            "Continuous Hold geometry must preserve submitted endpoints and widths");
        var straightUv = mesh.uv;
        Require(straightUv.All(value => Math.Abs(value.x - .1f) < .0001f || Math.Abs(value.x - .9f) < .0001f),
            "Continuous Hold geometry must preserve the configured horizontal texture inset");
        var straightColors = mesh.colors32;
        Require(Math.Abs(straightColors[0].a / 255f - .26f) < .01f &&
                Math.Abs(straightColors[2].a / 255f - .13f) < .01f &&
                Math.Abs(straightColors[4].a / 255f - .065f) < .01f,
            "Continuous Hold geometry must preserve fill alpha limits and per-point alpha multipliers");

        var repeatedPoint = new Vector2(10, 0);
        Populate(new[] { Vector2.zero, repeatedPoint, repeatedPoint, new Vector2(20, 0) },
            new[] { 4f, 6f, 10f, 8f }, new[] { 1f, .75f, .25f, .5f });
        var repeatedVertices = mesh.vertices;
        var repeatedColors = mesh.colors32;
        var repeatedCenter = (Vector2)(repeatedVertices[2] + repeatedVertices[3]) * .5f;
        var repeatedWidth = Vector3.Distance(repeatedVertices[2], repeatedVertices[3]);
        var repeatedAlpha = repeatedColors[2].a / 255f;
        var repeatedTopologyOk = mesh.vertexCount == 6 && mesh.triangles.Length == 12 &&
                                 EdgeUseCount(2, 3) == 2 && !HasZeroAreaTriangle() &&
                                 HasConsistentPositiveWinding() && !HasOverlappingTriangleInteriors();
        var repeatedSemanticsOk = (repeatedCenter - repeatedPoint).sqrMagnitude < .000001f &&
                                  Math.Abs(repeatedWidth - 10f) < .0001f &&
                                  Math.Abs(repeatedAlpha - .065f) < .01f;
        Debug.Log($"GUGARYTHM_CONNECTOR_COINCIDENT_FIXTURE vertices={mesh.vertexCount} triangles={mesh.triangles.Length / 3} " +
                  $"interiorUses={EdgeUseCount(2, 3)} width={repeatedWidth:F3} alpha={repeatedAlpha:F3} " +
                  $"topology={repeatedTopologyOk} lastSampleSemantics={repeatedSemanticsOk}");

        var cornerPoint = new Vector2(10, 0);
        Populate(new[] { Vector2.zero, cornerPoint, new Vector2(10, 10) }, new[] { 4f, 4f, 4f });
        var cornerVertices = mesh.vertices;
        var miterRatio = Vector2.Distance((Vector2)cornerVertices[2], cornerPoint) / 2f;
        Require(mesh.vertexCount == 6 && miterRatio > 1.4f && miterRatio < 1.42f && !HasZeroAreaTriangle(),
            "A normal turn must share one averaged-tangent miter section without changing its authored half-width");

        var sharpPoint = new Vector2(10, 0);
        var bevelFixturesOk = true;
        var bevelInnerRatio = 0f;
        var bevelOuterMaxRatio = 0f;
        foreach (var direction in new[] { 1f, -1f })
        {
            Populate(new[] { Vector2.zero, sharpPoint, new Vector2(5, direction * 5) }, new[] { 4f, 4f, 4f });
            var sharpVertices = mesh.vertices;
            var positiveInner = Vector2.Distance((Vector2)sharpVertices[2], (Vector2)sharpVertices[4]) < .0001f;
            var negativeInner = Vector2.Distance((Vector2)sharpVertices[3], (Vector2)sharpVertices[5]) < .0001f;
            var hasSingleInnerIntersection = positiveInner != negativeInner;
            var innerFirst = positiveInner ? 2 : 3;
            var innerSecond = positiveInner ? 4 : 5;
            var outerFirst = positiveInner ? 3 : 2;
            var outerSecond = positiveInner ? 5 : 4;
            var innerRatio = hasSingleInnerIntersection
                ? Math.Max(Vector2.Distance((Vector2)sharpVertices[innerFirst], sharpPoint),
                    Vector2.Distance((Vector2)sharpVertices[innerSecond], sharpPoint)) / 2f
                : 0;
            var outerMaxRatio = Math.Max(Vector2.Distance((Vector2)sharpVertices[outerFirst], sharpPoint),
                Vector2.Distance((Vector2)sharpVertices[outerSecond], sharpPoint)) / 2f;
            var windingOk = HasConsistentPositiveWinding();
            var overlap = HasOverlappingTriangleInteriors();
            var fixtureOk = mesh.vertexCount == 8 && mesh.triangles.Length == 15 &&
                            hasSingleInnerIntersection && innerRatio > 2f && innerRatio < 2.62f &&
                            outerMaxRatio <= 1.0001f && !HasZeroAreaTriangle() && windingOk && !overlap;
            bevelFixturesOk &= fixtureOk;
            bevelInnerRatio = Math.Max(bevelInnerRatio, innerRatio);
            bevelOuterMaxRatio = Math.Max(bevelOuterMaxRatio, outerMaxRatio);
            Debug.Log($"GUGARYTHM_CONNECTOR_BEVEL_FIXTURE direction={direction:+0;-0} vertices={mesh.vertexCount} " +
                      $"triangles={mesh.triangles.Length / 3} innerIntersection={hasSingleInnerIntersection} " +
                      $"innerRatio={innerRatio:F3} outerMaxRatio={outerMaxRatio:F3} winding={windingOk} overlap={overlap}");
        }

        Require(repeatedTopologyOk && repeatedSemanticsOk,
            "Consecutive coincident Hold samples must coalesce to one section using the last sample's width and alpha");
        Require(bevelFixturesOk,
            "Both 135-degree turn directions must emit one outer bevel triangle around a shared inner intersection with consistent winding and no overlap");

        graphic.drawGlow = true;
        graphic.drawEdges = true;
        Populate(straightPoints, new[] { 4f, 6f, 8f });
        Require(mesh.vertexCount == 24 && mesh.triangles.Length == 48,
            "Fill, glow, left-edge, and right-edge strips must each reuse their three cross-sections");
        Debug.Log($"GUGARYTHM_CONNECTOR_GEOMETRY_VALIDATION_OK straightVertices=6 straightTriangles=4 cornerVertices=6 bevelVertices=8 bevelTriangles=5 fullVertices={mesh.vertexCount} fullTriangles={mesh.triangles.Length / 3} miterRatio={miterRatio:F3} bevelInnerRatio={bevelInnerRatio:F3} bevelOuterMaxRatio={bevelOuterMaxRatio:F3}");

        helper.Dispose();
        UnityEngine.Object.DestroyImmediate(mesh);
        UnityEngine.Object.DestroyImmediate(graphicObject);
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

        Require(firstJudgedAfterNoneHead.Judged && firstJudgedAfterNoneHead.Kind == RuntimeNoteKind.Sustain,
            "firstJudgedConnection must not promote a structural Slide Tick to Tap");
        Require(terminalFirstJudgedAfterNoneHead.Judged && terminalFirstJudgedAfterNoneHead.Kind == RuntimeNoteKind.Sustain,
            "firstJudgedConnection must not promote a structural Slide End to Tap");

        Require(Enum.TryParse("Tail", out HoldCheckpointSource tailSource),
            "HoldCheckpointSource must define Tail for judged Slide terminals");
        Require(normalHead.Judged && normalHead.Kind == RuntimeNoteKind.Tap,
            "Only a normal structural Slide Start must use discrete Tap judgment");
        Require(traceHead.Judged && traceHead.Kind == RuntimeNoteKind.Sustain &&
                traceMid.Judged && traceMid.Kind == RuntimeNoteKind.Sustain &&
                traceTail.Judged && traceTail.Kind == RuntimeNoteKind.Sustain,
            "Every non-directional Trace Slide node must use sustained-contact judgment");
        Require(normalMid.Judged && normalMid.Kind == RuntimeNoteKind.Sustain,
            "A judged normal structural Slide Tick must use sustained-contact judgment");
        Require(normalTail.Judged && normalTail.Kind == RuntimeNoteKind.Sustain && normalTail.IsHoldTerminal && normalTail.HoldCheckpointSource == tailSource &&
                traceTail.Judged && traceTail.Kind == RuntimeNoteKind.Sustain && traceTail.IsHoldTerminal && traceTail.HoldCheckpointSource == tailSource,
            "Normal and trace Slide terminals must be judged Sustain Tail checkpoints");
        Require(flickTail.Judged && flickTail.Kind == RuntimeNoteKind.Flick && flickTail.IsHoldTerminal && flickTail.HoldCheckpointSource == tailSource,
            "Directional Slide terminals must remain judged Flick Tail checkpoints");
        Require(noneNodes.All(note => !note.Judged) && !noneDirectionTail.Judged && !noneHeadTail.Judged && chart.PlayableCount == 13,
            "Slide judgeType:none nodes must stay out of judgment and PlayableCount");
        Require(!noneHead.Judged && firstJudgedAfterNoneHead.Judged && firstJudgedAfterNoneHead.Kind == RuntimeNoteKind.Sustain,
            "A judged structural Slide Tick after a none head must remain contact-judged");
        Require(terminalFirstJudgedAfterNoneHead.Judged && terminalFirstJudgedAfterNoneHead.Kind == RuntimeNoteKind.Sustain &&
                terminalFirstJudgedAfterNoneHead.IsHoldTerminal && terminalFirstJudgedAfterNoneHead.HoldCheckpointSource == tailSource,
            "A normal structural Slide End after a none head must remain a contact-judged Tail");

        Require(normalHead.SlideNodeRole == SlideNodeRole.Start && normalHead.SlideJudgeMode == SlideJudgeMode.Normal &&
                traceHead.SlideNodeRole == SlideNodeRole.Start && traceHead.SlideJudgeMode == SlideJudgeMode.Trace &&
                traceMid.SlideNodeRole == SlideNodeRole.Tick && traceMid.SlideJudgeMode == SlideJudgeMode.Trace &&
                traceTail.SlideNodeRole == SlideNodeRole.End && traceTail.SlideJudgeMode == SlideJudgeMode.Trace &&
                flickTail.SlideNodeRole == SlideNodeRole.End && flickTail.SlideJudgeMode == SlideJudgeMode.Flick &&
                noneDirectionTail.SlideNodeRole == SlideNodeRole.End && noneDirectionTail.SlideJudgeMode == SlideJudgeMode.None,
            "USC Slide metadata must preserve authored Normal, Trace, Flick, and None judgment semantics");

        var headEngine = new JudgmentEngine(new[] { normalHead }, new ScoreState());
        headEngine.Process(normalHead.Time, Array.Empty<InputToken>(), new[] { new ActiveContact(1, normalHead.Lane, normalHead.Time - .1) });
        Require(normalHead.Grade == JudgmentGrade.Pending,
            "A static pre-held contact must not hit a Slide Tap head");
        headEngine.Process(normalHead.Time, new[] { new InputToken(1, RuntimeNoteKind.Tap, normalHead.Time, normalHead.Lane) }, Array.Empty<ActiveContact>());
        Require(normalHead.Grade == JudgmentGrade.Perfect,
            "A Slide Tap head must resolve from a discrete Tap token");

        var structuralEndEngine = new JudgmentEngine(new[] { terminalFirstJudgedAfterNoneHead }, new ScoreState());
        structuralEndEngine.Process(terminalFirstJudgedAfterNoneHead.Time, Array.Empty<InputToken>(),
            new[] { new ActiveContact(1, terminalFirstJudgedAfterNoneHead.Lane, terminalFirstJudgedAfterNoneHead.Time - .1) });
        Require(terminalFirstJudgedAfterNoneHead.Grade == JudgmentGrade.Perfect,
            "A normal structural Slide End must resolve from sustained contact even when it is the first judged node");

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
        Require(particleOnly.SlideNodeRole == SlideNodeRole.Attach && particleOnly.SlideJudgeMode == SlideJudgeMode.None,
            "An authored attach must retain Attach role and None judgment metadata");
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
        Require(autoCheckpoints.Length == 2 && autoCheckpoints.Select(note => note.Beat).SequenceEqual(new[] { .5, 1.5 }),
            "Hold Auto checkpoints must skip an eighth-note beat occupied by an authored judged mid");
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
        engine.Process(1.5, Array.Empty<InputToken>(), new[] { new ActiveContact(1, .5f, .8) });
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

    public static void ValidateTimingAndHotPathReuse()
    {
        const double dspTime = 200.75;
        const double scheduledDsp = 198;
        const double accumulatedPause = .25;
        const double chartBgmOffset = .5;
        var chartTime = GameplayTiming.ChartTimeAtDsp(dspTime, scheduledDsp, accumulatedPause, chartBgmOffset);
        var inputChartTime = GameplayTiming.ChartTimeAtDsp(200.625, scheduledDsp, accumulatedPause, chartBgmOffset);
        Require(Math.Abs(chartTime - 2) < .0001 && Math.Abs(inputChartTime - 1.875) < .0001,
            "Frame and input-event DSP clocks must share the same chart-time mapping");

        const double firstDeviceOffset = .08;
        var firstChartSchedule = GameplayTiming.ScheduledDspForChartTime(400, 10, .3);
        var secondChartSchedule = GameplayTiming.ScheduledDspForChartTime(400, 10, -.2);
        var firstPlayback = GameplayTiming.PlaybackDspForSchedule(firstChartSchedule, firstDeviceOffset);
        var secondPlayback = GameplayTiming.PlaybackDspForSchedule(secondChartSchedule, firstDeviceOffset);
        Require(Math.Abs(firstPlayback - firstChartSchedule - firstDeviceOffset) < .0001 &&
                Math.Abs(secondPlayback - secondChartSchedule - firstDeviceOffset) < .0001,
            "A device offset must change playback phase exactly once and survive chart changes");

        const string legacyOffsetKey = "gugarythm-audio-offset-seconds";
        const string settingsOffsetKey = "gugarythm-settings-delay-offset-seconds";
        var hadLegacyOffset = PlayerPrefs.HasKey(legacyOffsetKey);
        var hadSettingsOffset = PlayerPrefs.HasKey(settingsOffsetKey);
        var originalLegacyOffset = PlayerPrefs.GetFloat(legacyOffsetKey);
        var originalSettingsOffset = PlayerPrefs.GetFloat(settingsOffsetKey);
        double replacedOffset;
        try
        {
            PlayerPrefs.SetFloat(legacyOffsetKey, .11f);
            PlayerPrefs.SetFloat(settingsOffsetKey, -.04f);
            PlayerPrefs.Save();
            var migratedOffset = GameplayTimingPreferences.LoadDeviceOffset();
            Require(Math.Abs(migratedOffset + .04) < .0001 &&
                    Math.Abs(PlayerPrefs.GetFloat(legacyOffsetKey) + .04) < .0001,
                "The settings delay key must override and migrate the legacy audio-offset key");

            _ = GameplayTimingPreferences.PersistDeviceOffset(.08);
            replacedOffset = GameplayTimingPreferences.PersistDeviceOffset(.025);
            var reloadedOffset = GameplayTimingPreferences.LoadDeviceOffset();
            var persistedChartSchedule = GameplayTiming.ScheduledDspForChartTime(500, 12, .3);
            var persistedChartPlayback = GameplayTiming.PlaybackDspForSchedule(persistedChartSchedule, reloadedOffset);
            var afterChartChange = GameplayTimingPreferences.LoadDeviceOffset();
            var replaySchedule = GameplayTiming.ScheduledDspForChartTime(600, 0, -.2);
            var replayPlayback = GameplayTiming.PlaybackDspForSchedule(replaySchedule, afterChartChange);
            var afterReplay = GameplayTimingPreferences.LoadDeviceOffset();
            Require(Math.Abs(replacedOffset - .025) < .0001 && Math.Abs(reloadedOffset - .025) < .0001 &&
                    Math.Abs(afterChartChange - .025) < .0001 && Math.Abs(afterReplay - .025) < .0001 &&
                    Math.Abs(persistedChartPlayback - persistedChartSchedule - .025) < .0001 &&
                    Math.Abs(replayPlayback - replaySchedule - .025) < .0001 &&
                    Math.Abs(PlayerPrefs.GetFloat(legacyOffsetKey) - .025) < .0001 &&
                    Math.Abs(PlayerPrefs.GetFloat(settingsOffsetKey) - .025) < .0001,
                "Persisted calibration must replace rather than accumulate and survive chart changes and replay");
        }
        finally
        {
            if (hadLegacyOffset) PlayerPrefs.SetFloat(legacyOffsetKey, originalLegacyOffset);
            else PlayerPrefs.DeleteKey(legacyOffsetKey);
            if (hadSettingsOffset) PlayerPrefs.SetFloat(settingsOffsetKey, originalSettingsOffset);
            else PlayerPrefs.DeleteKey(settingsOffsetKey);
            PlayerPrefs.Save();
        }
        var chartTimeAfterCalibration = GameplayTiming.ChartTimeAtDsp(dspTime, scheduledDsp, accumulatedPause, chartBgmOffset);
        Require(Math.Abs(replacedOffset - .025) < .0001 && Math.Abs(chartTimeAfterCalibration - chartTime) < .0001,
            "Repeated calibration must replace the device offset without changing or accumulating into chart time");
        Require(Math.Abs(GameplayTiming.ClipTimeForChartTime(12.5, .3, .1, 60) - 12.7) < .0001 &&
                Math.Abs(GameplayTiming.PlaybackDspForChartTime(400, -.4, .3, .1) - 400.2) < .0001 &&
                Math.Abs(GameplayTiming.ScheduledDspForRecovery(400, 100, 0) - 300) < .0001,
            "Playback recovery must preserve the established device-offset sign and chart anchor");

        var tap = Note(540, 1, 0);
        var engine = new JudgmentEngine(new[] { tap }, new ScoreState());
        var reusableOutput = new List<JudgmentEvent>(2);
        var outputIdentity = reusableOutput;
        engine.ProcessInto(1, new[] { new InputToken(1, RuntimeNoteKind.Tap, 1, 0) },
            Array.Empty<ActiveContact>(), Array.Empty<ContactPathSegment>(), false, reusableOutput);
        Require(ReferenceEquals(outputIdentity, reusableOutput) && reusableOutput.Count == 1 &&
                reusableOutput[0].Grade == JudgmentGrade.Perfect,
            "JudgmentEngine.ProcessInto must fill the caller-owned output list");
        engine.ProcessInto(1.01, Array.Empty<InputToken>(), Array.Empty<ActiveContact>(),
            Array.Empty<ContactPathSegment>(), false, reusableOutput);
        Require(ReferenceEquals(outputIdentity, reusableOutput) && reusableOutput.Count == 0,
            "JudgmentEngine.ProcessInto must clear and reuse its output across frames");

        var cleanupBuffers = new GameplayContactCleanupBuffers();
        var activeIdentity = cleanupBuffers.ActiveContactIds;
        var removalIdentity = cleanupBuffers.RemovalIds;
        activeIdentity.Add(7);
        removalIdentity.Add(9);
        cleanupBuffers.BeginFrame();
        Require(ReferenceEquals(activeIdentity, cleanupBuffers.ActiveContactIds) &&
                ReferenceEquals(removalIdentity, cleanupBuffers.RemovalIds) &&
                activeIdentity.Count == 0 && removalIdentity.Count == 0,
            "Contact cleanup must retain and clear its caller-owned ID/removal buffers across frames");

        var hudState = new GameplayHudState();
        Require(hudState.ShouldUpdateAccuracy(0, 100) && !hudState.ShouldUpdateAccuracy(0, 100) &&
                hudState.ShouldUpdateAccuracy(1.01, 100),
            "HUD accuracy text must rebuild only when its displayed value changes");
        Require(hudState.ShouldUpdateCombo(0, false) && !hudState.ShouldUpdateCombo(0, false) &&
                hudState.ShouldUpdateCombo(1, true),
            "HUD combo text and visibility must rebuild only when displayed state changes");

        var sustain = Note(541, 1, 0);
        sustain.Kind = RuntimeNoteKind.Sustain;
        var steadyEngine = new JudgmentEngine(new[] { sustain }, new ScoreState());
        var offLaneContact = new[] { new ActiveContact(2, 99, 0) };
        var noInputs = Array.Empty<InputToken>();
        var noPaths = Array.Empty<ContactPathSegment>();
        var steadyOutput = new List<JudgmentEvent>(1);
        for (var index = 0; index < 16; index++)
        {
            activeIdentity.Add(7);
            removalIdentity.Add(9);
            cleanupBuffers.BeginFrame();
            _ = hudState.ShouldUpdateAccuracy(1.01, 100);
            _ = hudState.ShouldUpdateCombo(1, true);
            steadyEngine.ProcessInto(1, noInputs, offLaneContact, noPaths, false, steadyOutput);
        }
        _ = GC.GetAllocatedBytesForCurrentThread();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 128; index++)
        {
            activeIdentity.Add(7);
            removalIdentity.Add(9);
            cleanupBuffers.BeginFrame();
            _ = hudState.ShouldUpdateAccuracy(1.01, 100);
            _ = hudState.ShouldUpdateCombo(1, true);
            steadyEngine.ProcessInto(1, noInputs, offLaneContact, noPaths, false, steadyOutput);
        }
        var managedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Require(managedBytes == 0,
            $"Continuous Hold frames must allocate zero managed bytes after warm-up, got {managedBytes}");

        Debug.Log($"GUGARYTHM_TASK4_TIMING_REUSE_OK chartTime={chartTime:0.###} inputTime={inputChartTime:0.###} " +
                  $"deviceOffset={replacedOffset:0.###} managedBytes={managedBytes} outputCapacity={steadyOutput.Capacity}");
    }

    public static void ValidateGameplayUpdateThreadAllocation()
    {
        const string legacyOffsetKey = "gugarythm-audio-offset-seconds";
        const string settingsOffsetKey = "gugarythm-settings-delay-offset-seconds";
        const string scrollSpeedKey = "gugarythm-scroll-speed";
        const string musicVolumeKey = "gugarythm-music-volume";
        const string keyVolumeKey = "gugarythm-key-volume";
        const System.Reflection.BindingFlags instanceFlags = System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic;
        var controllerType = typeof(SonolusLandscapePrototype);
        var awakeMethod = controllerType.GetMethod("Awake", instanceFlags);
        var resetMethod = controllerType.GetMethod("ResetRuntime", instanceFlags);
        var updateMethod = controllerType.GetMethod("Update", instanceFlags);
        var chartField = controllerType.GetField("chart", instanceFlags);
        var runningField = controllerType.GetField("running", instanceFlags);
        var pausedField = controllerType.GetField("paused", instanceFlags);
        var scheduledDspField = controllerType.GetField("scheduledDsp", instanceFlags);
        Require(awakeMethod != null && resetMethod != null && updateMethod != null && chartField != null &&
                runningField != null && pausedField != null && scheduledDspField != null,
            "The allocation fixture must bind the real SonolusLandscapePrototype lifecycle and Update path");

        var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var originalRoots = new HashSet<int>(activeScene.GetRootGameObjects().Select(root => root.GetInstanceID()));
        var originalTargetFrameRate = Application.targetFrameRate;
        var originalVsync = QualitySettings.vSyncCount;
        var originalOrientation = Screen.orientation;
        var profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
        var enhancedTouchWasEnabled = UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled;
        var touchSimulationExisted = UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null;
        var touchSimulationWasEnabled =
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null &&
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled;
        var originalMouse = UnityEngine.InputSystem.Mouse.current;
        var originalMouseWasEnabled = originalMouse != null && originalMouse.enabled;
        var hadLegacyOffset = PlayerPrefs.HasKey(legacyOffsetKey);
        var hadSettingsOffset = PlayerPrefs.HasKey(settingsOffsetKey);
        var hadScrollSpeed = PlayerPrefs.HasKey(scrollSpeedKey);
        var hadMusicVolume = PlayerPrefs.HasKey(musicVolumeKey);
        var hadKeyVolume = PlayerPrefs.HasKey(keyVolumeKey);
        var originalLegacyOffset = PlayerPrefs.GetFloat(legacyOffsetKey);
        var originalSettingsOffset = PlayerPrefs.GetFloat(settingsOffsetKey);
        var originalScrollSpeed = PlayerPrefs.GetFloat(scrollSpeedKey);
        var originalMusicVolume = PlayerPrefs.GetFloat(musicVolumeKey);
        var originalKeyVolume = PlayerPrefs.GetFloat(keyVolumeKey);
        try
        {
            UnityEngine.Profiling.Profiler.enabled = true;
            var controllerObject = new GameObject("Task 4 actual Update allocation fixture");
            var controller = controllerObject.AddComponent<SonolusLandscapePrototype>();
            awakeMethod.Invoke(controller, null);

            var chart = new RuntimeChart();
            var start = new RuntimeNote
            {
                Index = 8800,
                SourceId = "task4-profiler:start",
                Beat = -1,
                Time = -1,
                Lane = -1,
                Size = 1,
                Kind = RuntimeNoteKind.Sustain,
                Visible = false,
                Judged = false,
                HoldRootIndex = 8800,
            };
            var end = new RuntimeNote
            {
                Index = 8801,
                SourceId = "task4-profiler:end",
                Beat = 10,
                Time = 10,
                Lane = 1,
                Size = 1,
                Kind = RuntimeNoteKind.Sustain,
                Visible = false,
                Judged = false,
                HoldRootIndex = 8800,
            };
            chart.Connectors.Add(new RuntimeConnector { Start = start, End = end });
            var pathBuild = HoldPathBuilder.Build(chart);
            Require(pathBuild.Paths.Count == 1 && pathBuild.FallbackConnectors.Count == 0,
                "The actual Update fixture must exercise one continuous indexed Hold run");
            chart.HoldPaths.AddRange(pathBuild.Paths);
            chartField.SetValue(controller, chart);
            resetMethod.Invoke(controller, null);
            runningField.SetValue(controller, true);
            pausedField.SetValue(controller, false);
            scheduledDspField.SetValue(controller, AudioSettings.dspTime);
            var updateAction = (Action)Delegate.CreateDelegate(typeof(Action), controller, updateMethod, true);
            Require(updateAction != null, "The allocation fixture must invoke the actual Update without reflection allocations");

            for (var index = 0; index < 32; index++) updateAction();
            _ = GC.GetAllocatedBytesForCurrentThread();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 128; index++) updateAction();
            var managedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            runningField.SetValue(controller, false);
            updateAction();
            Require(managedBytes == 0,
                $"The real warmed-up gameplay Update must allocate zero managed bytes, got {managedBytes}");
            Debug.Log($"GUGARYTHM_TASK4_UPDATE_THREAD_ALLOC_OK warmup=32 frames=128 " +
                      $"threadBytes={managedBytes} idleUpdate=1");
        }
        finally
        {
            foreach (var root in activeScene.GetRootGameObjects())
                if (!originalRoots.Contains(root.GetInstanceID())) UnityEngine.Object.DestroyImmediate(root);
            Application.targetFrameRate = originalTargetFrameRate;
            QualitySettings.vSyncCount = originalVsync;
            Screen.orientation = originalOrientation;
            UnityEngine.Profiling.Profiler.enabled = profilerWasEnabled;
            if (hadLegacyOffset) PlayerPrefs.SetFloat(legacyOffsetKey, originalLegacyOffset);
            else PlayerPrefs.DeleteKey(legacyOffsetKey);
            if (hadSettingsOffset) PlayerPrefs.SetFloat(settingsOffsetKey, originalSettingsOffset);
            else PlayerPrefs.DeleteKey(settingsOffsetKey);
            if (hadScrollSpeed) PlayerPrefs.SetFloat(scrollSpeedKey, originalScrollSpeed);
            else PlayerPrefs.DeleteKey(scrollSpeedKey);
            if (hadMusicVolume) PlayerPrefs.SetFloat(musicVolumeKey, originalMusicVolume);
            else PlayerPrefs.DeleteKey(musicVolumeKey);
            if (hadKeyVolume) PlayerPrefs.SetFloat(keyVolumeKey, originalKeyVolume);
            else PlayerPrefs.DeleteKey(keyVolumeKey);
            PlayerPrefs.Save();

            if (enhancedTouchWasEnabled)
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            else
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
            if (touchSimulationExisted)
            {
                if (touchSimulationWasEnabled)
                    UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
                else
                    UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Disable();
            }
            else if (UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null)
            {
                UnityEngine.Object.DestroyImmediate(
                    UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.gameObject);
            }
            if (originalMouse != null && originalMouse.added)
            {
                if (originalMouseWasEnabled) UnityEngine.InputSystem.InputSystem.EnableDevice(originalMouse);
                else UnityEngine.InputSystem.InputSystem.DisableDevice(originalMouse);
            }
        }
    }

    public static void ValidateGameplayUpdateStateRestoration()
    {
        const string scrollSpeedKey = "gugarythm-scroll-speed";
        const string musicVolumeKey = "gugarythm-music-volume";
        const string keyVolumeKey = "gugarythm-key-volume";
        var hadScrollSpeed = PlayerPrefs.HasKey(scrollSpeedKey);
        var hadMusicVolume = PlayerPrefs.HasKey(musicVolumeKey);
        var hadKeyVolume = PlayerPrefs.HasKey(keyVolumeKey);
        var originalScrollSpeed = PlayerPrefs.GetFloat(scrollSpeedKey);
        var originalMusicVolume = PlayerPrefs.GetFloat(musicVolumeKey);
        var originalKeyVolume = PlayerPrefs.GetFloat(keyVolumeKey);
        var touchSimulationExisted = UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null;
        var touchSimulationWasEnabled =
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null &&
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled;
        var originalMouse = UnityEngine.InputSystem.Mouse.current;
        var originalMouseWasEnabled = originalMouse != null && originalMouse.enabled;
        var enhancedTouchWasEnabled =
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled;
        try
        {
            PlayerPrefs.DeleteKey(scrollSpeedKey);
            PlayerPrefs.SetFloat(musicVolumeKey, .37f);
            PlayerPrefs.DeleteKey(keyVolumeKey);
            PlayerPrefs.Save();
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
            var fixtureMouse = UnityEngine.InputSystem.Mouse.current;
            var fixtureMouseWasEnabled = fixtureMouse != null && fixtureMouse.enabled;

            ValidateGameplayUpdateThreadAllocation();

            Require(!PlayerPrefs.HasKey(scrollSpeedKey) &&
                    PlayerPrefs.HasKey(musicVolumeKey) &&
                    Math.Abs(PlayerPrefs.GetFloat(musicVolumeKey) - .37f) < .0001f &&
                    !PlayerPrefs.HasKey(keyVolumeKey),
                "The real-Awake profiler fixture must restore existence and values for all UI-created settings keys");
            Require(UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null &&
                    UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled &&
                    (fixtureMouse == null || fixtureMouse.enabled == fixtureMouseWasEnabled),
                "The real-Awake profiler fixture must restore TouchSimulation and Mouse enabled states");
            Debug.Log("GUGARYTHM_TASK4_UPDATE_STATE_RESTORE_OK prefs=3 touchSimulation=True mouseRestored=True");
        }
        finally
        {
            if (hadScrollSpeed) PlayerPrefs.SetFloat(scrollSpeedKey, originalScrollSpeed);
            else PlayerPrefs.DeleteKey(scrollSpeedKey);
            if (hadMusicVolume) PlayerPrefs.SetFloat(musicVolumeKey, originalMusicVolume);
            else PlayerPrefs.DeleteKey(musicVolumeKey);
            if (hadKeyVolume) PlayerPrefs.SetFloat(keyVolumeKey, originalKeyVolume);
            else PlayerPrefs.DeleteKey(keyVolumeKey);
            PlayerPrefs.Save();

            if (touchSimulationExisted)
            {
                if (touchSimulationWasEnabled)
                    UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
                else
                    UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Disable();
            }
            else
            {
                var currentSimulation = UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance;
                if (currentSimulation != null)
                    UnityEngine.Object.DestroyImmediate(currentSimulation.gameObject);
            }
            if (originalMouse != null && originalMouse.added)
            {
                if (originalMouseWasEnabled) UnityEngine.InputSystem.InputSystem.EnableDevice(originalMouse);
                else UnityEngine.InputSystem.InputSystem.DisableDevice(originalMouse);
            }
            if (enhancedTouchWasEnabled)
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
            else
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
        }
    }

    public static void ValidateGameplayUpdateProfilerAcrossEditorFrames()
    {
        GameplayUpdateProfilerSession.Start();
    }

    static class GameplayUpdateProfilerSession
    {
        const int WarmupFrameCount = 32;
        const int MeasuredFrameCount = 128;
        const int MaximumCallbackCount = 512;
        const double WatchdogSeconds = 30;
        static Session current;

        public static void Start()
        {
            if (current != null)
                throw new InvalidOperationException("A gameplay Update profiler session is already active.");
            current = new Session();
            try
            {
                current.Initialize();
                EditorApplication.update -= Advance;
                EditorApplication.update += Advance;
            }
            catch (Exception exception)
            {
                Exit(1, exception);
            }
        }

        static void Advance()
        {
            var session = current;
            if (session == null) return;
            try
            {
                session.Advance();
            }
            catch (Exception exception)
            {
                Exit(1, exception);
            }
        }

        static void Exit(int exitCode, Exception exception = null)
        {
            EditorApplication.update -= Advance;
            var session = current;
            current = null;
            Exception cleanupException = null;
            try
            {
                session?.Cleanup();
                session?.RequireRestoredGlobals();
            }
            catch (Exception caught)
            {
                cleanupException = caught;
                exitCode = 1;
            }

            if (exception != null)
                Debug.LogError($"GUGARYTHM_VALIDATION_FAILED: {exception.Message}\n{exception}");
            if (cleanupException != null)
                Debug.LogError($"GUGARYTHM_VALIDATION_FAILED: Profiler cleanup failed: " +
                               $"{cleanupException.Message}\n{cleanupException}");
            if (exitCode == 0)
                Debug.Log("GUGARYTHM_TASK4_EDITOR_FRAME_STATE_RESTORE_OK prefs=5 " +
                          "touchSimulation=True mouse=True enhancedTouch=True globals=True");
            EditorApplication.Exit(exitCode);
        }

        readonly struct PreferenceSnapshot
        {
            public readonly string Key;
            readonly bool existed;
            readonly float value;

            public PreferenceSnapshot(string key)
            {
                Key = key;
                existed = PlayerPrefs.HasKey(key);
                value = PlayerPrefs.GetFloat(key);
            }

            public void Restore()
            {
                if (existed) PlayerPrefs.SetFloat(Key, value);
                else PlayerPrefs.DeleteKey(Key);
            }

            public bool IsRestored()
            {
                if (PlayerPrefs.HasKey(Key) != existed) return false;
                return !existed || Math.Abs(PlayerPrefs.GetFloat(Key) - value) < .000001f;
            }
        }

        sealed class Session
        {
            enum Phase
            {
                Warmup,
                PrimeRecorders,
                Measure,
                Idle,
                Flush,
                Finished,
            }

            readonly UnityEngine.SceneManagement.Scene activeScene =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            readonly HashSet<int> originalRoots;
            readonly int originalTargetFrameRate = Application.targetFrameRate;
            readonly int originalVsync = QualitySettings.vSyncCount;
            readonly ScreenOrientation originalOrientation = Screen.orientation;
            readonly bool profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
            readonly bool enhancedTouchWasEnabled =
                UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled;
            readonly bool touchSimulationExisted =
                UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null;
            readonly bool touchSimulationWasEnabled =
                UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null &&
                UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled;
            readonly UnityEngine.InputSystem.Mouse originalMouse = UnityEngine.InputSystem.Mouse.current;
            readonly bool originalMouseWasEnabled;
            readonly PreferenceSnapshot[] preferences =
            {
                new("gugarythm-audio-offset-seconds"),
                new("gugarythm-settings-delay-offset-seconds"),
                new("gugarythm-scroll-speed"),
                new("gugarythm-music-volume"),
                new("gugarythm-key-volume"),
            };

            Action updateAction;
            System.Reflection.FieldInfo runningField;
            SonolusLandscapePrototype controller;
            ProfilerRecorder frameRecorder;
            ProfilerRecorder gcAllocRecorder;
            ProfilerRecorder idleFrameRecorder;
            Phase phase;
            double startedAt;
            int callbackCount;
            int warmupFrames;
            int measuredFrames;
            int flushFrames;
            long scopedThreadBytes;
            bool recordersStarted;
            bool idleRecorderStarted;
            bool cleanedUp;

            public Session()
            {
                originalRoots = new HashSet<int>(
                    activeScene.GetRootGameObjects().Select(root => root.GetInstanceID()));
                originalMouseWasEnabled = originalMouse != null && originalMouse.enabled;
            }

            public void Initialize()
            {
                const System.Reflection.BindingFlags instanceFlags =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var controllerType = typeof(SonolusLandscapePrototype);
                var awakeMethod = controllerType.GetMethod("Awake", instanceFlags);
                var resetMethod = controllerType.GetMethod("ResetRuntime", instanceFlags);
                var updateMethod = controllerType.GetMethod("Update", instanceFlags);
                var chartField = controllerType.GetField("chart", instanceFlags);
                runningField = controllerType.GetField("running", instanceFlags);
                var pausedField = controllerType.GetField("paused", instanceFlags);
                var scheduledDspField = controllerType.GetField("scheduledDsp", instanceFlags);
                Require(awakeMethod != null && resetMethod != null && updateMethod != null && chartField != null &&
                        runningField != null && pausedField != null && scheduledDspField != null,
                    "The multi-frame profiler must bind the real controller lifecycle and Update path");

                UnityEngine.Profiling.Profiler.enabled = true;
                var controllerObject = new GameObject("Task 4 multi-frame Update profiler fixture");
                controller = controllerObject.AddComponent<SonolusLandscapePrototype>();
                awakeMethod.Invoke(controller, null);

                var chart = new RuntimeChart();
                var start = new RuntimeNote
                {
                    Index = 8810,
                    SourceId = "task4-multiframe-profiler:start",
                    Beat = -1,
                    Time = -1,
                    Lane = -1,
                    Size = 1,
                    Kind = RuntimeNoteKind.Sustain,
                    Visible = false,
                    Judged = false,
                    HoldRootIndex = 8810,
                };
                var end = new RuntimeNote
                {
                    Index = 8811,
                    SourceId = "task4-multiframe-profiler:end",
                    Beat = 10,
                    Time = 10,
                    Lane = 1,
                    Size = 1,
                    Kind = RuntimeNoteKind.Sustain,
                    Visible = false,
                    Judged = false,
                    HoldRootIndex = 8810,
                };
                chart.Connectors.Add(new RuntimeConnector { Start = start, End = end });
                var pathBuild = HoldPathBuilder.Build(chart);
                Require(pathBuild.Paths.Count == 1 && pathBuild.FallbackConnectors.Count == 0,
                    "The multi-frame profiler must exercise one continuous indexed Hold run");
                chart.HoldPaths.AddRange(pathBuild.Paths);
                chartField.SetValue(controller, chart);
                resetMethod.Invoke(controller, null);
                runningField.SetValue(controller, true);
                pausedField.SetValue(controller, false);
                scheduledDspField.SetValue(controller, AudioSettings.dspTime);
                updateAction = (Action)Delegate.CreateDelegate(typeof(Action), controller, updateMethod, true);
                Require(updateAction != null,
                    "The multi-frame profiler must invoke the actual Update without reflection allocations");

                startedAt = EditorApplication.timeSinceStartup;
                phase = Phase.Warmup;
                EditorApplication.QueuePlayerLoopUpdate();
            }

            public void Advance()
            {
                callbackCount++;
                if (callbackCount > MaximumCallbackCount ||
                    EditorApplication.timeSinceStartup - startedAt > WatchdogSeconds)
                    throw new TimeoutException(
                        $"Gameplay Update profiler watchdog expired at callback={callbackCount}, phase={phase}, " +
                        $"warmup={warmupFrames}, measured={measuredFrames}");

                switch (phase)
                {
                    case Phase.Warmup:
                        updateAction();
                        warmupFrames++;
                        if (warmupFrames == WarmupFrameCount)
                        {
                            var options = ProfilerRecorderOptions.StartImmediately |
                                          ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                                          ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                            frameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts,
                                "Gugarythm.GameplayFrame", MeasuredFrameCount + 16, options);
                            var allocationOptions = ProfilerRecorderOptions.StartImmediately |
                                                    ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                                                    ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                            gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal,
                                "GC.Alloc", 16, allocationOptions);
                            recordersStarted = true;
                            Require(frameRecorder.Valid,
                                "The multi-frame profiler must bind Gugarythm.GameplayFrame");
                            Require(gcAllocRecorder.Valid,
                                "The multi-frame profiler must bind Unity's current-thread GC.Alloc marker");
                            phase = Phase.PrimeRecorders;
                        }
                        break;
                    case Phase.PrimeRecorders:
                        gcAllocRecorder.Reset();
                        _ = GC.GetAllocatedBytesForCurrentThread();
                        phase = Phase.Measure;
                        break;
                    case Phase.Measure:
                        MeasureOneUpdate();
                        measuredFrames++;
                        if (measuredFrames == MeasuredFrameCount)
                        {
                            runningField.SetValue(controller, false);
                            var idleOptions = ProfilerRecorderOptions.StartImmediately |
                                              ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
                                              ProfilerRecorderOptions.CollectOnlyOnCurrentThread;
                            idleFrameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Scripts,
                                "Gugarythm.GameplayFrame", 8, idleOptions);
                            idleRecorderStarted = true;
                            Require(idleFrameRecorder.Valid,
                                "The multi-frame profiler must bind the early-return gameplay marker");
                            phase = Phase.Idle;
                        }
                        break;
                    case Phase.Idle:
                        updateAction();
                        phase = Phase.Flush;
                        break;
                    case Phase.Flush:
                        flushFrames++;
                        if (flushFrames < 2) break;
                        ValidateResultsAndExit();
                        return;
                    case Phase.Finished:
                        return;
                }
                EditorApplication.QueuePlayerLoopUpdate();
            }

            void MeasureOneUpdate()
            {
                var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                updateAction();
                scopedThreadBytes += GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            }

            void ValidateResultsAndExit()
            {
                phase = Phase.Finished;
                frameRecorder.Stop();
                gcAllocRecorder.Stop();
                idleFrameRecorder.Stop();
                var markerSamples = frameRecorder.Count;
                var gcAllocSamples = gcAllocRecorder.Count;
                var idleMarkerSamples = idleFrameRecorder.Count;
                var markerLastNanoseconds = markerSamples > 0 ? frameRecorder.LastValue : -1;
                Require(measuredFrames == MeasuredFrameCount && scopedThreadBytes == 0 &&
                        gcAllocSamples == 0,
                    $"The warmed real Update must report zero scoped managed allocation across " +
                    $"{MeasuredFrameCount} Editor frames, got bytes={scopedThreadBytes}, " +
                    $"GC.Alloc samples={gcAllocSamples}");
                Require(markerSamples >= MeasuredFrameCount,
                    $"ProfilerRecorder must inspect at least {MeasuredFrameCount} flushed gameplay marker samples, " +
                    $"got {markerSamples}");
                Require(idleMarkerSamples > 0,
                    "ProfilerRecorder must inspect a flushed gameplay marker sample on the early-return frame");

                Debug.Log($"GUGARYTHM_TASK4_EDITOR_FRAME_PROFILER_OK warmup={warmupFrames} " +
                          $"measuredFrames={measuredFrames} markerSamples={markerSamples} " +
                          $"markerLastNanoseconds={markerLastNanoseconds} idleMarkerSamples={idleMarkerSamples} " +
                          $"gcAllocSamples={gcAllocSamples} scopedThreadBytes={scopedThreadBytes} " +
                          $"callbacks={callbackCount}");
                Exit(0);
            }

            public void Cleanup()
            {
                if (cleanedUp) return;
                cleanedUp = true;
                if (idleRecorderStarted) idleFrameRecorder.Dispose();
                if (recordersStarted)
                {
                    gcAllocRecorder.Dispose();
                    frameRecorder.Dispose();
                }

                if (activeScene.IsValid() && activeScene.isLoaded)
                    foreach (var root in activeScene.GetRootGameObjects())
                        if (!originalRoots.Contains(root.GetInstanceID()))
                            UnityEngine.Object.DestroyImmediate(root);

                Application.targetFrameRate = originalTargetFrameRate;
                QualitySettings.vSyncCount = originalVsync;
                Screen.orientation = originalOrientation;
                UnityEngine.Profiling.Profiler.enabled = profilerWasEnabled;
                for (var index = 0; index < preferences.Length; index++) preferences[index].Restore();
                PlayerPrefs.Save();

                if (enhancedTouchWasEnabled)
                    UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
                else
                    UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
                if (touchSimulationExisted)
                {
                    if (touchSimulationWasEnabled)
                        UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
                    else
                        UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Disable();
                }
                else if (UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.gameObject);
                }
                if (originalMouse != null && originalMouse.added)
                {
                    if (originalMouseWasEnabled) UnityEngine.InputSystem.InputSystem.EnableDevice(originalMouse);
                    else UnityEngine.InputSystem.InputSystem.DisableDevice(originalMouse);
                }
            }

            public void RequireRestoredGlobals()
            {
                for (var index = 0; index < preferences.Length; index++)
                    Require(preferences[index].IsRestored(),
                        $"The multi-frame profiler must restore PlayerPrefs key {preferences[index].Key}");
                Require(Application.targetFrameRate == originalTargetFrameRate &&
                        QualitySettings.vSyncCount == originalVsync &&
                        Screen.orientation == originalOrientation &&
                        UnityEngine.Profiling.Profiler.enabled == profilerWasEnabled,
                    "The multi-frame profiler must restore Editor frame, display, and profiler globals");
                Require(UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled ==
                        enhancedTouchWasEnabled,
                    "The multi-frame profiler must restore EnhancedTouchSupport state");
                Require((UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance != null) ==
                        touchSimulationExisted &&
                        (!touchSimulationExisted ||
                         UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.instance.enabled ==
                         touchSimulationWasEnabled),
                    "The multi-frame profiler must restore TouchSimulation existence and enabled state");
                Require(originalMouse == null || !originalMouse.added ||
                        originalMouse.enabled == originalMouseWasEnabled,
                    "The multi-frame profiler must restore the original Mouse enabled state");
            }
        }
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
