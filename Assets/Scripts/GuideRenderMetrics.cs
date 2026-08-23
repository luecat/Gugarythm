namespace Gugarhythm
{
    public readonly struct GuideFrameSnapshot
    {
        public readonly int CandidateCount;
        public readonly int VisibleCount;
        public readonly int SampleCount;
        public readonly int VertexCount;
        public readonly int TriangleCount;
        public readonly int DirtyCount;
        public readonly float MeshBuildMilliseconds;

        public GuideFrameSnapshot(int candidateCount, int visibleCount, int sampleCount, int vertexCount,
            int triangleCount, int dirtyCount, float meshBuildMilliseconds)
        {
            CandidateCount = candidateCount;
            VisibleCount = visibleCount;
            SampleCount = sampleCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            DirtyCount = dirtyCount;
            MeshBuildMilliseconds = meshBuildMilliseconds;
        }

        public bool HasValidGeometry =>
            CandidateCount >= 0 && VisibleCount >= 0 && VisibleCount <= CandidateCount &&
            SampleCount >= 0 && VertexCount == SampleCount * 2 &&
            TriangleCount == System.Math.Max(0, SampleCount - VisibleCount) * 2;
    }

    // Per-frame presentation telemetry.  This is intentionally separate from
    // timing and judgment state so enabling the HUD cannot affect gameplay.
    public sealed class GuideRenderMetrics
    {
        public int CandidateCount { get; private set; }
        public int VisibleCount { get; private set; }
        public int SampleCount { get; private set; }
        public int VertexCount { get; private set; }
        public int TriangleCount { get; private set; }
        public int DirtyCount { get; private set; }
        public float MeshBuildMilliseconds { get; private set; }

        public void Reset()
        {
            CandidateCount = 0;
            VisibleCount = 0;
            SampleCount = 0;
            VertexCount = 0;
            TriangleCount = 0;
            DirtyCount = 0;
            MeshBuildMilliseconds = 0;
        }

        public void SetCandidateCount(int count) => CandidateCount = count < 0 ? 0 : count;

        public void RecordGuide(int samples, int vertices, int triangles)
        {
            VisibleCount++;
            SampleCount += samples < 0 ? 0 : samples;
            VertexCount += vertices < 0 ? 0 : vertices;
            TriangleCount += triangles < 0 ? 0 : triangles;
        }

        public void SetDirtyCount(int count) => DirtyCount = count < 0 ? 0 : count;
        public void SetMeshBuildMilliseconds(float milliseconds) => MeshBuildMilliseconds = milliseconds < 0 ? 0 : milliseconds;

        public GuideFrameSnapshot Snapshot() => new(CandidateCount, VisibleCount, SampleCount, VertexCount,
            TriangleCount, DirtyCount, MeshBuildMilliseconds);
    }
}
