using System;
using UnityEngine;

namespace Gugarhythm
{
    /// <summary>
    /// Keeps the chart selected in the library alive while Unity changes scenes.
    /// Prefers transferring an already-parsed <see cref="RuntimeChart"/> and decoded
    /// <see cref="AudioClip"/> so gameplay does not re-read / re-import / re-decode.
    /// Falls back to owning a copy of the raw GGR bytes when prepared assets are unavailable.
    /// </summary>
    public sealed class ChartSelectionSession : MonoBehaviour
    {
        static ChartSelectionSession instance;

        LocalChartEntry selectedEntry;
        byte[] selectedGgrBytes;
        RuntimeChart preparedChart;
        string preparedAudioCachePath;
        string preparedAudioExtension;
        double preparedLeadingSilenceSeconds;
        AudioClip preparedMusic;
        bool ownsPreparedMusic;

        public string DraftTitle { get; private set; }
        public string DraftArtist { get; private set; }
        public string DraftTag { get; private set; }
        public string DraftLevel { get; private set; }
        public bool ReturnToEditor { get; private set; }

        public bool HasPreparedChart => preparedChart != null;
        public bool HasSelection =>
            selectedEntry != null && (preparedChart != null || selectedGgrBytes is { Length: > 0 });

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

            ClearPreparedAssets();
            selectedEntry = CopyEntry(entry);
            selectedGgrBytes = CopyBytes(ggrBytes);
            return true;
        }

        /// <summary>
        /// Stores an already-loaded chart for the next gameplay scene. Optionally transfers
        /// ownership of a decoded <paramref name="music"/> clip (caller must null its own
        /// reference after a successful call). Raw GGR bytes are not required.
        /// </summary>
        public bool SetPreparedSelection(
            LocalChartEntry entry,
            RuntimeChart chart,
            string audioCachePath,
            string audioExtension,
            double leadingSilenceSeconds,
            AudioClip music)
        {
            if (entry == null || chart == null)
            {
                Clear();
                return false;
            }

            ClearPreparedAssets();
            if (selectedGgrBytes != null)
            {
                Array.Clear(selectedGgrBytes, 0, selectedGgrBytes.Length);
                selectedGgrBytes = null;
            }

            selectedEntry = CopyEntry(entry);
            preparedChart = chart;
            preparedAudioCachePath = audioCachePath ?? string.Empty;
            preparedAudioExtension = audioExtension ?? string.Empty;
            preparedLeadingSilenceSeconds = double.IsFinite(leadingSilenceSeconds) ? leadingSilenceSeconds : 0;
            preparedMusic = music;
            ownsPreparedMusic = music != null;
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
            ggrBytes = selectedGgrBytes != null ? CopyBytes(selectedGgrBytes) : null;
            return true;
        }

        /// <summary>
        /// Takes prepared chart/audio for gameplay. Music ownership transfers to the caller
        /// when a clip is returned; the session will not destroy it afterwards.
        /// </summary>
        public bool TryTakePreparedAssets(
            out RuntimeChart chart,
            out AudioClip music,
            out string audioCachePath,
            out string audioExtension,
            out double leadingSilenceSeconds)
        {
            if (preparedChart == null)
            {
                chart = null;
                music = null;
                audioCachePath = null;
                audioExtension = null;
                leadingSilenceSeconds = 0;
                return false;
            }

            chart = preparedChart;
            music = preparedMusic;
            audioCachePath = preparedAudioCachePath;
            audioExtension = preparedAudioExtension;
            leadingSilenceSeconds = preparedLeadingSilenceSeconds;
            preparedChart = null;
            preparedMusic = null;
            ownsPreparedMusic = false;
            preparedAudioCachePath = null;
            preparedAudioExtension = null;
            preparedLeadingSilenceSeconds = 0;
            return true;
        }

        public void Clear()
        {
            selectedEntry = null;
            if (selectedGgrBytes != null) Array.Clear(selectedGgrBytes, 0, selectedGgrBytes.Length);
            selectedGgrBytes = null;
            ClearPreparedAssets();
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

        void ClearPreparedAssets()
        {
            preparedChart = null;
            preparedAudioCachePath = null;
            preparedAudioExtension = null;
            preparedLeadingSilenceSeconds = 0;
            if (ownsPreparedMusic && preparedMusic != null)
                Destroy(preparedMusic);
            preparedMusic = null;
            ownsPreparedMusic = false;
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
