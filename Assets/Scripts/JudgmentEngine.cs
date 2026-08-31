using System;
using System.Collections.Generic;
using System.Linq;

namespace Gugarhythm
{
    public enum InputTokenSource
    {
        Unspecified,
        DirectPress,
        CellCrossing,
        GridRowCrossing,
        FlickPath,
    }

    public readonly struct InputToken
    {
        public readonly int FingerId;
        public readonly RuntimeNoteKind Kind;
        public readonly double Time;
        public readonly float Lane;
        public readonly float PreviousLane;
        public readonly double PreviousTime;
        public readonly InputTokenSource Source;
        public readonly float ContactLane;

        public InputToken(int fingerId, RuntimeNoteKind kind, double time, float lane,
            float previousLane = 0, double previousTime = 0,
            InputTokenSource source = InputTokenSource.Unspecified,
            float contactLane = float.NaN)
        {
            FingerId = fingerId;
            Kind = kind;
            Time = time;
            Lane = lane;
            PreviousLane = previousLane;
            PreviousTime = previousTime;
            Source = source;
            ContactLane = float.IsNaN(contactLane) ? lane : contactLane;
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
        public readonly float? HitLane;

        public JudgmentEvent(RuntimeNote note, JudgmentGrade grade, double delta, float? hitLane = null)
        {
            Note = note;
            Grade = grade;
            Delta = delta;
            HitLane = hitLane;
        }
    }

    public enum JudgmentInputDisposition
    {
        Unmatched,
        ProtectionBlocked,
        Matched,
    }

    public readonly struct JudgmentInputDiagnostic
    {
        public readonly InputToken Input;
        public readonly RuntimeNote Note;
        public readonly JudgmentInputDisposition Disposition;
        public readonly JudgmentGrade CandidateGrade;
        public readonly double EventTime;
        public readonly double Delta;

        public JudgmentInputDiagnostic(InputToken input, RuntimeNote note,
            JudgmentInputDisposition disposition, JudgmentGrade candidateGrade,
            double eventTime, double delta)
        {
            Input = input;
            Note = note;
            Disposition = disposition;
            CandidateGrade = candidateGrade;
            EventTime = eventTime;
            Delta = delta;
        }
    }

    public enum JudgmentTiming { None, Fast, Late }

    public static class JudgmentTimingClassifier
    {
        public static JudgmentTiming Classify(JudgmentGrade grade, double delta)
        {
            // FAST/LATE describes an actual non-perfect press. A MISS is
            // generated after the input window expires, so it must not be
            // presented or counted as a late press.
            if (grade != JudgmentGrade.Great && grade != JudgmentGrade.Good)
                return JudgmentTiming.None;
            return delta < 0d ? JudgmentTiming.Fast : JudgmentTiming.Late;
        }
    }

    public sealed class JudgmentTimingStatistics
    {
        public int Fast { get; private set; }
        public int Late { get; private set; }

        public void Reset()
        {
            Fast = 0;
            Late = 0;
        }

        public JudgmentTiming Register(JudgmentEvent judgment)
        {
            var timing = JudgmentTimingClassifier.Classify(judgment.Grade, judgment.Delta);
            if (timing == JudgmentTiming.Fast) Fast++;
            else if (timing == JudgmentTiming.Late) Late++;
            return timing;
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
        public const double SustainLookbackWindow = 9.0 / 60.0;
        public const double CommitGrace = .025;
        public const float LaneForgiveness = .85f;
        const double StackedTimeTolerance = 1e-9;
        public bool JudgmentProtectionEnabled { get; set; } = true;

        readonly List<RuntimeNote> notes;
        readonly ScoreState score;
        readonly Dictionary<int, TapProtectionPair[]> tapProtectionPairs;
        readonly List<ContactPathSegment> recentContactPaths = new();
        readonly RuntimeNote[][] notesByKind;
        readonly RuntimeNote[] contactNotes;
        readonly RuntimeNote[] discreteMissNotes;
        readonly List<Edge> edgeWorkspace = new();
        readonly List<List<Edge>> candidateWorkspace = new();
        readonly Dictionary<(int NoteIndex, int FingerId), Edge> authoredBestWorkspace = new();
        readonly List<int> inputOrderWorkspace = new();
        readonly Dictionary<int, Edge> matchedByNoteWorkspace = new();
        readonly List<Edge> matchedByInputWorkspace = new();
        readonly List<bool> hasMatchedInputWorkspace = new();
        readonly Dictionary<int, Edge> registrationBestByNoteWorkspace = new();
        readonly List<int> seenInputStamps = new();
        readonly HashSet<int> seenNoteIndexes = new();
        readonly List<Edge> registrationWorkspace = new();
        readonly List<RuntimeNote> dueMissWorkspace = new();
        int seenInputStamp;
        int discreteMissCursor;
        int contactMissCursor;
        int autoPlayCursor;

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

            var kindLists = new[]
            {
                new List<RuntimeNote>(),
                new List<RuntimeNote>(),
                new List<RuntimeNote>(),
                new List<RuntimeNote>(),
            };
            var contactList = new List<RuntimeNote>();
            var discreteMissList = new List<RuntimeNote>();
            for (var index = 0; index < this.notes.Count; index++)
            {
                var note = this.notes[index];
                var kindIndex = (int)note.Kind;
                if ((uint)kindIndex < (uint)kindLists.Length) kindLists[kindIndex].Add(note);
                if (IsContactNote(note)) contactList.Add(note);
                else discreteMissList.Add(note);
            }

            notesByKind = new RuntimeNote[kindLists.Length][];
            for (var kindIndex = 0; kindIndex < kindLists.Length; kindIndex++)
                notesByKind[kindIndex] = kindLists[kindIndex].ToArray();
            contactNotes = contactList.ToArray();
            discreteMissNotes = discreteMissList.ToArray();
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
            ProcessInto(songTime, inputBatch, contacts, contactPaths, autoPlay, output);
            return output;
        }

        public void ProcessInto(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts,
            List<JudgmentEvent> output) =>
            ProcessInto(songTime, inputBatch, contacts, Array.Empty<ContactPathSegment>(), false, output);

        public void ProcessInto(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts,
            IReadOnlyList<ContactPathSegment> contactPaths, List<JudgmentEvent> output) =>
            ProcessInto(songTime, inputBatch, contacts, contactPaths, false, output);

        public void ProcessInto(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts,
            IReadOnlyList<ContactPathSegment> contactPaths, bool autoPlay, List<JudgmentEvent> output)
            => ProcessInto(songTime, inputBatch, contacts, contactPaths, autoPlay, output, null);

        public void ProcessInto(double songTime, IReadOnlyList<InputToken> inputBatch, IReadOnlyList<ActiveContact> contacts,
            IReadOnlyList<ContactPathSegment> contactPaths, bool autoPlay, List<JudgmentEvent> output,
            List<JudgmentInputDiagnostic> inputDiagnostics)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            inputDiagnostics?.Clear();
            if (autoPlay)
            {
                ResolveAutoPlay(songTime, output);
                return;
            }
            RecordContactPaths(songTime, contactPaths);
            MatchDiscreteInputs(inputBatch, output, inputDiagnostics);
            ResolveContactNotes(songTime, contacts, contactPaths, output);
            CommitMisses(songTime, output);
        }

        void ResolveAutoPlay(double songTime, List<JudgmentEvent> output)
        {
            if (double.IsNaN(songTime)) return;
            while (autoPlayCursor < notes.Count)
            {
                var note = notes[autoPlayCursor];
                if (double.IsNaN(note.Time))
                {
                    autoPlayCursor++;
                    continue;
                }
                if (note.Time > songTime) break;
                autoPlayCursor++;
                if (note.Grade == JudgmentGrade.Pending) Register(note, JudgmentGrade.Perfect, 0, output);
            }
        }

        void MatchDiscreteInputs(IReadOnlyList<InputToken> inputs, List<JudgmentEvent> output,
            List<JudgmentInputDiagnostic> inputDiagnostics)
        {
            if (inputs == null || inputs.Count == 0) return;
            PrepareInputWorkspaces(inputs.Count);
            if (inputDiagnostics != null)
            {
                for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                    inputDiagnostics.Add(new JudgmentInputDiagnostic(inputs[inputIndex], null,
                        JudgmentInputDisposition.Unmatched, JudgmentGrade.Pending,
                        inputs[inputIndex].Time, double.NaN));
            }
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var input = inputs[inputIndex];
                var kindIndex = (int)input.Kind;
                if ((uint)kindIndex >= (uint)notesByKind.Length) continue;
                var indexedNotes = notesByKind[kindIndex];
                CandidateTimeRange(input, out var earliestNoteTime, out var latestNoteTime);
                var first = LowerBoundTime(indexedNotes, earliestNoteTime);
                var end = UpperBoundTime(indexedNotes, latestNoteTime);
                for (var noteIndex = first; noteIndex < end; noteIndex++)
                {
                    var note = indexedNotes[noteIndex];
                    if (note.Kind != input.Kind) continue;
                    if (!TryCreateEdge(inputIndex, input, note, out var edge,
                            out var blockedNote, out var blockedGrade, out var blockedEventTime))
                    {
                        if (inputDiagnostics != null && blockedNote != null &&
                            inputDiagnostics[inputIndex].Disposition == JudgmentInputDisposition.Unmatched)
                            inputDiagnostics[inputIndex] = new JudgmentInputDiagnostic(input, blockedNote,
                                JudgmentInputDisposition.ProtectionBlocked, blockedGrade,
                                blockedEventTime, blockedEventTime - blockedNote.Time);
                        continue;
                    }
                    edgeWorkspace.Add(edge);
                }
            }

            // A rub can emit several neighbouring cell activations in one
            // batch. If one of them is inside a note's authored span, do not
            // let an earlier forgiveness-only edge reserve that note first.
            for (var edgeIndex = 0; edgeIndex < edgeWorkspace.Count; edgeIndex++)
            {
                var edge = edgeWorkspace[edgeIndex];
                var input = inputs[edge.InputIndex];
                if (input.Kind == RuntimeNoteKind.Flick || !LaneInAuthoredSpan(edge.Note, input.Lane)) continue;
                var key = (edge.Note.Index, input.FingerId);
                if (!authoredBestWorkspace.TryGetValue(key, out var best) || CompareEdges(edge, best) < 0)
                    authoredBestWorkspace[key] = edge;
            }

            var retainedCount = 0;
            for (var edgeIndex = 0; edgeIndex < edgeWorkspace.Count; edgeIndex++)
            {
                var edge = edgeWorkspace[edgeIndex];
                var input = inputs[edge.InputIndex];
                var remove = input.Kind != RuntimeNoteKind.Flick &&
                    !LaneInAuthoredSpan(edge.Note, input.Lane) &&
                    authoredBestWorkspace.TryGetValue((edge.Note.Index, input.FingerId), out var authored) &&
                    CompareEdges(authored, edge) <= 0;
                if (!remove) edgeWorkspace[retainedCount++] = edge;
            }
            if (retainedCount < edgeWorkspace.Count)
                edgeWorkspace.RemoveRange(retainedCount, edgeWorkspace.Count - retainedCount);

            for (var edgeIndex = 0; edgeIndex < edgeWorkspace.Count; edgeIndex++)
            {
                var edge = edgeWorkspace[edgeIndex];
                candidateWorkspace[edge.InputIndex].Add(edge);
            }
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                var candidates = candidateWorkspace[inputIndex];
                if (candidates.Count == 0) continue;
                StableSortEdges(candidates);
                inputOrderWorkspace.Add(inputIndex);
            }
            StableSortInputOrder(inputOrderWorkspace, candidateWorkspace);

            for (var orderIndex = 0; orderIndex < inputOrderWorkspace.Count; orderIndex++)
            {
                BeginAugmentSearch();
                TryAugment(inputOrderWorkspace[orderIndex]);
            }

            if (inputDiagnostics != null)
            {
                for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
                {
                    if (!hasMatchedInputWorkspace[inputIndex]) continue;
                    var matched = matchedByInputWorkspace[inputIndex];
                    inputDiagnostics[inputIndex] = new JudgmentInputDiagnostic(inputs[inputIndex], matched.Note,
                        JudgmentInputDisposition.Matched, matched.Grade, matched.EventTime,
                        matched.EventTime - matched.Note.Time);
                }
            }

            registrationBestByNoteWorkspace.Clear();
            for (var inputIndex = 0; inputIndex < inputs.Count; inputIndex++)
            {
                if (!hasMatchedInputWorkspace[inputIndex]) continue;
                var matched = matchedByInputWorkspace[inputIndex];
                var input = inputs[inputIndex];

                // Hold terminals keep their authored release/flick semantics.
                // They may be matched normally, but never seed or join a
                // same-time stacked-note expansion.
                if (matched.Note.IsHoldTerminal)
                {
                    registrationBestByNoteWorkspace[matched.Note.Index] = matched;
                    continue;
                }

                var first = LowerBoundTime(discreteMissNotes, matched.Note.Time - StackedTimeTolerance);
                var end = UpperBoundTime(discreteMissNotes, matched.Note.Time + StackedTimeTolerance);
                for (var noteIndex = first; noteIndex < end; noteIndex++)
                {
                    var note = discreteMissNotes[noteIndex];
                    if (note.IsHoldTerminal ||
                        Math.Abs(note.Time - matched.Note.Time) > StackedTimeTolerance ||
                        !TryCreateEdge(inputIndex, input, note, out var candidate)) continue;
                    if (!registrationBestByNoteWorkspace.TryGetValue(candidate.Note.Index, out var best) ||
                        CompareEdges(candidate, best) < 0)
                        registrationBestByNoteWorkspace[candidate.Note.Index] = candidate;
                }
            }
            foreach (var pair in registrationBestByNoteWorkspace)
                registrationWorkspace.Add(pair.Value);
            StableSortRegistrationEdges(registrationWorkspace);
            for (var edgeIndex = 0; edgeIndex < registrationWorkspace.Count; edgeIndex++)
            {
                var edge = registrationWorkspace[edgeIndex];
                Register(edge.Note, edge.Grade, edge.EventTime - edge.Note.Time, output, edge.HitLane);
            }
        }

        bool TryCreateEdge(int inputIndex, InputToken input, RuntimeNote note, out Edge edge)
            => TryCreateEdge(inputIndex, input, note, out edge, out _, out _, out _);

        bool TryCreateEdge(int inputIndex, InputToken input, RuntimeNote note, out Edge edge,
            out RuntimeNote blockedNote, out JudgmentGrade blockedGrade, out double blockedEventTime)
        {
            edge = default;
            blockedNote = null;
            blockedGrade = JudgmentGrade.Pending;
            blockedEventTime = double.NaN;
            if (note.Grade != JudgmentGrade.Pending || IsContactNote(note)) return false;
            if (input.Kind != RuntimeNoteKind.Flick && !LaneMatches(note, input.Lane)) return false;
            var protectionLane = input.Lane;
            var eventTime = input.Kind == RuntimeNoteKind.Flick
                ? FlickIntersectionTime(input, note, out protectionLane)
                : input.Time;
            if (!eventTime.HasValue) return false;
            var grade = GradeFor(note, eventTime.Value - note.Time);
            if (grade == JudgmentGrade.Pending) return false;
            if (JudgmentProtectionEnabled && input.Source != InputTokenSource.DirectPress &&
                IsProtectedCandidate(note, eventTime.Value, protectionLane))
            {
                blockedNote = note;
                blockedGrade = grade;
                blockedEventTime = eventTime.Value;
                return false;
            }
            var hitLane = input.Kind == RuntimeNoteKind.Flick ? protectionLane : input.ContactLane;
            var spatial = Math.Abs(input.Lane - note.Lane);
            edge = new Edge(inputIndex, note, eventTime.Value, grade,
                Math.Abs(eventTime.Value - note.Time), spatial, hitLane);
            return true;
        }

        bool TryAugment(int inputIndex)
        {
            if (seenInputStamps[inputIndex] == seenInputStamp) return false;
            seenInputStamps[inputIndex] = seenInputStamp;
            var choices = candidateWorkspace[inputIndex];
            if (choices.Count == 0) return false;
            for (var choiceIndex = 0; choiceIndex < choices.Count; choiceIndex++)
            {
                var edge = choices[choiceIndex];
                if (!seenNoteIndexes.Add(edge.Note.Index)) continue;
                if (!matchedByNoteWorkspace.TryGetValue(edge.Note.Index, out var occupied))
                {
                    matchedByNoteWorkspace[edge.Note.Index] = edge;
                    matchedByInputWorkspace[edge.InputIndex] = edge;
                    hasMatchedInputWorkspace[edge.InputIndex] = true;
                    return true;
                }
                if (!TryAugment(occupied.InputIndex)) continue;
                matchedByNoteWorkspace[edge.Note.Index] = edge;
                matchedByInputWorkspace[edge.InputIndex] = edge;
                hasMatchedInputWorkspace[edge.InputIndex] = true;
                return true;
            }
            return false;
        }

        void PrepareInputWorkspaces(int inputCount)
        {
            edgeWorkspace.Clear();
            authoredBestWorkspace.Clear();
            inputOrderWorkspace.Clear();
            matchedByNoteWorkspace.Clear();
            seenNoteIndexes.Clear();
            registrationWorkspace.Clear();

            while (candidateWorkspace.Count < inputCount)
            {
                candidateWorkspace.Add(new List<Edge>());
                matchedByInputWorkspace.Add(default);
                hasMatchedInputWorkspace.Add(false);
                seenInputStamps.Add(0);
            }
            for (var inputIndex = 0; inputIndex < candidateWorkspace.Count; inputIndex++)
                candidateWorkspace[inputIndex].Clear();
            for (var inputIndex = 0; inputIndex < inputCount; inputIndex++)
                hasMatchedInputWorkspace[inputIndex] = false;
        }

        void BeginAugmentSearch()
        {
            seenNoteIndexes.Clear();
            if (seenInputStamp == int.MaxValue)
            {
                for (var index = 0; index < seenInputStamps.Count; index++) seenInputStamps[index] = 0;
                seenInputStamp = 1;
                return;
            }
            seenInputStamp++;
        }

        static void CandidateTimeRange(InputToken input, out double earliest, out double latest)
        {
            if (input.Kind == RuntimeNoteKind.Flick)
            {
                earliest = Math.Min(input.PreviousTime, input.Time) - AttackWindow;
                latest = Math.Max(input.PreviousTime, input.Time) + AttackWindow;
            }
            else
            {
                earliest = input.Time - AttackWindow;
                latest = input.Time + AttackWindow;
            }

            if (double.IsNaN(earliest) || double.IsNaN(latest))
            {
                earliest = double.NegativeInfinity;
                latest = double.PositiveInfinity;
                return;
            }
            WidenTimeRange(ref earliest, ref latest);
        }

        static void WidenTimeRange(ref double earliest, ref double latest)
        {
            earliest = PreviousDouble(earliest);
            latest = NextDouble(latest);
        }

        static double PreviousDouble(double value)
        {
            if (double.IsNaN(value) || double.IsNegativeInfinity(value)) return value;
            if (value == 0) return -double.Epsilon;
            var bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int64BitsToDouble(value > 0 ? bits - 1 : bits + 1);
        }

        static double NextDouble(double value)
        {
            if (double.IsNaN(value) || double.IsPositiveInfinity(value)) return value;
            if (value == 0) return double.Epsilon;
            var bits = BitConverter.DoubleToInt64Bits(value);
            return BitConverter.Int64BitsToDouble(value > 0 ? bits + 1 : bits - 1);
        }

        static int LowerBoundTime(RuntimeNote[] indexedNotes, double time)
        {
            var low = 0;
            var high = indexedNotes.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (indexedNotes[middle].Time.CompareTo(time) < 0) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        static int UpperBoundTime(RuntimeNote[] indexedNotes, double time)
        {
            var low = 0;
            var high = indexedNotes.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (indexedNotes[middle].Time.CompareTo(time) <= 0) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        static void StableSortEdges(List<Edge> edges)
        {
            for (var index = 1; index < edges.Count; index++)
            {
                var value = edges[index];
                var destination = index;
                while (destination > 0 && CompareEdges(value, edges[destination - 1]) < 0)
                {
                    edges[destination] = edges[destination - 1];
                    destination--;
                }
                edges[destination] = value;
            }
        }

        static void StableSortInputOrder(List<int> inputOrder, List<List<Edge>> candidates)
        {
            for (var index = 1; index < inputOrder.Count; index++)
            {
                var value = inputOrder[index];
                var destination = index;
                while (destination > 0 && CompareInputOrder(value, inputOrder[destination - 1], candidates) < 0)
                {
                    inputOrder[destination] = inputOrder[destination - 1];
                    destination--;
                }
                inputOrder[destination] = value;
            }
        }

        static int CompareInputOrder(int a, int b, List<List<Edge>> candidates)
        {
            var count = candidates[a].Count.CompareTo(candidates[b].Count);
            if (count != 0) return count;
            var first = CompareEdges(candidates[a][0], candidates[b][0]);
            return first != 0 ? first : a.CompareTo(b);
        }

        static void StableSortRegistrationEdges(List<Edge> edges)
        {
            for (var index = 1; index < edges.Count; index++)
            {
                var value = edges[index];
                var destination = index;
                while (destination > 0 && CompareRegistrationEdges(value, edges[destination - 1]) < 0)
                {
                    edges[destination] = edges[destination - 1];
                    destination--;
                }
                edges[destination] = value;
            }
        }

        static int CompareRegistrationEdges(Edge a, Edge b)
        {
            var time = a.EventTime.CompareTo(b.EventTime);
            return time != 0 ? time : a.Note.Index.CompareTo(b.Note.Index);
        }

        static void StableSortNotes(List<RuntimeNote> sortedNotes)
        {
            for (var index = 1; index < sortedNotes.Count; index++)
            {
                var value = sortedNotes[index];
                var destination = index;
                while (destination > 0 && CompareNotes(value, sortedNotes[destination - 1]) < 0)
                {
                    sortedNotes[destination] = sortedNotes[destination - 1];
                    destination--;
                }
                sortedNotes[destination] = value;
            }
        }

        static int CompareNotes(RuntimeNote a, RuntimeNote b)
        {
            var time = a.Time.CompareTo(b.Time);
            return time != 0 ? time : a.Index.CompareTo(b.Index);
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
            if (double.IsNaN(songTime)) return;
            var earliestNoteTime = PreviousDouble(songTime - SustainLateWindow);
            var latestNoteTime = NextDouble(songTime);
            var first = LowerBoundTime(contactNotes, earliestNoteTime);
            var end = UpperBoundTime(contactNotes, latestNoteTime);
            for (var noteIndex = first; noteIndex < end; noteIndex++)
            {
                var note = contactNotes[noteIndex];
                if (note.Grade != JudgmentGrade.Pending || !IsContactNote(note) || songTime < note.Time) continue;
                var coverageTime = LatestCoverageTime(note, songTime, contacts);
                if (coverageTime.HasValue && songTime - note.Time <= SustainLateWindow)
                    Register(note, JudgmentGrade.Perfect, coverageTime.Value - note.Time, output);
            }
        }

        void RecordContactPaths(double songTime, IReadOnlyList<ContactPathSegment> contactPaths)
        {
            if (contactPaths != null)
                for (var index = 0; index < contactPaths.Count; index++)
                {
                    var path = contactPaths[index];
                    if (path.EndTime >= path.StartTime) recentContactPaths.Add(path);
                }
            var oldestRelevantTime = songTime - SustainLookbackWindow;
            for (var index = recentContactPaths.Count - 1; index >= 0; index--)
                if (recentContactPaths[index].EndTime < oldestRelevantTime)
                    recentContactPaths.RemoveAt(index);
        }

        double? LatestCoverageTime(RuntimeNote note, double songTime, IReadOnlyList<ActiveContact> contacts)
        {
            double? latest = null;
            if (contacts != null)
                for (var index = 0; index < contacts.Count; index++)
                {
                    var contact = contacts[index];
                    if (!LaneMatches(note, contact.Lane) || contact.StartTime > songTime) continue;
                    latest = songTime;
                    break;
                }
            var earliest = note.Time - SustainLookbackWindow;
            foreach (var path in recentContactPaths)
            {
                var coveredAt = LastIntersectionTime(path, note, earliest, songTime);
                if (coveredAt.HasValue && (!latest.HasValue || coveredAt.Value > latest.Value)) latest = coveredAt;
            }
            return latest;
        }

        void CommitMisses(double songTime, List<JudgmentEvent> output)
        {
            dueMissWorkspace.Clear();
            CollectDueMisses(discreteMissNotes, ref discreteMissCursor, songTime, AttackWindow);
            CollectDueMisses(contactNotes, ref contactMissCursor, songTime, SustainLateWindow);
            StableSortNotes(dueMissWorkspace);
            for (var noteIndex = 0; noteIndex < dueMissWorkspace.Count; noteIndex++)
            {
                var note = dueMissWorkspace[noteIndex];
                Register(note, JudgmentGrade.Miss, songTime - note.Time, output);
            }
        }

        void CollectDueMisses(RuntimeNote[] indexedNotes, ref int cursor, double songTime, double lateWindow)
        {
            while (cursor < indexedNotes.Length)
            {
                var note = indexedNotes[cursor];
                if (double.IsNaN(note.Time))
                {
                    cursor++;
                    continue;
                }
                if (!(songTime - note.Time > lateWindow + CommitGrace)) break;
                cursor++;
                if (note.Grade == JudgmentGrade.Pending) dueMissWorkspace.Add(note);
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

        static double? LastIntersectionTime(ContactPathSegment path, RuntimeNote note, double earliestTime, double latestTime)
        {
            var startTime = Math.Max(path.StartTime, earliestTime);
            var endTime = Math.Min(path.EndTime, latestTime);
            if (endTime < startTime) return null;

            var startLane = LaneAt(path, startTime);
            var endLane = LaneAt(path, endTime);
            var minimum = note.Lane - note.Size - LaneForgiveness;
            var maximum = note.Lane + note.Size + LaneForgiveness;
            var laneDelta = endLane - startLane;
            if (Math.Abs(laneDelta) < .0001f)
                return startLane >= minimum && startLane <= maximum ? endTime : null;

            var first = (minimum - startLane) / laneDelta;
            var second = (maximum - startLane) / laneDelta;
            var enter = Math.Max(0d, Math.Min(first, second));
            var exit = Math.Min(1d, Math.Max(first, second));
            return enter <= exit ? startTime + (endTime - startTime) * exit : null;
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

                    // CHUNITHM-style protection follows the physical input
                    // area, not only the visible note sprites. A rub can
                    // cross two adjacent playable regions whose artwork does
                    // not overlap, and that transition must retain protection.
                    var sharedMinimum = Math.Max(earlier.Lane - earlier.Size - LaneForgiveness,
                        later.Lane - later.Size - LaneForgiveness);
                    var sharedMaximum = Math.Min(earlier.Lane + earlier.Size + LaneForgiveness,
                        later.Lane + later.Size + LaneForgiveness);
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
                if (wrongHalf && pair.ContainsSharedLane(inputLane)) return true;
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

        void Register(RuntimeNote note, JudgmentGrade grade, double delta, List<JudgmentEvent> output,
            float? hitLane = null)
        {
            if (note.Grade != JudgmentGrade.Pending) return;
            note.Grade = grade;
            score.Register(grade);
            output.Add(new JudgmentEvent(note, grade, delta, hitLane));
        }

        readonly struct Edge
        {
            public readonly int InputIndex;
            public readonly RuntimeNote Note;
            public readonly double EventTime;
            public readonly JudgmentGrade Grade;
            public readonly double TimeError;
            public readonly double SpaceError;
            public readonly float HitLane;

            public Edge(int inputIndex, RuntimeNote note, double eventTime, JudgmentGrade grade,
                double timeError, double spaceError, float hitLane)
            {
                InputIndex = inputIndex; Note = note; EventTime = eventTime; Grade = grade;
                TimeError = timeError; SpaceError = spaceError; HitLane = hitLane;
            }
        }
    }
}
