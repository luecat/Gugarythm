using System;
using System.Collections.Generic;

namespace Gugarythm
{
    public enum RuntimeNoteKind { Tap, Flick, Sustain, Release }
    public enum JudgmentGrade { Pending, Perfect, Great, Good, Miss }

    [Serializable]
    public sealed class RuntimeNote
    {
        public int Index;
        public string SourceId;
        public string Archetype;
        public double Time;
        public double Beat;
        public float Lane;
        public float Size = 1;
        public int Direction;
        public RuntimeNoteKind Kind;
        public bool Critical;
        public JudgmentGrade Grade;
        public bool Visible = true;
    }

    [Serializable]
    public sealed class RuntimeConnector
    {
        public RuntimeNote Start;
        public RuntimeNote End;
        public bool Critical;
        public int Ease;
    }

    [Serializable]
    public struct RuntimeGuidePoint
    {
        public double Time;
        public double Beat;
        public float Lane;
        public float Size;
    }

    [Serializable]
    public sealed class RuntimeGuide
    {
        public RuntimeGuidePoint Start;
        public RuntimeGuidePoint Head;
        public RuntimeGuidePoint Tail;
        public RuntimeGuidePoint End;
        public int Color;
        public int Fade;
        public int Ease;
        public bool FadeOut;
        public float HeadOpacity = 1;
        public float TailOpacity = 1;
    }

    [Serializable]
    public sealed class RuntimeSimLine
    {
        public RuntimeNote A;
        public RuntimeNote B;
    }

    [Serializable]
    public sealed class RuntimeChart
    {
        public string SourceFormat;
        public string Title = "Untitled";
        public string Artist = "";
        public string Author = "";
        public string Engine = "";
        public double BgmOffset;
        public byte[] BgmBytes;
        public string BgmExtension = ".mp3";
        public string ReferencedBgm;
        public byte[] CoverBytes;
        public readonly List<RuntimeNote> Notes = new();
        public readonly List<RuntimeConnector> Connectors = new();
        // SimLine is a visual-only synchronization link between notes. It is
        // neither a playable hold nor an engine decoration guide.
        public readonly List<RuntimeSimLine> SimLines = new();
        // Engine guides are visual-only ribbons. They may extend beyond the
        // playable lane range and must never be judged as slide connectors.
        public readonly List<RuntimeGuide> Guides = new();
        public readonly List<string> Warnings = new();

        public int PlayableCount => Notes.Count;
        public double LastNoteTime => Notes.Count == 0 ? 0 : Notes[^1].Time;
    }

    public sealed class ImportResult
    {
        public RuntimeChart Chart;
        public string Error;
        public bool Success => Chart != null && string.IsNullOrEmpty(Error);

        public static ImportResult Ok(RuntimeChart chart) => new() { Chart = chart };
        public static ImportResult Fail(string error) => new() { Error = error };
    }

    public interface IChartImporter
    {
        bool CanImport(string fileName, byte[] header);
        ImportResult Import(string fileName, byte[] data, IReadOnlyDictionary<string, byte[]> companionFiles = null);
    }
}
