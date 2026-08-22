using System;
using UnityEngine;

namespace Gugarhythm
{
    /// <summary>
    /// Keeps the chart selected in the library alive while Unity changes scenes.
    /// The session owns copies of both values so callers cannot mutate its state
    /// after selection or through a value returned by <see cref="TryGetSelection"/>.
    /// </summary>
    public sealed class ChartSelectionSession : MonoBehaviour
    {
        static ChartSelectionSession instance;

        LocalChartEntry selectedEntry;
        byte[] selectedGgrBytes;
        public string DraftTitle { get; private set; }
        public string DraftArtist { get; private set; }
        public string DraftTag { get; private set; }
        public string DraftLevel { get; private set; }
        public bool ReturnToEditor { get; private set; }

        public bool HasSelection => selectedEntry != null && selectedGgrBytes is { Length: > 0 };

        public static ChartSelectionSession Ensure()
        {
            if (instance != null) return instance;

            instance = FindFirstObjectByType<ChartSelectionSession>();
            if (instance != null) return instance;

            var sessionObject = new GameObject(nameof(ChartSelectionSession));
            instance = sessionObject.AddComponent<ChartSelectionSession>();
            return instance;
        }

        public bool SetSelection(LocalChartEntry entry, byte[] ggrBytes)
        {
            if (entry == null || ggrBytes == null || ggrBytes.Length == 0)
            {
                Clear();
                return false;
            }

            selectedEntry = CopyEntry(entry);
            selectedGgrBytes = CopyBytes(ggrBytes);
            return true;
        }

        public bool TryGetSelection(out LocalChartEntry entry, out byte[] ggrBytes)
        {
            if (!HasSelection)
            {
                entry = null;
                ggrBytes = null;
                return false;
            }

            entry = CopyEntry(selectedEntry);
            ggrBytes = CopyBytes(selectedGgrBytes);
            return true;
        }

        public void Clear()
        {
            selectedEntry = null;
            if (selectedGgrBytes != null) Array.Clear(selectedGgrBytes, 0, selectedGgrBytes.Length);
            selectedGgrBytes = null;
        }

        public void SetEditorDraft(string title, string artist, string tag, string level)
        {
            DraftTitle = title ?? string.Empty; DraftArtist = artist ?? string.Empty; DraftTag = tag ?? string.Empty; DraftLevel = level ?? string.Empty; ReturnToEditor = true;
        }

        public bool TryGetEditorDraft(out string title, out string artist, out string tag, out string level)
        {
            title = DraftTitle; artist = DraftArtist; tag = DraftTag; level = DraftLevel; return ReturnToEditor;
        }

        public void ClearEditorDraft() { DraftTitle = DraftArtist = DraftTag = DraftLevel = string.Empty; ReturnToEditor = false; }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
            Clear();
        }

        static byte[] CopyBytes(byte[] source)
        {
            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }

        static LocalChartEntry CopyEntry(LocalChartEntry source) => new()
        {
            Id = source.Id,
            Title = source.Title,
            Artist = source.Artist,
            Author = source.Author,
            DifficultyName = source.DifficultyName,
            DifficultyLevel = source.DifficultyLevel,
            GroupId = source.GroupId,
            BestAccuracy = source.BestAccuracy,
            Format = source.Format,
            SourceFile = source.SourceFile,
            NoteCount = source.NoteCount,
            ImportedAtUnixMilliseconds = source.ImportedAtUnixMilliseconds,
        };
    }
}
