# Changelog

Development phases and completed work for the Economic Simulator project.

## Phase 12: Procedural Map Generation Pipeline ✓

Replaced Azgaar JSON import with a fully custom, engine-agnostic map generation library (`src/MapGen/`). Maps are now generated from scratch — no external data files required.

- **Standalone MapGen library** (`src/MapGen/`)
  - ~7,500 lines of C#, zero Unity dependencies (`noEngineReferences: true`)
  - Symlinked into Unity project for seamless editor integration
  - Fully deterministic: same seed + config → identical map every time
  - Headless orchestrator (`MapGenPipeline.cs`) runs 6-stage pipeline in one call

- **Stage 1: Voronoi mesh generation**
  - Jittered grid point placement for ~10,000 cells (configurable)
  - Delaunay triangulation → dual Voronoi diagram
  - Cell/vertex/edge connectivity with neighbor graphs and boundary detection

- **Stage 2: Heightmap sculpting via DSL**
  - Domain-specific language with operations: Hill, Ridge, Smooth, Pit, Trough, Multiply, Mask
  - 14 terrain templates: Volcano, LowIsland, Archipelago, Continents, Pangea, HighIsland, Atoll, Peninsula, Mediterranean, Isthmus, Shattered, Taklamakan, OldWorld, Fractious
  - BFS blob growth matching Azgaar's integer-precision behavior
  - Heights 0-100 with water threshold at 20

- **Stage 3: Climate simulation**
  - Temperature: latitude-based with tropical plateau, cosine polar falloff, altitude lapse rate
  - Precipitation: wind-sweep moisture propagation across neighbor graph
    - Multiple wind bands weighted by latitude overlap
    - Orographic lift/rain shadow, coastal bonus, permafrost damping
    - 4th-root normalization (exponent 0.225) for realistic distribution

- **Stage 4: River extraction**
  - Vertex-level height/precipitation interpolation
  - Priority flood depression filling (handles sinks and plateaus)
  - Steepest-descent flow accumulation on Voronoi vertices
  - River polylines extracted for edges exceeding flux threshold

- **Stage 5: Biomes, suitability & resources**
  - 17-step biome pipeline: lakes, slope, soil, fertility, 16 biome categories
  - Rock types (granite, basalt, sandstone, limestone, shale) via deterministic noise
  - Geological resources: iron, gold, lead deposits; salt near coasts
  - Suitability scoring with geographic bonuses (coastal, estuary, confluence, harbor, defensibility)
  - Population derived from suitability × cell area

- **Stage 6: Political hierarchy**
  - Landmass detection (flood-fill, filters noise islands)
  - Capital placement via suitability-weighted Voronoi with spacing constraints
  - State growth from capitals (~200k pop target), merge small realms
  - Province subdivision (~40k pop target) within state boundaries
  - County grouping: cities (pop ≥ 20k) as single-cell; flood-fill rural clusters (~5k pop, max 64 cells)

- **MapGenAdapter** (`src/EconSim.Core/Import/MapGenAdapter.cs`)
  - Converts `MapGenResult` → `MapData` for the EconSim engine
  - Computes coast distance (BFS), water features (flood-fill), river cell paths
  - Builds full political hierarchy: states, provinces, counties with burgs

- **Removed legacy import pipeline**
  - Deleted `AzgaarParser`, `AzgaarData`, `MapConverter`, `CountyGrouper`, `MapDataCache`
  - Deleted JSON loading infrastructure (~1,700 lines removed)
  - No external map files needed — startup screen "Generate New" button works end-to-end

- **Prototype Unity project** (`unity-mapgen/`)
  - Standalone test project for iterating on MapGen stages
  - Editor inspectors for each pipeline stage (heightmap, climate, biomes, politics)
  - Cell mesh visualizer with per-stage gizmo overlays

- **Biome/resource documentation** (`docs/biomes/`)
  - Pipeline overview, soil classification, biome definitions
  - Vegetation, fauna, movement cost, and resource extraction specs

---

## Phase 11: Relaxed Border System ✓

- **Relaxed cell geometry** (`RelaxedCellGeometry.cs`)
  - Organic curved cell boundaries replace straight Voronoi edges
  - Catmull-Rom spline interpolation for smooth curves
  - Deterministic noise displacement (amplitude, frequency parameters)
  - Shared edges cached by sorted vertex pair (ensures symmetry)
  - Map boundary edges kept straight (no relaxation at map edges)
  - Parameters: Amplitude=1.2, Frequency=0.36, SamplesPerSegment=5

- **Noise utilities** (`NoiseUtils.cs`)
  - `HashToFloat()` - deterministic hash to [-1, 1] range
  - `HashCombine()` - combine multiple ints into single seed

- **Border rendering updates** (`BorderRenderer.cs`)
  - Uses multi-point relaxed edges instead of straight vertex-to-vertex
  - Edge chaining updated to concatenate point lists
  - Curved province/county borders align with texture boundaries

- **Texture rasterization** (`MapOverlayManager.cs`)
  - Hybrid approach: fast Voronoi base + boundary refinement
  - Phase 1: Nearest-center fill for complete coverage (no gaps)
  - Phase 2: Point-in-polygon refinement for boundary pixels only
  - Refinement radius scales with amplitude parameter
  - Texture edges align with mesh borders (shared relaxed geometry)
  - Spatial grid cache key includes relaxed parameters

---

## Phase 10: Startup Screen & UI Polish

- **Startup screen modal**
  - Centered overlay on game launch with "Load Map" and "Generate New" buttons
  - "Generate New" stubbed for future procedural map generation
  - Dark semi-transparent background covers camera noise
  - Hides automatically when map finishes loading

- **Deferred map loading**
  - Map no longer auto-loads on Start()
  - `GameManager.OnMapReady` event fires when map and simulation are ready
  - `GameManager.IsMapReady` property for synchronous checks
  - UI panels subscribe to OnMapReady before accessing simulation

- **Removed domain warping from spatial grid generation**
  - Cell boundaries now use clean Voronoi edges (straight lines to cell centers)
  - Removed `WarpAmplitude`, `WarpFrequency`, `WarpOctaves` constants
  - Removed fractal noise functions (`HashNoise`, `HashToFloat`, `FractalNoise`)
  - Simplified `BuildSpatialGridFromScratch` to use direct coordinates
  - Updated cache key to invalidate old warped grids

- **Border rendering overhaul** (Phase 10b)
  - Province/county borders: mesh-based polyline rendering
    - `BorderRenderer.cs`: chains Voronoi edges into polylines
    - Per-state coloring derived from `PoliticalPalette` (darkened, saturated)
    - `SimpleBorder.shader`: vertex color rendering for border meshes
  - State borders: shader-based double border effect
    - `CalculateStateBorderProximity()` detects only state-to-state boundaries (ignores water/rivers)
    - World-space sizing via `_StateBorderWidth` (texels of data texture, default 24)
    - Border color: state color at 65% V (floor 35%), multiplied with terrain
    - `_StateBorderOpacity` slider for blend control
    - Each country's border band in its own hue → parallel double-line effect at boundaries

---

## Phase 9: Gradient Fill System ✓

### Completed

- **Land-only borders**
  - Borders only render between neighboring land cells (no coastline borders)
  - Water cells skipped in border detection
  - Selection borders still appear at coastlines

- **Gradient fill for political modes** (state/province/county)
  - Edge proximity calculated by sampling 8 directions at 8 radii
  - Multiply blend at edges: `terrain × politicalColor`
  - Gradient from dark multiplied edge to full color in center

- **Province/county borders rendered under political fill**
  - State borders always on top
  - Border layer order: market → county → province → state

- **Gradient fill for market mode**
  - Same gradient style as political modes
  - Market borders on top of fill, political borders underneath

- **River mask texture**
  - `_RiverMaskTex` (R8): 1 = river, 0 = not river
  - Generated by rasterizing river paths with Catmull-Rom spline smoothing
  - Width tapers from 30% at source to 100% at mouth (matches RiverRenderer)
  - Circular caps at river endpoints fill gaps

- **Rivers knocked out from land**
  - Shader combines `isCellWater || isRiver` for water detection
  - Rivers show water color instead of land terrain

- **Rivers treated as edges for gradient fill**
  - Water cells and rivers both count as edges
  - Gradient darkens near coastlines and rivers

- **Startup performance optimization** (68s → 12.7s)
  - `StartupProfiler` utility for hierarchical timing of startup phases
  - MapData binary caching (`MapDataCache.cs`) - skips JSON parsing on subsequent loads
  - Spatial grid caching - most expensive operation: 45.5s → instant on cache hit
  - River mask caching - 4.2s → 1.2s cached
  - Parallelized texture generation with striped row locks - 2.5s → 647ms
  - Cold start ~32s, warm start 12.7s

- **Shader compile time optimization**
  - Converted MapOverlay from surface shader to vert/frag shader
  - Removed unused lighting model (was custom unlit anyway)
  - Faster iteration during shader development

- **Camera tilt system**
  - Camera pitch dynamically interpolates based on zoom level
  - Zoomed out (max): 75° (more top-down, strategic view)
  - Zoomed in (min): 50° (more angled, detailed view)
  - All camera math updated to account for tilted view angle
  - FocusOn, ApplyConstraints, CenterOnMap use look-at point calculations
  - Bounds constraints work correctly with any pitch angle

### Remaining

- Water shader refinement
- Selection shader refinement

---

## Phase 8: County Grouping System ✓

- **N:1 cell-to-county relationship**
  - Multiple cells can form a single county (previously 1:1 cell=county)
  - County class: Id, Name, ProvinceId, CellIds (list), Population
  - Cell.CountyId links each cell to its parent county
  - CountyEconomy keyed by CountyId (int), not CellId (string)

- **CountyGrouper algorithm**
  - Flood-fill algorithm groups adjacent cells within same province
  - Population thresholds: 500-2000 target per county
  - Province boundaries respected (no cross-province counties)
  - Larger provinces get more counties, small ones may be single county

- **Economy migration**
  - CountyEconomy keyed by County.Id (int) instead of cell identifier
  - Facilities assigned to counties, not cells
  - Resources aggregated across county's cells
  - Population pooled at county level

- **Data texture update**
  - A channel changed from CellId to CountyId
  - Enables shader-based county coloring and border detection
  - Selection uniforms: `_SelectedCountyId` (was cell-based)

- **County border rendering**
  - Shader properties: `_CountyBorderColor`, `_CountyBorderWidth`, `_ShowCountyBorders`
  - CalculateBorderAA extended to support A channel (channel 2)
  - Compositing order: market → county → province → state
  - County borders shown only in County map mode

- **Logging cleanup**
  - Disabled periodic runtime logging (Production, Consumption, Trade, Roads)
  - Initialization logs preserved for debugging

---

## Phase 7: Rendering Refinement ✓

- **Political color generation**
  - State colors: even hue distribution across spectrum, hash-based S/V variance
  - Province colors: derived in shader from state color + hash(provinceId) variance
  - County colors: derived in shader from province color + hash(cellId) variance
  - HSV clamping: S [0.25, 0.60], V [0.45, 0.80] to avoid conflict with UI/borders
  - Unowned cells: neutral grey (0.5, 0.5, 0.5)

- **Data texture restructure**
  - R: StateId, G: ProvinceId, B: BiomeId+WaterFlag, A: CellId (was MarketId)
  - CellId enables true per-cell flat shading in county mode
  - Separate `cellToMarketTexture` (16384x1) for dynamic market zone mapping
  - Market zones can now change without regenerating main data texture

- Removed heightmap map mode (redundant with elevation tinting)

---

## Phase 6d: Palette-Based Map Rendering ✓

- Refactored from blurred color textures to palette lookups
  - Removed expensive bilateral blur on 4 textures (~200 lines removed)
  - Shader samples data texture → looks up color from 256x1 palette
  - Faster initialization, smaller memory footprint

- Biome palette texture for terrain mode (colors from Azgaar biome data)

- Water detection decoupled from heightmap
  - Water flag packed into data texture B channel (BiomeId + 32768 if water)

- Instant update capability for ownership changes

---

## Phase 6c: Heightmap Integration ✓

- Shader vertex displacement from heightmap
- Shader-computed normals from heightmap gradients
- Height-based coloring (used for water depth)
- Biome texture for Terrain map mode with elevation tinting
- Grid mesh integrated into MapView (default renderer)
- Voronoi fallback toggle (context menu "Toggle Grid Mesh")

---

## Phase 6b: Grid Mesh Test ✓

- Grid mesh renders correctly with data texture sampling
- UV mapping verified (UV1/mesh.uv2 → shader texcoord1)
- Triangle winding order for top-down camera (clockwise)
- Vertex colors for water/fallback areas
- Test scene and editor tooling (`GridMeshTest.cs`, `GridMeshTestSceneBuilder.cs`)

---

## Phase 6a: Heightmap Texture Generation ✓

- Heightmap texture from Azgaar cell data
- Y-axis flip for Unity texture coordinates (Azgaar uses Y-down)
- Bilinear filtering for smooth sampling
- TextureDebugger utility for verification
- Shader property added (`_HeightmapTex`)

---

## Phase 5.6: Shader-Based Map Overlays ✓

- **GPU border detection**
  - Replaces mesh-based BorderRenderer with shader-driven approach
  - Screen-space derivatives (ddx/ddy) for consistent pixel-width borders
  - 16-sample multi-radius anti-aliasing for smooth edges
  - Borders stay constant width regardless of camera zoom

- **Data textures**
  - RGBAFloat format: R=StateId, G=ProvinceId, B=BiomeId+WaterFlag, A=CellId
  - Configurable resolution multiplier (1-4x, default 2x = 2880×1620)
  - Spatial grid maps Azgaar coordinates to cell IDs

- **Color palette textures**
  - 256-entry palettes for state/province/market coloring
  - UV1 mesh coordinates for data texture sampling

- **MapOverlayManager**
  - Generates and uploads textures to GPU
  - API for map mode switching, border visibility, border width
  - Water cell handling (falls back to vertex colors)

- Future overlay support infrastructure (heat maps, fog of war, trade routes)

---

## Phase 5.5: Transport & Rivers ✓

- **Ocean transportation**
  - Ocean cells traversable with low movement cost (0.15 vs ~1.0 for land)
  - Port transition cost (+3.0) for loading/unloading at coast
  - Enables sea trade routes - islands connect to mainland markets

- **Ocean and lake rendering**
  - Water cells now rendered (previously land-only)
  - Ocean: depth-based blue gradient (shallow to deep)
  - Lakes: distinct steel blue color
  - Feature data preserved from Azgaar (ocean/lake/island types)

- **River rendering**
  - Rivers rendered as blue line strips through cell centers
  - Width tapers from source (30%) to mouth (100%)
  - 269 rivers visible on current map

- **Selection highlight** - Yellow outline around selected cell

- **Emergent roads**
  - Roads form from accumulated trade traffic on cell edges
  - Two tiers: path (500 traffic, 0.7x cost) and road (2000 traffic, 0.5x cost)
  - River and road bonuses don't stack - best one wins
  - Paths computed lazily (max 50/tick) to avoid performance spikes
  - Roads rendered as brown line segments between cell centers

- **UI improvements**
  - Click-through prevention: UI panels block clicks to map
  - County map mode: cells colored by province hue with S/V variation
  - Political mode cycling: 1 key cycles Country → Province → County
  - Hotkeys: 1=political modes, 2=terrain, 3=height, 4=market
  - Mode-aware selection panel (country/province/county inspector)

---

## Phase 5: Multiple Markets & Black Market ✓

- Multiple markets (3 markets in different states, nearest-market assignment, distinct zone colors)
- Black market (theft feeds underground economy, price-based market selection)

---

## Phase 4: UI Layer ✓

- Time controls (pause, speed) - UI Toolkit panel
- Selection & inspection panels - click-to-select counties
- County detail view (name, location, population, resources, stockpile, facilities)
- Market map mode (key 5) - colors counties by market zone, highlights hub
- Market inspector panel - click market hub to see name, zone size, goods
- Global economy panel (E key) - tabbed: Overview, Production, Trade

---

## Phase 3: Markets & Trade ✓

- Market placement (3 markets in different states)
- Transport cost pathfinding
- Trade flow simulation
- Price discovery

---

## Phase 2: Simulation Foundation ✓

- Data structures (County, Resource, Facility, Population)
- Tick loop architecture (ISimulation, SimulationRunner, ITickSystem)
- 3 production chains (harvest → refine → manufacture)
- Basic consumption by population
- Time control HUD (day display, pause/play, speed)

---

## Phase 1.5: Architecture Refactor ✓

- Created `src/EconSim.Core/` as standalone C# project (netstandard2.1)
- Custom Vec2/Color32 types (no Unity dependency)
- Moved Import code (AzgaarParser, MapConverter) to Core
- Moved Data types (MapData, Cell, etc.) to Core
- Unity bridge layer with type conversions (CoreExtensions)

---

## Phase 1: Map Import & Rendering ✓

- Parse Azgaar JSON format
- Extract heightmap, cells, regions, rivers
- Generate 3D terrain mesh in Unity
- Render political boundaries (state + province borders)
- Camera controls (pan, zoom, spacebar drag)
- Map mode switching (1-4 keys)
- Validate rendering against Azgaar SVG export

---

## Future (Phase 10+)

- More production chains
- Population growth/decline
- Additional map modes (population density, resources)
- Facility emergence
- Resource depletion
- Roads (rendering + transport bonus)
- Individual important characters
- Dynamic political boundaries
