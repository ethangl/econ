# Claude Context

## Project Overview

Real-time economic simulator with EU4-style map visualization.

**Stack:** Unity + C# + UI Toolkit

## Dumps / Dump Analysis

The user runs economy simulations via the EconDebugBridge in Unity, which writes results to `unity/econ_debug_output.json`. To analyze a dump:

1. **Wait for the user** to tell you a dump is ready. Do NOT trigger dumps yourself.
2. Run the analyzer: `python3 scripts/analyze_econ.py`
3. The script reads `unity/econ_debug_output.json` and prints a full summary (economy, fiscal, convergence, roads, etc.).

### Unity Instructions

**Important: Do NOT use [SerializeField] before features are complete.** Unity's serialization causes Inspector values to override code defaults, making iteration difficult. Use hardcoded constants or non-serialized fields until the feature is confirmed to work.

**Important: Do NOT use screenshots or automated visual testing for visual verification.** Ask the user to run the test scene and describe what they see.

**Important: Do NOT tell the user to check Unity console logs.** Use the MCP `read_console` tool to check logs yourself. There are 100+ logs at startup; filter appropriately.

### Review Preference

**When the user asks for a "sanity check":** prioritize checking intent alignment, logical consistency, likely regressions, and API/contract mismatches in the code. Do not start by running builds/tests unless the user asks for that explicitly.

### Unity Gotchas

No standalone csproj — this is a Unity project with assembly definitions.

**Namespace conflicts:** The `EconSim.Renderer` namespace conflicts with Unity types. Use fully qualified names:

- `UnityEngine.Camera` not `Camera`
- `MeshRenderer` not `Renderer`

**Editor scripts:** Scripts in `Assets/Editor/` need an assembly definition (`.asmdef`) that references the main `EconSim` assembly. See `Assets/Editor/EconSim.Editor.asmdef`.

**UV channels:** Unity's mesh UV naming is confusing:

- `mesh.uv` = UV0 = shader `texcoord0`
- `mesh.uv2` = UV1 = shader `texcoord1`
- The MapOverlay shader uses a single UV channel (`texcoord0`) for all texture sampling

**Triangle winding:** With Z-positive (Y-up data), triangles are wound **counter-clockwise** for the top-down camera. If mesh is invisible but exists in Scene view, check winding order.

**Material instances:** `meshRenderer.material` creates an instance; `meshRenderer.sharedMaterial` uses the asset directly. When overlay manager sets textures on a material, ensure the renderer uses the same material reference.

## Quick Reference

| What           | Where                   |
| -------------- | ----------------------- |
| Core library   | `src/EconSim.Core/`     |
| Unity frontend | `unity/Assets/Scripts/` |

### Key Classes

**EconSim.Core** (engine-independent):

- `WorldGenImporter` - Converts MapGen + PopGen results → `MapData`
- `MapData`, `Cell`, `County`, `Province`, `Realm` - Core data structures
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

- `GameManager` - Entry point, generates map, owns simulation
- `MapView` - Generates grid mesh (default) or Voronoi mesh, map modes (1=political cycle, 2=terrain with elevation tinting, 3=market), click-to-select
- `BorderRenderer` - Province/county borders as straight polyline meshes (realm borders via shader)
- `MapOverlayManager` - Generates data textures, palettes, and biome-elevation matrix for shader overlays
- `RiverRenderer` - River line strips (blue, tapered)
- `RoadRenderer` - Emergent road segments (brown)
- `SelectionHighlight` - Legacy outline (disabled when shader overlays enabled)
- `MapCamera` - WASD + drag + zoom
- `CoreExtensions` - Bridge (Vec2↔Vector2, etc.)
- `TimeControlPanel` - UI Toolkit: day display, pause/play, speed controls
- `SelectionPanel` - UI Toolkit: mode-aware political inspector (realm/province/county)

**UI Toolkit** (`Assets/UI/`):

- `Documents/MainHUD.uxml` - Main UI layout (selection panel, time controls)
- `Styles/Main.uss` - Stylesheet

## Setup

Maps are generated procedurally via the MapGen pipeline. Use the "Generate New" button on the startup screen.

## Shader-Based Overlay System

The map uses a GPU-driven overlay system for borders and map modes. See [docs/shader-overlay-system.md](docs/shader-overlay-system.md) for full details (data texture formats, palette textures, border rendering, gradient fill, selection highlight).

**Key files:** `Assets/Shaders/MapOverlayFlat.shader`, `MapOverlayBiome.shader`, `Assets/Scripts/Renderer/MapOverlayManager.cs`, `EconSim.Core/Rendering/PoliticalPalette.cs`

## Coordinate Systems

**Unity:** Y=0 at bottom for textures, Y-up in world space

MapGen uses Y-up coordinates natively (Y=0=south). MapData stores these directly — no flip. In world space, data Y maps to Z-positive. A single UV channel handles both heightmap and data texture sampling.

## County Grouping System

Cells and counties have an N:1 relationship — multiple cells form a single county. This allows cells to represent geography while counties represent the economic unit.

**Algorithm** (`PoliticalOps.GroupCounties` in MapGen):

1. High-density cells (pop ≥ 20,000) become single-cell counties (cities)
2. Highest-population cells seed county growth
3. Flood fill expands counties to target population (~5,000), max 64 cells
4. Province boundaries are respected (counties don't span provinces)
5. Orphan cells form counties from remaining highest-pop seeds
6. County seats (the seed cells) become the settlements — burgs are derived from seats, not the other way around

**Data model:**

- `Cell.CountyId` - which county a cell belongs to
- `County` - id, name, seat cell, cell list, province/realm, total population, centroid
- `MapData.Counties` / `MapData.CountyById` - county registry

**Economic integration:**

- `CountyEconomy` uses `CountyId` (not CellId)
- `Facility` has both `CellId` (physical location) and `CountyId` (economic owner)
- `EconomyState.CellToCounty` - lookup cell → county
- `EconomyState.CountyToMarket` - lookup county → market
- County resources are the union of all cell resources within it
- Trade routing uses the county seat cell for pathfinding

**Market zones:**

- Markets are assigned at county level via `CountyToMarket`
- Market zone texture maps cells to markets via their county
- Zone display has county granularity (all cells in a county share the same market)
