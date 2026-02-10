# EconSim

Economic simulation on a deterministic procedural world.

This repository contains:

- `src/MapGen`: engine-agnostic world generation pipeline.
- `src/EconSim.Core`: simulation and shared core logic.
- `unity/`: Unity client, rendering, UI, and tests.
- `docs/`: architecture notes, debugging playbooks, backlog, and design docs.

## Getting Started

1. Open `/Users/ethan/w/econ/unity` in Unity (`6000.3.6f1`).
2. Load scene: `Assets/Scenes/Main.unity`.
3. Press Play.
4. Use the startup panel to generate a map.

## Keyboard Controls

Full control reference:

- `/Users/ethan/w/econ/docs/KEYBOARD_CONTROLS.md`

Quick map/debug keys:

- `1` Political mode
- `2` Terrain mode
- `3` Market mode
- `4` Soil mode
- `0` Channel Inspector mode
- `O` Cycle Channel Inspector views
- `P` Toggle ID probe overlay

Simulation:

- `Backspace` Pause/unpause

## Debugging

- Shader/overlay workflow:
  - `docs/debug/SHADER_OVERLAY_DEBUGGING.md`
- General project context:
  - `docs/overview.md`
- Active backlog:
  - `docs/BACKLOG.md`
