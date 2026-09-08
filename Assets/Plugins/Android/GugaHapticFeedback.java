package com.gugarhythm.player;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;

public final class GugaHapticFeedback {
    private GugaHapticFeedback() {}

    public static void play(Context context, long durationMilliseconds) {
        if (context == null) return;
        Vibrator vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
        if (vibrator == null || !vibrator.hasVibrator()) return;

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            vibrator.vibrate(VibrationEffect.createPredefined(VibrationEffect.EFFECT_CLICK));
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            long duration = Math.max(1L, durationMilliseconds);
            vibrator.vibrate(VibrationEffect.createOneShot(duration, VibrationEffect.DEFAULT_AMPLITUDE));
        } else {
            vibrator.vibrate(Math.max(1L, durationMilliseconds));
        }
    }
}
