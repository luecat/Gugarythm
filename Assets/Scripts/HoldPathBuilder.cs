using System;
using System.Collections.Generic;

namespace Gugarythm
{
    public static class HoldPathBuilder
    {
        public static HoldPathBuildResult Build(RuntimeChart chart)
        {
            var result = new HoldPathBuildResult();
            if (chart == null) return result;

            var usable = new List<RuntimeConnector>();
            foreach (var connector in chart.Connectors)
            {
                if (connector?.Start == null || connector.End == null)
                {
                    if (connector != null) result.MutableFallbackConnectors.Add(connector);
                    result.MutableWarnings.Add("Hold path contains a null endpoint; using legacy connector rendering.");
                }
                else usable.Add(connector);
            }

            var unseen = new HashSet<RuntimeConnector>(usable);
            while (unseen.Count > 0)
            {
                RuntimeConnector seed = null;
                foreach (var connector in unseen) { seed = connector; break; }
                var component = CollectComponent(seed, usable, unseen);
                if (!TryBuildComponent(component, chart, out var path, out var warning))
                {
                    result.MutableFallbackConnectors.AddRange(component);
                    result.MutableWarnings.Add(warning);
                    continue;
                }
                result.MutablePaths.Add(path);
            }
            return result;
        }

        static List<RuntimeConnector> CollectComponent(RuntimeConnector seed, IReadOnlyList<RuntimeConnector> all,
            HashSet<RuntimeConnector> unseen)
        {
            var component = new List<RuntimeConnector>();
            var nodes = new HashSet<RuntimeNote> { seed.Start, seed.End };
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var connector in all)
                {
                    if (!unseen.Contains(connector) || (!nodes.Contains(connector.Start) && !nodes.Contains(connector.End))) continue;
                    unseen.Remove(connector);
                    component.Add(connector);
                    changed |= nodes.Add(connector.Start);
                    changed |= nodes.Add(connector.End);
                }
            }
            return component;
        }

        static bool TryBuildComponent(List<RuntimeConnector> component, RuntimeChart chart,
            out RuntimeHoldPath path, out string warning)
        {
            path = null;
            warning = null;
            var outgoing = new Dictionary<RuntimeNote, RuntimeConnector>();
            var incoming = new Dictionary<RuntimeNote, RuntimeConnector>();
            var nodes = new HashSet<RuntimeNote>();
            foreach (var connector in component)
            {
                var startGroup = string.IsNullOrEmpty(connector.Start.TimeScaleGroup)
                    ? chart.DefaultTimeScaleGroup ?? string.Empty : connector.Start.TimeScaleGroup;
                var endGroup = string.IsNullOrEmpty(connector.End.TimeScaleGroup)
                    ? chart.DefaultTimeScaleGroup ?? string.Empty : connector.End.TimeScaleGroup;
                if (!string.Equals(startGroup, endGroup, StringComparison.Ordinal))
                {
                    warning = "Hold path changes TimeScaleGroup; using legacy connector rendering.";
                    return false;
                }
                if (!chart.CanInvertVisualTime(startGroup))
                {
                    warning = "Hold path uses a non-invertible TimeScaleGroup; using legacy connector rendering.";
                    return false;
                }
                nodes.Add(connector.Start);
                nodes.Add(connector.End);
                if (outgoing.ContainsKey(connector.Start) || incoming.ContainsKey(connector.End))
                {
                    warning = "Hold path branches or merges; using legacy connector rendering.";
                    return false;
                }
                outgoing[connector.Start] = connector;
                incoming[connector.End] = connector;
            }

            RuntimeNote head = null;
            var headCount = 0;
            foreach (var node in nodes)
                if (!incoming.ContainsKey(node)) { head = node; headCount++; }
            if (headCount != 1)
            {
                warning = "Hold path is cyclic or has no unique root; using legacy connector rendering.";
                return false;
            }

            var orderedNodes = new List<RuntimeNote> { head };
            var orderedSegments = new List<RuntimeHoldPathSegment>();
            var visited = new HashSet<RuntimeConnector>();
            var current = head;
            while (outgoing.TryGetValue(current, out var connector))
            {
                if (!visited.Add(connector))
                {
                    warning = "Hold path contains a cycle; using legacy connector rendering.";
                    return false;
                }
                if (connector.End.Time < connector.Start.Time - 1e-9)
                {
                    warning = "Hold path time moves backwards; using legacy connector rendering.";
                    return false;
                }
                orderedSegments.Add(new RuntimeHoldPathSegment(connector));
                orderedNodes.Add(connector.End);
                current = connector.End;
            }
            if (visited.Count != component.Count)
            {
                warning = "Hold path is disconnected or cyclic; using legacy connector rendering.";
                return false;
            }

            var rootIndex = head.HoldRootIndex >= 0 ? head.HoldRootIndex : head.Index;
            foreach (var node in orderedNodes) node.HoldRootIndex = rootIndex;
            var runs = BuildRenderRuns(orderedSegments);
            path = new RuntimeHoldPath(rootIndex, orderedNodes, orderedSegments, runs);
            return true;
        }

        static List<HoldRenderRun> BuildRenderRuns(IReadOnlyList<RuntimeHoldPathSegment> segments)
        {
            var runs = new List<HoldRenderRun>();
            if (segments.Count == 0) return runs;
            var first = 0;
            var critical = segments[0].Critical;
            for (var index = 1; index < segments.Count; index++)
            {
                if (segments[index].Critical == critical) continue;
                runs.Add(new HoldRenderRun(first, index - 1, critical));
                first = index;
                critical = segments[index].Critical;
            }
            runs.Add(new HoldRenderRun(first, segments.Count - 1, critical));
            return runs;
        }
    }
}
