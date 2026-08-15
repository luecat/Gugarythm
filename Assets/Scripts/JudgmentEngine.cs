using System;
using System.Collections.Generic;
using System.Linq;

namespace Gugarythm
{
    public readonly struct InputToken
    {
        public readonly int FingerId;
        public readonly RuntimeNoteKind Kind;
        public readonly double Time;
        public readonly float Lane;
        public readonly float PreviousLane;
        public readonly double PreviousTime;

        public InputToken(int fingerId, RuntimeNoteKind kind, double time, float lane, float previousLane = 0, double previousTime = 0)
        {
            FingerId = fingerId;
            Kind = kind;
            Time = time;
            Lane = lane;
            PreviousLane = previousLane;
            PreviousTime = previousTime;
        }
    }

    public readonly struct ActiveContact
    {
        public readonly int FingerId;
        public readonly float Lane;
        public readonly double StartTime;

        public ActiveContact(int fingerId, float lane, double startTime)
        {
            FingerId = fingerId;
            Lane = lane;
            StartTime = startTime;
        }
    }

    public readonly struct JudgmentEvent
    {
        public readonly RuntimeNote Note;
        public readonly JudgmentGrade Grade;
        public readonly double Delta;

        public JudgmentEvent(RuntimeNote note, JudgmentGrade grade, double delta)
        {
            Note = note;
            Grade = grade;
            Delta = delta;
        }
    }

    public sealed class ScoreState
    {
        public int Perfect { get; private set; }
        public int Great { get; private set; }
        public int Good { get; private set; }
        public int Miss { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Judged => Perfect + Great + Good + Miss;
        public double AccuracyNumerator { get; private set; }
        public double AccuracyPercent(int totalNotes) => totalNotes <= 0 ? 0 : AccuracyNumerator / totalNotes * 100;

        public void Reset()
        {
            Perfect = Great = Good = Miss = Combo = MaxCombo = 0;
            AccuracyNumerator = 0;
        }

        public void Register(JudgmentGrade grade)
        {
            switch (grade)
            {
                case JudgmentGrade.Perfect: Perfect++; AccuracyNumerator += 1.01; Combo++; break;
                case JudgmentGrade.Great: Great++; AccuracyNumerator += 1.00; Combo++; break;
                case JudgmentGrade.Good: Good++; AccuracyNumerator += .50; Combo++; break;
                case JudgmentGrade.Miss: Miss++; Combo = 0; break;
            }
            if (Combo > MaxCombo) MaxCombo = Combo;
        }
    }

    public sealed class JudgmentEngine
    {
        public const double SustainLateWindow = 5.0 / 60.0;
        public const double CommitGrace = .025;
        public const float LaneForgiveness = .85f;
        public bool JudgmentProtectionEnabled { get; set; } = true;

        readonly List<RuntimeNote> notes;
        readonly ScoreState score;

        public JudgmentEngine(IEnumerable<RuntimeNote> notes, ScoreState score)
        {
            this.notes = notes.Where(note => note.Judged).OrderBy(note => note.Time).ThenBy(note => note.Index).ToList();
            this.score = score;
        }

        public IReadOnlyList<JudgmentEvent> Process(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts)
        {
            var output = new List<JudgmentEvent>();
            MatchDiscreteInputs(inputBatch, output);
            ResolveSustains(songTime, contacts, output);
            CommitMisses(songTime, output);
            return output;
        }

        void MatchDiscreteInputs(IReadOnlyList<InputToken> inputs, List<JudgmentEvent> output)
        {
            if (inputs == null || inputs.Count == 0) return;
            var edges = new List<Edge>();
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var input = inputs[inputIndex];
                foreach (var note in notes)
                {
                    if (note.Grade != JudgmentGrade.Pending || note.Kind == RuntimeNoteKind.Sustain || note.Kind != input.Kind) continue;
                    if (input.Kind != RuntimeNoteKind.Flick && !LaneMatches(note, input.Lane)) continue;
                    var eventTime = input.Kind == RuntimeNoteKind.Flick ? FlickIntersectionTime(input, note) : input.Time;
                    if (!eventTime.HasValue) continue;
                    var grade = GradeFor(note, eventTime.Value - note.Time);
                    if (grade == JudgmentGrade.Pending) continue;
                    if (JudgmentProtectionEnabled && grade != JudgmentGrade.Perfect && IsProtectedOuterWindow(note, eventTime.Value)) continue;
                    var spatial = Math.Abs(input.Lane - note.Lane);
                    edges.Add(new Edge(inputIndex, note, eventTime.Value, grade, Math.Abs(eventTime.Value - note.Time), spatial));
                }
            }

            var candidates = edges.GroupBy(edge => edge.InputIndex).ToDictionary(group => group.Key, group => group.OrderBy(edge => GradeRank(edge.Grade))
                .ThenBy(edge => edge.TimeError).ThenBy(edge => edge.SpaceError).ThenBy(edge => edge.Note.Index).ToList());
            var inputOrder = Enumerable.Range(0, inputs.Count).Where(candidates.ContainsKey)
                .OrderBy(index => candidates[index].Count)
                .ThenBy(index => candidates[index][0], Comparer<Edge>.Create(CompareEdges))
                .ToArray();
            var matchedByNote = new Dictionary<int, Edge>();
            var matchedByInput = new Dictionary<int, Edge>();
            foreach (var inputIndex in inputOrder)
                TryAugment(inputIndex, candidates, matchedByNote, matchedByInput, new HashSet<int>(), new HashSet<int>());

            foreach (var edge in matchedByInput.Values.OrderBy(edge => edge.EventTime).ThenBy(edge => edge.Note.Index))
                Register(edge.Note, edge.Grade, edge.EventTime - edge.Note.Time, output);
        }

        static bool TryAugment(int inputIndex, IReadOnlyDictionary<int, List<Edge>> candidates, Dictionary<int, Edge> matchedByNote,
            Dictionary<int, Edge> matchedByInput, HashSet<int> seenInputs, HashSet<int> seenNotes)
        {
            if (!seenInputs.Add(inputIndex) || !candidates.TryGetValue(inputIndex, out var choices)) return false;
            foreach (var edge in choices)
            {
                if (!seenNotes.Add(edge.Note.Index)) continue;
                if (!matchedByNote.TryGetValue(edge.Note.Index, out var occupied))
                {
                    matchedByNote[edge.Note.Index] = edge;
                    matchedByInput[edge.InputIndex] = edge;
                    return true;
                }
                if (!TryAugment(occupied.InputIndex, candidates, matchedByNote, matchedByInput, seenInputs, seenNotes)) continue;
                matchedByNote[edge.Note.Index] = edge;
                matchedByInput[edge.InputIndex] = edge;
                return true;
            }
            return false;
        }

        static int CompareEdges(Edge a, Edge b)
        {
            var grade = GradeRank(a.Grade).CompareTo(GradeRank(b.Grade));
            if (grade != 0) return grade;
            var time = a.TimeError.CompareTo(b.TimeError);
            if (time != 0) return time;
            var space = a.SpaceError.CompareTo(b.SpaceError);
            if (space != 0) return space;
            return a.Note.Index.CompareTo(b.Note.Index);
        }

        void ResolveSustains(double songTime, IReadOnlyList<ActiveContact> contacts, List<JudgmentEvent> output)
        {
            foreach (var note in notes)
            {
                if (note.Grade != JudgmentGrade.Pending || note.Kind != RuntimeNoteKind.Sustain || songTime < note.Time) continue;
                var covered = contacts != null && contacts.Any(contact => LaneMatches(note, contact.Lane) && contact.StartTime <= songTime);
                if (covered && songTime - note.Time <= SustainLateWindow)
                    Register(note, JudgmentGrade.Perfect, songTime - note.Time, output);
            }
        }

        void CommitMisses(double songTime, List<JudgmentEvent> output)
        {
            foreach (var note in notes)
            {
                if (note.Grade != JudgmentGrade.Pending) continue;
                var late = note.Kind == RuntimeNoteKind.Sustain ? SustainLateWindow : OuterLateWindow(note);
                if (songTime - note.Time > late + CommitGrace)
                    Register(note, JudgmentGrade.Miss, songTime - note.Time, output);
            }
        }

        static double? FlickIntersectionTime(InputToken input, RuntimeNote note)
        {
            if (Math.Abs(input.Lane - input.PreviousLane) < .0001f && input.Time > input.PreviousTime) return null;
            var min = note.Lane - note.Size - LaneForgiveness;
            var max = note.Lane + note.Size + LaneForgiveness;
            var segmentMin = Math.Min(input.PreviousLane, input.Lane);
            var segmentMax = Math.Max(input.PreviousLane, input.Lane);
            if (segmentMax < min || segmentMin > max) return null;
            if (input.Time <= input.PreviousTime) return input.Time;

            var desired = Math.Clamp(note.Time, input.PreviousTime, input.Time);
            var duration = input.Time - input.PreviousTime;
            var t = (desired - input.PreviousTime) / duration;
            var laneAtDesired = input.PreviousLane + (input.Lane - input.PreviousLane) * (float)t;
            if (laneAtDesired >= min && laneAtDesired <= max) return desired;

            var boundary = laneAtDesired < min ? min : max;
            var laneDelta = input.Lane - input.PreviousLane;
            if (Math.Abs(laneDelta) < .0001f) return input.Time;
            var crossing = (boundary - input.PreviousLane) / laneDelta;
            return input.PreviousTime + duration * Math.Clamp(crossing, 0, 1);
        }

        public static bool LaneMatches(RuntimeNote note, float lane) =>
            lane >= note.Lane - note.Size - LaneForgiveness && lane <= note.Lane + note.Size + LaneForgiveness;

        bool IsProtectedOuterWindow(RuntimeNote note, double eventTime)
        {
            foreach (var other in notes)
            {
                if (ReferenceEquals(note, other) || other.Grade != JudgmentGrade.Pending || other.Kind != note.Kind) continue;
                if (!GeometryOverlaps(note, other)) continue;
                var earlier = note.Time < other.Time ? note : other;
                var later = ReferenceEquals(earlier, note) ? other : note;
                var distance = later.Time - earlier.Time;
                if (distance <= 0 || distance >= OuterLateWindow(earlier) + OuterEarlyWindow(later)) continue;
                var boundary = (earlier.Time + later.Time) * .5;
                if ((ReferenceEquals(note, earlier) && eventTime > boundary) || (ReferenceEquals(note, later) && eventTime < boundary)) return true;
            }
            return false;
        }

        static bool GeometryOverlaps(RuntimeNote a, RuntimeNote b) =>
            a.Lane - a.Size <= b.Lane + b.Size && b.Lane - b.Size <= a.Lane + a.Size;

        public static JudgmentGrade GradeFor(RuntimeNote note, double delta)
        {
            var early = delta < 0;
            var absolute = Math.Abs(delta);
            if (note.Kind == RuntimeNoteKind.Flick)
            {
                var perfect = note.Critical ? 3.5 / 60.0 : 2.5 / 60.0;
                if (absolute <= perfect) return JudgmentGrade.Perfect;
                var great = early ? 6.5 / 60.0 : 7.5 / 60.0;
                if (absolute <= great) return JudgmentGrade.Great;
                var good = early ? 7.5 / 60.0 : 8.5 / 60.0;
                return absolute <= good ? JudgmentGrade.Good : JudgmentGrade.Pending;
            }

            var perfectTap = note.Critical ? 3.3 / 60.0 : 2.5 / 60.0;
            var greatTap = note.Critical ? 4.5 / 60.0 : 5.0 / 60.0;
            if (absolute <= perfectTap) return JudgmentGrade.Perfect;
            if (absolute <= greatTap) return JudgmentGrade.Great;
            return absolute <= 7.5 / 60.0 ? JudgmentGrade.Good : JudgmentGrade.Pending;
        }

        static double OuterLateWindow(RuntimeNote note) => note.Kind == RuntimeNoteKind.Flick ? 8.5 / 60.0 : 7.5 / 60.0;
        static double OuterEarlyWindow(RuntimeNote note) => 7.5 / 60.0;
        static int GradeRank(JudgmentGrade grade) => grade switch { JudgmentGrade.Perfect => 0, JudgmentGrade.Great => 1, JudgmentGrade.Good => 2, _ => 3 };

        void Register(RuntimeNote note, JudgmentGrade grade, double delta, List<JudgmentEvent> output)
        {
            if (note.Grade != JudgmentGrade.Pending) return;
            note.Grade = grade;
            score.Register(grade);
            output.Add(new JudgmentEvent(note, grade, delta));
        }

        readonly struct Edge
        {
            public readonly int InputIndex;
            public readonly RuntimeNote Note;
            public readonly double EventTime;
            public readonly JudgmentGrade Grade;
            public readonly double TimeError;
            public readonly double SpaceError;

            public Edge(int inputIndex, RuntimeNote note, double eventTime, JudgmentGrade grade, double timeError, double spaceError)
            {
                InputIndex = inputIndex; Note = note; EventTime = eventTime; Grade = grade; TimeError = timeError; SpaceError = spaceError;
            }
        }
    }
}
