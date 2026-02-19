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

### Time System

- Base tick = 1 day (structured to allow sub-day ticks later)
- All rates stored as "per day", converted at tick time
- Speed settings: paused, slow (0.5/sec), normal (1/sec), fast (5/sec)
- Multi-rate ticking: different systems update at different frequencies

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
  - theft_risk: float                         # legacy security coefficient (not currently consumed by systems)
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

### Implementation Notes (Current)

**Classes:** `Market`, `MarketPlacer`, `MarketSystem`, `OrderSystem`, `PriceSystem`

Market placement uses suitability scoring:

- Settlement bonus (burg population, capital, port)
- Coastal proximity (CoastDistance ≤ 2)
- River access (HasRiver, high flow bonus)
- Population density
- Accessibility (low neighbor movement costs)
- Resource diversity (unique resources in cell + neighbors)
- Centrality (land neighbor count)

**Multiple markets:** Markets are placed in different realms to ensure geographic spread. Each market's zone includes all cells within a world-scale normalized transport budget (legacy baseline `100` on default map scale). Cells in overlapping zones are assigned to the nearest market by transport cost.

**Zone visualization:** Each market has a distinct color from a predefined palette (8 colors). The hub province (not just the hub cell) is highlighted with a vivid version of the market color for visibility.

Economy tick flow is daily:

- `MarketSystem` clears yesterday's buy orders against consignment inventory.
- `ProductionSystem` runs facility production and lists output consignments.
- `OrderSystem` posts next-day population and facility input demand.
- `WageSystem` pays workers from facility treasuries.
- `PriceSystem` adjusts legitimate market prices from supply/demand.
- `LaborSystem` reallocates workers with friction in daily slices.

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

Storage decay:

- `GoodDef.DecayRate` is applied to market consignment inventory.
- County stockpiles currently do not run a global daily spoilage pass.

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

### Implementation Notes (Current)

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

### Transport Losses & Fees

Transport inefficiency causes goods to be lost in transit, and hauling generates fee transfers to county households.

**Transport efficiency:**

```
efficiency = 1 / (1 + transportCost * 0.01)
arriving = sent * efficiency
lost = sent - arriving
```

Hauling fees:

```
hauling_fee = quantity * market_price * transportCost * 0.005
```

- Seller-side hauling fees are paid when consignments are listed.
- Buyer-side hauling fees are included in effective purchase price.
- Fee proceeds are credited to county population treasury (teamster/hauler income).

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
│       │   ├── MapData.cs         # Cell, Province, Realm, County, World metadata
│       │   └── WorldScale.cs      # Shared world-scale and transport distance helpers
│       ├── Import/
│       │   └── WorldGenImporter.cs # MapGen + PopGen result → MapData conversion
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
│               ├── MarketSystem.cs     # Market clearing
│               ├── ProductionSystem.cs # Extraction + processing + consignments
│               ├── OrderSystem.cs      # Population/facility buy orders
│               ├── WageSystem.cs       # Wage payments and subsistence updates
│               ├── PriceSystem.cs      # Price adjustment
│               └── LaborSystem.cs      # Worker reallocation
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

### Principles

1. **Simulation is a black box** - produces state, knows nothing about rendering
2. **Data flows one way** - simulation → renderer (renderer reads, never writes)
3. **Plain data classes** - simulation uses POCOs/structs, no Unity types
4. **Single assembly boundary** - Unity references `EconSim.Core.dll`, clean separation

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
