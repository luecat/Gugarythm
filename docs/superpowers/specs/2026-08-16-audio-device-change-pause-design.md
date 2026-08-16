# Audio Device Change Pause Design

## Goal

When Unity reports a global audio configuration change during an active song,
the game pauses safely. Resuming continues the song and chart at the same
saved point using a new DSP schedule.

## Cause

The gameplay clock is calculated from `AudioSettings.dspTime` and the BGM is
started with `AudioSource.PlayScheduled`. Changing the operating system's
default output device can rebuild Unity's audio configuration. The existing
code does not observe that event, so the old schedule and the rebuilt audio
clock can no longer be trusted together.

## Behaviour

- Subscribe to `AudioSettings.OnAudioConfigurationChanged` while the runtime
  component is alive, and unsubscribe on destruction.
- The event callback only records a pending audio interruption. It does not
  touch UI or `AudioSource` state.
- `Update` consumes the pending interruption only during active gameplay. It
  snapshots the most recently observed chart time, clears active input, pauses
  the game, and tells the player that the audio device changed.
- Resume keeps the existing three-second countdown. For an interrupted game it
  seeks the BGM to the saved playback position and creates a fresh
  `PlayScheduled` time based on the new `AudioSettings.dspTime`; it never
  unpauses the pre-change schedule.
- Device changes outside active gameplay have no visible effect.

## Testing

- Extract clock/schedule arithmetic to a small pure helper so it can be unit
  tested without an audio device.
- Add tests for preserving a chart time after rescheduling and for the normal
  manual-pause path remaining unchanged.
- In the Unity Editor, start a chart, change the macOS default output device,
  verify immediate pause, then resume after the countdown and check that song,
  notes, and input remain aligned.
