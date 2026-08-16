# Supplied audio archive metadata

Source archive: `3ac52ee309090423039c307cadcea20345d96003.zip`

| Entry | Actual format | Duration | Channels | Embedded title | Track | Interpretation |
| ---: | --- | ---: | ---: | --- | ---: | --- |
| 0 | MP3 | 0.707 s | 2 | `se_live_perfect` | 8 | Perfect judgment |
| 1 | MP3 | 0.367 s | 2 | `se_live_great` | 7 | Great judgment |
| 2 | MP3 | 0.262 s | 1 | `se_live_good` | 6 | Good judgment |
| 3 | MP3 | 2.903 s | 2 | — | — | Hold-like clip; identical to entry 7 |
| 4 | MP3 | 0.917 s | 2 | `se_live_flick` | 4 | Normal Flick |
| 5 | MP3 | 0.917 s | 2 | `se_live_flick` | 4 | Normal Flick; duplicate of entry 4 |
| 6 | MP3 | 0.917 s | 2 | `se_live_flick` | 4 | Normal Flick; duplicate of entry 4 |
| 7 | MP3 | 2.903 s | 2 | — | — | Hold-like clip; duplicate of entry 3 |
| 8 | MP3 | 0.315 s | 1 | `se_live_tap` | 9 | Tap |
| 9 | MP3 | 0.341 s | 1 | `se_live_connect` | 1 | Connect / hold tick |
| 10 | MP3 | 0.524 s | 1 | `se_live_critical` | 3 | Critical tap |
| 11 | MP3 | 4.000 s | 2 | — | — | Likely critical Hold; metadata is inconclusive |
| 12 | MP3 | 0.628 s | 2 | `se_live_flick_critical` | 5 | Critical Flick |
| 13 | MP3 | 0.472 s | 2 | `se_live_connect_critical` | 2 | Critical connect / hold tick |
| 14 | WAV (PCM 16-bit) | 0.254 s | 2 | `custom01#1 (se_live_trace)` | — | Trace |
| 15 | WAV (PCM 16-bit) | 0.352 s | 2 | `custom01#2 (se_live_trace_critical)` | — | Critical Trace |

All entries use a 44.1 kHz sample rate. Entries 14 and 15 must retain a `.wav` extension; renaming them to `.mp3` does not change their RIFF/WAVE content.

The separately supplied `3.mp3` is byte-identical to archive entry 4. The separately supplied `4.mp3` is byte-identical to archive entry 3 and is therefore not the Critical Flick clip.

## Current project audit

- `flick.mp3` matches archive entry 4 (`se_live_flick`).
- `critical-flick.mp3` matches archive entry 12 (`se_live_flick_critical`).
- Existing `perfect.mp3`, `great.mp3`, `good.mp3`, `stage.mp3`, and `alternative.mp3` are the older SCP-derived 8-bit clips. None matches a file in the supplied archive and none has a descriptive title tag.
- The supplied archive provides semantic replacements for Perfect (0), Great (1), Good (2), Tap (8), Connect (9), Critical (10), Critical Connect (13), Trace (14), and Critical Trace (15).
- Every current Unity effect AudioImporter has `preloadAudioData` disabled. This may delay the first playback of a judgment sound and should be changed before latency-sensitive release testing.
- The current runtime plays `stage.mp3` once when starting a song. The reference Sonolus engine uses the Stage effect for an empty-lane press instead, so the current usage is semantically incorrect.
