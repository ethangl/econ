# Economic Simulator - Design Document

## Overview

A real-time economic simulator with a game-like UI inspired by Europa Universalis. Features a 3D map view with camera controls, multiple map modes, and a comprehensive economic simulation running underneath.

## Technology

### Engine

- **Decision:** Unity + C#
- **Rationale:**
  - C# is better suited for complex simulation logic
  - UI Toolkit provides CSS-like layouts (flexbox, stylesheets)
  - Better profiling/debugging tools for optimization
  - Cleaner native interop path (C# ↔ C++/Rust)
  - Larger ecosystem and more resources

### UI System

- **Decision:** Unity UI Toolkit (not legacy uGUI)
- **Rationale:**
  - UXML for declarative markup structure
  - USS for CSS-like styling (flexbox, selectors)
  - Better fit for dense, data-heavy grand strategy UI

```xml
<!-- UXML structure -->
<ui:VisualElement class="panel">
    <ui:VisualElement class="header">
        <ui:Label name="title" text="County Details" />
    </ui:VisualElement>
    <ui:VisualElement class="stats-grid">
        <ui:Label text="Population" />
        <ui:Label name="population-value" />
    </ui:VisualElement>
    <ui:Slider name="tax-slider" low-value="0" high-value="1" />
</ui:VisualElement>
```

```css
/* USS styling */
.panel {
  flex-direction: column;
  padding: 10px;
  background-color: rgba(0, 0, 0, 0.8);
}

.stats-grid {
  flex-direction: row;
  justify-content: space-between;
}
```

### UI Reactivity

- **Decision:** [R3](https://github.com/Cysharp/R3) for reactive data binding
- **Rationale:**
  - Actively maintained (successor to UniRx)
  - Mature reactive extensions for C#
  - Observable streams, reactive properties
  - Good Unity integration

```csharp
// R3 patterns
public class CountyViewModel
{
    public ReactiveProperty<int> Population { get; } = new(1000);
    public ReactiveProperty<float> TaxRate { get; } = new(0.1f);

    // Computed value
    public ReadOnlyReactiveProperty<float> Revenue { get; }

    public CountyViewModel()
    {
        Revenue = Population.CombineLatest(TaxRate, (pop, tax) => pop * tax)
            .ToReadOnlyReactiveProperty();
    }
}

// UI binding
Population
    .Subscribe(value => populationLabel.text = value.ToString())
    .AddTo(this);

taxSlider.RegisterValueChangedCallback(evt => TaxRate.Value = evt.newValue);
```

### Time System

- Base tick = 1 day (structured to allow sub-day ticks later)
- All rates stored as "per day", converted at tick time
- Speed settings: paused, slow (0.5/sec), normal (1/sec), fast (5/sec)
- Multi-rate ticking: different systems update at different frequencies

```
System              Frequency
─────────────────────────────────────
Production          every tick
Consumption         every tick
Market prices       every 7 ticks
Population growth   every 30 ticks
Facility emergence  every 90 ticks
Political events    every 365 ticks
```

### Persistence

- JSON-based with schema tolerance
- Missing fields get defaults on load
- Extra fields preserved/ignored
- Version field for future migrations

---

## Map Generation

### References

Primary inspiration: [Azgaar's Fantasy Map Generator](https://github.com/Azgaar/Fantasy-Map-Generator)

Key references from that project:

- Martin O'Leary's "Generating fantasy maps"
- Amit Patel's "Polygonal Map Generation for Games" (Red Blob Games)
- Scott Turner's "Here Dragons Abound"

### Scope Note

**The economic simulation is the intellectual focus of this project, not map generation.**

Valid approaches for landmass/terrain:

- Copy existing algorithms wholesale
- Use Azgaar's Fantasy Map Generator to generate/export heightmaps for import
- Use any combination of borrowed techniques

Don't over-invest in novel map generation - get something that looks right and move on to the simulation.

### Design Goal: Organic Landmasses

Pure noise-based approaches (Perlin/Simplex threshold) create blobby, unrealistic shapes. Organic landmasses need:

- Clear continental structure
- Interesting coastline features (peninsulas, bays, archipelagos, isthmuses)
- Mountain ranges that follow geological logic
- River systems that carve realistic valleys

**Techniques for organic shapes:**

| Technique           | What it does                                                            |
| ------------------- | ----------------------------------------------------------------------- |
| Tectonic simulation | Plates with drift vectors; collisions → mountains, rifts → seas         |
| Continental cores   | Place landmass "seeds", grow outward with noise-perturbed edges         |
| Blob sculpting      | Start with simple shapes, procedurally add/subtract peninsulas and bays |
| Hydraulic erosion   | Simulate water flow to carve coastlines and river valleys               |
| Coastline fractals  | Recursive subdivision with displacement for jagged coastal detail       |

The approach should combine macro structure (continental shapes, mountain spine placement) with micro detail (coastline noise, erosion effects).

### Core Technique: Voronoi/Delaunay Dual

```
Delaunay Triangulation          Voronoi Diagram
(connects cell centers)         (cell polygons)

        ·───────·                 ┌─────┬─────┐
       ╱│╲     ╱│╲                │     │     │
      ╱ │ ╲   ╱ │ ╲               │  ·  │  ·  │
     ╱  │  ╲ ╱  │  ╲              ├─────┼─────┤
    ·───┼───·───┼───·             │     │     │
     ╲  │  ╱ ╲  │  ╱              │  ·  │  ·  │
      ╲ │ ╱   ╲ │ ╱               │     │     │
       ╲│╱     ╲│╱                └─────┴─────┘
        ·───────·

- Voronoi cells = map regions (counties)
- Delaunay edges = adjacency graph (for pathfinding, rivers)
- Use library like Delaunator for fast computation
```

### Two-Phase Generation (Azgaar pattern)

```
Phase 1: Grid
  └── Generate initial Voronoi diagram
  └── Jittered point distribution for organic shapes
  └── Relaxation passes (Lloyd's algorithm) for evenness

Phase 2: Pack (repack for landmass)
  └── Apply elevation/landmass
  └── Discard or merge ocean cells
  └── Optimize cell structure for actual terrain
```

### Pipeline

```
1. Point Distribution
   └── Poisson disk sampling or jittered grid
   └── Lloyd relaxation for even spacing
   └── Density variation (more points in interesting areas)

2. Voronoi/Delaunay Construction
   └── Delaunator library for triangulation
   └── Derive Voronoi polygons from dual

3. Elevation Generation
   └── Noise-based heightmap (Perlin/Simplex)
   └── Optional: paintable regions (user-defined mountains/valleys)
   └── Elevation range 0-100 (20 = sea level threshold)
   └── Distance fields from coastlines

4. Landmass Definition
   └── Cells above sea level = land
   └── Identify features: islands, lakes, continents
   └── Repack: optimize Voronoi for land cells

5. Rivers
   └── Model as binary trees (tributaries → main river → mouth)
   └── Flow follows elevation gradient (downhill)
   └── Water accumulation determines river size
   └── Rivers carve into elevation (erosion)

6. Climate/Biomes
   └── Temperature: latitude + elevation
   └── Moisture: evaporation → wind → rainfall simulation
   └── Or simpler: distance from water + rain shadow
   └── Biome matrix lookup: (temperature, moisture) → biome

7. Political Boundaries (hierarchical)
   └── Voronoi cells = counties (smallest unit)
   └── Agglomerate counties → provinces
   └── Agglomerate provinces → realms
   └── Respect natural boundaries (rivers, mountains)

8. Sub-county Detail (WFC)
   └── Terrain texture variety
   └── Resource distribution patterns
   └── Settlement placement
   └── Road/trade route networks
```

### Data Structure (inspired by Azgaar)

```
Grid (initial):
  - points: [(x, y)]           # cell centers
  - voronoi: polygons          # cell boundaries
  - delaunay: triangulation    # adjacency
  - elevation: Float32Array    # 0-100
  - moisture: Float32Array

Pack (optimized for landmass):
  - cells: [Cell]              # land cells only (or flagged)
  - features: [Feature]        # islands, lakes, continents
  - rivers: [River]            # river trees
  - biomes: Uint8Array         # biome index per cell

Cell:
  - id: int
  - center: (x, y)
  - vertices: [(x, y)]
  - neighbors: [cell_id]       # from Delaunay
  - elevation: float
  - moisture: float
  - biome: int
  - feature_id: int            # which island/continent
  - river_id: int | null
  - distance_to_coast: int     # positive = land, negative = water

Feature:
  - id: int
  - type: continent | island | lake
  - cells: [cell_id]
  - border: bool               # touches map edge

River:
  - id: int
  - cells: [cell_id]           # path from source to mouth
  - parent: river_id | null    # tributary of
  - discharge: float           # water volume
  - length: float
```

### Target Scale

- One continent
- Thousands of counties (Voronoi cells)
- Hundreds of provinces
- Tens of regions

---

## Political Geography

### Hierarchy

```
Realm
└── Province (10s per realm)
    └── County (10s per province)
```

### Data Model

```
County:
  - id: string
  - geometry: polygon vertices
  - terrain: {elevation, biome, features[]}
  - resources: {resource_type: abundance}
  - population_pools: [{age_bracket, skill, count}]
  - facilities: [facility_id]
  - stockpile: {material: qty}
  - markets: [(market_id, efficiency)]
  - parent_province: id

Province:
  - id: string
  - counties: id[]
  - parent_realm: id
  - capital_county: id

Realm:
  - id: string
  - provinces: id[]
  - capital_province: id
```

### Boundary Dynamics

- Initially static
- Future: dynamic boundaries (conquest, politics)

---

## Economic Model

### Production Chain

```
Raw Resource          Refined Material         Finished Good
(location-bound)      (processing)             (manufacturing)
     │                      │                        │
     ▼                      ▼                        ▼
┌──────────┐          ┌──────────┐            ┌──────────┐
│ Oak Tree │ ───────► │ Hardwood │ ──┐        │          │
│ (forest) │ harvest  │  Lumber  │   │ ┌────► │Furniture │
└──────────┘          └──────────┘   │ │      │          │
                                     └─┼──────┤          │
┌──────────┐          ┌──────────┐     │      └──────────┘
│ Iron Ore │ ───────► │  Iron    │ ────┘           │
│(mountain)│ mine     │  Ingots  │                 ▼
└──────────┘          └──────────┘            consumer
                                              demand
```

### Resource Types

```
ResourceType:
  - id: string
  - category: raw
  - harvest_method: string (logging, mining, farming, etc.)
  - terrain_affinity: [terrain_types]
  - base_yield: rate (per day)
  - depletes: bool (future: true for mines, false for farms)
```

### Material Types

```
MaterialType:
  - id: string
  - category: refined
  - inputs: [{type: resource_id, qty: int}]
  - facility: facility_type
  - processing_time: ticks
```

### Good Types

```
GoodType:
  - id: string
  - category: raw | refined | finished
  - inputs: [{type: material_id, qty: int}]  # for refined/finished
  - facility: facility_type                   # for refined/finished
  - consumer_demand: base_rate                # for finished
  - need_category: basic | comfort | luxury   # for finished
  - base_price: float                         # equilibrium market price
  - decay_rate: float                         # spoilage per day
  - theft_risk: float                         # black market appeal (finished only)
```

### Facilities

```
Facility:
  - id: string
  - type: string (sawmill, smelter, workshop, etc.)
  - location: county_id
  - labor_required: int
  - labor_type: laborer | craftsman
  - throughput: units_per_tick (base)
  - efficiency: computed from staffing
  - inventory: {material: qty}
  - output_buffer: {material: qty}

Throughput scaling:
  effective_throughput = base_throughput * (workers / required)^α
```

### Initial Setup

- Facilities placed randomly (weighted by geography)
  - Extraction: only where matching resource exists
  - Refining: near resource sources
  - Manufacturing: near population centers
- Resources infinite with rate limits (depletion is future feature)

---

## Market System

### Concept

Markets are discrete physical locations (market towns) that serve geographic zones. Market zones can span political boundaries.

```
                    ┌─────────────────┐
                    │   Market Zone   │
                    │    (abstract)   │
                    └────────┬────────┘
                             │
              physically located at
                             │
                             ▼
                    ┌─────────────────┐
                    │  Market County  │
                    │   (hub town)    │
                    └─────────────────┘
                           ╱ ╲
            access radius ╱   ╲ determined by
                         ╱     ╲
              ┌─────────┐       ┌─────────┐
              │geography│       │ politics│
              │(terrain,│       │(borders,│
              │ rivers) │       │ tariffs)│
              └─────────┘       └─────────┘
```

### Data Model

```
Market:
  - id: string
  - location: county_id (physical hub)
  - zone: set<county_id> (computed from accessibility)
  - goods: {type: {supply, demand, price}}
```

### Market Placement

Derived from geography (suitability scoring):

- River confluence (multiple rivers meet)
- Coastal (port access)
- Terrain accessibility (low avg cost to neighbors)
- Centrality (graph centrality in region)
- Resource diversity (nearby varied production)

### Market Access

```
For each county C:
  accessible_markets = []
  for each market M:
    transport_cost = pathfind(C, M.location)
    political_cost = tariffs, border friction
    if total_cost < threshold:
      accessible_markets.append((M, efficiency))
```

### Future: Emergent Markets

- Track trade volume per county
- High volume → infrastructure investment → lower costs → more volume
- Counties crossing threshold become recognized markets
- Declining markets fade to local trading

### Implementation Notes (v1)

**Classes:** `Market`, `MarketPlacer`, `TradeSystem`

Market placement uses suitability scoring:

- Settlement bonus (burg population, capital, port)
- Coastal proximity (CoastDistance ≤ 2)
- River access (HasRiver, high flow bonus)
- Population density
- Accessibility (low neighbor movement costs)
- Resource diversity (unique resources in cell + neighbors)
- Centrality (land neighbor count)

**Multiple markets:** Markets are placed in different realms to ensure geographic spread. Each market's zone includes all cells within transport cost budget (100). Cells in overlapping zones are assigned to the nearest market by transport cost.

**Zone visualization:** Each market has a distinct color from a predefined palette (8 colors). The hub province (not just the hub cell) is highlighted with a vivid version of the market color for visibility.

Trade system runs weekly:

- Counties with >7 days stock sell surplus to market (consumer goods)
- Counties with >10 units sell surplus (non-consumer goods)
- Counties with <3 days stock buy from market
- Prices adjust via supply/demand ratio (±10% per tick)
- Price bounds: 0.1x - 10x base price (relative to each good's BasePrice)
- **Transport cost markup**: goods lose ~1% per unit of transport cost during trade
- Transport efficiency = `1 / (1 + cost * 0.01)`, minimum 50%

**Base prices by good:**

| Category | Good      | BasePrice | Notes                       |
| -------- | --------- | --------- | --------------------------- |
| Raw      | Wheat     | 1         | Abundant                    |
| Raw      | Iron Ore  | 1         | Common mineral              |
| Raw      | Gold Ore  | 5         | Rare precious metal         |
| Raw      | Timber    | 1         | Abundant                    |
| Refined  | Flour     | 3         | 2 wheat + processing        |
| Refined  | Iron      | 5         | 3 ore + smelting            |
| Refined  | Gold      | 20        | 3 ore + refining            |
| Refined  | Lumber    | 3         | 2 timber + processing       |
| Finished | Bread     | 5         | Basic staple                |
| Finished | Tools     | 15        | Comfort item, skilled labor |
| Finished | Jewelry   | 50        | Luxury, artisan premium     |
| Finished | Furniture | 12        | Luxury, crafted goods       |

Storage decay (in `ConsumptionSystem`):

- `GoodDef.DecayRate`: fraction of stockpile lost per day
- Bread: 5%/day, Flour: 1%/day, Wheat: 0.5%/day
- Wood products: 0.2%/day, Metals: 0%

### Black Market

A global underground market that receives stolen finished goods and sells them at premium prices.

**Design:**

- Special `Market` instance with `Type = MarketType.Black` (ID 0)
- No physical location (`LocationCellId = -1`), no zone
- Accessible from all counties (no transport cost for purchases)
- 2x base price markup, higher price floor (0.5x vs 0.1x)
- Supply persists between ticks (unlike legitimate markets which reset)
- **Only finished goods** are stolen - raw materials and refined goods stay in the legitimate economy

**Theft sources (finished goods only):**

1. **Transport theft**: When finished goods are transported to/from markets, a portion of transport losses become stolen goods based on `GoodDef.TheftRisk`
2. **Stockpile theft**: Daily theft from county stockpiles (`TheftSystem`)

**TheftRisk values (finished goods):**

| Good      | TheftRisk | BasePrice | Rationale                |
| --------- | --------- | --------- | ------------------------ |
| Jewelry   | 1.0       | 50        | Maximum theft appeal     |
| Tools     | 0.8       | 15        | High value, high demand  |
| Furniture | 0.6       | 12        | High value, identifiable |
| Bread     | 0.1       | 5         | Perishable               |

**Buying behavior:**

- Counties compare effective prices: local market (adjusted for transport loss) vs black market
- Buy from whichever source is cheaper and has supply
- Black market purchases have no transport loss (delivered directly)

**Economic dynamics:**

- Heavy theft → black market oversupply → prices drop → can become cheaper than legitimate markets
- Creates underground economy that competes with legitimate trade
- Raw/refined goods remain in legitimate trade, only consumer goods enter black market

**UI:** Economy panel (E key) Trade tab shows black market in separate section with stock, sales volume, and prices.

---

## Transport System

### Graph Structure

```
Nodes: counties
Edges: adjacency between counties
Edge cost: distance * terrain_factor * modifiers
```

### Terrain Factors

```
plains     → 1.0
forest     → 1.5
hills      → 2.0
mountains  → 4.0+ or impassable
marsh      → 2.5
```

### Modifiers

```
path       → cost * 0.7 (emerges at 500 traffic)
road       → cost * 0.5 (emerges at 2000 traffic)
river      → cost * 0.8 (same-river travel)
sea_route  → cost * 0.15 + port transition (3.0 per crossing)
```

Note: Road/path and river bonuses don't stack - the best modifier wins.

### Pathfinding

- Dijkstra/A\* for transport cost between counties
- Caching strategy needed (1000s of counties):
  - Lazy compute + LRU cache
  - Or hierarchical (region→region precomputed, local on demand)

### Implementation Notes (v1)

**Class:** `TransportGraph`

Terrain costs derived from Azgaar biome data:

- Azgaar costs (10-5000 scale) normalized by dividing by 50
- Result: Grassland=1.0, Forest=1.4-1.8, Desert=3-4, Tundra=20
- Clamped to 1-20 range

Additional modifiers:

- Height penalty: cells above height 70 get up to 3x cost multiplier
- River bonus: same-river travel gets 0.8x multiplier
- Sea transport: ocean cells cost 0.15 (vs ~1.0 for land)
- Port transition: +3.0 flat cost when crossing land↔sea boundary

Edge cost formula:

```
baseCost = (fromCellCost + toCellCost) / 2
distanceNormalizationKm = (worldCellSizeKm > 0 ? worldCellSizeKm * 12 : 30)
distanceFactor = euclideanDistance / distanceNormalizationKm
totalCost = baseCost * distanceFactor * riverBonus
```

Key methods:

- `FindPath(from, to)` → full path with total cost
- `FindReachable(from, maxCost)` → all cells within budget (for market zones)
- `GetTransportCost(from, to)` → cost only (uses cached path)

Caching: Simple LRU with configurable max size (default 10k entries).

### Transport Losses & Theft

Transport inefficiency causes goods to be lost in transit. A portion of these losses become theft, feeding the black market.

**Transport efficiency:**

```
efficiency = 1 / (1 + transportCost * 0.01)
arriving = sent * efficiency
lost = sent - arriving
```

Minimum efficiency is 50% (goods always lose some portion to transport).

**Theft from transport (finished goods only):**

```
if (good.IsFinished && good.TheftRisk > 0):
    stolen = lost * good.TheftRisk
```

Stolen finished goods are added to black market supply. See Black Market section for TheftRisk values.

**Stockpile theft (`TheftSystem`):**

Runs daily. Small percentage of stockpiled **finished goods** stolen based on TheftRisk:

```
if (good.IsFinished && good.TheftRisk > 0):
    stolen = stockpile * 0.005 * TheftRisk
```

For tools (TheftRisk=0.8): ~0.4% stolen per day. Minimum stockpile of 1 unit required. Raw materials and refined goods are not stolen.

### Future: Fragility

Additional transport loss for fragile goods (pottery, glass, fresh produce):

```
TransportLossRate: float  # % lost per unit of transport cost
arriving = sent * (1 - transportCost * TransportLossRate)
```

---

## Population Model

### Cohort-Based Simulation

```
County.population_pools:
  - {age_bracket: working, skill: unskilled, count: 2000}
  - {age_bracket: working, skill: craftsman, count: 400}
  - {age_bracket: young, skill: none, count: 800}
  - {age_bracket: elderly, skill: -, count: 300}
```

### Lifecycle

- Aging: young → working → elderly (probabilistic per tick)
- Skill acquisition: unskilled → craftsman (based on employment)

### Employment

```
employed = sum of workers assigned to facilities
idle = total_working_age - employed

Facilities draw from county's labor pools
Understaffing reduces facility efficiency
```

### Consumption

Population consumes goods based on needs hierarchy:

```
Basic needs (must have):
  - food: grain, bread, meat
  - shelter: lumber, stone

Comfort needs (want):
  - clothing: cloth, leather goods
  - tools: iron tools

Luxury needs (premium):
  - furniture, jewelry, spices

Consumption per tick:
  base_demand = population * per_capita_rate[good]
  actual = min(base_demand, available)
  unmet = base_demand - actual
```

### Unmet Demand Effects

- Basic: population decline, unrest
- Comfort: slower growth, mild unrest
- Luxury: missed economic activity only

### Future: Population Mobility

- Workers migrate between counties
- Chasing employment and prosperity
- Tied to facility emergence feature

### Future: Important Individuals

- Named characters (rulers, guild leaders, merchants)
- Individual decision-making
- Relationships and goals
- CK3-style character layer on top of cohorts

---

## Architecture: Simulation / Renderer Separation

The simulation and renderer are decoupled from day one to allow:

1. Swapping backends as complexity grows
2. Porting to multiple engines (Unity, Godot, Bevy)

### Architecture Decisions

| Decision      | Choice             | Rationale                                                                           |
| ------------- | ------------------ | ----------------------------------------------------------------------------------- |
| Sim language  | C#                 | Fast iteration, easy Unity interop. Port to Rust later if needed for Bevy.          |
| IPC mechanism | Shared assembly    | Sim is a separate C# project/DLL that Unity references directly. Simplest approach. |
| Tick model    | Lockstep           | Unity calls `sim.Tick()`, waits for result, renders. Deterministic, easy to debug.  |
| Map import    | Sim engine owns it | Sim loads Azgaar/custom maps, Unity only receives data for rendering.               |

### Project Structure

```
econ/
├── CLAUDE.md                      # AI assistant context
├── docs/
│   └── DESIGN.md                  # This document
├── reference/                     # Azgaar exports (gitignored)
│   └── *.json, *.svg, *.map
│
├── src/
│   └── EconSim.Core/              # Simulation engine (netstandard2.1)
│       ├── EconSim.Core.csproj
│       ├── Common/
│       │   ├── Types.cs           # Vec2, Color32 (Unity-independent)
│       │   └── SimLog.cs          # Logging utility
│       ├── Data/
│       │   └── MapData.cs         # Cell, Province, State, Biome, etc.
│       ├── Import/
│       │   ├── AzgaarData.cs      # Azgaar JSON structure
│       │   ├── AzgaarParser.cs    # JSON parsing
│       │   └── MapConverter.cs    # AzgaarMap → MapData
│       ├── Economy/
│       │   ├── GoodDef.cs         # Good definitions & registry
│       │   ├── FacilityDef.cs     # Facility definitions & registry
│       │   ├── Facility.cs        # Facility instance
│       │   ├── Stockpile.cs       # Good inventory
│       │   ├── Population.cs      # Population cohorts
│       │   ├── CountyEconomy.cs   # Per-county economic state
│       │   ├── EconomyState.cs    # Global economy container
│       │   ├── EconomyInitializer.cs # Setup resources & facilities
│       │   ├── InitialData.cs     # Production chain definitions
│       │   ├── Market.cs          # Market data structure
│       │   └── MarketPlacer.cs    # Market placement algorithm
│       ├── Transport/
│       │   └── TransportGraph.cs  # Dijkstra pathfinding on cell graph
│       └── Simulation/
│           ├── ISimulation.cs     # Main interface for Unity
│           ├── SimulationRunner.cs # Tick loop implementation
│           ├── SimulationState.cs # Current state (day, speed, economy)
│           ├── SimulationConfig.cs # Speed presets, tick intervals
│           └── Systems/
│               ├── ITickSystem.cs     # Interface for subsystems
│               ├── ProductionSystem.cs # Extraction & processing
│               ├── ConsumptionSystem.cs # Population consumption
│               ├── TradeSystem.cs     # Market trade & pricing
│               └── TheftSystem.cs     # Stockpile theft → black market
│
└── unity/                         # Unity frontend
    ├── Assets/
    │   ├── Scripts/
    │   │   ├── EconSim.Core/      # Symlink → src/EconSim.Core/
    │   │   ├── Bridge/            # CoreExtensions (Vec2↔Vector2, etc.)
    │   │   ├── Renderer/          # MapView, BorderRenderer
    │   │   ├── Camera/            # MapCamera
    │   │   ├── Core/              # GameManager
    │   │   └── UI/                # SelectionPanel, MarketInspectorPanel, TimeControlPanel
    │   ├── UI/
    │   │   ├── Documents/         # UXML templates (MainHUD.uxml)
    │   │   └── Styles/            # USS stylesheets (Main.uss)
    │   ├── Shaders/               # Vertex color shaders
    │   └── Scenes/
    ├── Packages/
    └── ProjectSettings/
```

### Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                      EconSim.Core                           │
│                   (standalone C# DLL)                       │
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │    Import    │───►│   MapData    │───►│  Simulation  │  │
│  │ (Azgaar JSON)│    │   (state)    │    │   (tick)     │  │
│  └──────────────┘    └──────────────┘    └──────────────┘  │
│                                                             │
└─────────────────────────────┬───────────────────────────────┘
                              │
                    ISimulation interface
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                         Unity                               │
│                                                             │
│  ┌─────────────────┐       read        ┌────────────────┐  │
│  │    Renderer     │◄──────────────────│  GameManager   │  │
│  │  (MapView, etc) │    MapData        │                │  │
│  │                 │                   │  sim.Tick()    │  │
│  │  - Mesh gen     │                   │  sim.GetState()│  │
│  │  - Borders      │                   │                │  │
│  │  - Map modes    │                   └────────▲───────┘  │
│  └────────┬────────┘                            │          │
│           │                                     │          │
│  ┌────────▼────────┐       commands            │          │
│  │       UI        │───────────────────────────┘          │
│  │  (UI Toolkit)   │   pause, speed, selection            │
│  └─────────────────┘                                      │
└─────────────────────────────────────────────────────────────┘
```

### Future: Bevy Port

If Bevy becomes a priority, the path is:

1. Port `EconSim.Core` to Rust (algorithms transfer 1:1)
2. Bevy uses Rust sim directly
3. Unity can still use Rust sim via P/Invoke if desired
4. The API design from C# version carries over

### Principles

1. **Simulation is a black box** - produces state, knows nothing about rendering
2. **Data flows one way** - simulation → renderer (renderer reads, never writes)
3. **Plain data classes** - simulation uses POCOs/structs, no Unity types
4. **Single assembly boundary** - Unity references `EconSim.Core.dll`, clean separation

---

## UI Requirements

### 3D Map View

- Terrain mesh from heightmap
- Region meshes colored by map mode
- Border rendering
- Camera: pan, zoom, tilt

### Map Modes

- **Political** (key 1) - colored by state
- **Province** (key 2) - colored by province
- **Terrain** (key 3) - colored by biome
- **Height** (key 4) - elevation gradient
- **Market** (key 5) - colored by market zone (distinct color per market), hub province highlighted with vivid color

Future:

- Population (density)
- Resources (by type)

### Overlay UI (2D)

- Top bar: date display, speed controls
- Sidebar: selected entity info
- Map mode selector
- Collapsible panels for reports and parameters

### Reports

- Economic summaries
- Trade balances
- Population statistics
- Production/consumption charts

### Parameter Controls

- Simulation speed
- Debug/visualization toggles
- Future: scenario editing

---

## Development Roadmap

See `CHANGELOG.md` for detailed phase history and completed work.

**Future (Phase 10+):**

- More production chains
- Population growth/decline
- Additional map modes (population density, resources)
- Facility emergence
- Resource depletion
- Roads (rendering + transport bonus)
- Individual important characters
- Dynamic political boundaries
