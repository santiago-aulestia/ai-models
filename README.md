# Hybrid Temporal Neuro-Fuzzy Optimizer

A self-contained C# implementation of a hybrid real-time decision system that combines a neural network, a heuristic rule engine, and a fuzzy logic engine into a single weighted decision loop. The system learns online via backpropagation over 3,000 simulated episodes and outputs a binary action — **Safe** or **Fast** — based on a 7-dimensional temporal feature vector.

---

## Overview

The optimizer addresses a classic control dilemma: when is it safer to act cautiously versus quickly? Each timestep, the system observes three raw signals (load, error, urgency), extracts temporal derivatives and integrals, and combines the outputs of three independent scoring modules into a final confidence score.

**Decision rule:** if `combined ≥ 0.5` → choose `Safe`; otherwise choose `Fast`.

---

## Architecture

```
Raw Signals: load, error, urgency
       │
       ▼
TemporalFeatureExtractor
  DLoad, DError, ILoad, IError
       │
       ├──► HeuristicEngine  (weight 0.30)
       ├──► FuzzyEngine      (weight 0.25)
       └──► NeuralNetwork    (weight 0.45)
                │
                ▼
        Combined Score [0,1]
                │
        Safe (≥0.5) / Fast (<0.5)
                │
        EnvironmentReward  ──► NeuralNetwork.Train()
```

---

## Components

### `TemporalFeatureExtractor`

Converts raw per-step signals into a 4-dimensional temporal context:

| Property | Formula | Description |
|----------|---------|-------------|
| `DLoad`  | `(load − prevLoad) / dt` | First derivative of load |
| `DError` | `(error − prevError) / dt` | First derivative of error |
| `ILoad`  | `decay × ILoad + load × dt`, capped at 5.0 | Decayed integral of load |
| `IError` | `decay × IError + error × dt`, capped at 5.0 | Decayed integral of error |

- **Decay factor:** `0.98` (exponential forgetting — older values contribute less)
- Initialization: derivatives are 0 on the first step; the extractor seeds `prevLoad`/`prevError` on first call.

---

### `HeuristicEngine`

A deterministic weighted rule scorer. Returns a value in `[0, 1]` representing how strongly the current state calls for a safe action.

**Scoring logic:**

```
score  = 0.30 × load
       + 0.35 × error
       + 0.20 × min(1, dError)         if dError > 0
       + 0.25 × min(1, iError / 3)
       − 0.25 × urgency

Override bonuses:
  +0.15  if error > 0.75 AND dError > 0.10
  +0.20  if iError > 2.5
```

High urgency pushes the score down (favoring Fast), while persistent or rapidly worsening errors push it up (favoring Safe).

---

### `FuzzyEngine`

A Mamdani-style fuzzy inference system with weighted-average defuzzification. Uses three membership functions:

| Function | Input | Shape |
|----------|-------|-------|
| `High(x)` | load, error, urgency | Linear ramp: 0 at x≤0.5, 1 at x≥1.0 |
| `Rising(dx)` | dError | Linear ramp: 0 at dx≤0, 1 at dx≥1.0 |
| `LargeIntegral(i)` | iError | Linear ramp: 0 at i≤1.0, 1 at i≥3.0 |

**Fuzzy rules:**

| Rule | Antecedent | Consequent weight |
|------|-----------|-------------------|
| R1 | error is High | Safe — 0.80 |
| R2 | error is Rising | Safe — 0.70 |
| R3 | integrated error is Large | Safe — 0.95 |
| R4 | load is High AND error is Rising | Safe — 0.90 |
| R5 | urgency is High AND error is NOT High | Safe — 0.20 |

**Defuzzification:**

```
output = Σ(rule_strength × consequent_weight) / Σ(rule_strength)
```

Falls back to `0.5` when all rule strengths are zero.

---

### `NeuralNetwork`

A single-hidden-layer feedforward network trained online with standard backpropagation.

| Parameter | Value |
|-----------|-------|
| Input size | 7 |
| Hidden size | 10 |
| Output size | 1 |
| Activation | Sigmoid (all layers) |
| Weight init | Uniform random in `[−0.25, 0.25]` |
| Learning rate | `0.03` |
| Random seed | `42` |

**Forward pass:**

```
h[j] = σ(b1[j] + Σ x[i] × w1[i,j])
y    = σ(b2    + Σ h[j] × w2[j])
```

**Backward pass (one-step gradient descent):**

```
outputDelta   = (target − y) × σ'(y)
hiddenDelta[j] = outputDelta × w2[j] × σ'(h[j])

w2[j]   += lr × outputDelta × h[j]
b2      += lr × outputDelta
w1[i,j] += lr × hiddenDelta[j] × x[i]
b1[j]   += lr × hiddenDelta[j]
```

**Training target construction:**

```csharp
double target = chooseSafe ? reward : (1.0 - reward);
```

If Safe was chosen, push the network toward `reward` (high when correct, low when wrong). If Fast was chosen, push toward `1 − reward` (inverted, since the network's output is a "safe score").

---

### `EnvironmentReward`

Computes a ground-truth reward signal that the network and evaluation loop use to assess decision quality.

**Danger estimate:**

```
danger = 0.35 × load
       + 0.30 × error
       + 0.20 × clamp01((dError + 1) / 2)
       + 0.25 × clamp01(iError)
```

| Condition | Reward |
|-----------|--------|
| Correct choice (danger ≥ 0.55 → Safe, or danger < 0.55 → Fast) | `1.0` |
| Wrong choice | `0.15` |
| Bonus: urgency > 0.75 AND Fast was correct | `+0.10` |
| Penalty: action switched from last step | `−0.05` |

The reward is always clamped to `[0, 1]`.

---

## Feature Vector

The 7-element input to the neural network, assembled each timestep:

```
index 0 → load       (raw signal, [0,1])
index 1 → error      (raw signal, [0,1])
index 2 → urgency    (raw signal, [0,1])
index 3 → DLoad      (derivative of load)
index 4 → DError     (derivative of error)
index 5 → ILoad      (decayed integral of load, [0,5])
index 6 → IError     (decayed integral of error, [0,5])
```

---

## Training Loop

```
Episodes: 3000
Log interval: every 300 steps

Per step:
  1. Sample load, error, urgency from Uniform(0,1)
  2. Update TemporalFeatureExtractor
  3. Score via HeuristicEngine, FuzzyEngine, NeuralNetwork
  4. Compute weighted combined score
  5. Choose Safe (≥0.5) or Fast (<0.5)
  6. Compute reward from EnvironmentReward
  7. Derive training target
  8. Backpropagate through NeuralNetwork
  9. Log metrics every 300 steps
```

**Sample output (console):**

```
Step  300 | AvgReward=0.6213 | NN=0.512 H=0.447 F=0.381 Combined=0.463 Action=Fast Reward=0.150
Step  600 | AvgReward=0.6418 | NN=0.558 H=0.512 F=0.420 Combined=0.503 Action=Safe Reward=1.000
...
Training complete.
```

---

## Running the Code

**Requirements:** .NET 6+ (or any .NET version supporting C# 9+)

```bash
dotnet new console -n HybridTemporalAI
# Replace Program.cs with Hybrid-Temporal-Neuro‑Fuzzy-Optimizer.cs content
dotnet run
```

Or compile directly:

```bash
csc "Hybrid-Temporal-Neuro‑Fuzzy-Optimizer.cs" -out:optimizer.exe
mono optimizer.exe   # on non-Windows
```

---

## Design Notes

- **No external dependencies.** The entire implementation — neural net, fuzzy engine, temporal features, reward — is in a single file using only `System`.
- **Online learning.** The network updates after every single timestep, not in batches.
- **Module weights** (`0.45 / 0.30 / 0.25`) are fixed. The neural net is the primary learner; heuristic and fuzzy modules provide stable priors that prevent the network from oscillating early in training.
- **Oscillation penalty** (`−0.05`) in the reward function discourages the system from rapidly alternating between Safe and Fast when danger is near the `0.55` threshold.
- **Integral cap** at `5.0` prevents runaway accumulation when the system is in a sustained high-error state.
