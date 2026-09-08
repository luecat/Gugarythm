package com.gugarhythm.player;

import android.content.Context;
import android.os.Build;
import android.os.SystemClock;
import android.os.VibrationAttributes;
import android.os.VibrationEffect;
import android.os.Vibrator;

public final class GugaHapticFeedback {
    // A trace checkpoint and the flick that closes the same slide can be judged
    // only a few milliseconds apart. In that burst, repeated predefined
    // EFFECT_CLICK pulses are de-duplicated / rate-limited by several OEM
    // vibrator services, and the LRA motor cannot re-articulate in time, so
    // trace or flick silently plays no haptic. Outside a burst we keep the
    // tuned predefined click; inside a burst we emit a fresh one-shot after an
    // explicit cancel so every hit stays felt.
    private static final long BURST_WINDOW_MS = 30L;
    private static long lastPulseUptime = 0L;

    private GugaHapticFeedback() {}

    public static void play(Context context, long durationMilliseconds) {
        if (context == null) return;
        Vibrator vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        if (vibrator == null || !vibrator.hasVibrator()) return;

        long now = SystemClock.uptimeMillis();
        boolean bursting = now - lastPulseUptime < BURST_WINDOW_MS;
        lastPulseUptime = now;

        long duration = Math.max(1L, durationMilliseconds);

        if (!bursting && Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            vibrator.vibrate(VibrationEffect.createPredefined(VibrationEffect.EFFECT_CLICK));
            return;
        }

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            // Fresh one-shot per call so the service treats it as a new request,
            // plus an explicit cancel so the burst's final hit is not masked.
            VibrationEffect effect =
                    VibrationEffect.createOneShot(duration, VibrationEffect.DEFAULT_AMPLITUDE);
            vibrator.cancel();
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                vibrator.vibrate(effect,
                        VibrationAttributes.createForUsage(VibrationAttributes.USAGE_TOUCH));
            } else {
                vibrator.vibrate(effect);
            }
        } else {
            vibrator.vibrate(duration);
        }
    }
}
