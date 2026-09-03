using System;
using System.Collections.Generic;
using System.Linq;

namespace Gugarhythm
{
    public static class HoldPathMath
    {
        public static float EaseProgress(float progress, int ease) => ease switch
        {
            1 => progress * progress,
            2 => 1f - (1f - progress) * (1f - progress),
            3 => progress < .5f ? 2 * progress * progress :
                1 - (float)Math.Pow(-2 * progress + 2, 2) * .5f,
            4 => progress < .5f
                ? (1f - (1f - progress * 2) * (1f - progress * 2)) * .5f
                : .5f + (progress * 2 - 1) * (progress * 2 - 1) * .5f,
            _ => progress,
        };
    }

    public readonly struct HoldPathSample
    {
        public readonly float Lane;
        public readonly float Size;
        public readonly int SegmentIndex;
        public readonly float SegmentProgress;

        public HoldPathSample(float lane, float size, int segmentIndex, float segmentProgress)
        {
            Lane = lane;
            Size = Math.Max(.25f, size);
            SegmentIndex = segmentIndex;
            SegmentProgress = segmentProgress;
        }
    }

    public sealed class RuntimeHoldPathSegment
    {
        public RuntimeNote Start { get; }
        public RuntimeNote End { get; }
        public int Ease { get; }
        public bool Critical { get; }
        public bool HardCorner { get; }

        internal RuntimeHoldPathSegment(RuntimeConnector connector)
        {
            Start = connector.Start;
            End = connector.End;
            Ease = connector.Ease;
            Critical = connector.Critical;
            HardCorner = End.Time <= Start.Time + 1e-9;
        }
    }

    public sealed class HoldRenderRun
    {
        public RuntimeHoldPath Path { get; internal set; }
        public int FirstSegmentIndex { get; }
        public int LastSegmentIndex { get; }
        public bool Critical { get; }

        public RuntimeNote Start => Path.Segments[FirstSegmentIndex].Start;
        public RuntimeNote End => Path.Segments[LastSegmentIndex].End;

        // Remembers the tolerance that satisfied the point budget last frame,
        // so a dense run with a stable point count does not have to re-walk
        // the doubling ladder from the default every frame.
        internal float LastSuccessfulTolerance = AdaptiveHoldTessellator.DefaultScreenErrorPixels;

        internal HoldRenderRun(int firstSegmentIndex, int lastSegmentIndex, bool critical)
        {
            FirstSegmentIndex = firstSegmentIndex;
            LastSegmentIndex = lastSegmentIndex;
            Critical = critical;
        }
    }

    public sealed class RuntimeHoldPath
    {
        readonly List<RuntimeNote> nodes;
        readonly List<RuntimeNote> semanticNodes;
        readonly List<RuntimeHoldPathSegment> segments;
        readonly List<HoldRenderRun> renderRuns;

        public int RootIndex { get; }
        public IReadOnlyList<RuntimeNote> Nodes => nodes;
        public IReadOnlyList<RuntimeNote> SemanticNodes => semanticNodes;
        public IReadOnlyList<RuntimeHoldPathSegment> Segments => segments;
        public IReadOnlyList<HoldRenderRun> RenderRuns => renderRuns;
        public HoldPathEvaluator Evaluator { get; }
        public double VisualStartBeat { get; private set; }
        public double VisualEndBeat { get; private set; }
        public double VisualStartTime { get; private set; }
        public double VisualEndTime { get; private set; }
        public bool HasPlayableRange { get; private set; }
        internal bool PreservesLegacyCheckpointSemantics { get; }
        public double? PlayableStartBeat { get; private set; }
        public double? PlayableEndBeat { get; private set; }
        public double? PlayableStartTime { get; private set; }
        public double? PlayableEndTime { get; private set; }

        internal RuntimeHoldPath(int rootIndex, List<RuntimeNote> nodes, List<RuntimeNote> semanticNodes,
            List<RuntimeHoldPathSegment> segments, List<HoldRenderRun> renderRuns, bool preserveLegacyPlayableRange)
        {
            RootIndex = rootIndex;
            this.nodes = nodes;
            this.semanticNodes = semanticNodes;
            this.segments = segments;
            this.renderRuns = renderRuns;
            foreach (var run in renderRuns) run.Path = this;
            PreservesLegacyCheckpointSemantics = preserveLegacyPlayableRange;
            RefreshTimingBounds();
            Evaluator = new HoldPathEvaluator(this);
        }

        internal void RefreshTimingBounds()
        {
            VisualStartBeat = nodes[0].Beat;
            VisualEndBeat = nodes[^1].Beat;
            VisualStartTime = nodes[0].Time;
            VisualEndTime = nodes[^1].Time;
            // A connector path is a Hold regardless of whether any authored
            // node has its own judgment. judgeType only controls explicit node
            // judgments; runtime checkpoints sustain the complete Hold path.
            HasPlayableRange = true;
            PlayableStartBeat = VisualStartBeat;
            PlayableEndBeat = VisualEndBeat;
            PlayableStartTime = VisualStartTime;
            PlayableEndTime = VisualEndTime;
        }
    }

    public sealed class HoldPathBuildResult
    {
        readonly List<RuntimeHoldPath> paths = new();
        readonly List<RuntimeConnector> fallbackConnectors = new();
        readonly List<string> warnings = new();

        public IReadOnlyList<RuntimeHoldPath> Paths => paths;
        public IReadOnlyList<RuntimeConnector> FallbackConnectors => fallbackConnectors;
        public IReadOnlyList<string> Warnings => warnings;

        internal List<RuntimeHoldPath> MutablePaths => paths;
        internal List<RuntimeConnector> MutableFallbackConnectors => fallbackConnectors;
        internal List<string> MutableWarnings => warnings;
    }
}
