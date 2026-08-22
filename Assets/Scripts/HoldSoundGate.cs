using System.Collections.Generic;

namespace Gugarhythm
{
    /// <summary>
    /// Tracks which Hold roots currently require the shared looping Hold voice.
    /// </summary>
    public sealed class HoldSoundGate
    {
        readonly HashSet<int> activeRoots = new();

        public bool ShouldPlay => ActiveCount > 0;
        public int ActiveCount => activeRoots.Count;

        public void Activate(int holdRootIndex)
        {
            activeRoots.Add(holdRootIndex);
        }

        public void Deactivate(int holdRootIndex)
        {
            activeRoots.Remove(holdRootIndex);
        }

        public void Clear()
        {
            activeRoots.Clear();
        }
    }
}
