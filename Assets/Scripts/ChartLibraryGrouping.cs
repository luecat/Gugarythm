using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Gugarythm
{
    public enum ChartLibrarySort { Accuracy, Difficulty, Title }

    public sealed class LocalChartGroup
    {
        public string GroupId { get; }
        public string Title { get; }
        public string Artist { get; }
        public IReadOnlyList<LocalChartEntry> Difficulties { get; }

        public LocalChartGroup(string groupId, string title, string artist, IReadOnlyList<LocalChartEntry> difficulties)
        {
            GroupId = groupId;
            Title = title;
            Artist = artist;
            Difficulties = difficulties;
        }

        public LocalChartEntry FindDifficulty(string name) => Difficulties.FirstOrDefault(entry =>
            string.Equals(entry.DifficultyName ?? string.Empty, name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    public static class ChartLibraryGrouping
    {
        public static IReadOnlyList<LocalChartGroup> Group(IReadOnlyList<LocalChartEntry> entries)
        {
            if (entries == null) return Array.Empty<LocalChartGroup>();
            return entries.Where(entry => entry != null)
                .GroupBy(entry => entry.GroupId ?? string.Empty, StringComparer.Ordinal)
                .Select(group =>
                {
                    var ordered = group.OrderByDescending(entry => entry.ImportedAtUnixMilliseconds).ToList();
                    var first = ordered[0];
                    return new LocalChartGroup(group.Key, first.Title ?? string.Empty, first.Artist ?? string.Empty, ordered);
                })
                .ToList();
        }

        public static IReadOnlyList<LocalChartGroup> Sort(IReadOnlyList<LocalChartGroup> groups, ChartLibrarySort sort, bool ascending, string difficultyName)
        {
            if (groups == null) return Array.Empty<LocalChartGroup>();
            var matching = groups.Where(group => group.FindDifficulty(difficultyName) != null);
            var remaining = groups.Where(group => group.FindDifficulty(difficultyName) == null);
            return SortPartition(matching, sort, ascending, difficultyName)
                .Concat(SortPartition(remaining, sort, ascending, difficultyName)).ToList();
        }

        static IEnumerable<LocalChartGroup> SortPartition(IEnumerable<LocalChartGroup> groups, ChartLibrarySort sort, bool ascending, string difficultyName)
        {
            Func<LocalChartGroup, IComparable> key = sort switch
            {
                ChartLibrarySort.Accuracy => group => group.FindDifficulty(difficultyName)?.BestAccuracy ?? -1f,
                ChartLibrarySort.Difficulty => group => DifficultyRank(group.FindDifficulty(difficultyName)?.DifficultyLevel),
                _ => group => group.Title ?? string.Empty,
            };
            return ascending ? groups.OrderBy(key).ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase) :
                groups.OrderByDescending(key).ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase);
        }

        static float DifficultyRank(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1f;
            var digits = new string(value.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
            return float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : -1f;
        }
    }
}
