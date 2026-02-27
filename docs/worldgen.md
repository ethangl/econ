# WorldGen

Generate a 3D voronoi globe with a tectonic layer underneath a terrain layer.

Inspiration: https://civilization.2k.com/civ-vii/from-the-devs/map-generation/ and our current MapGen process

## Process

1. Generate a coarse voronoi sphere with perhaps 500 cells
2. Select cells to seed plates
3. Grow plates by adding adjacent cells to plates
4. Once complete, raise some plates to form continental shelves
5. Generate a denser voronoi sphere with perhaps 10,000 cells "on top" of the tectonic plates
6. Use the tectonic layer to influence terrain generated on the denser sphere — add ridges on plate edges, etc.
7. Extract a rectangular region from the globe into the existing flat-map pipeline

## Step 1: Spherical Voronoi Mesh (DONE)

Standalone library at `src/WorldGen/` generating a `SphereMesh` — the spherical analog of MapGen's `CellMesh`.

**Algorithm:** Fibonacci spiral distributes N points on a unit sphere. Their 3D convex hull gives the spherical Delaunay triangulation (incremental algorithm, half-edge output). The Voronoi diagram is the dual — each triangle's circumcenter (outward face normal, normalized) becomes a Voronoi vertex.

**Key difference from MapGen:** A sphere has no boundary edges. Every cell is interior, every half-edge has a valid opposite. This simplifies the Voronoi builder vs the flat-map version.

**Files:**

| File                         | Purpose                                                                          |
| ---------------------------- | -------------------------------------------------------------------------------- |
| `Vec3.cs`                    | 3D vector struct (mirrors MapGen's `Vec2`, adds `Cross`)                         |
| `FibonacciSphere.cs`         | Golden-spiral point distribution on unit sphere                                  |
| `ConvexHull.cs`              | Incremental 3D convex hull, half-edge output matching `Delaunay.cs` conventions  |
| `SphereMesh.cs`              | Core data model: cell centers/vertices/neighbors/edges, spherical excess areas   |
| `SphericalVoronoiBuilder.cs` | Dual construction: hull triangles -> Voronoi cells                               |
| `WorldGenPipeline.cs`        | Entry point: points -> hull -> Voronoi -> areas -> tectonics -> `WorldGenResult` |
| `WorldGenResult.cs`          | Composite result: `SphereMesh` + `TectonicData`                                  |
| `WorldGenConfig.cs`          | Config: `CellCount`, `Seed`, `Radius`, `Jitter`, `PlateCount`                    |

**Performance:** 10,000 cells in ~5s (incremental hull is O(n^2)). Fine for the coarse tectonic mesh. The dense 100k mesh in step 5 may need a faster algorithm (Quickhull or divide-and-conquer).

**Unity wiring:** Symlinked at `unity/Assets/Scripts/WorldGen`, assembly def with `noEngineReferences: true`.

## Step 2: Tectonic Plates (DONE)

Seeds 20 plates on the 500-cell sphere, grows them via flood-fill, assigns drift vectors, and classifies boundaries.

**Algorithm:**

1. **Seeding** — Farthest-point heuristic: first seed random, each subsequent seed maximizes min-distance to existing seeds. Produces well-separated plate origins.
2. **Growth** — Multi-source BFS: all seeds enqueued simultaneously, first plate to reach a cell claims it. Sphere connectivity guarantees full coverage.
3. **Drift** — Per plate: random 3D direction projected onto the tangent plane at the seed, normalized. Represents plate motion direction.
4. **Boundary classification** — For each edge between different plates: project both drift vectors onto the edge direction. If convergence dominates shear → Convergent/Divergent (by sign); otherwise → Transform.

**Data model (`TectonicData`):**

- `CellPlate[cellIndex]` — plate ID (0-based)
- `PlateSeeds[plateId]` — seed cell index
- `PlateDrift[plateId]` — tangent drift vector
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
2. **Base elevation** — Oceanic cells get 0.2, continental cells get 0.6. Implicit sea level ~0.4.
3. **Boundary effects** — For each boundary edge, compute effect from type (convergent +0.25, divergent -0.15, transform +0.05) scaled by `min(|convergence| / 2, 1)`. Both adjacent cells receive the effect (max-abs-wins for overlaps at triple junctions).
4. **BFS propagation** — Effects propagate 3 hops inward from boundary cells with linear decay. Source effect preserved across hops so decay is relative to the original boundary magnitude.
5. **Smoothing** — 2 passes of Laplacian smoothing (0.3 neighbor pull weight).
6. **Clamp** — Final values clamped to [0, 1].

**Data model additions:**

- `TectonicData.PlateIsOceanic[plateId]` — true for oceanic plates
- `TectonicData.CellElevation[cellIndex]` — normalized 0-1 elevation
- `WorldGenConfig.OceanFraction` — fraction of plates that are oceanic (default 0.6)

**Visualization:** `SphereView` supports two modes toggled with Tab:
- **Plates** — HSV palette by plate (same as before)
- **Elevation** — Color ramp: deep blue (0.0) → medium blue (0.4/sea level) → green (0.4) → brown (0.7) → white (1.0)

Vertex colors are rebuilt without regenerating mesh geometry.

**Files:**

| File                | Purpose                                          |
| ------------------- | ------------------------------------------------ |
| `ElevationOps.cs`   | Full elevation pipeline (plate types → smooth)   |
| `TectonicData.cs`   | Added `PlateIsOceanic`, `CellElevation`          |
| `WorldGenConfig.cs` | Added `OceanFraction`                            |

## Next Steps

### Step 4: Dense Sphere + Terrain Transfer

Generate a second, denser SphereMesh (~10k cells). For each dense cell, find the enclosing coarse cell and inherit its tectonic elevation. Apply noise and detail at the dense scale. This is where biomes, climate, rivers, etc. would eventually live on the globe.

### Step 5: Region Extraction

Select a rectangular lat/lon window on the globe. Project the enclosed dense cells onto a flat plane (equirectangular or Mercator). Convert to a flat `CellMesh` compatible with the existing MapGen/EconSim pipeline, so all downstream systems (heightmap, climate, political, economy) work unchanged.
