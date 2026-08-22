using System;
using System.Collections.Generic;

namespace Gugarythm
{
    public static class HoldPathMath
    {
        public static float EaseProgress(float progress, int ease) => ease switch
        {
            1 => 1f - (float)Math.Cos(progress * Math.PI * .5),
            2 => (float)Math.Sin(progress * Math.PI * .5),
            3 => progress < .5f ? 2 * progress * progress :
                1 - (float)Math.Pow(-2 * progress + 2, 2) * .5f,
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
            HasPlayableRange = false;
            PlayableStartBeat = null;
            PlayableEndBeat = null;
            PlayableStartTime = null;
            PlayableEndTime = null;

            if (PreservesLegacyCheckpointSemantics)
            {
                HasPlayableRange = true;
                PlayableStartBeat = VisualStartBeat;
                PlayableEndBeat = VisualEndBeat;
                PlayableStartTime = VisualStartTime;
                PlayableEndTime = VisualEndTime;
            }
            else
            {
                var hasJudgedNode = false;
                var startBeat = double.PositiveInfinity;
                var endBeat = double.NegativeInfinity;
                var startTime = double.PositiveInfinity;
                var endTime = double.NegativeInfinity;
                foreach (var node in semanticNodes)
                {
                    if (!node.Judged) continue;
                    hasJudgedNode = true;
                    startBeat = Math.Min(startBeat, node.Beat);
                    endBeat = Math.Max(endBeat, node.Beat);
                    startTime = Math.Min(startTime, node.Time);
                    endTime = Math.Max(endTime, node.Time);
                }
                HasPlayableRange = hasJudgedNode;
                if (hasJudgedNode)
                {
                    PlayableStartBeat = startBeat;
                    PlayableEndBeat = endBeat;
                    PlayableStartTime = startTime;
                    PlayableEndTime = endTime;
                }
            }
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
