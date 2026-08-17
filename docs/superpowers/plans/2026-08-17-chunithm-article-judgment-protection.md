# CHUNITHM Article Judgment Protection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current shared-lane-only protection with the Bilibili article's authored-overlap, whole-width Justice/Attack, shared-width JC model without breaking normal input.

**Architecture:** `JudgmentEngine` precomputes chart-defined TAP-class protection pairs from strict positive authored-span intersections. Candidate filtering uses a separate internal `ProtectionBand`, so visible Perfect results on Critical TAP and early FLICK still obey Justice/Attack trimming. Runtime validation proves the article-specific spatial and temporal distinctions and guards normal input paths.

**Tech Stack:** Unity 6.3 LTS, C#, editor batch validation through `RuntimeValidation.ValidateRuntime`.

## Global Constraints

- The Bilibili article is the highest authority when prior assumptions conflict.
- JC is `|delta| <= 2/60 s`, Justice is `2/60 s < |delta| <= 4/60 s`, and Attack is `4/60 s < |delta| <= 6/60 s`.
- Protection pairs use authored spans only; `LaneForgiveness` cannot create a pair or expand the JC shared span.
- Justice and Attack are midpoint-trimmed across the whole candidate width once an authored-overlap pair exists.
- JC is midpoint-trimmed only when input lies in the pair's authored shared span.
- Trimmed candidates disappear and do not downgrade; the normal Miss lifecycle remains responsible for Miss results.
- Protection remains chart-defined after either neighbor resolves.
- Do not change scoring, combo, sustain checkpoints, latency, rendering, or autoplay behavior.

---

### Task 1: Article-Specific Behavioral Validation

**Files:**
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Consumes: `JudgmentEngine.Process`, `JudgmentEngine.GradeFor`, `RuntimeNote`, `InputToken`.
- Produces: `ValidateTapJackProtection()` coverage for the authoritative article rules.

- [ ] **Step 1: Replace the superseded unique-lane Justice assertion**

Add fixtures where two authored TAP spans overlap positively, an input is in a lane unique to the later TAP, and its pre-midpoint Justice or Attack candidate must remain Pending. Keep the existing unique-lane JC fixture, but assert that JC stays Perfect.

- [ ] **Step 2: Add hidden-band validation**

Add a Critical TAP and an early FLICK whose visible grade is Perfect but whose time error is internally Justice. Assert that both are rejected before their midpoint outside the shared span, then add JC controls that remain Perfect outside the shared span.

- [ ] **Step 3: Add pair-construction validation**

Add separate fixtures for no authored overlap and edge-touch-only spans. Assert that Justice remains available because neither geometry creates a protection pair.

- [ ] **Step 4: Add lifecycle and chain validation**

Keep a resolved-neighbor fixture and a three-note fixture. Assert that protection survives neighbor resolution and that the center note must satisfy both pair boundaries.

- [ ] **Step 5: Run the focused runtime validation and confirm RED**

Run:

```bash
/Volumes/Crucial\ X9/Applications/Unity/6000.3.22f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -quit \
  -projectPath /private/tmp/gugarythm-tap-jack-judgment-protection \
  -executeMethod RuntimeValidation.ValidateRuntime \
  -logFile /private/tmp/chunithm-article-protection-red.log
```

Expected: exit code `1` with the first new whole-width Justice/Critical/FLICK assertion, proving the current model is wrong for the article.

### Task 2: Protection Band and Pair Semantics

**Files:**
- Modify: `Assets/Scripts/JudgmentEngine.cs`
- Test: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Produces: private `ProtectionBand` enum and `ProtectionBandFor(double delta)`.
- Consumes: `TapProtectionPair.ContainsSharedLane(float lane)` and midpoint `Boundary`.

- [ ] **Step 1: Define shared timing constants and internal bands**

Add `JusticeCriticalWindow`, `JusticeWindow`, and `AttackWindow` constants, and classify absolute delta independently of visible grade:

```csharp
enum ProtectionBand { Outside, Critical, Justice, Attack }

static ProtectionBand ProtectionBandFor(double delta)
{
    var absolute = Math.Abs(delta);
    if (absolute <= JusticeCriticalWindow) return ProtectionBand.Critical;
    if (absolute <= JusticeWindow) return ProtectionBand.Justice;
    if (absolute <= AttackWindow) return ProtectionBand.Attack;
    return ProtectionBand.Outside;
}
```

- [ ] **Step 2: Make authored overlap strict**

Change pair creation so `sharedMaximum <= sharedMinimum` is rejected. This prevents edge contact from being treated as spatial overlap while preserving every positive-width intersection.

- [ ] **Step 3: Rewrite candidate protection**

Compute the internal band from `eventTime - note.Time`. If the input is on the wrong midpoint half, reject Justice and Attack regardless of lane. Reject Critical only when `ContainsSharedLane(inputLane)` is true. Ignore visible `JudgmentGrade` for protection-band selection.

- [ ] **Step 4: Share constants with visible grading**

Update `GradeFor`, `OuterLateWindow`, and `OuterEarlyWindow` to use the same three constants without changing their currently accepted visible results.

- [ ] **Step 5: Preserve batched rub matching after edge-touch pairs are removed**

When a batch has an authored-span TAP candidate for a Note, discard forgiveness-only TAP edges for that same Note before bipartite matching. This prevents an earlier virtual-cell token from reserving a Note that a later exact cell token can hit at JC.

- [ ] **Step 6: Run runtime validation and confirm GREEN**

Run the same batch command with log file `/private/tmp/chunithm-article-protection-green.log`.

Expected: exit code `0` and `GUGARYTHM_VALIDATION_OK`, including Rub, multi-input, Hold, autoplay, and full-chart checks.

### Task 3: Remove Temporary Diagnostics and Integrate

**Files:**
- Modify: `Assets/Scripts/JudgmentEngine.cs`
- Modify: `Assets/Scripts/SonolusLandscapePrototype.cs`
- Modify: `Assets/Editor/RuntimeValidation.cs`

**Interfaces:**
- Consumes: validated Task 2 implementation and tests.
- Produces: clean formal-project source with no per-input diagnostic logging.

- [ ] **Step 1: Remove temporary logs**

Remove `GUGARYTHM_PROTECTION_REJECT`, `GUGARYTHM_INPUT`, `GUGARYTHM_INPUT_RESULT`, and `LogDiscreteInputDiagnostics`. Preserve the actual latency calibration controls and offset sanitization.

- [ ] **Step 2: Synchronize only the intended files**

Copy the validated code through an explicit patch into the formal project. Do not touch scene, project settings, physics material, `.utmp`, agent files, or `.gitignore`.

- [ ] **Step 3: Verify source hygiene**

Run:

```bash
git diff --check
rg -n "GUGARYTHM_(PROTECTION_REJECT|INPUT_RESULT|INPUT token)|LogDiscreteInputDiagnostics" Assets/Scripts
```

Expected: `git diff --check` exits `0`, and the diagnostic search returns no matches.

- [ ] **Step 4: Run final full validation**

Run isolated Unity validation once more and confirm `GUGARYTHM_VALIDATION_OK`. If the formal editor is unlocked, run the same validation against `/Volumes/GugarythmWorkspace/Gugarythm`; otherwise verify formal source identity against the already validated isolated files.

- [ ] **Step 5: Commit only requested source and tests**

```bash
git add Assets/Scripts/JudgmentEngine.cs Assets/Scripts/SonolusLandscapePrototype.cs Assets/Editor/RuntimeValidation.cs
git commit -m "fix: match chunithm judgment protection"
```

Confirm `git status --short` leaves unrelated user files untracked or untouched.
