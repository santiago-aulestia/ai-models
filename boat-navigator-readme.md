# Boat Navigator AI

A self-contained C# WinForms application that trains a neural network to steer a boat from point A to point B through a grid of dynamic wind and water current conditions. Two independent engines and a sail are the only controls. The agent learns purely from environmental feedback over thousands of parallel training episodes.

---

## Overview

The simulator presents a navigable ocean grid where every cell has its own wind vector and water current vector, both of which evolve over time. A policy neural network observes the boat's position, velocity, heading, and the local environmental forces at each step, then outputs throttle commands for the port engine, the starboard engine, and the sail angle. Eight training workers run in parallel on background threads, sharing weights and accumulating experience until the boat learns to reach the destination consistently.

---

## Architecture

```
EnvironmentGrid (N×N cells)
  wind vector per cell ─────────────────────────────────┐
  current vector per cell ──────────────────────────────┤
                                                         ▼
                                              BoatPhysics.Step()
                                                │
                                   ┌────────────┴────────────┐
                              BoatState                  StepResult
                         (pos, vel, heading, trail)   (reward, done)
                                   │
                                   ▼
                          TrainingManager
                    ┌──────────────────────────┐
                    │  8 parallel Task workers  │
                    │  StateToFeatures(13D)     │
                    │  PolicyNetwork.Forward()  │
                    │  + exploration noise      │
                    │  PolicyNetwork.TrainBatch │
                    └──────────────────────────┘
                                   │
                            NavigatorForm
                     600×600 animated grid + controls
```

---

## Components

### `EnvironmentGrid`

A dynamic N×N grid of wind and current vectors.

| Method | Behavior |
|--------|----------|
| `EnvironmentGrid(int initialSize)` | Starts at the given size (default 5) |
| `Grow()` | Adds one row and one column; existing cells are preserved |
| `Reset()` | Randomizes all cells using sinusoidal base patterns |
| `Update()` | Advances the environment one timestep with small random drift |
| `GetWindAt(x, y)` | Bilinear interpolation of the wind field |
| `GetCurrentAt(x, y)` | Bilinear interpolation of the current field |
| `SetWindCell(col, row, angleDeg, mag)` | Directly set one cell's wind |
| `SetCurrentCell(col, row, angleDeg, mag)` | Directly set one cell's current |

Wind magnitude range: `[0.02, 0.12]`. Current magnitude range: `[0.01, 0.06]`.

---

### `BoatPhysics`

Stateless physics engine applied each simulation step.

**Physics per step:**

```
new_heading = heading + TurnRate × (port − star)
thrust      = FromAngle(new_heading) × (port + star) × EnginePower
sail_force  = wind.Normalized × cos(windAngle − heading) × SailAngle × SailEfficiency
current     = GetCurrentAt(pos)
new_vel     = (old_vel + thrust + sail_force) × Drag + current × 0.30
new_vel     = clamp(new_vel, MaxSpeed)
new_pos     = old_pos + new_vel
```

| Constant | Value |
|----------|-------|
| `MaxSpeed` | 0.15 grid units/step |
| `Drag` | 0.96 |
| `EnginePower` | 0.06 |
| `TurnRate` | 0.12 rad/step per unit differential |
| `SailEfficiency` | 0.04 |
| `ArrivalRadius` | 1.2 grid units |

**Point A and B** are placed at 10% margin from each corner and update automatically when the grid grows (`UpdatePoints(gridSize)`).

**Reward:**

| Condition | Reward |
|-----------|--------|
| Arrived at B | `+10.0` |
| Out of bounds | `−2.0` |
| Otherwise | `1.0 − dist / diagonal` (dense, 0–1 range) |

---

### `PolicyNetwork`

Manual-backprop feedforward network.

| Layer | Size | Activation |
|-------|------|------------|
| Input | 13 | — |
| Hidden 1 | 32 | tanh |
| Hidden 2 | 16 | tanh |
| Output | 3 | sigmoid |

**Input features (13):**

| Index | Feature |
|-------|---------|
| 0–1 | Normalized position (x/size, y/size) |
| 2–3 | Normalized velocity (vx/MaxSpeed, vy/MaxSpeed) |
| 4–5 | sin/cos of heading |
| 6–7 | sin/cos of angle to target |
| 8 | Normalized distance to target |
| 9–10 | Wind vector at position (clamped ±1) |
| 11–12 | Current vector at position (clamped ±1) |

**Outputs (3):** `[port_throttle, star_throttle, (sail_angle+1)/2]` — all in `[0,1]` via sigmoid.

**Training:** reward-weighted regression — episodes above the recent average are used as supervised examples, weighted by `max(0, reward) / (|avg| + 1)`. Learning rate `0.003`, gradient clipped to `[−1, 1]`.

---

### `TrainingManager`

Manages 8 parallel background `Task` workers. Each worker:

1. Runs a full episode (up to 1000 steps) using `Policy.Forward()` plus Gaussian exploration noise (σ = 0.15)
2. Accumulates `(state, action)` pairs
3. Locks the shared policy to submit the training batch
4. Fires `Updated` event; the form marshals this to the UI thread via `BeginInvoke`

Workers share a single `PolicyNetwork` instance protected by a lock. Episode statistics (total reward, best reward, running average) accumulate across all workers.

---

## GUI

```
┌──────────────────────────────────────────────┬─────────────────────────┐
│                                              │  ⚓ Boat Navigator AI    │
│         600 × 600 px animated grid          │  [▶ Start Training]      │
│                                              │  [■ Stop]                │
│  Yellow arrows  → wind direction             │  [↺ Reset]               │
│  Cyan arrows    → water current              │                          │
│  White triangle → boat (heading = nose)      │  Grid: 5×5  [+ Grow]    │
│  Green dot trail → boat history              │  Episodes: 0             │
│  Green line     → best learned path          │  Best Reward: –          │
│  [A] green      → start                      │  Avg (50): –             │
│  [B] red        → destination                │  Status: Idle            │
│                                              │  [reward chart]          │
│  Click any cell → select for editing         │  Demo Speed: ────        │
│  Selected cell outlined in white             │                          │
│                                              │  ── Cell Editor ──       │
│                                              │  Cell (col, row)         │
│                                              │  Wind Dir (°): [___]     │
│                                              │  Wind Mag:     [___]     │
│                                              │  Curr Dir (°): [___]     │
│                                              │  Curr Mag:     [___]     │
└──────────────────────────────────────────────┴─────────────────────────┘
```

### Cell Editor

Click any grid cell to select it (white border appears). The **Cell Editor** panel on the right immediately shows the cell's current wind and current values. Editing any value takes effect on the grid in real time — no Apply button needed.

| Control | Range | Description |
|---------|-------|-------------|
| Wind Dir (°) | 0–360 | Wind direction in degrees (0 = east, 90 = south) |
| Wind Mag | 0.02–0.12 | Wind force magnitude |
| Curr Dir (°) | 0–360 | Current direction in degrees |
| Curr Mag | 0.01–0.06 | Current force magnitude |

### Grid Size

The grid starts at 5×5. Click **+ Grow** to add one row and column at a time (6×6, 7×7, …). Growing:
- Preserves all existing cell values
- Resets the neural network (new grid = new state space)
- Moves Point A and B to maintain 10% margin proportions
- Stops any in-progress training

Cells grow proportionally to always fill the 600×600 display area.

---

## Running

**Requirements:** .NET 9, Windows

```bash
dotnet new console -n BoatNavigatorAI
# Replace Program.cs with boat-navigator.cs content
dotnet run
```

Or use the pre-built executable:

```
.\bin\boat-navigator.exe
```

The executable is in `./bin/` and requires the .NET 9 runtime installed on the machine.

---

## Design Notes

- **No external dependencies.** Single file using only `System`, `System.Drawing`, and `System.Windows.Forms`.
- **Differential steering.** Port engine faster than starboard → turns right; starboard faster → turns left. Equal throttle = straight ahead.
- **Sail physics.** Sail effectiveness depends on the angle between wind direction and boat heading. A sail perpendicular to the wind (`cos ≈ 1`) provides maximum force; a sail aligned with the wind provides none.
- **Dynamic grid size.** `EnvironmentGrid` allocates arrays at runtime and reallocates on `Grow()`, copying existing data. Arrow rendering scales proportionally — a 5×5 grid shows large cells with prominent arrows; a 25×25 grid shows fine-grained detail.
- **Reward shaping.** The dense reward `1 − dist/diagonal` provides a gradient signal at every step, not just on arrival. This accelerates learning compared to sparse terminal rewards.
- **Thread safety.** All `PolicyNetwork` reads and writes are wrapped in a single shared lock. Workers read the same weights but batch updates are serialized.

---

## Use Cases

**Autonomous vehicle navigation research.** The differential engine + sail model is a simplified analog of underactuated vehicles (boats, gliders, aircraft) that cannot thrust in all directions. The grid environment provides a clean testbed for studying how learned policies generalize across varying force fields.

**Curriculum learning experiments.** Start with a 5×5 grid (short paths, simple currents), train to convergence, then grow to 6×6 and retrain. The preserved cell data means the new grid inherits the learned environment structure at the edges while adding complexity in the new area.

**Environmental condition design.** The cell editor lets you hand-craft scenarios — a strong crosscurrent in the middle, a headwind at the destination, a sheltered channel — and observe how the learned policy responds without rewriting any code.

**Teaching reinforcement learning.** The reward chart, episode counter, best path overlay, and live boat animation make the learning process fully transparent. Each component (physics, policy, reward, exploration noise) is isolated and inspectable, making it useful for classroom or self-study demonstrations of policy gradient methods.

**Benchmarking control algorithms.** Swap `PolicyNetwork.Forward()` for any other continuous control policy (PID controller, A* planner, Q-network) to compare performance on the same environment under identical conditions.
