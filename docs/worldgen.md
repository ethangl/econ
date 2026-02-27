# WorldGen

Generate a 3D voronoi globe with a tectonic layer underneath a terrain layer.

Inspiration: https://civilization.2k.com/civ-vii/from-the-devs/map-generation/ and our current MapGen process

## Process

1. Generate a coarse voronoi sphere with perhaps 400 cells
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

| File                         | Purpose                                                                         |
| ---------------------------- | ------------------------------------------------------------------------------- |
| `Vec3.cs`                    | 3D vector struct (mirrors MapGen's `Vec2`, adds `Cross`)                        |
| `FibonacciSphere.cs`         | Golden-spiral point distribution on unit sphere                                 |
| `ConvexHull.cs`              | Incremental 3D convex hull, half-edge output matching `Delaunay.cs` conventions |
| `SphereMesh.cs`              | Core data model: cell centers/vertices/neighbors/edges, spherical excess areas  |
| `SphericalVoronoiBuilder.cs` | Dual construction: hull triangles -> Voronoi cells                              |
| `WorldGenPipeline.cs`        | Entry point: points -> hull -> Voronoi -> areas -> `SphereMesh`                 |
| `WorldGenConfig.cs`          | Config: `CellCount`, `Seed`, `Radius`, `Jitter`                                 |

**Performance:** 10,000 cells in ~5s (incremental hull is O(n^2)). Fine for the coarse tectonic mesh. The dense 100k mesh in step 5 may need a faster algorithm (Quickhull or divide-and-conquer).

**Unity wiring:** Symlinked at `unity/Assets/Scripts/WorldGen`, assembly def with `noEngineReferences: true`.

## Next Steps

### Step 2: Tectonic Plates

Seed N plates (8–12) on the coarse ~400-cell sphere. Flood-fill to assign every cell to a plate. Each plate gets a drift vector (random direction on sphere surface). Classify plate boundaries by relative motion: convergent, divergent, or transform.

### Step 3: Elevation from Tectonics

Assign base elevation per plate (continental vs oceanic). Modify elevation at boundaries — convergent boundaries push up mountain ridges, divergent boundaries create rifts/ocean ridges, transform boundaries get minor uplift. Smooth the result across neighboring cells.

### Step 4: Dense Sphere + Terrain Transfer

Generate a second, denser SphereMesh (~10k cells). For each dense cell, find the enclosing coarse cell and inherit its tectonic elevation. Apply noise and detail at the dense scale. This is where biomes, climate, rivers, etc. would eventually live on the globe.

### Step 5: Region Extraction

Select a rectangular lat/lon window on the globe. Project the enclosed dense cells onto a flat plane (equirectangular or Mercator). Convert to a flat `CellMesh` compatible with the existing MapGen/EconSim pipeline, so all downstream systems (heightmap, climate, political, economy) work unchanged.
