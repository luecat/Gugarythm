using System;
using System.Collections.Generic;

namespace Gugarythm
{
    /// <summary>
    /// Converts continuous touch motion into CHUNITHM-style virtual slider
    /// activations. A stationary contact only activates its initial cell;
    /// rubbing across cell boundaries produces new Tap tokens.
    /// </summary>
    public sealed class VirtualSliderInput
    {
        public const int CellCount = 12;
        public const float MinimumLane = -6f;
        public const float MaximumLane = 6f;
        public const float FlickActivationDistance = .35f;

        readonly Dictionary<int, ContactState> contacts = new();

        public void Reset() => contacts.Clear();

        public void Begin(int fingerId, double time, float lane, ICollection<InputToken> output)
        {
            var state = new ContactState(lane, time);
            contacts[fingerId] = state;
            EmitCell(fingerId, CellAt(lane), time, state, output);
        }

        public void Move(int fingerId, double time, float lane, ICollection<InputToken> output)
        {
            if (!contacts.TryGetValue(fingerId, out var state))
            {
                Begin(fingerId, time, lane, output);
                return;
            }

            var previousLane = state.Lane;
            var previousTime = state.Time;
            var direction = Math.Sign(lane - previousLane);
            var previousCell = CellAt(previousLane);
            var currentCell = CellAt(lane);
            if (direction > 0 && previousLane < MaximumLane && lane >= MinimumLane)
            {
                var first = previousCell >= 0 ? previousCell + 1 : 0;
                var last = currentCell >= 0 ? currentCell : CellCount - 1;
                for (var cell = first; cell <= last && cell < CellCount; cell++)
                {
                    var crossingTime = CrossingTime(previousLane, previousTime, lane, time, cell, direction);
                    EmitCell(fingerId, cell, crossingTime, state, output);
                }
            }
            else if (direction < 0 && previousLane > MinimumLane && lane <= MaximumLane)
            {
                var first = previousCell >= 0 ? previousCell - 1 : CellCount - 1;
                var last = currentCell >= 0 ? currentCell : 0;
                for (var cell = first; cell >= last && cell >= 0; cell--)
                {
                    var crossingTime = CrossingTime(previousLane, previousTime, lane, time, cell, direction);
                    EmitCell(fingerId, cell, crossingTime, state, output);
                }
            }

            EmitFlicks(fingerId, lane, time, state, output);

            state.Lane = lane;
            state.Time = time;
        }

        /// <summary>
        /// Ends contact ownership without producing a discrete activation.
        /// The lift position is intentionally not processed as Move: TouchUp
        /// releases the virtual key and never presses a newly entered cell.
        /// </summary>
        public void End(int fingerId, double time, float lane, ICollection<InputToken> output) => contacts.Remove(fingerId);

        public void Cancel(int fingerId) => contacts.Remove(fingerId);

        public static int CellAt(float lane)
        {
            if (lane < MinimumLane || lane > MaximumLane) return -1;
            return Math.Clamp((int)Math.Floor(lane - MinimumLane), 0, CellCount - 1);
        }

        public static float CellCenter(int cell) => MinimumLane + cell + .5f;

        static double CrossingTime(float previousLane, double previousTime, float lane, double time, int enteredCell, int direction)
        {
            var boundary = direction > 0 ? MinimumLane + enteredCell : MinimumLane + enteredCell + 1;
            var distance = lane - previousLane;
            if (Math.Abs(distance) < .0001f || time <= previousTime) return time;
            var progress = Math.Clamp((boundary - previousLane) / distance, 0, 1);
            return previousTime + (time - previousTime) * progress;
        }

        static void EmitCell(int fingerId, int cell, double time, ContactState state, ICollection<InputToken> output)
        {
            if (cell < 0 || output == null) return;
            output.Add(new InputToken(fingerId, RuntimeNoteKind.Tap, time, CellCenter(cell)));
        }

        static void EmitFlicks(int fingerId, float lane, double time, ContactState state, ICollection<InputToken> output)
        {
            if (output == null) return;
            while (true)
            {
                var distance = lane - state.FlickAnchorLane;
                if (Math.Abs(distance) + .00001f < FlickActivationDistance) return;

                var direction = Math.Sign(distance);
                var thresholdLane = state.FlickAnchorLane + direction * FlickActivationDistance;
                var thresholdTime = time;
                if (Math.Abs(lane - state.Lane) > .0001f && time > state.Time)
                {
                    var progress = Math.Clamp((thresholdLane - state.Lane) / (lane - state.Lane), 0, 1);
                    thresholdTime = state.Time + (time - state.Time) * progress;
                }
                else thresholdTime = time;

                output.Add(new InputToken(fingerId, RuntimeNoteKind.Flick, thresholdTime, thresholdLane,
                    state.FlickAnchorLane, state.FlickAnchorTime));
                state.FlickAnchorLane = thresholdLane;
                state.FlickAnchorTime = thresholdTime;
            }
        }

        sealed class ContactState
        {
            public float Lane;
            public double Time;
            public float FlickAnchorLane;
            public double FlickAnchorTime;

            public ContactState(float lane, double time)
            {
                Lane = lane;
                Time = time;
                FlickAnchorLane = lane;
                FlickAnchorTime = time;
            }
        }
    }
}
