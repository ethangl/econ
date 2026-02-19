# Map Generation

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
