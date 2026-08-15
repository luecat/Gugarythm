# Touch and Judgment Design

## Scope

Improve the Android-first touch model without changing judgment windows, scores, combo behavior, chart serialization, note presentation, or audio. Editor mouse input follows the same pipeline for repeatable validation.

## Input pipeline

`VirtualSliderInput` owns continuous-contact normalization only. A contact begins with one Tap activation. Moving across virtual-slider cells emits interpolated Tap activations, isolated by finger and cell. The 25 ms rearm remains in effect unless the contact has departed the activated cell by 0.15 lane. Flick activations are direction-agnostic and are emitted at interpolated 0.35-lane net-displacement thresholds from a movable action anchor. Ending a contact removes all its state.

## Judgment pipeline

`JudgmentEngine` receives discrete input tokens, active contacts, and per-frame contact path segments using the same offset-adjusted song-time basis. It processes, in order:

1. One-to-one discrete matching for Tap, Hold heads, and Flicks, preserving midpoint overlap protection.
2. Contact matching for Sustain and Release checkpoints. Current coverage or a valid post-note path intersection within `SustainLateWindow` awards Perfect. A Flick tail remains discrete and therefore requires a Flick activation.
3. Miss commitment, with Sustain and Release using `SustainLateWindow` independently so later checkpoints can recover after an earlier miss.

## Rendering pipeline

Gameplay Hold connectors resolve their owning graph root and next playable checkpoint. Only a successful graph root establishes a fixed anchor at the judgment line. Connector rendering is explicit: anchor-clipped for successfully connected segments, natural pass-through for unanchored, pending-at-line, or missed segments, and hidden after a successful segment fully closes. Guide and SimLine decoration retain their independent, unconditional clipping behavior. A Hold anchor is released only after all graph checkpoints and their success/miss visuals complete.

## Validation

The existing `Gugarythm/Validate Runtime` command is the deterministic regression harness. Every behavior change starts with a focused failing validation, receives minimal implementation, then reruns the full harness before the next task. Validation covers debounce/reentry, bidirectional Flick thresholds, interpolated tokens and paths, independent Hold checkpoint recovery, overlap protection, and root-anchor visual lifecycle.

## Delivery boundaries

The existing dirty worktree is preserved. No Unity packages, public configuration UI, chart format, scoring rules, timing windows, visual skins, or sound behavior are changed. Pause, restart, chart replacement, menu exit, and lost-contact cleanup clear transient slider, path, and visual-anchor state.
