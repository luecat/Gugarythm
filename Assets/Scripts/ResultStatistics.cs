using System;
using System.Collections.Generic;

namespace Gugarhythm
{
    public enum ResultNoteCategory
    {
        Tap,
        Critical,
        Flick,
        Hold,
        Trace,
        Damage,
    }

    public sealed class ResultNoteCategoryStats
    {
        public ResultNoteCategory Category { get; }
        public int Perfect { get; private set; }
        public int Great { get; private set; }
        public int Good { get; private set; }
        public int Miss { get; private set; }
        public int Fast { get; private set; }
        public int Late { get; private set; }
        public double AccuracyNumerator { get; private set; }
        public int Count => Perfect + Great + Good + Miss;
        public double AccuracyPercent => Count <= 0 ? 0 : AccuracyNumerator / Count * 100;

        public ResultNoteCategoryStats(ResultNoteCategory category) => Category = category;

        public void Register(JudgmentEvent judgment)
        {
            switch (judgment.Grade)
            {
                case JudgmentGrade.Perfect:
                    Perfect++;
                    AccuracyNumerator += 1.01;
                    break;
                case JudgmentGrade.Great:
                    Great++;
                    AccuracyNumerator += 1.00;
                    break;
                case JudgmentGrade.Good:
                    Good++;
                    AccuracyNumerator += .50;
                    break;
                case JudgmentGrade.Miss:
                    Miss++;
                    break;
                default:
                    return;
            }

            var timing = JudgmentTimingClassifier.Classify(judgment.Grade, judgment.Delta);
            if (timing == JudgmentTiming.Fast) Fast++;
            else if (timing == JudgmentTiming.Late) Late++;
        }
    }

    public sealed class ResultTimingHistogram
    {
        public const int RangeMilliseconds = 100;
        public const int BinWidthMilliseconds = 5;
        public const int BinCount = RangeMilliseconds * 2 / BinWidthMilliseconds;

        readonly int[] counts = new int[BinCount];

        public IReadOnlyList<int> Counts => counts;
        public int MaxCount { get; private set; }

        public void Reset()
        {
            Array.Clear(counts, 0, counts.Length);
            MaxCount = 0;
        }

        public void Register(JudgmentEvent judgment)
        {
            if (judgment.Grade is not (JudgmentGrade.Perfect or JudgmentGrade.Great or JudgmentGrade.Good))
                return;

            var ms = judgment.Delta * 1000d;
            if (ms < -RangeMilliseconds || ms >= RangeMilliseconds) return;
            var bin = (int)Math.Floor((ms + RangeMilliseconds) / BinWidthMilliseconds);
            if ((uint)bin >= BinCount) return;
            counts[bin]++;
            if (counts[bin] > MaxCount) MaxCount = counts[bin];
        }
    }

    public sealed class ResultRunStatistics
    {
        static readonly ResultNoteCategory[] CategoryOrder =
        {
            ResultNoteCategory.Tap,
            ResultNoteCategory.Critical,
            ResultNoteCategory.Flick,
            ResultNoteCategory.Hold,
            ResultNoteCategory.Trace,
            ResultNoteCategory.Damage,
        };

        readonly Dictionary<ResultNoteCategory, ResultNoteCategoryStats> byCategory = new();
        readonly ResultTimingHistogram histogram = new();

        public ResultTimingHistogram Histogram => histogram;
        public IReadOnlyList<ResultNoteCategory> OrderedCategories => CategoryOrder;

        public void Reset()
        {
            byCategory.Clear();
            histogram.Reset();
        }

        public void Build(IReadOnlyList<JudgmentEvent> events)
        {
            Reset();
            if (events == null) return;
            for (var index = 0; index < events.Count; index++)
            {
                var judgment = events[index];
                if (judgment.Note == null || judgment.Grade == JudgmentGrade.Pending) continue;
                histogram.Register(judgment);
                var category = Classify(judgment.Note);
                if (!byCategory.TryGetValue(category, out var stats))
                {
                    stats = new ResultNoteCategoryStats(category);
                    byCategory[category] = stats;
                }
                stats.Register(judgment);
            }
        }

        public bool TryGet(ResultNoteCategory category, out ResultNoteCategoryStats stats) =>
            byCategory.TryGetValue(category, out stats);

        public static ResultNoteCategory Classify(RuntimeNote note)
        {
            if (note == null) return ResultNoteCategory.Tap;
            if (note.IsDamageArchetype) return ResultNoteCategory.Damage;
            if (note.IsTraceArchetype) return ResultNoteCategory.Trace;
            if (note.Critical) return ResultNoteCategory.Critical;
            if (note.Kind == RuntimeNoteKind.Flick) return ResultNoteCategory.Flick;
            if (IsHoldJudgment(note)) return ResultNoteCategory.Hold;
            return ResultNoteCategory.Tap;
        }

        public static string DisplayName(ResultNoteCategory category) => category switch
        {
            ResultNoteCategory.Tap => "Tap",
            ResultNoteCategory.Critical => "Critical",
            ResultNoteCategory.Flick => "Flick",
            ResultNoteCategory.Hold => "Hold",
            ResultNoteCategory.Trace => "Trace",
            ResultNoteCategory.Damage => "Damage",
            _ => category.ToString(),
        };

        static bool IsHoldJudgment(RuntimeNote note) =>
            note.IsHoldMidArchetype ||
            note.IsHoldTerminal ||
            note.HoldCheckpointSource != HoldCheckpointSource.None ||
            note.Kind is RuntimeNoteKind.Sustain or RuntimeNoteKind.Release;
    }
}
