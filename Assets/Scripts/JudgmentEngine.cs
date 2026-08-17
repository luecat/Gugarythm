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

    public readonly struct ContactPathSegment
    {
        public readonly int FingerId;
        public readonly double StartTime;
        public readonly double EndTime;
        public readonly float StartLane;
        public readonly float EndLane;
        public readonly bool Ended;

        public ContactPathSegment(int fingerId, double startTime, double endTime, float startLane, float endLane, bool ended)
        {
            FingerId = fingerId;
            StartTime = startTime;
            EndTime = endTime;
            StartLane = startLane;
            EndLane = endLane;
            Ended = ended;
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
        const double JusticeCriticalWindow = 2.0 / 60.0;
        const double JusticeWindow = 4.0 / 60.0;
        const double AttackWindow = 6.0 / 60.0;
        public const double SustainLateWindow = 5.0 / 60.0;
        public const double CommitGrace = .025;
        public const float LaneForgiveness = .85f;
        public bool JudgmentProtectionEnabled { get; set; } = true;

        readonly List<RuntimeNote> notes;
        readonly ScoreState score;
        readonly Dictionary<int, TapProtectionPair[]> tapProtectionPairs;

        enum ProtectionBand
        {
            Outside,
            Critical,
            Justice,
            Attack,
        }

        readonly struct TapProtectionPair
        {
            public readonly RuntimeNote Earlier;
            public readonly RuntimeNote Later;
            public readonly double Boundary;
            public readonly float SharedMinimum;
            public readonly float SharedMaximum;

            public TapProtectionPair(RuntimeNote earlier, RuntimeNote later, float sharedMinimum, float sharedMaximum)
            {
                Earlier = earlier;
                Later = later;
                Boundary = (earlier.Time + later.Time) * .5;
                SharedMinimum = sharedMinimum;
                SharedMaximum = sharedMaximum;
            }

            public bool ContainsSharedLane(float lane) => lane >= SharedMinimum && lane <= SharedMaximum;
        }

        public JudgmentEngine(IEnumerable<RuntimeNote> notes, ScoreState score)
        {
            this.notes = notes.Where(note => note.Judged).OrderBy(note => note.Time).ThenBy(note => note.Index).ToList();
            this.score = score;
            tapProtectionPairs = BuildTapProtectionPairs(this.notes);
        }

        public IReadOnlyList<JudgmentEvent> Process(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts)
            => Process(songTime, inputBatch, contacts, Array.Empty<ContactPathSegment>());

        public IReadOnlyList<JudgmentEvent> Process(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts,
            IReadOnlyList<ContactPathSegment> contactPaths)
            => Process(songTime, inputBatch, contacts, contactPaths, false);

        public IReadOnlyList<JudgmentEvent> Process(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts,
            IReadOnlyList<ContactPathSegment> contactPaths, bool autoPlay)
        {
            var output = new List<JudgmentEvent>();
            if (autoPlay)
            {
                ResolveAutoPlay(songTime, output);
                return output;
            }
            MatchDiscreteInputs(inputBatch, output);
            ResolveContactNotes(songTime, contacts, contactPaths, output);
            CommitMisses(songTime, output);
            return output;
        }

        void ResolveAutoPlay(double songTime, List<JudgmentEvent> output)
        {
            foreach (var note in notes)
                if (note.Grade == JudgmentGrade.Pending && note.Time <= songTime)
                    Register(note, JudgmentGrade.Perfect, 0, output);
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
                    if (note.Grade != JudgmentGrade.Pending || IsContactNote(note) || note.Kind != input.Kind) continue;
                    if (input.Kind != RuntimeNoteKind.Flick && !LaneMatches(note, input.Lane)) continue;
                    var protectionLane = input.Lane;
                    var eventTime = input.Kind == RuntimeNoteKind.Flick
                        ? FlickIntersectionTime(input, note, out protectionLane)
                        : input.Time;
                    if (!eventTime.HasValue) continue;
                    var grade = GradeFor(note, eventTime.Value - note.Time);
                    if (grade == JudgmentGrade.Pending) continue;
                    if (JudgmentProtectionEnabled &&
                        IsProtectedCandidate(note, eventTime.Value, protectionLane)) continue;
                    var spatial = Math.Abs(input.Lane - note.Lane);
                    edges.Add(new Edge(inputIndex, note, eventTime.Value, grade, Math.Abs(eventTime.Value - note.Time), spatial));
                }
            }

            // A rub can emit several neighbouring cell activations in one
            // batch. If one of them is inside a note's authored span, do not
            // let an earlier forgiveness-only edge reserve that note first.
            var bestAuthoredMatches = edges
                .Where(edge => inputs[edge.InputIndex].Kind != RuntimeNoteKind.Flick &&
                               LaneInAuthoredSpan(edge.Note, inputs[edge.InputIndex].Lane))
                .GroupBy(edge => (edge.Note.Index, inputs[edge.InputIndex].FingerId))
                .ToDictionary(group => group.Key, group => group.Aggregate((best, candidate) =>
                    CompareEdges(candidate, best) < 0 ? candidate : best));
            edges.RemoveAll(edge => bestAuthoredMatches.TryGetValue(
                    (edge.Note.Index, inputs[edge.InputIndex].FingerId), out var authored) &&
                inputs[edge.InputIndex].Kind != RuntimeNoteKind.Flick &&
                !LaneInAuthoredSpan(edge.Note, inputs[edge.InputIndex].Lane) &&
                CompareEdges(authored, edge) <= 0);

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

        void ResolveContactNotes(double songTime, IReadOnlyList<ActiveContact> contacts, IReadOnlyList<ContactPathSegment> contactPaths,
            List<JudgmentEvent> output)
        {
            foreach (var note in notes)
            {
                if (note.Grade != JudgmentGrade.Pending || !IsContactNote(note) || songTime < note.Time) continue;
                var covered = contacts != null && contacts.Any(contact => LaneMatches(note, contact.Lane) && contact.StartTime <= songTime);
                var crossed = contactPaths != null && contactPaths.Any(path =>
                    FirstIntersectionTime(path, note, note.Time, note.Time + SustainLateWindow).HasValue);
                if ((covered || crossed) && songTime - note.Time <= SustainLateWindow)
                    Register(note, JudgmentGrade.Perfect, songTime - note.Time, output);
            }
        }

        void CommitMisses(double songTime, List<JudgmentEvent> output)
        {
            foreach (var note in notes)
            {
                if (note.Grade != JudgmentGrade.Pending) continue;
                var late = IsContactNote(note) ? SustainLateWindow : OuterLateWindow(note);
                if (songTime - note.Time > late + CommitGrace)
                    Register(note, JudgmentGrade.Miss, songTime - note.Time, output);
            }
        }

        static bool IsContactNote(RuntimeNote note) => note.Kind is RuntimeNoteKind.Sustain or RuntimeNoteKind.Release;

        static double? FirstIntersectionTime(ContactPathSegment path, RuntimeNote note, double earliestTime, double latestTime)
        {
            var startTime = Math.Max(path.StartTime, earliestTime);
            var endTime = Math.Min(path.EndTime, latestTime);
            if (endTime < startTime) return null;

            var startLane = LaneAt(path, startTime);
            var endLane = LaneAt(path, endTime);
            var minimum = note.Lane - note.Size - LaneForgiveness;
            var maximum = note.Lane + note.Size + LaneForgiveness;
            if (startLane >= minimum && startLane <= maximum) return startTime;

            var laneDelta = endLane - startLane;
            if (Math.Abs(laneDelta) < .0001f) return null;
            var entryLane = laneDelta > 0 ? minimum : maximum;
            var progress = (entryLane - startLane) / laneDelta;
            if (progress < 0 || progress > 1) return null;
            return startTime + (endTime - startTime) * progress;
        }

        static float LaneAt(ContactPathSegment path, double time)
        {
            if (path.EndTime <= path.StartTime) return path.EndLane;
            var progress = Math.Clamp((time - path.StartTime) / (path.EndTime - path.StartTime), 0, 1);
            return path.StartLane + (path.EndLane - path.StartLane) * (float)progress;
        }

        static double? FlickIntersectionTime(InputToken input, RuntimeNote note, out float intersectionLane)
        {
            intersectionLane = input.Lane;
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
            if (laneAtDesired >= min && laneAtDesired <= max)
            {
                intersectionLane = laneAtDesired;
                return desired;
            }

            var boundary = laneAtDesired < min ? min : max;
            var laneDelta = input.Lane - input.PreviousLane;
            if (Math.Abs(laneDelta) < .0001f) return input.Time;
            var crossing = (boundary - input.PreviousLane) / laneDelta;
            intersectionLane = boundary;
            return input.PreviousTime + duration * Math.Clamp(crossing, 0, 1);
        }

        public static bool LaneMatches(RuntimeNote note, float lane) =>
            lane >= note.Lane - note.Size - LaneForgiveness && lane <= note.Lane + note.Size + LaneForgiveness;

        static bool LaneInAuthoredSpan(RuntimeNote note, float lane) =>
            lane >= note.Lane - note.Size && lane <= note.Lane + note.Size;

        static Dictionary<int, TapProtectionPair[]> BuildTapProtectionPairs(IReadOnlyList<RuntimeNote> notes)
        {
            var mutable = new Dictionary<int, List<TapProtectionPair>>();
            for (var i = 0; i < notes.Count; i++)
            {
                var earlier = notes[i];
                if (!IsTapProtectionKind(earlier.Kind)) continue;
                for (var j = i + 1; j < notes.Count; j++)
                {
                    var later = notes[j];
                    var distance = later.Time - earlier.Time;
                    if (distance >= OuterLateWindow(earlier) + OuterEarlyWindow(later)) break;
                    if (distance <= 0 || !IsTapProtectionKind(later.Kind)) continue;

                    var sharedMinimum = Math.Max(earlier.Lane - earlier.Size, later.Lane - later.Size);
                    var sharedMaximum = Math.Min(earlier.Lane + earlier.Size, later.Lane + later.Size);
                    if (sharedMinimum >= sharedMaximum) continue;

                    var pair = new TapProtectionPair(earlier, later, sharedMinimum, sharedMaximum);
                    AddProtectionPair(mutable, earlier.Index, pair);
                    AddProtectionPair(mutable, later.Index, pair);
                }
            }

            return mutable.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        static bool IsTapProtectionKind(RuntimeNoteKind kind) => kind is RuntimeNoteKind.Tap or RuntimeNoteKind.Flick;

        static void AddProtectionPair(Dictionary<int, List<TapProtectionPair>> map, int index, TapProtectionPair pair)
        {
            if (!map.TryGetValue(index, out var pairs))
            {
                pairs = new List<TapProtectionPair>();
                map.Add(index, pairs);
            }
            pairs.Add(pair);
        }

        bool IsProtectedCandidate(RuntimeNote note, double eventTime, float inputLane)
        {
            if (!IsTapProtectionKind(note.Kind) ||
                !tapProtectionPairs.TryGetValue(note.Index, out var pairs)) return false;

            var band = ProtectionBandFor(eventTime - note.Time);
            if (band == ProtectionBand.Outside) return false;
            foreach (var pair in pairs)
            {
                var wrongHalf = ReferenceEquals(note, pair.Earlier)
                    ? eventTime > pair.Boundary
                    : eventTime < pair.Boundary;
                if (!wrongHalf) continue;
                if (band is ProtectionBand.Justice or ProtectionBand.Attack) return true;
                if (band == ProtectionBand.Critical && pair.ContainsSharedLane(inputLane)) return true;
            }

            return false;
        }

        static ProtectionBand ProtectionBandFor(double delta)
        {
            var absolute = Math.Abs(delta);
            if (absolute <= JusticeCriticalWindow) return ProtectionBand.Critical;
            if (absolute <= JusticeWindow) return ProtectionBand.Justice;
            if (absolute <= AttackWindow) return ProtectionBand.Attack;
            return ProtectionBand.Outside;
        }

        public static JudgmentGrade GradeFor(RuntimeNote note, double delta)
        {
            var early = delta < 0;
            var absolute = Math.Abs(delta);
            if (note.Kind == RuntimeNoteKind.Flick)
            {
                if (early && absolute <= AttackWindow) return JudgmentGrade.Perfect;
                if (absolute <= JusticeCriticalWindow) return JudgmentGrade.Perfect;
                if (absolute <= JusticeWindow) return JudgmentGrade.Great;
                return absolute <= AttackWindow ? JudgmentGrade.Good : JudgmentGrade.Pending;
            }

            if (note.Kind == RuntimeNoteKind.Tap && note.Critical)
                return absolute <= AttackWindow
                    ? JudgmentGrade.Perfect
                    : JudgmentGrade.Pending;

            if (absolute <= JusticeCriticalWindow) return JudgmentGrade.Perfect;
            if (absolute <= JusticeWindow) return JudgmentGrade.Great;
            return absolute <= AttackWindow ? JudgmentGrade.Good : JudgmentGrade.Pending;
        }

        static double OuterLateWindow(RuntimeNote note) => AttackWindow;
        static double OuterEarlyWindow(RuntimeNote note) => AttackWindow;
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
