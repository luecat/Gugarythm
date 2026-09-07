using System;
using UnityEngine;
using UnityEngine.UI;

namespace Gugarhythm
{
    // Batches judgment hit-burst effects (HitBurstGraphic.Draw) into a single
    // CanvasRenderer. Chords used to spawn one GameObject + CanvasRenderer +
    // coroutine per simultaneous hit via SpawnHitParticle; every one of those
    // dirtied the shared gameplay Canvas every frame for its ~0.3s lifetime,
    // so dense chords multiplied Canvas rebuild cost directly by the number
    // of notes hit in the same frame. This mirrors NoteParticleBatchGraphic's
    // approach but the mesh always changes while any burst is alive (each
    // burst's progress keeps advancing every frame), so there is no
    // frame-hash dedupe here — only "no active bursts" skips work entirely.
    public sealed class HitBurstBatchGraphic : MaskableGraphic
    {
        struct Burst
        {
            public Vector2 Center;
            public float UpperWidth;
            public float Elapsed;
            public HitParticleEffectMode EffectMode;
            public Color32 Tint;
        }

        public override Texture mainTexture => Texture2D.whiteTexture;

        // A dense chord section can hit far more notes per second than any
        // single burst's ~0.3s lifetime can drain, so overlapping bursts
        // accumulate. Each one still runs its full vertex/particle math in
        // OnPopulateMesh every frame regardless of how many CanvasRenderers
        // house them, so batching alone does not bound that per-frame cost —
        // only a cap on how many can be alive at once does. Oldest bursts are
        // dropped first: a fresh hit is the one the player is looking at.
        public const int MaxActiveBursts = 24;

        Burst[] bursts = Array.Empty<Burst>();
        int count;

        public int ActiveCount => count;

        public void Spawn(Vector2 center, float upperWidth, HitParticleEffectMode effectMode, Color tint)
        {
            if (count >= MaxActiveBursts)
            {
                Array.Copy(bursts, 1, bursts, 0, count - 1);
                count--;
            }
            if (count >= bursts.Length)
                Array.Resize(ref bursts, Math.Max(8, bursts.Length * 2));
            bursts[count++] = new Burst
            {
                Center = center,
                UpperWidth = upperWidth,
                Elapsed = 0f,
                EffectMode = effectMode,
                Tint = tint,
            };
            SetVerticesDirty();
        }

        public void Advance(float deltaTime)
        {
            if (count == 0) return;
            var writeIndex = 0;
            for (var index = 0; index < count; index++)
            {
                var burst = bursts[index];
                burst.Elapsed += deltaTime;
                if (burst.Elapsed >= HitBurstGraphic.DurationSeconds) continue;
                bursts[writeIndex++] = burst;
            }
            count = writeIndex;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            for (var index = 0; index < count; index++)
            {
                var burst = bursts[index];
                var progress = Mathf.Clamp01(burst.Elapsed / HitBurstGraphic.DurationSeconds);
                HitBurstGraphic.Draw(helper, burst.Center, burst.UpperWidth, burst.EffectMode, burst.Tint, progress);
            }
        }
    }
}
