# Self-Knowledge Explorer

A self-contained C# program that autonomously generates small probe experiments to learn about its own decision behavior, updates a confidence-weighted knowledge base, and asks a human for guidance when confidence is too low to proceed alone.

---

## Overview

The explorer mimics how a learning system might reason about itself: it maintains a set of hypotheses about its own behavior, designs targeted experiments to test each hypothesis, and updates its beliefs based on the results. When an experiment produces a finding that contradicts expectations or falls below a confidence threshold, the system pauses and requests human input.

The underlying subject being explored is the `Probe` class — a simplified version of the Hybrid Temporal Neuro-Fuzzy Optimizer's decision and reward logic.

---

## Architecture

```
KnowledgeBase (8 seeded topics)
       │
       ▼
SelfKnowledgeExplorer.Run()
  │
  ├─ FindLowestConfidence()  ──► pick next topic to investigate
  │
  ├─ ProgramGenerator.For()  ──► generate targeted experiment
  │
  ├─ Experiment.Execute()    ──► run probe, collect result
  │
  ├─ KnowledgeBase.Update()  ──► record value, confidence, strikes, attempts
  │
  ├─ Escape checks:
  │    Strikes ≥ 3  ──► Resolve (hypothesis contradiction)
  │    Attempts ≥ 5 ──► Resolve (ceiling effect accepted)
  │
  └─ Confidence < threshold  ──► HumanConsole.Ask()
       │                              │
       └── HandleGuidance() ◄─────────┘
             AdjustConfidence / Resolve / Rerun / Override
```

---

## Components

### `KnowledgeFact`

Represents one item of self-knowledge.

| Field | Type | Description |
|-------|------|-------------|
| `Topic` | string | Identifier (e.g., `danger_crossover`) |
| `Hypothesis` | string | The belief being tested |
| `Value` | double | Numeric finding from the last experiment |
| `Confidence` | double | How certain the system is (0 = unknown, 1 = certain) |
| `Strikes` | int | Consecutive contradictions of the hypothesis |
| `Attempts` | int | Total experiment runs on this topic |
| `Resolved` | bool | When true, topic is closed and excluded from future exploration |
| `HumanNote` | string | Notes from human guidance or resolution messages |

---

### `KnowledgeBase`

Manages all facts and controls exploration priority.

| Method | Behavior |
|--------|----------|
| `Seed(topic, hypothesis)` | Adds a new fact at confidence 0.05 |
| `FindLowestConfidence()` | Returns the unresolved fact with lowest confidence |
| `Update(topic, value, confidence, contradiction)` | Increments `Attempts`; increments or resets `Strikes` |
| `AdjustConfidence(topic, value, confidence)` | Updates value/confidence only — leaves `Strikes` and `Attempts` untouched (used in human guidance responses) |
| `Resolve(topic, value, confidence, note)` | Permanently closes a topic (`Resolved = true`); excluded from `FindLowestConfidence` |
| `AverageConfidence()` | Mean confidence across all facts |

---

### `Probe`

A static class that encapsulates the same danger scoring and reward logic used in the Hybrid Temporal Neuro-Fuzzy Optimizer. It is the subject of the experiments.

**Danger formula:**

```
danger = 0.35 × load
       + 0.30 × error
       + 0.20 × clamp01((dError + 1) / 2)
       + 0.25 × clamp01(iError)
```

Default parameters (`dError = 0`, `iError = 1.5`) contribute a fixed offset of **+0.35** to any probe result, which shifts the effective crossover point upward in input space.

**`Probe.Run()`** samples (load, error, urgency) uniformly from configurable ranges, computes the oracle action (Safe if danger ≥ 0.55, Fast otherwise), optionally forces a specific action, and returns the average reward, safe-action rate, and variance over `trials` samples.

---

### `ProgramGenerator`

Generates a targeted `Experiment` for each topic. Each experiment has:
- A `Code` property: a pseudocode description of the program, printed for transparency
- An `Execute()` delegate: the actual C# implementation

| Topic | Experiment strategy |
|-------|-------------------|
| `safe_accuracy_high_danger` | Force Safe, sample from high load/error region |
| `fast_accuracy_low_danger` | Force Fast, sample from low load/error region |
| `danger_crossover` | Sweep load/error midpoint `m`; compute `crossoverDanger = 0.65m + 0.35` |
| `urgency_bonus` | Compare mean Fast reward at low vs. high urgency |
| `confusion_zone_width` | Scan danger ∈ [0.45, 0.65] for bands where `|safe_r − fast_r| < 0.08` |
| `high_load_effect` | Oracle, load ∈ [0.8, 1], error ∈ [0, 0.3] |
| `low_error_fast_rate` | Oracle, load ∈ [0, 0.5], error ∈ [0, 0.2] |
| `oscillation_cost` | Compare same-action vs. switched-action reward near the decision boundary |
| _(unknown)_ | General sweep across all inputs |

---

### `HumanConsole`

Pauses execution and asks for guidance when the system is uncertain. Detected commands:

| Input | Effect |
|-------|--------|
| `[Enter]` / `accept` | Accept the finding; boost confidence slightly |
| `correct` / `yes` | Confirm finding; higher confidence boost |
| `wrong` / `no` | Mark incorrect; reset confidence to 0.1 |
| `rerun` | Re-queue this topic for the next iteration |
| `<number>` | Override the value; set confidence to 0.80 |
| `<text>` | Save note; set confidence to 0.75 |

**Non-interactive mode** (`Console.IsInputRedirected`): automatically prints `[non-interactive] auto-accepting` and returns `"accept"`, allowing the program to run in CI or piped environments without blocking.

---

### `SelfKnowledgeExplorer`

The main control loop.

| Parameter | Value |
|-----------|-------|
| `CONFIDENCE_THRESHOLD` | `0.62` |
| `MAX_ITERATIONS` | `40` |
| Topics seeded | `8` |

**Loop logic per iteration:**

1. Find the unresolved topic with lowest confidence via `FindLowestConfidence()`
2. If that topic's confidence ≥ `CONFIDENCE_THRESHOLD`, all topics are settled — exit early
3. Generate and run the targeted experiment
4. Update the knowledge base (increments `Attempts`, manages `Strikes`)
5. **Escape: Strikes ≥ 3** — hypothesis is contradicted by evidence; call `Resolve()` at confidence 0.72
6. **Escape: Attempts ≥ 5** — topic hits a ceiling (e.g., reward saturation); call `Resolve()` at confidence 0.70
7. If `confidence < threshold` or result contradicts hypothesis, call `HumanConsole.Ask()`
8. Print knowledge snapshot every 8 iterations
9. **Early exit: average confidence ≥ 0.88**

---

## Seeded Topics and Expected Findings

| Topic | Hypothesis | Typical Finding | Notes |
|-------|-----------|-----------------|-------|
| `safe_accuracy_high_danger` | Safe reward ≥ 0.9 when danger > 0.6 | 100% — conf 1.00 | Strong oracle region |
| `fast_accuracy_low_danger` | Fast correct ≥ 80% when load < 0.4, error < 0.3 | 96–98% — conf 0.86–0.92 | Above threshold |
| `danger_crossover` | Crossover at danger ≈ 0.55 | danger ≈ 0.545 — conf 0.52 | Fixed +0.35 offset shifts crossover; guidance needed |
| `urgency_bonus` | High urgency adds ~0.10 Fast reward | ≈ 0.006 — conf 0.50 | Reward ceiling (base ≈ 1.0); invisible effect; Attempts escape |
| `confusion_zone_width` | Confusion zone ≈ 0.10 wide | 0.0 — conf 0.35 | Sharp threshold; 3 contradictions → Resolve |
| `high_load_effect` | High load → Safe ≥ 75% of the time | 100% — conf 1.00 | Oracle |
| `low_error_fast_rate` | Low error → Fast ≥ 70% of the time | 93% — conf 1.00 | Oracle |
| `oscillation_cost` | Oscillation penalty = 0.05 per switch | 0.0500 — conf 0.92 | Exact match |

---

## Sample Run (non-interactive)

```
╔═══════════════════════════════════════════════════════════════╗
║           Self-Knowledge Explorer  — Hybrid AI               ║
║  Generates programs to understand its own decision behavior   ║
╚═══════════════════════════════════════════════════════════════╝

  Seeded 8 knowledge topics.
  Confidence threshold : 0.62
  Max iterations       : 40
  Below threshold → asks human for guidance

━━━ Iteration  1/40 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  Knowledge gap : "safe_accuracy_high_danger"  (confidence = 0.05)
  ...
  Finding    : Safe is correct 100.0% in high-danger (avg reward 1.000)
  Confidence : 1.00

━━━ Iteration 10/40 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ...
  ✗ Hypothesis revised after 3 contradictions — accepting measured value.

━━━ Iteration 11/40 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

  All topics at or above confidence threshold. Learning complete.

╔═══════════════════════════════════════════════════════════════╗
║  Done.  Iterations: 11     Human interactions: 4             ║
║  Average confidence: 0.85                                  ║
╚═══════════════════════════════════════════════════════════════╝

── Knowledge Base ──────────────────────────────────────────────
  ████████████ 1.00    safe_accuracy_high_danger      =  1.000
  ████████████ 1.00    high_load_effect               =  1.000
  ████████████ 1.00    low_error_fast_rate            =  0.930
  ███████████  0.92    oscillation_cost               =  0.050
  ██████████   0.86    fast_accuracy_low_danger       =  0.961
  ████████     0.72 ✓  confusion_zone_width           =  0.000  [hypothesis revised...]
  ████████     0.67    danger_crossover               =  0.545  [accept]
  ███████      0.65    urgency_bonus                  =  0.006  [accept]
────────────────────────────────────────────────────────────────
```

Typically completes in **11 iterations** with **4 human interactions** (non-interactive) and an average confidence of **0.85**.

---

## Interesting Behaviors

**Danger offset:** `Probe.Danger()` adds a fixed `+0.35` contribution from its `dError`/`iError` default parameters. The crossover experiment accounts for this by sweeping load/error midpoint `m` and computing `crossoverDanger = 0.65m + 0.35`. Without this correction, naive probes would report a crossover at ~0.35 instead of ~0.55.

**Reward ceiling effect:** The urgency bonus (`+0.10`) is applied only when Fast is the correct choice and base reward is already `1.0`. `clamp01(1.1) = 1.0`, making the bonus effectively invisible. The explorer consistently measures a bonus near `0.006` (random noise) rather than `0.10`. The Attempts escape accepts this finding after 5 tries.

**Sharp threshold:** The confusion zone experiment finds zero ambiguous bands — the reward function has a hard cliff at `danger = 0.55` with no gradual transition. Three consecutive contradictions of the `~0.10 wide` hypothesis cause the topic to be resolved via the Strikes escape.

---

## Running the Code

**Requirements:** .NET 6+

```bash
dotnet new console -n SelfKnowledgeAI
# Replace Program.cs with self-knowledge-explorer.cs content
dotnet run
```

The program runs fully non-interactively when stdin is redirected (CI, scripts):

```bash
dotnet run < /dev/null
```

## Persistence

On every run the program creates a `.data/` folder next to the working directory and writes:

| File | Contents |
|------|----------|
| `.data/self-knowledge-state.dat` | All 8 knowledge facts: topic, hypothesis, confidence, value, strikes, attempts, resolved flag, notes |
| `.data/self-knowledge-state.bmp` | Confidence dashboard for the current run (256×256 px) |
| `.data/self-knowledge-state.N.bmp` | Numbered backup of the previous dashboard (increments each run) |

On the next run the program loads the `.dat` file and skips seeding — topics already above the confidence threshold are not re-explored, so a converged knowledge base exits after a single iteration. Delete `.data/self-knowledge-state.dat` to start fresh.

The `.data/` folder is gitignored and never committed.

---

## Design Notes

- **No external dependencies.** Single file using only `System`, `System.Collections.Generic`, and `System.Linq`.
- **`Resolved` flag** is the key mechanism for preventing infinite loops: once a topic is force-accepted (via Strikes or Attempts escape), it is permanently excluded from `FindLowestConfidence()`.
- **`AdjustConfidence` vs `Update`:** Human guidance responses use `AdjustConfidence` to preserve the `Strikes` and `Attempts` counters. Using `Update` would have reset `Strikes = 0` on each acceptance, blocking the Strikes escape.
- **Deterministic escapes:** Both escapes (Strikes ≥ 3 and Attempts ≥ 5) call `Resolve()`, not `Update()`, ensuring each topic is closed exactly once regardless of how many guidance responses have been given.
- **Non-interactive detection** via `Console.IsInputRedirected` ensures the program never blocks when run in background or piped environments.

---

## Use Cases

**Behavioral auditing of trained models.** After training a decision model, run the explorer against it to verify that its actual behavior matches design intent — confirming decision boundaries, reward sensitivities, and edge-case handling before deployment.

**Reward function debugging.** Reward functions are often written once and rarely interrogated. The explorer surfaces hidden effects like ceiling saturation (the urgency bonus) or fixed offsets (the danger probe) that would otherwise only appear as unexplained performance plateaus during training.

**Regression testing for decision logic.** Pin the knowledge base findings as acceptance criteria. If a refactor shifts the `danger_crossover` from 0.545 to 0.60, a CI run of the explorer will surface the discrepancy without requiring hand-crafted unit tests for every boundary.

**Active learning scaffolding.** The confidence-weighted exploration loop (pick lowest confidence → experiment → update → escalate) is a reusable pattern for any system that needs to learn about itself incrementally. Swap out `Probe` for a different subject — a routing algorithm, a classifier, a simulation — and the framework handles prioritization and human escalation automatically.

**Human-AI collaborative auditing.** In interactive mode, a domain expert can steer the exploration: confirming findings, overriding values, or flagging hypotheses as wrong. The system learns from each response and routes subsequent experiments to the remaining uncertainty, making efficient use of limited expert time.

**Educational demonstration of epistemic uncertainty.** The confidence scores, contradiction tracking, and guided escalation make the learning process transparent — useful for teaching active learning, Bayesian belief updating, or human-in-the-loop ML concepts with a running, inspectable example.
