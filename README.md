# ai-models

Self-contained C# AI models. Each model is a single `.cs` file with no NuGet dependencies — copy the file into a `dotnet new console` project and run.

---

## Models

### [Hybrid Temporal Neuro-Fuzzy Optimizer](hybrid-temporal-neuro-fuzzy-optimizer-readme.md)

**File:** `Hybrid-Temporal-Neuro‑Fuzzy-Optimizer.cs`

A real-time binary decision system (Safe vs. Fast) that combines three independent scoring engines into a single weighted output. Learns online via backpropagation over 100,000 simulated episodes.

| Component | Role | Weight |
|-----------|------|--------|
| Neural Network (7→10→1) | Online learner | 45% |
| Heuristic Engine | Weighted rule scorer | 30% |
| Fuzzy Engine (Mamdani) | Linguistic rule inference | 25% |

- Inputs: load, error, urgency + 4 temporal features (derivatives, decayed integrals)
- Final reward at 100,000 episodes: **0.8956** (+10.9% from start)

→ [Full documentation](hybrid-temporal-neuro-fuzzy-optimizer-readme.md)

---

### [Self-Knowledge Explorer](self-knowledge-explorer-readme.md)

**File:** `self-knowledge-explorer.cs`

An autonomous agent that generates small probe programs to learn about its own decision behavior. Maintains a confidence-weighted knowledge base of 8 hypotheses and asks a human for guidance when confidence is too low to proceed alone.

| Parameter | Value |
|-----------|-------|
| Topics | 8 seeded hypotheses |
| Confidence threshold | 0.62 |
| Max iterations | 40 |
| Typical completion | ~11 iterations, avg confidence 0.85 |

Key behaviors:
- **Strikes escape:** 3 consecutive contradictions → hypothesis revised, topic closed
- **Attempts escape:** 5 runs without reaching threshold → ceiling effect accepted, topic closed
- **Non-interactive mode:** auto-accepts when stdin is redirected (CI/scripts)

→ [Full documentation](self-knowledge-explorer-readme.md)

---

## Running Any Model

```bash
dotnet new console -n MyModel
# Replace Program.cs with the target .cs file content
dotnet run
```

**.NET 6+ required.** No external packages.
