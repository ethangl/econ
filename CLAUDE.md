# Claude Context

## Project Overview

Real-time economic simulator with EU4-style map visualization. See `docs/DESIGN.md` for full design document.

**Stack:** Unity + C# + R3 + UI Toolkit

## Current Phase

**Phase 6a: Heightmap Texture Generation** (complete)

- ✓ Smooth heightmap texture from Azgaar cell data
- ✓ Gaussian blur (radius 15) for smooth cell boundaries
- ✓ Land height clamping to survive blur near coastlines
- ✓ Y-axis flip for Unity texture coordinates
- ✓ TextureDebugger utility for verification

Previous phases complete:

- ✓ Phase 1: Map Import & Rendering
- ✓ Phase 1.5: Extract Simulation Engine
- ✓ Phase 2: Simulation Foundation
- ✓ Phase 3: Markets & Trade
- ✓ Phase 4: UI Layer (time controls, county/market inspection, market map mode, economy panel)
- ✓ Phase 5: Multiple markets, black market
- ✓ Phase 5.5: Transport & Rivers (ocean/river transport, emergent roads, selection highlight)
- ✓ Phase 5.6: Shader-Based Map Overlays (GPU borders, data textures, palettes)

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
- `BorderRenderer` - State/province borders (legacy, replaced by shader system)
- `MapOverlayManager` - Generates data textures for shader-based borders/overlays
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

## Shader-Based Overlay System

The map uses a GPU-driven overlay system for borders and map modes:

**Key files:**
- `Assets/Shaders/MapOverlay.shader` - GPU border detection with anti-aliasing
- `Assets/Scripts/Renderer/MapOverlayManager.cs` - Data texture generation

**Data texture format (RGBAHalf):**
- R: StateId / 65535
- G: ProvinceId / 65535
- B: CellId / 65535
- A: MarketId / 65535

**Border detection:** Uses screen-space derivatives (ddx/ddy) with 16-sample multi-radius sampling for smooth anti-aliased borders at constant pixel width.

**Future overlays supported by this infrastructure:**
- Heat maps (population, wealth, unrest)
- Trade routes / flow visualization
- Fog of war
- War/occupation overlays

## Coordinate Systems

**Azgaar (imported maps):** Y=0 at top, increases downward (screen coordinates)

**Unity (native):** Y=0 at bottom for textures, Y-up in world space

Currently, Azgaar coordinates are used internally in `MapData`, `Cell`, etc. The Y-flip happens at texture generation time in `MapOverlayManager`. This is a pragmatic choice to minimize refactoring.

**Future consideration:** If we generate our own maps, use Unity coordinates natively and remove the Y-flip in texture generation. Ideally, the flip would happen once at the Azgaar import boundary (`AzgaarParser`/`MapConverter`) so all internal data uses Unity conventions.
