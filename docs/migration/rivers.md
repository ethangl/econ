# River Generation

## Model

Water falls on cells as precipitation. Heights and precipitation are interpolated onto Voronoi vertices (circumcenters of Delaunay triangles). Water flows between adjacent vertices along Voronoi edges, downhill. Accumulated flow above a threshold is a river. A vertex where water level exceeds terrain height is a lake vertex.

## Why Vertex-Based

Rivers flow on Voronoi vertices, not cells. Each Voronoi vertex has ~3 neighbors. Two flow steps from the same vertex always share that vertex, so river paths are automatically connected polylines along Voronoi edges. No edge stitching or vertex ring walking needed.

This follows the Red Blob Games mapgen4 approach: Delaunay triangle circumcenter = Voronoi vertex. Flow between adjacent triangles = flow between Voronoi vertices = Voronoi edge.

Rivers still follow cell boundaries, so they remain natural political borders. Cells on opposite banks are in different jurisdictions without special-case code.

## Algorithm

### 1. Interpolate Cell Data onto Vertices

For each Voronoi vertex v, average the heights and precipitation of its surrounding cells (~3):

    VertexHeight[v] = avg(Heights[c] for c in VertexCells[v])
    VertexPrecip[v] = avg(Precipitation[c] for c in VertexCells[v])

A vertex is "ocean" if VertexHeight <= SeaLevel (20).

### 2. Depression Fill (Priority Flood on Vertex Graph)

Use a priority queue (min-heap) seeded with land vertices adjacent to ocean vertices (or boundary cells). Process vertices lowest-first. For each vertex, visit its VertexNeighbors:

- If the neighbor's terrain height >= current vertex's water level: the neighbor's water level = its terrain height (no flooding needed).
- If the neighbor's terrain height < current vertex's water level: the neighbor's water level = current vertex's water level (water fills the depression). Set the neighbor's **flow target** to the current vertex.

Ocean vertices are skipped (marked visited immediately). After processing, every land vertex has `WaterLevel >= VertexHeight`. Where `WaterLevel > VertexHeight`, the vertex is a lake vertex. Lake vertices already have their flow targets assigned.

### 3. Flow Accumulation

Sort all land vertices by water level, highest first. Tiebreak: terrain height ascending (lake interiors before rim). For each vertex:

1. Add the vertex's precipitation to its flux.
2. If the vertex has no flow target yet (not a lake vertex): find the neighbor with the lowest water level. That neighbor is this vertex's **flow target**.
3. Add this vertex's flux to the flow target's flux (if the target is land).

After this pass, every land vertex has a flux value and a flow target.

### 4. Edge Flux

Each land vertex drains to exactly one neighbor. That drainage crosses the Voronoi edge connecting the two vertices. Assign each such edge the flux of the draining vertex.

Look up which edge connects vertex A to vertex B using a dictionary keyed by `(min(A,B), max(A,B))` → edge index. Built once from the mesh topology.

### 5. River Extraction

A vertex with flux above a threshold is a river vertex.

1. **Find mouths.** A mouth is a land vertex whose flow target is an ocean vertex, with flux >= threshold.
2. **Trace upstream.** From the mouth vertex, follow the inflow with the highest flux that's above threshold. Repeat until no upstream vertex qualifies. This trace is one river.
3. **Tributaries.** BFS from vertices on existing rivers. Any upstream inflow vertex with flux >= threshold that isn't already claimed starts a new tributary river.
4. **Filter.** Remove rivers with fewer than `minVertices` vertices.

## Data Structures

### Per-Vertex Arrays

| Array          | Type    | Description                                                       |
| -------------- | ------- | ----------------------------------------------------------------- |
| `VertexHeight` | float[] | Interpolated terrain height (avg of surrounding cells).           |
| `VertexPrecip` | float[] | Interpolated precipitation (avg of surrounding cells).            |
| `WaterLevel`   | float[] | After depression fill. >= VertexHeight. Lake if >.                |
| `VertexFlux`   | float[] | Accumulated water flux through this vertex.                       |
| `FlowTarget`   | int[]   | Index of the vertex this vertex drains to. -1 if none/unresolved. |

### Per-Edge Arrays

| Array      | Type    | Description                                          |
| ---------- | ------- | ---------------------------------------------------- |
| `EdgeFlux` | float[] | Flux crossing this edge (= flux of draining vertex). |

### River Struct

```
River {
    Id: int
    Vertices: int[]     // ordered vertex indices, mouth-first
    MouthVertex: int    // vertex at ocean/confluence end
    SourceVertex: int   // vertex at upstream end
    Discharge: float    // flux at mouth
}
```

## Rendering

A river's geometry is its sequence of Voronoi vertices. Consecutive vertices are connected by Voronoi edges. The renderer draws these as a polyline. Width is derived from flux (tapers from source to mouth).
