using System;
using System.Collections.Generic;

namespace Gugarythm
{
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
        readonly List<RuntimeHoldPathSegment> segments;
        readonly List<HoldRenderRun> renderRuns;

        public int RootIndex { get; }
        public IReadOnlyList<RuntimeNote> Nodes => nodes;
        public IReadOnlyList<RuntimeHoldPathSegment> Segments => segments;
        public IReadOnlyList<HoldRenderRun> RenderRuns => renderRuns;
        public HoldPathEvaluator Evaluator { get; }

        internal RuntimeHoldPath(int rootIndex, List<RuntimeNote> nodes, List<RuntimeHoldPathSegment> segments,
            List<HoldRenderRun> renderRuns)
        {
            RootIndex = rootIndex;
            this.nodes = nodes;
            this.segments = segments;
            this.renderRuns = renderRuns;
            foreach (var run in renderRuns) run.Path = this;
            Evaluator = new HoldPathEvaluator(this);
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
