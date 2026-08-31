using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace Gugarhythm
{
    /// <summary>
    /// Converts continuous touch motion into CHUNITHM-style virtual slider
    /// activations. A stationary contact only activates its initial cell;
    /// rubbing across cell boundaries produces new Tap tokens. Cells extend
    /// beyond the central visible track so perspective side lanes remain
    /// playable without adding visual feedback there.
    /// </summary>
    public sealed class VirtualSliderInput
    {
        public const float MinimumLane = -6f;
        public const float MaximumLane = 6f;
        public const int CellCount = 24;
        public const float CellWidth = (MaximumLane - MinimumLane) / CellCount;
        public const float TapDragActivationDistance = CellWidth;
        public const float FlickActivationDistance = .35f;
        public const float FlickGridRowLaneScale = CellWidth;

        readonly Dictionary<int, ContactState> contacts = new();

        public void Reset() => contacts.Clear();

        public void Begin(int fingerId, double time, float lane, ICollection<InputToken> output) =>
            Begin(fingerId, time, lane, float.NaN, output);

        public void Begin(int fingerId, double time, float lane, float gridCoordinate,
            ICollection<InputToken> output)
        {
            var state = new ContactState(lane, time, gridCoordinate);
            contacts[fingerId] = state;
            EmitCell(fingerId, CellAt(lane), time, state, output, InputTokenSource.DirectPress, lane);
        }

        public void Move(int fingerId, double time, float lane, ICollection<InputToken> output) =>
            Move(fingerId, time, lane, float.NaN, output);

        public void Move(int fingerId, double time, float lane, float gridCoordinate,
            ICollection<InputToken> output)
        {
            if (!contacts.TryGetValue(fingerId, out var state))
            {
                Begin(fingerId, time, lane, gridCoordinate, output);
                return;
            }

            var previousLane = state.Lane;
            var previousTime = state.Time;
            var emittedTap = false;
            if (state.TapDragActive)
                emittedTap = EmitCrossedCells(
                    fingerId, previousLane, previousTime, lane, time, state, output);
            else if (Math.Abs(lane - state.StartLane) + .00001f >= TapDragActivationDistance)
            {
                // Touch centroids can cross a cell boundary by a few pixels
                // during an otherwise stationary Tap. Wait for one full cell
                // of travel before treating Move samples as intentional rub,
                // then replay every crossing from the original contact point.
                state.TapDragActive = true;
                emittedTap = EmitCrossedCells(
                    fingerId, state.StartLane, state.StartTime, lane, time, state, output);
            }

            if (ShouldEmitGridRowTap(state, gridCoordinate) && !emittedTap && output != null)
                output.Add(new InputToken(fingerId, RuntimeNoteKind.Tap, time, lane,
                    source: InputTokenSource.GridRowCrossing));
            EmitFlicks(fingerId, lane, gridCoordinate, time, state, output);

            state.Lane = lane;
            state.Time = time;
            state.GridCoordinate = gridCoordinate;
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
            if (Math.Abs(lane - MaximumLane) < .0001f) return CellCount - 1;
            return (int)Math.Floor((lane - MinimumLane) / CellWidth);
        }

        public static float CellCenter(int cell) => MinimumLane + (cell + .5f) * CellWidth;

        static bool EmitCrossedCells(int fingerId, float previousLane, double previousTime,
            float lane, double time, ContactState state, ICollection<InputToken> output)
        {
            var emitted = false;
            var direction = Math.Sign(lane - previousLane);
            var previousCell = CellAt(previousLane);
            var currentCell = CellAt(lane);
            if (direction > 0)
            {
                var first = previousCell + 1;
                var last = currentCell;
                for (var cell = first; cell <= last; cell++)
                {
                    var crossingTime = CrossingTime(previousLane, previousTime, lane, time, cell, direction);
                    var contactLane = MinimumLane + cell * CellWidth;
                    EmitCell(fingerId, cell, crossingTime, state, output,
                        InputTokenSource.CellCrossing, contactLane);
                    emitted = true;
                }
            }
            else if (direction < 0)
            {
                var first = previousCell - 1;
                var last = currentCell;
                for (var cell = first; cell >= last; cell--)
                {
                    var crossingTime = CrossingTime(previousLane, previousTime, lane, time, cell, direction);
                    var contactLane = MinimumLane + (cell + 1) * CellWidth;
                    EmitCell(fingerId, cell, crossingTime, state, output,
                        InputTokenSource.CellCrossing, contactLane);
                    emitted = true;
                }
            }
            return emitted;
        }

        static bool ShouldEmitGridRowTap(ContactState state, float gridCoordinate)
        {
            if (!float.IsFinite(state.StartGridCoordinate) || !float.IsFinite(state.GridCoordinate) ||
                !float.IsFinite(gridCoordinate))
                return false;

            // Starting close to a row boundary must not turn ordinary Tap
            // centroid jitter into another Tap. Once a finger has travelled a
            // complete row from its contact point, subsequent row crossings
            // remain intentional rub activations until the contact ends.
            if (!state.GridRowTapActive)
            {
                if (Math.Abs(gridCoordinate - state.StartGridCoordinate) + .00001f < 1f)
                    return false;
                state.GridRowTapActive = true;
                return (int)Math.Floor(state.StartGridCoordinate) != (int)Math.Floor(gridCoordinate);
            }
            return (int)Math.Floor(state.GridCoordinate) != (int)Math.Floor(gridCoordinate);
        }

        static double CrossingTime(float previousLane, double previousTime, float lane, double time, int enteredCell, int direction)
        {
            var boundary = direction > 0
                ? MinimumLane + enteredCell * CellWidth
                : MinimumLane + (enteredCell + 1) * CellWidth;
            var distance = lane - previousLane;
            if (Math.Abs(distance) < .0001f || time <= previousTime) return time;
            var progress = Math.Clamp((boundary - previousLane) / distance, 0, 1);
            return previousTime + (time - previousTime) * progress;
        }

        static void EmitCell(int fingerId, int cell, double time, ContactState state,
            ICollection<InputToken> output, InputTokenSource source, float contactLane)
        {
            if (output == null) return;
            output.Add(new InputToken(fingerId, RuntimeNoteKind.Tap, time, CellCenter(cell),
                source: source, contactLane: contactLane));
        }

        static void EmitFlicks(int fingerId, float lane, float gridCoordinate, double time,
            ContactState state, ICollection<InputToken> output)
        {
            if (output == null) return;
            while (true)
            {
                var laneDistance = lane - state.FlickAnchorLane;
                var hasGridDistance = float.IsFinite(gridCoordinate) &&
                    float.IsFinite(state.FlickAnchorGridCoordinate);
                var gridDistance = hasGridDistance
                    ? (gridCoordinate - state.FlickAnchorGridCoordinate) * FlickGridRowLaneScale
                    : 0f;
                var distance = Math.Sqrt(laneDistance * laneDistance + gridDistance * gridDistance);
                if (distance + .00001f < FlickActivationDistance) return;

                // Flick direction is intentionally unrestricted. Normalize the
                // judgment-strip row axis into lane units, then emit at the
                // point where the two-dimensional gesture crosses the same
                // activation distance used by horizontal flicks.
                var progress = (float)Math.Clamp(FlickActivationDistance / distance, 0d, 1d);
                var thresholdLane = state.FlickAnchorLane + laneDistance * progress;
                var thresholdGridCoordinate = hasGridDistance
                    ? state.FlickAnchorGridCoordinate +
                      (gridCoordinate - state.FlickAnchorGridCoordinate) * progress
                    : float.NaN;
                var thresholdTime = time > state.FlickAnchorTime
                    ? state.FlickAnchorTime + (time - state.FlickAnchorTime) * progress
                    : time;

                output.Add(new InputToken(fingerId, RuntimeNoteKind.Flick, thresholdTime, thresholdLane,
                    state.FlickAnchorLane, state.FlickAnchorTime, InputTokenSource.FlickPath));
                state.FlickAnchorLane = thresholdLane;
                state.FlickAnchorGridCoordinate = thresholdGridCoordinate;
                state.FlickAnchorTime = thresholdTime;
            }
        }

        sealed class ContactState
        {
            public readonly float StartLane;
            public readonly double StartTime;
            public readonly float StartGridCoordinate;
            public float Lane;
            public double Time;
            public float GridCoordinate;
            public float FlickAnchorLane;
            public float FlickAnchorGridCoordinate;
            public double FlickAnchorTime;
            public bool TapDragActive;
            public bool GridRowTapActive;

            public ContactState(float lane, double time, float gridCoordinate)
            {
                StartLane = lane;
                StartTime = time;
                StartGridCoordinate = gridCoordinate;
                Lane = lane;
                Time = time;
                GridCoordinate = gridCoordinate;
                FlickAnchorLane = lane;
                FlickAnchorGridCoordinate = gridCoordinate;
                FlickAnchorTime = time;
            }
        }
    }

    public readonly struct BufferedTouchSample
    {
        public int FingerId { get; }
        public double Time { get; }
        public Vector2 ScreenPosition { get; }
        public InputTouchPhase Phase { get; }

        public BufferedTouchSample(int fingerId, double time, Vector2 screenPosition, InputTouchPhase phase)
        {
            FingerId = fingerId;
            Time = time;
            ScreenPosition = screenPosition;
            Phase = phase;
        }
    }

    public sealed class TouchInputBuffer
    {
        readonly List<BufferedTouchSample> pending = new(32);

        public void Enqueue(int fingerId, double time, Vector2 screenPosition, InputTouchPhase phase) =>
            pending.Add(new BufferedTouchSample(fingerId, time, screenPosition, phase));

        public void DrainTo(List<BufferedTouchSample> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            output.AddRange(pending);
            pending.Clear();
        }

        public void Clear() => pending.Clear();
    }
}
