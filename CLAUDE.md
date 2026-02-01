# Claude Context

## Project Overview

Real-time economic simulator with EU4-style map visualization. See `docs/DESIGN.md` for full design document.

**Stack:** Unity + C# + R3 + UI Toolkit

## Current Phase

**Phase 5.5: Transport & Rivers** (complete)

- ✓ Ocean transportation (sea routes with port transition costs)
- ✓ River rendering (blue tapered line strips)
- ✓ Selection highlight (yellow outline on selected cell)
- ✓ Emergent roads (form from trade traffic, path→road tiers)
- ✓ UI improvements (county map mode, mode-aware inspectors, click-through fix)

Previous phases complete:

- ✓ Phase 1: Map Import & Rendering
- ✓ Phase 1.5: Extract Simulation Engine
- ✓ Phase 2: Simulation Foundation
- ✓ Phase 3: Markets & Trade
- ✓ Phase 4: UI Layer (time controls, county/market inspection, market map mode, economy panel)
- ✓ Phase 5: Multiple markets, black market

See `docs/DESIGN.md` → Development Roadmap for full status.

## Quick Reference

| What           | Where                     |
| -------------- | ------------------------- |
| Design doc     | `docs/DESIGN.md`          |
| Core library   | `src/EconSim.Core/`       |
| Unity frontend | `unity/Assets/Scripts/`   |
| Reference maps | `reference/` (gitignored) |

### Key Classes

**EconSim.Core** (engine-independent):

- `AzgaarParser` - Parses Azgaar JSON → `AzgaarMap`
- `MapConverter` - Converts `AzgaarMap` → `MapData`
- `MapData`, `Cell`, `Province`, `State` - Core data structures
- `ISimulation`, `SimulationRunner` - Tick loop interface/implementation
- `ITickSystem` - Interface for simulation subsystems
- `EconomyState` - Global economy (registries, counties, facilities)
- `GoodDef`, `FacilityDef` - Static definitions for goods/facilities
- `Facility`, `Stockpile`, `CountyPopulation` - Runtime economic state
- `ProductionSystem` - Extraction and processing each tick
- `ConsumptionSystem` - Population consumption each tick
- `TransportGraph` - Dijkstra pathfinding on cell graph with terrain costs
- `Market`, `MarketPlacer` - Market data structure and placement algorithm
- `TradeSystem` - Trade flows between counties and markets, price discovery, black market integration, road traffic recording
- `TheftSystem` - Daily stockpile theft feeding black market
- `RoadState` - Tracks traffic per edge, determines road tier (path/road)

**Unity** (symlinks `EconSim.Core/` from `src/`):

- `GameManager` - Entry point, loads map, owns simulation
- `MapView` - Generates Voronoi mesh, map modes (1=political cycle, 2=terrain, 3=height, 4=market), click-to-select
- `BorderRenderer` - State/province borders
- `RiverRenderer` - River line strips (blue, tapered)
- `RoadRenderer` - Emergent road segments (brown)
- `SelectionHighlight` - Yellow outline on selected cell
- `MapCamera` - WASD + drag + zoom
- `CoreExtensions` - Bridge (Vec2↔Vector2, etc.)
- `TimeControlPanel` - UI Toolkit: day display, pause/play, speed controls
- `SelectionPanel` - UI Toolkit: mode-aware political inspector (country/province/county)
- `MarketInspectorPanel` - UI Toolkit: market inspection (hub, zone, goods table)
- `EconomyPanel` - UI Toolkit: global economy (E key, tabbed: overview/production/trade)

**UI Toolkit** (`Assets/UI/`):

- `Documents/MainHUD.uxml` - Main UI layout (selection panel, market panel, time controls)
- `Styles/Main.uss` - Stylesheet

## Setup

Reference data is gitignored. To set up:

1. Export a map from [Azgaar's FMG](https://azgaar.github.io/Fantasy-Map-Generator/) as JSON
2. Place in `reference/`

Current map: seed 1234, low island, 40k points, 1440x810
