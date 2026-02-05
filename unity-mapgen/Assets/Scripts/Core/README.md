# MapGen Core

Engine-independent map generation code.

## Files

- **CellMesh.cs** — Core data structure (cells, vertices, edges with full adjacency)
- **PointGenerator.cs** — Jittered grid + boundary points (Azgaar's approach)
- **Delaunay.cs** — Delaunay triangulation with half-edge representation
- **VoronoiBuilder.cs** — Converts Delaunay → CellMesh

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
