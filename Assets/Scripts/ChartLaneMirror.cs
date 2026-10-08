using System.Collections.Generic;

namespace Gugarhythm
{
    /// <summary>
    /// Flips chart geometry across lane 0 so left and right hands swap.
    /// Safe to call repeatedly: each call toggles the current layout.
    /// </summary>
    public static class ChartLaneMirror
    {
        public static void Apply(RuntimeChart chart)
        {
            if (chart == null) return;

            var seen = new HashSet<RuntimeNote>();
            foreach (var note in chart.Notes) FlipNote(note, seen);
            foreach (var connector in chart.Connectors)
            {
                FlipNote(connector?.Start, seen);
                FlipNote(connector?.End, seen);
            }
            foreach (var connector in chart.FallbackConnectors)
            {
                FlipNote(connector?.Start, seen);
                FlipNote(connector?.End, seen);
            }
            foreach (var path in chart.HoldPaths)
            {
                if (path == null) continue;
                foreach (var node in path.Nodes) FlipNote(node, seen);
                foreach (var node in path.SemanticNodes) FlipNote(node, seen);
            }

            foreach (var guide in chart.Guides)
            {
                if (guide == null) continue;
                FlipGuidePoint(ref guide.Start);
                FlipGuidePoint(ref guide.Head);
                FlipGuidePoint(ref guide.Tail);
                FlipGuidePoint(ref guide.End);
            }
        }

        static void FlipNote(RuntimeNote note, HashSet<RuntimeNote> seen)
        {
            if (note == null || !seen.Add(note)) return;
            note.Lane = -note.Lane;
            note.Direction = -note.Direction;
        }

        static void FlipGuidePoint(ref RuntimeGuidePoint point) => point.Lane = -point.Lane;
    }
}
