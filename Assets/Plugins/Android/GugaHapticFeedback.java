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
 * Android vibrator services typically replace the active effect on each vibrate()
 * call, and older app-side throttles discarded near-gap requests. We compose
 * same-window hits into one on/off waveform instead of stacking / dropping.
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

    /** Fallback path: enqueue one tick and flush after a short coalesce window. */
    public static void play(Context context, long durationMilliseconds) {
        if (context == null) return;
        long onMs = clampOnMs(durationMilliseconds);
        Context app = context.getApplicationContext();
        synchronized (Lock) {
            ensureHandler();
            queuedTicks = Math.min(MAX_TICKS_PER_WAVE, queuedTicks + 1);
            queuedOnMs = onMs;
            getVibrator(app);
            if (!flushPosted) {
                flushPosted = true;
                mainHandler.postDelayed(FlushRunnable, COMPOSE_WINDOW_MS);
            }
        }
    }

    /**
     * Immediate multi-tick burst (used by Unity after a judgment drain).
     * Any pending coalesce window is folded in so we do not double-fire.
     */
    public static void playBurst(Context context, int tickCount, long onMilliseconds) {
        if (context == null || tickCount <= 0) return;
        long onMs = clampOnMs(onMilliseconds);
        int ticks;
        Vibrator vibrator;
        synchronized (Lock) {
            ensureHandler();
            if (flushPosted) {
                mainHandler.removeCallbacks(FlushRunnable);
                flushPosted = false;
                tickCount += queuedTicks;
                queuedTicks = 0;
            }
            ticks = Math.min(MAX_TICKS_PER_WAVE, tickCount);
            queuedOnMs = onMs;
            vibrator = getVibrator(context.getApplicationContext());
        }
        if (vibrator == null || !vibrator.hasVibrator()) return;
        vibrateWave(vibrator, buildBurst(vibrator, ticks, onMs, DEFAULT_OFF_MS));
    }

    private static long clampOnMs(long durationMilliseconds) {
        long onMs = durationMilliseconds > 0 ? durationMilliseconds : DEFAULT_ON_MS;
        return Math.max(8L, Math.min(16L, onMs));
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
        vibrateWave(vibrator, buildBurst(vibrator, ticks, onMs, DEFAULT_OFF_MS));
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

    private static VibrationEffect buildBurst(Vibrator vibrator, int tickCount, long onMs, long offMs) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return null;
        int ticks = Math.max(1, Math.min(MAX_TICKS_PER_WAVE, tickCount));
        // Pattern: on, off, on, off, ... ending on an on segment.
        int segmentCount = ticks * 2 - 1;
        long[] timings = new long[segmentCount + 1];
        int[] amplitudes = new int[segmentCount + 1];
        timings[0] = 0L;
        amplitudes[0] = 0;
        int write = 1;
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

        if (hasAmplitudeControl(vibrator)) {
            return VibrationEffect.createWaveform(timings, amplitudes, -1);
        }
        return VibrationEffect.createWaveform(timings, -1);
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
