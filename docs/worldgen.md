# WorldGen

Generate a 3D voronoi globe with a tectonic layer underneath a terrain layer.

Inspiration: https://civilization.2k.com/civ-vii/from-the-devs/map-generation/ and our current MapGen process

## Process

1. Generate a coarse voronoi sphere with ~2040 cells
2. Select cells to seed plates
3. Grow plates by adding adjacent cells to plates
4. Once complete, raise some plates to form continental shelves
5. Generate a denser voronoi sphere with ~20,400 cells "on top" of the tectonic plates
6. Use the tectonic layer to influence terrain generated on the denser sphere — add ridges on plate edges, etc.
7. Extract a rectangular region from the globe into the existing flat-map pipeline

## Step 1: Spherical Voronoi Mesh (DONE)

Standalone library at `src/WorldGen/` generating a `SphereMesh` — the spherical analog of MapGen's `CellMesh`.

**Algorithm:** Fibonacci spiral distributes N points on a unit sphere. Their 3D convex hull gives the spherical Delaunay triangulation (half-edge output). Uses **Quickhull** (O(n log n) average via conflict lists + max-heap face selection). The Voronoi diagram is the dual — each triangle's circumcenter (outward face normal, normalized) becomes a Voronoi vertex.

**Key difference from MapGen:** A sphere has no boundary edges. Every cell is interior, every half-edge has a valid opposite. This simplifies the Voronoi builder vs the flat-map version.

**Files:**

| File                         | Purpose                                                                                                                                                   |
| ---------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Vec3.cs`                    | 3D vector struct (mirrors MapGen's `Vec2`, adds `Cross`)                                                                                                  |
| `FibonacciSphere.cs`         | Golden-spiral point distribution on unit sphere                                                                                                           |
| `ConvexHull.cs`              | Hull data model + `Build()` entry point                                                                                                                   |
| `QuickhullBuilder.cs`        | Quickhull 3D algorithm with conflict lists + max-heap (O(n log n) average)                                                                                |
| `SphereMesh.cs`              | Core data model: cell centers/vertices/neighbors/edges, spherical excess areas                                                                            |
| `SphericalVoronoiBuilder.cs` | Dual construction: hull triangles -> Voronoi cells                                                                                                        |
| `WorldGenPipeline.cs`        | Entry point: points -> hull -> Voronoi -> areas -> tectonics -> `WorldGenResult`                                                                          |
| `WorldGenResult.cs`          | Composite result: `SphereMesh` + `TectonicData`                                                                                                           |
| `SubdivisionBuilder.cs`      | Midpoint subdivision of convex hulls with Delaunay restoration via edge flips                                                                             |
| `WorldGenConfig.cs`          | Config: `CoarseCellCount`, `DenseCellCount`, `Seed`, `Radius`, `Jitter`, `SubdivisionJitter`, `MajorPlateCount`, `MinorPlateCount`, etc.                  |

**Performance:** Quickhull handles 2k cells in ~40ms, 20k cells in ~4s.

**Unity wiring:** Symlinked at `unity/Assets/Scripts/WorldGen`, assembly def with `noEngineReferences: true`.

## Step 2: Tectonic Plates (DONE)

Seeds 8 major and 40 minor plates on the 2040-cell sphere with a two-phase growth strategy, assigns drift vectors, and classifies boundaries. Optional polar ice caps claim high-latitude cells first, pushing tectonic activity toward the equatorial band.

**Algorithm:**

1. **Polar caps** (optional) — If `PolarCapLatitude > 0`, two cap plates (north=ID 0, south=ID 1) are pre-assigned. All cells with `|sinLat| > sin(threshold)` are claimed. Seeds are the cells closest to each pole. Cap plates have zero drift, are always oceanic, and cannot be promoted to continental. Set `PolarCapLatitude = 0` to disable.
2. **Major seeding** — Farthest-point heuristic among unclaimed cells: first seed random, each subsequent seed maximizes min-distance to existing seeds. Produces well-separated plate origins for major plates.
3. **Head-start growth** — Major plates grow via level-synchronous BFS for `MajorHeadStartRounds` (default 3) before any minor plates exist.
4. **Minor seeding** — Farthest-point heuristic among unclaimed cells, measuring distance to all existing seeds (major + already-placed minor).
5. **Final growth** — All plates (major + minor) continue standard BFS until every cell is claimed. Major plates are naturally larger due to their head start.
6. **Drift** — Per plate: random 3D direction projected onto the tangent plane at the seed, normalized. Polar cap drifts are zeroed after generation.
7. **Boundary classification** — For each edge between different plates: project both drift vectors onto the edge direction. If convergence dominates shear → Convergent/Divergent (by sign); otherwise → Transform. Zero drift → all polar boundaries classify as Transform with near-zero convergence → minimal elevation effects.

Plate IDs: `[0..polarCount-1=polar caps, polarCount..polarCount+majorCount-1=majors, rest=minors]` (with `PolarCapLatitude=65`, polarCount=2).

**Data model (`TectonicData`):**

- `CellPlate[cellIndex]` — plate ID (0-based)
- `PlateSeeds[plateId]` — seed cell index
- `PlateDrift[plateId]` — tangent drift vector
- `PlateIsMajor[plateId]` — true for major plates
- `PolarPlateCount` — number of polar cap plates (0 or 2)
- `EdgeBoundary[edgeIndex]` — `None | Convergent | Divergent | Transform`
- `EdgeConvergence[edgeIndex]` — signed scalar (positive = convergent)

**Files:**

| File                | Purpose                                               |
| ------------------- | ----------------------------------------------------- |
| `TectonicData.cs`   | Data model: plate assignments, drift, boundaries      |
| `TectonicOps.cs`    | Seeding, BFS growth, drift generation, classification |
| `WorldGenResult.cs` | Composite result: `SphereMesh` + `TectonicData`       |

**Visualization:** `SphereView` colors cells by plate using an evenly-spaced HSV palette (saturation 0.6, value 0.8).

## Step 3: Elevation from Tectonics (DONE)

Assigns each plate as continental or oceanic, computes base elevation, applies boundary effects, propagates inward, and smooths.

**Algorithm:**

1. **Plate types** — Fisher-Yates shuffle of plate indices, first `floor(plateCount * oceanFraction)` marked oceanic (default 60%).
2. **Subcontinent promotion** — Build plate adjacency graph from boundary edges. Minor oceanic plates whose neighbors are all oceanic are candidates; up to `MaxSubcontinents` (default 8) are randomly promoted to continental, creating island sub-continents.
3. **Base elevation** — Oceanic cells get 0.2, continental cells get 0.7. Sea level at 0.5.
4. **Boundary effects** — For each boundary edge, compute effect from type (convergent +0.25, divergent -0.25, transform +0.125) scaled by `min(|convergence| / 2, 1)`. Both adjacent cells receive the effect (max-abs-wins for overlaps at triple junctions).
5. **BFS propagation** — Effects propagate 3 hops inward from boundary cells with linear decay. Source effect preserved across hops so decay is relative to the original boundary magnitude.
6. **Smoothing** — 2 passes of Laplacian smoothing (0.2 neighbor pull weight).
7. **Clamp** — Final values clamped to [0, 1].

**Constants (`ElevationOps`):**

| Constant           | Value | Purpose                                                                         |
| ------------------ | ----- | ------------------------------------------------------------------------------- |
| `OceanicBase`      | 0.15  | Starting elevation for cells on oceanic plates                                  |
| `ContinentalBase`  | 0.65  | Starting elevation for cells on continental plates                              |
| `ConvergentLift`   | +0.4  | Elevation boost at convergent boundaries (mountain ranges)                      |
| `DivergentDrop`    | -0.4  | Elevation drop at divergent boundaries (rifts)                                  |
| `TransformLift`    | +0.4  | Uplift at transform boundaries (plates sliding past each other)                 |
| `PropagationDepth` | 3     | BFS hops inward from boundary edges. Wider = broader mountain ranges/rift zones |
| `SmoothingPasses`  | 2     | Laplacian smoothing iterations after boundary effects                           |
| `SmoothingWeight`  | 0.2   | Each pass pulls each cell 20% toward its neighbors' average                     |
| `MaxSubcontinents` | 8     | Max minor oceanic plates promoted to continental                                |

**Data model additions:**

- `TectonicData.PlateIsOceanic[plateId]` — true for oceanic plates
- `TectonicData.CellElevation[cellIndex]` — normalized 0-1 elevation
- `WorldGenConfig.OceanFraction` — fraction of plates that are oceanic (default 0.6)

**Visualization:** `SphereView` supports two modes toggled with Tab:

- **Plates** — HSV palette by plate (same as before)
- **Elevation** — Color ramp: deep blue (0.0) → medium blue (0.5/sea level) → green (0.5) → brown (0.7) → white (1.0)

Vertex colors are rebuilt without regenerating mesh geometry.

**Files:**

| File                | Purpose                                        |
| ------------------- | ---------------------------------------------- |
| `ElevationOps.cs`   | Full elevation pipeline (plate types → smooth) |
| `TectonicData.cs`   | Added `PlateIsOceanic`, `CellElevation`        |
| `WorldGenConfig.cs` | Added `OceanFraction`                          |

## Step 4: Dense Sphere + Terrain Transfer (DONE)

Generates a dense SphereMesh (~20.4k cells) on top of the coarse tectonic mesh, transfers elevation via nearest-neighbor mapping, and adds fractal 3D Perlin noise for terrain detail.

**Algorithm:**

1. **Dense mesh** — `FibonacciSphere(20.4k, jitter, seed+100)` → ConvexHull → SphericalVoronoi → ComputeAreas. Seed offset avoids correlation with coarse points.
2. **Nearest-neighbor mapping** — Brute-force: for each dense cell, find closest coarse cell center via `Vec3.SqrDistance`. O(20k × 2k) ≈ 40M comparisons.
3. **Elevation transfer + noise** — Each dense cell inherits its coarse cell's elevation, plus fractal 3D Perlin noise (8 octaves, amplitude 0.5). Sea level at 0.5.

**3D Perlin noise** avoids UV seam artifacts by sampling at 3D cell center positions on the sphere. Classic implementation: 256-entry permutation table, Ken Perlin's optimized 12-gradient function, quintic fade, trilinear interpolation.

**Constants (`DenseTerrainOps`):**

| Constant            | Value | Purpose                                                                           |
| ------------------- | ----- | --------------------------------------------------------------------------------- |
| `NoiseOctaves`      | 8     | Number of noise layers. Each adds finer detail                                    |
| `NoiseFrequency`    | 8.0   | Base frequency — roughly 8 "bumps" around the circumference at the coarsest scale |
| `NoiseLacunarity`   | 2.0   | Frequency multiplier per octave (each octave is 2x finer)                         |
| `NoisePersistence`  | 0.5   | Amplitude multiplier per octave (each octave contributes half the previous)       |
| `NoiseAmplitude`    | 0.5   | Overall noise strength applied to base elevation                                  |
| `CoastDampingRange` | 0.0   | Distance from sea level over which noise is attenuated (0 = no damping)           |
| `SeaLevel`          | 0.5   | Reference elevation for coast damping                                             |

**Data model (`DenseTerrainData`):**

- `Mesh` — high-resolution SphereMesh
- `DenseToCoarse[denseCell]` — index of nearest coarse cell
- `CellElevation[denseCell]` — final elevation (0-1)

**Config (`WorldGenConfig`):**

- `CoarseCellCount` (default 2040) — renamed from `CellCount`
- `DenseCellCount` (default 20400) — chosen so each dense cell ≈ 25,000 km² = exactly 10,000 MapGen cells (at 2.5 km² each)

**Visualization:** `SphereView` renders the dense mesh. Plates mode maps dense → coarse via `DenseToCoarse` for plate palette lookup. Elevation mode uses dense `CellElevation` directly.

**Files:**

| File                 | Purpose                                           |
| -------------------- | ------------------------------------------------- |
| `Noise3D.cs`         | 3D Perlin noise with fractal octave support       |
| `DenseTerrainOps.cs` | Dense mesh generation, mapping, elevation + noise |
| `WorldGenResult.cs`  | Added `DenseTerrainData` class + field            |
| `WorldGenConfig.cs`  | `CoarseCellCount`, `DenseCellCount`               |

## Step 4b: Ultra-Dense Tessellation (DONE)

Subdivides the dense hull (~20k) to produce an ultra-dense mesh (~80k cells) in O(n) time, avoiding the O(n²) Quickhull cost at high cell counts.

**Algorithm:**

1. **Midpoint subdivision** — For each unique edge in the dense hull, compute the spherical midpoint (normalized average of endpoints). Each triangle splits into 4 sub-triangles via the 3 edge midpoints. Half-edge connectivity is synthesized directly from combinatorial rules (no hull recomputation).
2. **Jitter** — Each midpoint is displaced by a random tangent-plane vector scaled by `SubdivisionJitter * edgeLength`, then re-projected to the unit sphere. Breaks grid regularity.
3. **Delaunay restoration** — Midpoint subdivision does not preserve the Delaunay property. Non-Delaunay edges produce circumcenters outside their triangles, causing overlapping Voronoi cells. A sweep-based edge flip pass (Lawson's algorithm) restores Delaunay: for each edge, if the opposite vertex is inside the circumcircle (spherical InCircle test via circumcenter angular distance), the edge is flipped. Repeats until convergence.
4. **Voronoi + elevation** — The restored hull feeds into `SphericalVoronoiBuilder` unchanged. Elevation transfer and fractal noise use the same `ComputeElevation` pipeline as the dense mesh, mapping ultra-dense → coarse via the composed `ultraToDense[i] → denseToCoarse[j]` chain.

**Data model (`DenseTerrainData` additions):**

- `UltraDenseMesh` — SphereMesh from tessellated hull (~80k cells)
- `UltraDenseToCoarse[ultraCell]` — index of nearest coarse cell (composed mapping)
- `UltraDenseCellElevation[ultraCell]` — elevation with noise at ultra-dense resolution

**Config (`WorldGenConfig`):**

- `SubdivisionJitter` (default 0.0) — tangent-plane jitter for subdivision midpoints (0-1)

**Visualization:** `SphereView` adds an **UltraDense** mode (Tab cycles: Plates → Elevation → UltraDense → Plates). Switching to/from UltraDense rebuilds mesh geometry; switching between Plates and Elevation only rebuilds vertex colors.

**Files:**

| File                       | Purpose                                                  |
| -------------------------- | -------------------------------------------------------- |
| `SubdivisionBuilder.cs`    | Midpoint subdivision + Delaunay edge flips               |
| `DenseTerrainOps.cs`       | Added tessellation step after dense mesh generation      |
| `WorldGenResult.cs`        | Added ultra-dense fields to `DenseTerrainData`           |
| `WorldGenConfig.cs`        | Added `SubdivisionJitter`                                |
| `SphereView.cs` (Unity)    | Added `UltraDense` view mode with geometry switch        |

## Next Steps: Globe-Informed MapGen

Instead of extracting terrain directly from the globe, the globe serves as a **context oracle** — each generated map has a specific lat/lng and tectonic neighborhood that cascades through every downstream system. The globe provides macro-scale structure; MapGen provides local detail.

The key insight: pick an *uninteresting* (oceanic) region on the globe and run MapGen there. The globe doesn't need to produce play-ready terrain — it provides the constraints that make MapGen's output feel like it belongs to a specific place in the world.

### Step 5: Location Picking + Template Selection

Find a site on the globe and use tectonic context to drive MapGen's heightmap template.

1. **Site selection** — Scan oceanic cells at a target latitude band, filtering for proximity to continental coastline (near enough for trade relevance, far enough to be a distinct island). This gives a specific lat/lng.
2. **Tectonic context** — Read the local plate environment: convergent boundary nearby → volcanic island. Divergent/rift → low atoll. Multiple plate intersection → complex archipelago. Stable oceanic interior → flat reef island.
3. **Template mapping** — Translate tectonic context into a HeightmapTemplateType (or new island-specific templates) and DSL parameters. Key tectonic data points (boundary type, convergence magnitude, distance to boundary) become DSL hints for feature placement — e.g., place the volcano peak near the convergent edge direction.

### Step 6: Tectonic-Informed Climate

Use continental positions, polar proximity, and ocean currents derived from the globe to build a more physically grounded weather model for the generated map.

- Prevailing wind direction from global circulation patterns at the site's latitude
- Rain shadow effects based on nearby continental mountain ranges
- Ocean current temperature from basin-scale flow (warm equatorial vs cold polar currents)
- Replaces the current latitude-only temperature/precipitation model with one that accounts for the island's global context

### Step 7: Tectonic Geology

Use the plate environment to make resource and geology placement less random.

- Volcanic islands get obsidian, sulfur, fertile volcanic soil
- Rift zones get mineral veins, geothermal features
- Stable continental shelf sites get limestone, clay, coral
- Distance from convergent boundaries influences metamorphic rock distribution

### Step 8: Culture + Trade Context

Use the island's global position to determine cultural influences and economic connections.

- Longitude added as a culture-type axis — the island's lat/lng determines which cultures have settled it and in what mixture
- Nearby continental civilizations influence available trade goods, technology level, and political pressures
- The virtual market is placed nearby, relative to the island's lat/lng, connecting it to the broader world's trade network
- Distance to major continental ports determines trade cost and cultural isolation
