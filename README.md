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

### [Boat Navigator AI](boat-navigator-readme.md)

**File:** `boat-navigator.cs`  **Project:** `boat-navigator.csproj`  **GUI:** WinForms

A WinForms application that trains a neural network to navigate a boat from point A to point B through a dynamic grid of wind and water currents. Two independent engines (differential steering) and a sail are the only controls.

| Component | Details |
|-----------|---------|
| Environment | N×N grid (starts 5×5, grows on demand), each cell has wind + current vector |
| Policy Network | 13 inputs → 32 → 16 → 3 outputs (port throttle, starboard throttle, sail angle) |
| Training | 8 parallel background workers, reward-weighted regression |
| GUI | 600×600 animated grid, cell editor, live reward chart, best-path overlay |

Key features:
- **Grow grid** one cell at a time (5×5 → 6×6 → … → any size) — existing cells preserved
- **Click any cell** to edit its wind direction, wind magnitude, current direction, and current magnitude in real time
- **Arrows scale** with cell size — prominent at 5×5, fine-grained at 25×25

→ [Full documentation](boat-navigator-readme.md)

---

## Running Any Model

```bash
dotnet new console -n MyModel
# Replace Program.cs with the target .cs file content
dotnet run
```

**.NET 6+ required.** No external packages.
