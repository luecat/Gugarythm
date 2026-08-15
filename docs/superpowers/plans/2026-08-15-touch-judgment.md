# Touch Judgment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver debounced virtual-slider Tap/Flick input plus independent Hold contact/path judgment and result-driven Hold visuals.

**Architecture:** `VirtualSliderInput` emits only discrete, interpolated Tap/Flick activations. `SonolusLandscapePrototype` supplies those tokens plus persistent contacts and offset-adjusted movement paths. `JudgmentEngine` matches discrete notes, contact checkpoints, and misses in separate passes; the renderer maps Hold graph state to anchor-clipped or pass-through connector modes.

**Tech Stack:** Unity 6.3 LTS, C#, Unity Input System 1.17, existing Editor `RuntimeValidation`.

## Global Constraints

- Preserve every existing modified and untracked file; never reset, checkout, clean, or rebuild the scene wholesale.
- Do not change scoring, Combo behavior, Perfect/Great/Good windows, lane forgiveness, chart serialization, note art, audio, or packages.
- Android touch is primary; editor mouse must use the identical data flow.
- Use 12 cells in `[-6, 6]`, 25 ms rearm, 0.15 lane departure, and 0.35 lane Flick activation.
- Add a focused failing deterministic validation before each production behavior change, observe its failure, implement minimally, and rerun `Gugarythm/Validate Runtime` before proceeding.

---

### Task 1: Virtual-cell debounce and reentry

**Files:**
- Modify: `Assets/Scripts/VirtualSliderInput.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Produces `VirtualSliderInput.Begin`, `Move`, `End`, and `Reset` Tap/Flick token behavior without chart access.

- [ ] Add validation for one Begin Tap, no same-cell movement Tap, 25 ms boundary jitter suppression, and early reentry after 0.15-lane departure.
- [ ] Run `Gugarythm/Validate Runtime`; confirm the new assertions fail because departure state is not implemented.
- [ ] Add per-finger, per-cell last-activation and departure tracking; update departure state across the whole movement segment before emitting interpolated cell crossings.
- [ ] Rerun validation and retain the existing full-slider low-frame-rate crossing assertions.

### Task 2: Thresholded Flick activations

**Files:**
- Modify: `Assets/Scripts/VirtualSliderInput.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Produces Flick `InputToken`s with movement segment endpoints and interpolated song times.

- [ ] Add validation for 0.349 lane (none), exactly 0.35 lane (one), both directions, long moves producing multiple threshold tokens, and oscillation below threshold never accumulating.
- [ ] Run validation; confirm it fails because slider moves emit no threshold Flick tokens.
- [ ] Add an action-anchor lane/time per contact and emit one Flick token per interpolated 0.35-lane threshold, advancing the anchor after each emission.
- [ ] Rerun validation; retain Tap crossing behavior and ensure Begin never emits Flick.

### Task 3: Contact path collection

**Files:**
- Modify: `Assets/Scripts/JudgmentEngine.cs`
- Modify: `Assets/Scripts/SonolusLandscapePrototype.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Add `ContactPathSegment(int fingerId, double startTime, double endTime, float startLane, float endLane, bool ended)`.
- Add four-argument `JudgmentEngine.Process(songTime, inputBatch, contacts, contactPaths)` and retain the three-argument overload.

- [ ] Add validation that a low-frame-rate A-to-B move preserves its endpoints, offset-adjusted times, and Ended path semantics, and stale state vanishes after reset.
- [ ] Run validation; confirm the four-argument path API is not yet available.
- [ ] Introduce the immutable segment type and forward the legacy overload with an empty path list.
- [ ] In shared Touch/Mouse collection, append each actual move and termination segment, clear paths with slider/contact cleanup, and pass the batch to the new Process overload.
- [ ] Rerun validation.

### Task 4: Independent Hold contact checkpoints

**Files:**
- Modify: `Assets/Scripts/JudgmentEngine.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- `Process` runs discrete matching, contact-note resolution, then miss commitment.

- [ ] Add failing validations for Sustain/Release coverage at note time, path intersection after note time, no early consumption, held Release success without Release token, missed early release, independent recovery after a missed checkpoint, and Flick-tail threshold requirements.
- [ ] Run validation; confirm Release still needs a release token and path-only coverage is absent.
- [ ] Exclude Sustain and Release from discrete candidate construction; resolve both from current contacts or a path intersection in `[note.Time, note.Time + SustainLateWindow]` and use the same late window for misses.
- [ ] Extract the segment/range intersection helper; Flick uses its existing timing window and contact paths use the sustain-only window.
- [ ] Rerun validation and ensure overlapping Sustain checkpoints may all resolve from the same contact.

### Task 5: Discrete overlap protection regression

**Files:**
- Modify: `Assets/Scripts/JudgmentEngine.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

- [ ] Add failing validations for Flick-generated tokens against overlapping Tap/Hold heads, one-token/one-discrete-note matching, dual contacts matching two overlaps, and departure-enabled rapid return.
- [ ] Run validation and identify any mismatch in midpoint protection or candidate ordering.
- [ ] Apply only the smallest matching changes needed to preserve existing rank order: grade, absolute time error, spatial error, note index.
- [ ] Rerun validation.

### Task 6: Result-driven Hold graph visuals

**Files:**
- Modify: `Assets/Scripts/SonolusLandscapePrototype.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Add internal `HoldConnectorRenderMode { AnchorClipped, NaturalPassThrough, Hidden }` behavior.
- Resolve a connector's graph root and next playable checkpoint through control nodes.

- [ ] Add failing graph-lifecycle validation for root-only anchors, fixed root geometry, successful hidden segments, pending-at-line pass-through, missed checkpoint flight, delayed anchor cleanup, all Hold checkpoint kinds, and independent Guide/SimLine clipping.
- [ ] Run validation; confirm the existing outgoing-connector heuristic treats intermediate checkpoints as anchors and clips every gameplay connector.
- [ ] Build incoming/outgoing connector indices when chart state is initialized; identify graph roots and successor playable checkpoints.
- [ ] Replace unconditional gameplay clipping with mode-specific anchor-clipped, natural-pass-through, and hidden paths. Keep decoration clipping separate.
- [ ] Retain Hold checkpoint misses until `NoteExitY`; remove anchors only after pending and visual-flight work is complete. Clear graph/anchor state on pause, restart, chart replacement, and menu exit.
- [ ] Rerun validation and manually compare the supplied success/miss video intervals in Unity Play Mode.

### Task 7: End-to-end validation

**Files:**
- Verify: `Assets/Editor/RuntimeValidation.cs`
- Verify: `Assets/Scripts/VirtualSliderInput.cs`
- Verify: `Assets/Scripts/JudgmentEngine.cs`
- Verify: `Assets/Scripts/SonolusLandscapePrototype.cs`

- [ ] Run `Gugarythm/Validate Runtime` in Unity and require `GUGARYTHM_VALIDATION_OK` with no new Console errors.
- [ ] In Play Mode, exercise mouse single Tap, stationary pre-press, cross-cell Tap, 0.35 lane Flick, and held normal Hold tail.
- [ ] Run Android device checks at 60 Hz and its highest available refresh rate using the specified mixed chart cases; record FAST/LATE observations without changing time windows.
- [ ] Inspect `git diff` and status to verify no unrelated existing work was altered.
