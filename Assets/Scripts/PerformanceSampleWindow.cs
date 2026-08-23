using System;

namespace Gugarhythm
{
    public readonly struct PerformanceSnapshot
    {
        public readonly int SampleCount;
        public readonly float CurrentFps;
        public readonly float AverageFps;
        public readonly float MinimumFps;

        public PerformanceSnapshot(int sampleCount, float currentFps, float averageFps, float minimumFps)
        {
            SampleCount = sampleCount;
            CurrentFps = currentFps;
            AverageFps = averageFps;
            MinimumFps = minimumFps;
        }
    }

    public sealed class PerformanceSampleWindow
    {
        readonly float[] frameDurations;
        readonly float maximumDuration;
        int firstIndex;
        int count;
        float totalDuration;
        float longestDuration;
        float latestDuration;

        public PerformanceSampleWindow(int capacity, float maximumDurationSeconds = float.PositiveInfinity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (float.IsNaN(maximumDurationSeconds) || maximumDurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumDurationSeconds));
            frameDurations = new float[capacity];
            maximumDuration = maximumDurationSeconds;
        }

        public void AddFrame(float durationSeconds)
        {
            if (!float.IsFinite(durationSeconds) || durationSeconds <= 0) return;

            latestDuration = durationSeconds;
            if (count == frameDurations.Length)
                RemoveOldest();

            var insertionIndex = (firstIndex + count) % frameDurations.Length;
            frameDurations[insertionIndex] = durationSeconds;
            count++;
            totalDuration += durationSeconds;
            if (durationSeconds > longestDuration)
                longestDuration = durationSeconds;

            while (count > 1 && totalDuration > maximumDuration)
                RemoveOldest();
        }

        void RemoveOldest()
        {
            var removed = frameDurations[firstIndex];
            frameDurations[firstIndex] = 0;
            firstIndex = (firstIndex + 1) % frameDurations.Length;
            count--;
            totalDuration -= removed;
            if (removed < longestDuration) return;

            longestDuration = 0;
            for (var index = 0; index < count; index++)
            {
                var duration = frameDurations[(firstIndex + index) % frameDurations.Length];
                if (duration > longestDuration) longestDuration = duration;
            }
        }

        public PerformanceSnapshot Snapshot()
        {
            if (count == 0 || totalDuration <= 0 || latestDuration <= 0 || longestDuration <= 0)
                return default;
            return new PerformanceSnapshot(
                count,
                1f / latestDuration,
                count / totalDuration,
                1f / longestDuration);
        }

        public void Reset()
        {
            Array.Clear(frameDurations, 0, frameDurations.Length);
            count = 0;
            firstIndex = 0;
            totalDuration = 0;
            longestDuration = 0;
            latestDuration = 0;
        }
    }

    public sealed class FrameBudgetCounter
    {
        const float FrameBudget120Hz = 1f / 120f;
        const float FrameBudget60Hz = 1f / 60f;
        const float FrameBudget30Hz = 1f / 30f;

        public int Over120HzBudget { get; private set; }
        public int Over60HzBudget { get; private set; }
        public int Over30HzBudget { get; private set; }

        public void AddFrame(float durationSeconds)
        {
            if (!float.IsFinite(durationSeconds) || durationSeconds <= 0) return;
            if (durationSeconds > FrameBudget120Hz) Over120HzBudget++;
            if (durationSeconds > FrameBudget60Hz) Over60HzBudget++;
            if (durationSeconds > FrameBudget30Hz) Over30HzBudget++;
        }

        public void Reset()
        {
            Over120HzBudget = 0;
            Over60HzBudget = 0;
            Over30HzBudget = 0;
        }
    }

    public readonly struct TimingSnapshot
    {
        public readonly int SampleCount;
        public readonly float MaximumMilliseconds;
        public readonly float P95Milliseconds;
        public readonly float P99Milliseconds;

        public TimingSnapshot(int sampleCount, float maximumMilliseconds, float p95Milliseconds, float p99Milliseconds)
        {
            SampleCount = sampleCount;
            MaximumMilliseconds = maximumMilliseconds;
            P95Milliseconds = p95Milliseconds;
            P99Milliseconds = p99Milliseconds;
        }
    }

    public sealed class TimingSampleWindow
    {
        readonly float[] samples;
        readonly float[] elapsedDurations;
        readonly float[] sortedScratch;
        readonly float maximumDuration;
        int firstIndex;
        int count;
        float coveredDuration;

        public TimingSampleWindow(int capacity, float maximumDurationSeconds)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (!float.IsFinite(maximumDurationSeconds) || maximumDurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumDurationSeconds));
            samples = new float[capacity];
            elapsedDurations = new float[capacity];
            sortedScratch = new float[capacity];
            maximumDuration = maximumDurationSeconds;
        }

        public void AddSample(float milliseconds, float elapsedSeconds)
        {
            if (!float.IsFinite(milliseconds) || milliseconds < 0 ||
                !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return;

            if (count == samples.Length)
                RemoveOldest();

            var insertionIndex = (firstIndex + count) % samples.Length;
            samples[insertionIndex] = milliseconds;
            elapsedDurations[insertionIndex] = elapsedSeconds;
            count++;
            coveredDuration += elapsedSeconds;

            while (count > 1 && coveredDuration > maximumDuration)
                RemoveOldest();
        }

        void RemoveOldest()
        {
            coveredDuration -= elapsedDurations[firstIndex];
            samples[firstIndex] = 0;
            elapsedDurations[firstIndex] = 0;
            firstIndex = (firstIndex + 1) % samples.Length;
            count--;
        }

        public TimingSnapshot Snapshot()
        {
            if (count == 0) return default;
            for (var index = 0; index < count; index++)
                sortedScratch[index] = samples[(firstIndex + index) % samples.Length];
            Array.Sort(sortedScratch, 0, count);
            var p95Index = Math.Max(0, (int)Math.Ceiling(count * .95) - 1);
            var p99Index = Math.Max(0, (int)Math.Ceiling(count * .99) - 1);
            return new TimingSnapshot(count, sortedScratch[count - 1], sortedScratch[p95Index], sortedScratch[p99Index]);
        }

        public void Reset()
        {
            Array.Clear(samples, 0, samples.Length);
            Array.Clear(elapsedDurations, 0, elapsedDurations.Length);
            Array.Clear(sortedScratch, 0, sortedScratch.Length);
            firstIndex = 0;
            count = 0;
            coveredDuration = 0;
        }
    }

    public readonly struct GameplayTimingSnapshot
    {
        public readonly TimingSnapshot Total;
        public readonly TimingSnapshot Notes;
        public readonly TimingSnapshot Holds;
        public readonly TimingSnapshot Guides;
        public readonly TimingSnapshot SimLines;
        public readonly TimingSnapshot Other;

        public GameplayTimingSnapshot(TimingSnapshot total, TimingSnapshot notes, TimingSnapshot holds,
            TimingSnapshot guides, TimingSnapshot simLines, TimingSnapshot other)
        {
            Total = total;
            Notes = notes;
            Holds = holds;
            Guides = guides;
            SimLines = simLines;
            Other = other;
        }
    }

    public sealed class GameplayTimingSampleSet
    {
        readonly TimingSampleWindow total;
        readonly TimingSampleWindow notes;
        readonly TimingSampleWindow holds;
        readonly TimingSampleWindow guides;
        readonly TimingSampleWindow simLines;
        readonly TimingSampleWindow other;

        public GameplayTimingSampleSet(int capacity, float maximumDurationSeconds)
        {
            total = new TimingSampleWindow(capacity, maximumDurationSeconds);
            notes = new TimingSampleWindow(capacity, maximumDurationSeconds);
            holds = new TimingSampleWindow(capacity, maximumDurationSeconds);
            guides = new TimingSampleWindow(capacity, maximumDurationSeconds);
            simLines = new TimingSampleWindow(capacity, maximumDurationSeconds);
            other = new TimingSampleWindow(capacity, maximumDurationSeconds);
        }

        public void AddFrame(float totalMilliseconds, float notesMilliseconds, float holdsMilliseconds,
            float guidesMilliseconds, float simLinesMilliseconds, float elapsedSeconds)
        {
            if (!IsValid(totalMilliseconds) || !IsValid(notesMilliseconds) || !IsValid(holdsMilliseconds) ||
                !IsValid(guidesMilliseconds) || !IsValid(simLinesMilliseconds) ||
                !float.IsFinite(elapsedSeconds) || elapsedSeconds <= 0) return;

            var otherMilliseconds = Math.Max(0,
                totalMilliseconds - notesMilliseconds - holdsMilliseconds - guidesMilliseconds - simLinesMilliseconds);
            total.AddSample(totalMilliseconds, elapsedSeconds);
            notes.AddSample(notesMilliseconds, elapsedSeconds);
            holds.AddSample(holdsMilliseconds, elapsedSeconds);
            guides.AddSample(guidesMilliseconds, elapsedSeconds);
            simLines.AddSample(simLinesMilliseconds, elapsedSeconds);
            other.AddSample(otherMilliseconds, elapsedSeconds);
        }

        static bool IsValid(float value) => float.IsFinite(value) && value >= 0;

        public GameplayTimingSnapshot Snapshot() => new(
            total.Snapshot(), notes.Snapshot(), holds.Snapshot(), guides.Snapshot(), simLines.Snapshot(), other.Snapshot());

        public void Reset()
        {
            total.Reset();
            notes.Reset();
            holds.Reset();
            guides.Reset();
            simLines.Reset();
            other.Reset();
        }
    }

    public enum HotPathStage
    {
        TimeScalePositionAt,
        GuideClipping,
        GuideTessellation,
        GuideProjection,
        GuideMeshWrite,
        HoldTessellation,
        HoldProjection,
        HoldMeshWrite,
    }

    public readonly struct HotPathStageSnapshot
    {
        public readonly int Calls;
        public readonly float ElapsedMilliseconds;

        public HotPathStageSnapshot(int calls, float elapsedMilliseconds)
        {
            Calls = calls;
            ElapsedMilliseconds = elapsedMilliseconds;
        }
    }

    public readonly struct HotPathFrameSnapshot
    {
        public readonly HotPathStageSnapshot TimeScalePositionAt;
        public readonly HotPathStageSnapshot GuideClipping;
        public readonly HotPathStageSnapshot GuideTessellation;
        public readonly HotPathStageSnapshot GuideProjection;
        public readonly HotPathStageSnapshot GuideMeshWrite;
        public readonly HotPathStageSnapshot HoldTessellation;
        public readonly HotPathStageSnapshot HoldProjection;
        public readonly HotPathStageSnapshot HoldMeshWrite;
        public readonly int TimeScaleSearchSteps;

        public HotPathFrameSnapshot(HotPathStageSnapshot timeScalePositionAt, HotPathStageSnapshot guideClipping,
            HotPathStageSnapshot guideTessellation, HotPathStageSnapshot guideProjection,
            HotPathStageSnapshot guideMeshWrite, HotPathStageSnapshot holdTessellation,
            HotPathStageSnapshot holdProjection, HotPathStageSnapshot holdMeshWrite, int timeScaleSearchSteps)
        {
            TimeScalePositionAt = timeScalePositionAt;
            GuideClipping = guideClipping;
            GuideTessellation = guideTessellation;
            GuideProjection = guideProjection;
            GuideMeshWrite = guideMeshWrite;
            HoldTessellation = holdTessellation;
            HoldProjection = holdProjection;
            HoldMeshWrite = holdMeshWrite;
            TimeScaleSearchSteps = Math.Max(0, timeScaleSearchSteps);
        }
    }

    // This frame-local collector owns the mutable counters.  Its snapshots are
    // value types so HUD rendering cannot combine numbers from different frames.
    public sealed class HotPathFrameMetrics
    {
        readonly int[] calls = new int[8];
        readonly float[] elapsedMilliseconds = new float[8];
        int timeScaleSearchSteps;

        public void Reset()
        {
            Array.Clear(calls, 0, calls.Length);
            Array.Clear(elapsedMilliseconds, 0, elapsedMilliseconds.Length);
            timeScaleSearchSteps = 0;
        }

        public void Record(HotPathStage stage, float milliseconds, int callCount = 1)
        {
            var index = (int)stage;
            if (index < 0 || index >= calls.Length) return;
            calls[index] += Math.Max(0, callCount);
            if (float.IsFinite(milliseconds) && milliseconds >= 0) elapsedMilliseconds[index] += milliseconds;
        }

        public void SetTimeScaleSearchSteps(int value) => timeScaleSearchSteps = Math.Max(0, value);

        public HotPathFrameSnapshot Snapshot() => new(
            At(HotPathStage.TimeScalePositionAt), At(HotPathStage.GuideClipping),
            At(HotPathStage.GuideTessellation), At(HotPathStage.GuideProjection),
            At(HotPathStage.GuideMeshWrite), At(HotPathStage.HoldTessellation),
            At(HotPathStage.HoldProjection), At(HotPathStage.HoldMeshWrite), timeScaleSearchSteps);

        HotPathStageSnapshot At(HotPathStage stage)
        {
            var index = (int)stage;
            return new HotPathStageSnapshot(calls[index], elapsedMilliseconds[index]);
        }
    }

    public readonly struct HotPathTimingSnapshot
    {
        public readonly TimingSnapshot TimeScalePositionAt;
        public readonly TimingSnapshot GuideClipping;
        public readonly TimingSnapshot GuideTessellation;
        public readonly TimingSnapshot GuideProjection;
        public readonly TimingSnapshot GuideMeshWrite;
        public readonly TimingSnapshot HoldTessellation;
        public readonly TimingSnapshot HoldProjection;
        public readonly TimingSnapshot HoldMeshWrite;

        public HotPathTimingSnapshot(TimingSnapshot timeScalePositionAt, TimingSnapshot guideClipping,
            TimingSnapshot guideTessellation, TimingSnapshot guideProjection, TimingSnapshot guideMeshWrite,
            TimingSnapshot holdTessellation, TimingSnapshot holdProjection, TimingSnapshot holdMeshWrite)
        {
            TimeScalePositionAt = timeScalePositionAt;
            GuideClipping = guideClipping;
            GuideTessellation = guideTessellation;
            GuideProjection = guideProjection;
            GuideMeshWrite = guideMeshWrite;
            HoldTessellation = holdTessellation;
            HoldProjection = holdProjection;
            HoldMeshWrite = holdMeshWrite;
        }
    }

    public sealed class HotPathTimingSampleSet
    {
        readonly TimingSampleWindow timeScalePositionAt;
        readonly TimingSampleWindow guideClipping;
        readonly TimingSampleWindow guideTessellation;
        readonly TimingSampleWindow guideProjection;
        readonly TimingSampleWindow guideMeshWrite;
        readonly TimingSampleWindow holdTessellation;
        readonly TimingSampleWindow holdProjection;
        readonly TimingSampleWindow holdMeshWrite;

        public HotPathTimingSampleSet(int capacity, float maximumDurationSeconds)
        {
            timeScalePositionAt = new TimingSampleWindow(capacity, maximumDurationSeconds);
            guideClipping = new TimingSampleWindow(capacity, maximumDurationSeconds);
            guideTessellation = new TimingSampleWindow(capacity, maximumDurationSeconds);
            guideProjection = new TimingSampleWindow(capacity, maximumDurationSeconds);
            guideMeshWrite = new TimingSampleWindow(capacity, maximumDurationSeconds);
            holdTessellation = new TimingSampleWindow(capacity, maximumDurationSeconds);
            holdProjection = new TimingSampleWindow(capacity, maximumDurationSeconds);
            holdMeshWrite = new TimingSampleWindow(capacity, maximumDurationSeconds);
        }

        public void AddFrame(HotPathFrameSnapshot frame, float elapsedSeconds)
        {
            timeScalePositionAt.AddSample(frame.TimeScalePositionAt.ElapsedMilliseconds, elapsedSeconds);
            guideClipping.AddSample(frame.GuideClipping.ElapsedMilliseconds, elapsedSeconds);
            guideTessellation.AddSample(frame.GuideTessellation.ElapsedMilliseconds, elapsedSeconds);
            guideProjection.AddSample(frame.GuideProjection.ElapsedMilliseconds, elapsedSeconds);
            guideMeshWrite.AddSample(frame.GuideMeshWrite.ElapsedMilliseconds, elapsedSeconds);
            holdTessellation.AddSample(frame.HoldTessellation.ElapsedMilliseconds, elapsedSeconds);
            holdProjection.AddSample(frame.HoldProjection.ElapsedMilliseconds, elapsedSeconds);
            holdMeshWrite.AddSample(frame.HoldMeshWrite.ElapsedMilliseconds, elapsedSeconds);
        }

        public HotPathTimingSnapshot Snapshot() => new(
            timeScalePositionAt.Snapshot(), guideClipping.Snapshot(), guideTessellation.Snapshot(),
            guideProjection.Snapshot(), guideMeshWrite.Snapshot(), holdTessellation.Snapshot(),
            holdProjection.Snapshot(), holdMeshWrite.Snapshot());

        public void Reset()
        {
            timeScalePositionAt.Reset();
            guideClipping.Reset();
            guideTessellation.Reset();
            guideProjection.Reset();
            guideMeshWrite.Reset();
            holdTessellation.Reset();
            holdProjection.Reset();
            holdMeshWrite.Reset();
        }
    }
}
