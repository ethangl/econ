# Claude Context

## Project Overview

Real-time economic simulator with EU4-style map visualization. See `docs/DESIGN.md` for full design document.

**Stack:** Unity + C# + R3 + UI Toolkit

## Current Phase

**Phase 2: Simulation Foundation** (next)

Previous phases complete:
- ✓ Phase 1: Map Import & Rendering
- ✓ Phase 1.5: Extract Simulation Engine

See `docs/DESIGN.md` → Development Roadmap for full status.

## Quick Reference

| What | Where |
|------|-------|
| Design doc | `docs/DESIGN.md` |
| Core library | `src/EconSim.Core/` |
| Unity frontend | `unity/Assets/Scripts/` |
| Reference maps | `reference/` (gitignored) |

### Key Classes

**EconSim.Core** (engine-independent):
- `AzgaarParser` - Parses Azgaar JSON → `AzgaarMap`
- `MapConverter` - Converts `AzgaarMap` → `MapData`
- `MapData`, `Cell`, `Province`, `State` - Core data structures

**Unity**:
- `GameManager` - Entry point, loads map
- `MapView` - Generates Voronoi mesh, map modes
- `BorderRenderer` - State/province borders
- `MapCamera` - WASD + drag + zoom
- `CoreExtensions` - Bridge (Vec2↔Vector2, etc.)

## Setup

Reference data is gitignored. To set up:
1. Export a map from [Azgaar's FMG](https://azgaar.github.io/Fantasy-Map-Generator/) as JSON
2. Place in `reference/`

Current map: seed 1234, low island, 40k points, 1440x810
