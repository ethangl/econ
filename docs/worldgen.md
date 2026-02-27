# WorldGen

Generate a 3D voronoi globe with a tectonic layer underneath a terrain layer.

Inspiration: https://civilization.2k.com/civ-vii/from-the-devs/map-generation/ and our current MapGen process

## Process

1. Generate a coarse voronoi sphere with ~2000 cells
2. Select cells to seed plates
3. Grow plates by adding adjacent cells to plates
4. Once complete, raise some plates to form continental shelves
5. Generate a denser voronoi sphere with ~20,000 cells "on top" of the tectonic plates
6. Use the tectonic layer to influence terrain generated on the denser sphere — add ridges on plate edges, etc.
7. Extract a rectangular region from the globe into the existing flat-map pipeline

## Step 1: Spherical Voronoi Mesh (DONE)

Standalone library at `src/WorldGen/` generating a `SphereMesh` — the spherical analog of MapGen's `CellMesh`.

**Algorithm:** Fibonacci spiral distributes N points on a unit sphere. Their 3D convex hull gives the spherical Delaunay triangulation (half-edge output). Two algorithms available: **Quickhull** (default, O(n log n) average via conflict lists) and **Incremental** (O(n²), scans all faces per point). The Voronoi diagram is the dual — each triangle's circumcenter (outward face normal, normalized) becomes a Voronoi vertex.

**Key difference from MapGen:** A sphere has no boundary edges. Every cell is interior, every half-edge has a valid opposite. This simplifies the Voronoi builder vs the flat-map version.

**Files:**

| File                         | Purpose                                                                                                                                                |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `Vec3.cs`                    | 3D vector struct (mirrors MapGen's `Vec2`, adds `Cross`)                                                                                               |
| `FibonacciSphere.cs`         | Golden-spiral point distribution on unit sphere                                                                                                        |
| `ConvexHull.cs`              | Hull data model + `Build()` factory dispatching to Quickhull or Incremental; `ConvexHullBuilder` (incremental O(n²))                                   |
| `QuickhullBuilder.cs`        | Quickhull 3D algorithm with conflict lists (O(n log n) average)                                                                                        |
| `SphereMesh.cs`              | Core data model: cell centers/vertices/neighbors/edges, spherical excess areas                                                                         |
| `SphericalVoronoiBuilder.cs` | Dual construction: hull triangles -> Voronoi cells                                                                                                     |
| `WorldGenPipeline.cs`        | Entry point: points -> hull -> Voronoi -> areas -> tectonics -> `WorldGenResult`                                                                       |
| `WorldGenResult.cs`          | Composite result: `SphereMesh` + `TectonicData`                                                                                                        |
| `WorldGenConfig.cs`          | Config: `CoarseCellCount`, `DenseCellCount`, `Seed`, `Radius`, `Jitter`, `MajorPlateCount`, `MinorPlateCount`, `MajorHeadStartRounds`, `HullAlgorithm` |

**Performance:** Quickhull (default) handles 20k cells efficiently. Incremental is O(n²) — fine for ≤2k cells but slow for dense meshes. Set `WorldGenConfig.HullAlgorithm` to switch between them.

**Unity wiring:** Symlinked at `unity/Assets/Scripts/WorldGen`, assembly def with `noEngineReferences: true`.

## Step 2: Tectonic Plates (DONE)

Seeds 8 major and 40 minor plates on the 2000-cell sphere with a two-phase growth strategy, assigns drift vectors, and classifies boundaries.

**Algorithm:**

1. **Major seeding** — Farthest-point heuristic: first seed random, each subsequent seed maximizes min-distance to existing seeds. Produces well-separated plate origins for major plates.
2. **Head-start growth** — Major plates grow via level-synchronous BFS for `MajorHeadStartRounds` (default 3) before any minor plates exist.
3. **Minor seeding** — Farthest-point heuristic among unclaimed cells, measuring distance to all existing seeds (major + already-placed minor).
4. **Final growth** — All plates (major + minor) continue standard BFS until every cell is claimed. Major plates are naturally larger due to their head start.
5. **Drift** — Per plate: random 3D direction projected onto the tangent plane at the seed, normalized. Represents plate motion direction.
6. **Boundary classification** — For each edge between different plates: project both drift vectors onto the edge direction. If convergence dominates shear → Convergent/Divergent (by sign); otherwise → Transform.

Plate IDs: major plates are `0..majorCount-1`, minor plates are `majorCount..totalCount-1`.

**Data model (`TectonicData`):**

- `CellPlate[cellIndex]` — plate ID (0-based)
- `PlateSeeds[plateId]` — seed cell index
- `PlateDrift[plateId]` — tangent drift vector
- `PlateIsMajor[plateId]` — true for major plates
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

Generates a dense SphereMesh (~20k cells) on top of the coarse tectonic mesh, transfers elevation via nearest-neighbor mapping, and adds fractal 3D Perlin noise for terrain detail.

**Algorithm:**

1. **Dense mesh** — `FibonacciSphere(20k, jitter, seed+100)` → ConvexHull → SphericalVoronoi → ComputeAreas. Seed offset avoids correlation with coarse points.
2. **Nearest-neighbor mapping** — Brute-force: for each dense cell, find closest coarse cell center via `Vec3.SqrDistance`. O(20k × 2k) ≈ 40M comparisons.
3. **Elevation transfer + noise** — Each dense cell inherits its coarse cell's elevation, plus fractal 3D Perlin noise (6 octaves, amplitude 0.5). Sea level at 0.5.

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

- `CoarseCellCount` (default 2000) — renamed from `CellCount`
- `DenseCellCount` (default 20000) — new

**Visualization:** `SphereView` renders the dense mesh. Plates mode maps dense → coarse via `DenseToCoarse` for plate palette lookup. Elevation mode uses dense `CellElevation` directly.

**Files:**

| File                 | Purpose                                           |
| -------------------- | ------------------------------------------------- |
| `Noise3D.cs`         | 3D Perlin noise with fractal octave support       |
| `DenseTerrainOps.cs` | Dense mesh generation, mapping, elevation + noise |
| `WorldGenResult.cs`  | Added `DenseTerrainData` class + field            |
| `WorldGenConfig.cs`  | `CoarseCellCount`, `DenseCellCount`               |

## Next Steps

### Step 5: Region Extraction

Select a rectangular lat/lon window on the globe. Project the enclosed dense cells onto a flat plane (equirectangular or Mercator). Convert to a flat `CellMesh` compatible with the existing MapGen/EconSim pipeline, so all downstream systems (heightmap, climate, political, economy) work unchanged.
