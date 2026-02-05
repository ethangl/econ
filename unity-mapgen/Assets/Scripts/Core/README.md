# MapGen Core

Engine-independent map generation code.

## Files

### Cell Mesh
- **CellMesh.cs** — Core data structure (cells, vertices, edges with full adjacency)
- **PointGenerator.cs** — Jittered grid + boundary points (Azgaar's approach)
- **Delaunay.cs** — Delaunay triangulation with half-edge representation
- **VoronoiBuilder.cs** — Converts Delaunay → CellMesh

### Heightmap Generation
- **HeightGrid.cs** — Height values for cells (0-100 range, 20 = sea level)
- **HeightmapOps.cs** — DSL operations (Hill, Pit, Range, Trough, Mask, etc.)
- **HeightmapDSL.cs** — DSL parser for heightmap scripts
- **HeightmapTemplates.cs** — Predefined templates (LowIsland, Archipelago, etc.)

## Delaunator

Uses [DelaunatorSharp](https://github.com/nol1fe/delaunator-sharp) (MIT licensed, vendored in `Vendor/`).

The `Delaunay` class wraps DelaunatorSharp with helper methods:
- `Triangles[e]` = point where half-edge e starts
- `Halfedges[e]` = opposite half-edge, or -1 if boundary
- `Circumcenter(t)` = circumcenter of triangle t
- `EdgesAroundPoint(e)` = all half-edges touching a point
- `AdjacentTriangles(t)` = neighboring triangles

## Edge-Based Design

Unlike Azgaar, we store explicit edges:
- `EdgeVertices` — vertex pairs defining each edge
- `EdgeCells` — the two cells separated by each edge
- `CellEdges` — edges forming each cell's boundary

This supports edge-based rivers: rivers flow along edges, not through cells.

## Heightmap DSL

The heightmap DSL matches Azgaar's format:

```
# Comments start with #
Hill count height x% y%       # Add hills (positive blobs)
Pit count height x% y%        # Add pits (negative blobs)
Range count height x% y%      # Add mountain ranges
Trough count height x% y%     # Add valleys
Strait width direction [pos]  # Water passage (0=horiz, 1=vert)
Mask fraction                 # Edge falloff for islands
Add value [minH-maxH]         # Add constant to heights
Multiply factor [minH-maxH]   # Scale heights
Smooth passes                 # Average with neighbors
Invert probability axis       # Mirror heightmap
```

Ranges use "min-max" syntax: "20-30" means random value between 20 and 30.

### Example: Low Island

```
Hill 1 90-99 60-80 45-55    # Main landmass, center-east
Hill 1-2 20-30 10-30 10-90  # Secondary hills, west side
Smooth 2                     # Blend everything
Pit 5-7 15-25 15-85 20-80   # Lakes/depressions
Multiply 0.4 20-100         # Flatten land (keep it low)
Mask 4                       # Insulate from edges
```

### Available Templates

- **LowIsland** — Small landmass, minimal elevation
- **Archipelago** — Scattered islands, lots of coastline
- **Continents** — Large landmasses with inland seas
- **Pangaea** — Single supercontinent
- **Highland** — Mountainous, dramatic terrain
- **Atoll** — Ring islands around central lagoon
- **Peninsula** — Land extending into water
- **Mediterranean** — Inland sea with surrounding land
