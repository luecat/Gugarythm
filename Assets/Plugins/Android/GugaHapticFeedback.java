package com.gugarhythm.player;

import android.content.Context;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.os.VibrationAttributes;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;

/**
 * Short haptic ticks for rhythm hits.
 *
 * Rapid judgment streams used to drop or smear because each vibrate() replaced the
 * previous ~30 ms solid pulse, and both C# and Java discarded near-gap requests.
 * Instead we coalesce hits inside a short window into one on/off waveform so chords
 * and streams stay articulated without cancel()+oneshot thrash.
 */
public final class GugaHapticFeedback {
    private static final long COMPOSE_WINDOW_MS = 12L;
    private static final long DEFAULT_ON_MS = 10L;
    private static final long DEFAULT_OFF_MS = 14L;
    private static final int MAX_TICKS_PER_WAVE = 8;
    private static final int AMP_PEAK = 255;
    private static final int AMP_BODY = 200;

    private static final Object Lock = new Object();
    private static Vibrator cachedVibrator;
    private static Handler mainHandler;
    private static int queuedTicks;
    private static long queuedOnMs = DEFAULT_ON_MS;
    private static boolean flushPosted;
    private static final Runnable FlushRunnable = GugaHapticFeedback::flushQueue;

    private GugaHapticFeedback() {}

    public static void play(Context context, long durationMilliseconds) {
        if (context == null) return;
        long onMs = Math.max(8L, Math.min(16L, durationMilliseconds > 0 ? durationMilliseconds : DEFAULT_ON_MS));
        // When callers pass the legacy "pulse length" (~30), treat it as a short tick on-time.
        if (onMs > 16L) onMs = DEFAULT_ON_MS;

        Context app = context.getApplicationContext();
        synchronized (Lock) {
            ensureHandler();
            queuedTicks = Math.min(MAX_TICKS_PER_WAVE, queuedTicks + 1);
            queuedOnMs = onMs;
            if (!flushPosted) {
                flushPosted = true;
                mainHandler.postDelayed(FlushRunnable, COMPOSE_WINDOW_MS);
            }
            // Keep a strong Context via vibrator cache while a flush is pending.
            getVibrator(app);
        }
    }

    /** Plays an already-composed multi-tick burst immediately (optional C# batching). */
    public static void playBurst(Context context, int tickCount, long onMilliseconds) {
        if (context == null || tickCount <= 0) return;
        Vibrator vibrator = getVibrator(context.getApplicationContext());
        if (vibrator == null || !vibrator.hasVibrator()) return;
        long onMs = Math.max(8L, Math.min(16L, onMilliseconds > 0 ? onMilliseconds : DEFAULT_ON_MS));
        int ticks = Math.min(MAX_TICKS_PER_WAVE, tickCount);
        vibrateWave(vibrator, buildBurst(ticks, onMs, DEFAULT_OFF_MS));
    }

    private static void flushQueue() {
        int ticks;
        long onMs;
        Vibrator vibrator;
        synchronized (Lock) {
            flushPosted = false;
            ticks = queuedTicks;
            onMs = queuedOnMs;
            queuedTicks = 0;
            vibrator = cachedVibrator;
        }
        if (ticks <= 0 || vibrator == null || !vibrator.hasVibrator()) return;
        vibrateWave(vibrator, buildBurst(ticks, onMs, DEFAULT_OFF_MS));
    }

    private static void vibrateWave(Vibrator vibrator, VibrationEffect effect) {
        if (effect == null) return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            vibrator.vibrate(
                    effect,
                    VibrationAttributes.createForUsage(VibrationAttributes.USAGE_GAME));
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            vibrator.vibrate(effect);
        } else {
            vibrator.vibrate(DEFAULT_ON_MS);
        }
    }

    private static VibrationEffect buildBurst(int tickCount, long onMs, long offMs) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return null;
        int ticks = Math.max(1, Math.min(MAX_TICKS_PER_WAVE, tickCount));
        // timings: leading 0, then (on, off) * (ticks-1), final on
        int segmentCount = ticks * 2; // pairs of on/off, last off unused → trim
        long[] timings = new long[ticks * 2];
        int[] amplitudes = new int[ticks * 2];
        int write = 0;
        for (int i = 0; i < ticks; i++) {
            timings[write] = onMs;
            amplitudes[write] = (i & 1) == 0 ? AMP_PEAK : AMP_BODY;
            write++;
            if (i < ticks - 1) {
                timings[write] = offMs;
                amplitudes[write] = 0;
                write++;
            }
        }
        if (write != timings.length) {
            long[] trimmedTimings = new long[write];
            int[] trimmedAmps = new int[write];
            System.arraycopy(timings, 0, trimmedTimings, 0, write);
            System.arraycopy(amplitudes, 0, trimmedAmps, 0, write);
            timings = trimmedTimings;
            amplitudes = trimmedAmps;
        }

        // createWaveform(timings, amplitudes) expects timings[0] as delay before first pulse.
        long[] withLead = new long[timings.length + 1];
        int[] withLeadAmp = new int[amplitudes.length + 1];
        withLead[0] = 0L;
        withLeadAmp[0] = 0;
        System.arraycopy(timings, 0, withLead, 1, timings.length);
        System.arraycopy(amplitudes, 0, withLeadAmp, 1, amplitudes.length);

        if (hasAmplitudeControl(cachedVibrator)) {
            return VibrationEffect.createWaveform(withLead, withLeadAmp, -1);
        }
        return VibrationEffect.createWaveform(withLead, -1);
    }

    private static boolean hasAmplitudeControl(Vibrator vibrator) {
        return vibrator != null
                && Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                && vibrator.hasAmplitudeControl();
    }

    private static void ensureHandler() {
        if (mainHandler != null) return;
        mainHandler = new Handler(Looper.getMainLooper());
    }

    private static Vibrator getVibrator(Context context) {
        if (cachedVibrator != null) return cachedVibrator;

        Context app = context.getApplicationContext();
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            VibratorManager manager =
                    (VibratorManager) app.getSystemService(Context.VIBRATOR_MANAGER_SERVICE);
            if (manager != null) {
                cachedVibrator = manager.getDefaultVibrator();
            }
        }
        if (cachedVibrator == null) {
            cachedVibrator = (Vibrator) app.getSystemService(Context.VIBRATOR_SERVICE);
        }
        return cachedVibrator;
    }
}
