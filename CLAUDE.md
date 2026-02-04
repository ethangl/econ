# Claude Context

## Project Overview

Real-time economic simulator with EU4-style map visualization. See `docs/DESIGN.md` for full design document.

**Stack:** Unity + C# + R3 + UI Toolkit

### Unity Instructions

**Important: Do NOT use [SerializeField] before features are complete.** Unity's serialization causes Inspector values to override code defaults, making iteration difficult. Use hardcoded constants or non-serialized fields until the feature is confirmed to work.

**Important: Do NOT use screenshots or automated visual testing for visual verification.** Ask the user to run the test scene and describe what they see.

### Unity Gotchas

**Namespace conflicts:** The `EconSim.Renderer` namespace conflicts with Unity types. Use fully qualified names:

- `UnityEngine.Camera` not `Camera`
- `MeshRenderer` not `Renderer`

**Editor scripts:** Scripts in `Assets/Editor/` need an assembly definition (`.asmdef`) that references the main `EconSim` assembly. See `Assets/Editor/EconSim.Editor.asmdef`.

**UV channels:** Unity's mesh UV naming is confusing:

- `mesh.uv` = UV0 = shader `texcoord0`
- `mesh.uv2` = UV1 = shader `texcoord1`
- The MapOverlay shader uses `texcoord1` for data texture sampling

**Triangle winding:** For top-down orthographic camera, triangles must be wound **clockwise** (reversed from typical). If mesh is invisible but exists in Scene view, check winding order.

**Material instances:** `meshRenderer.material` creates an instance; `meshRenderer.sharedMaterial` uses the asset directly. When overlay manager sets textures on a material, ensure the renderer uses the same material reference.

## Quick Reference

| What           | Where                     |
| -------------- | ------------------------- |
| Design doc     | `docs/DESIGN.md`          |
| Changelog      | `docs/CHANGELOG.md`       |
| Core library   | `src/EconSim.Core/`       |
| Unity frontend | `unity/Assets/Scripts/`   |
| Reference maps | `reference/` (gitignored) |

### Key Classes

**EconSim.Core** (engine-independent):

- `AzgaarParser` - Parses Azgaar JSON → `AzgaarMap`
- `MapConverter` - Converts `AzgaarMap` → `MapData`
- `MapData`, `Cell`, `County`, `Province`, `State` - Core data structures
- `CountyGrouper` - Groups cells into counties based on population density
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
- `MapView` - Generates grid mesh (default) or Voronoi mesh, map modes (1=political cycle, 2=terrain with elevation tinting, 3=market), click-to-select
- `GridMeshTest` - Test harness for grid mesh rendering (validates UV mapping, winding order)
- `BorderRenderer` - State/province borders (legacy, replaced by shader system)
- `MapOverlayManager` - Generates data textures, palettes, and biome-elevation matrix for shader overlays
- `RiverRenderer` - River line strips (blue, tapered)
- `RoadRenderer` - Emergent road segments (brown)
- `SelectionHighlight` - Legacy outline (disabled when shader overlays enabled)
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

Current map: seed 1234, low island, 40k points, 1920x1080 (map dimensions are read from JSON, not hardcoded)

## Shader-Based Overlay System

The map uses a GPU-driven overlay system for borders and map modes:

**Key files:**

- `Assets/Shaders/MapOverlay.shader` - GPU border detection, color derivation
- `Assets/Scripts/Renderer/MapOverlayManager.cs` - Data texture generation
- `EconSim.Core/Rendering/PoliticalPalette.cs` - State color generation

**Data texture format (RGBAFloat):**

- R: StateId / 65535
- G: ProvinceId / 65535
- B: (BiomeId + WaterFlag) / 65535 — water flag adds 32768 if cell is water
- A: CountyId / 65535 — enables per-county coloring in county mode (32-bit float for precision)

**Palette textures:**

- State palette (256x1): colors from PoliticalPalette (even hue distribution)
- Market palette (256x1): derived from hub state colors
- Biome palette (256x1): colors from Azgaar data (unused, kept for reference)
- Biome-elevation matrix (64x64): biome colors with elevation-based shading
- River mask (gridWidth×gridHeight, R8): rasterized river paths for water knockout

**Biome-elevation matrix (terrain mode):**

- X axis (U) = biome ID (0-63)
- Y axis (V) = normalized land elevation (0 = sea level, 1 = max height)
- Uses Azgaar's absolute elevation scale: 0% = height 20, 100% = height 100
- Brightness gradient: 0.4 at sea level → 1.0 at high elevation
- Snow blend at 85%+ elevation (Azgaar height 88-100)
- Low-elevation maps (islands) won't show snow — this is intentional

**Color derivation (shader-based cascade):**

- Political mode: state color from palette
- Province mode: derived from state color + hash(provinceId) HSV variance (±0.07 S/V)
- County mode: derived from province color + hash(countyId) HSV variance (±0.07 S/V)
- State HSV: S base 0.52 [0.38, 0.68], V base 0.70 [0.58, 0.85]
- Derived HSV clamp: S [0.15, 0.95], V [0.25, 0.95] (allows ± variance around parent)

**Market zone mapping:**

- Separate `cellToMarketTexture` (16384x1, RHalf)
- Maps CellId → MarketId via CountyToMarket lookup
- Updated when economy state changes

**State border rendering (shader-based double borders):**

- `CalculateStateBorderProximity()` detects only state-to-state boundaries (ignores water/rivers)
- World-space sizing: `_StateBorderWidth` in texels of data texture (default 24)
- Border band drawn when `stateBorderProximity < 0.5`
- Border color: state color at 65% V (floor 35%), multiplied with grayscale terrain
- `_StateBorderOpacity` controls blend with interior color
- Each country gets its own colored border band → parallel double-line effect at boundaries

**Province/county border rendering (mesh-based):**

- `BorderRenderer.cs` generates polyline meshes from Voronoi cell edges
- Edges chained into continuous polylines
- Province/county borders use shared edge positions (single line)
- Colors derived from `PoliticalPalette` (darkened, saturated state colors)
- `SimpleBorder.shader` renders vertex-colored border meshes
- Border layer order (bottom to top): county → province → state

**River mask texture (Phase 8):**

- `_RiverMaskTex` (R8): 1 = river, 0 = not river
- Generated by rasterizing river paths with Catmull-Rom spline smoothing
- Width tapers from 30% at source to 100% at mouth (matches RiverRenderer)
- Rivers are "knocked out" from land — shader combines `isCellWater || isRiver` for water detection
- Rivers count as edges for gradient fill calculation (darkening near rivers)
- Circular caps at river endpoints fill gaps in gradient coverage

**Gradient fill system (Phase 8):**

Political modes (1/2/3) and market mode (4) use a gradient fill style:

- Edge proximity calculated by sampling 8 directions at 8 radii (6.25% to 50% of max radius)
- Water cells and rivers both count as edges (gradient darkens near coastlines and rivers)
- Multiply blend at edges: `terrain × politicalColor` (Photoshop-style multiply)
- Gradient from dark multiplied edge to full color in center
- Borders rendered via mesh-based `BorderRenderer` (separate from shader gradient)

Shader uniforms for gradient control:

- `_GradientRadius` (default 40) - how far from edges the gradient extends (pixels)
- `_GradientEdgeDarkening` (default 0.5, range 0-1) - multiply blend strength at edges
- `_GradientCenterOpacity` (default 0.5, range 0-1) - how much terrain shows through in center

Shader renders terrain with gradient fill; borders are separate mesh layer on top.

**Selection highlight:** Mode-aware GPU selection using 16-sample multi-radius AA. Selects state in political mode, province in province mode, market zone in market mode, county otherwise. Uniforms: `_SelectedStateId`, `_SelectedProvinceId`, `_SelectedMarketId`, `_SelectedCountyId` (normalized, -1 = none).

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

## County Grouping System

Cells and counties have an N:1 relationship — multiple cells form a single county. This allows cells to represent geography while counties represent the economic unit.

**Algorithm (`CountyGrouper`):**

1. High-density cells (pop ≥ 500) become single-cell counties (cities)
2. Burg cells seed county growth
3. Flood fill expands counties to target population (~200), max 12 cells
4. Province boundaries are respected (counties don't span provinces)
5. Orphan cells form rural counties

**Data model:**

- `Cell.CountyId` - which county a cell belongs to
- `County` - id, name, seat cell, cell list, province/state, total population, centroid
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
