# Plan: Relaxed Border System

**Status:** Not started

**Current state (Phase 10):** Domain warping was removed; borders and textures now use straight Voronoi edges. This plan adds organic curved edges to replace those straight lines.

## Goal
Create a relaxed mesh with organic curved edges that becomes the single source of truth for all map boundaries. Both border rendering and texture rasterization derive from this geometry.

---

## Stage 1: Relaxed Cell Geometry + Border Rendering

**Goal:** Generate relaxed polygons for ALL cells, then render political borders from that geometry. Visual checkpoint.

### 1.1 Verify vertex winding order

Before implementing, confirm `cell.VertexIndices` is stored in consistent winding order (CW or CCW). Check by:
- Reading a few cells and their vertex positions
- Verifying consecutive vertices form the polygon boundary

If not in order, we'll need to sort them by angle from cell center.

### 1.2 Create RelaxedCellGeometry class

**New file:** `unity/Assets/Scripts/Renderer/RelaxedCellGeometry.cs`

```csharp
public class RelaxedCellGeometry
{
    // Relaxed polygon for each cell (closed polyline in 2D MAP coords)
    public Dictionary<int, List<Vector2>> CellPolygons;

    // Relaxed edges keyed by sorted vertex pair - PUBLIC for border lookup
    // Key: (min(v1,v2), max(v1,v2)) ensures symmetry
    public Dictionary<(int, int), List<Vector2>> RelaxedEdges;

    // Parameters
    public float Amplitude;   // Perpendicular displacement (map units)
    public float Frequency;   // Control points per map unit
    public int SamplesPerSegment; // Catmull-Rom samples between control points

    public void Build(MapData mapData);
    public List<Vector2> GetEdge(int v1, int v2); // Returns edge, reversed if needed
}
```

### 1.3 Relaxation algorithm (detailed)

**For each edge (v1, v2):**

```
1. Compute symmetric key: (min(v1,v2), max(v1,v2))
2. If cached, return copy (reversed if v1 > v2)

3. Get positions: p1 = Vertices[key.min], p2 = Vertices[key.max]
4. Compute edge vector: dir = p2 - p1, length = |dir|
5. Compute perpendicular: perp = (-dir.y, dir.x).normalized

6. Determine control point count: numSegments = max(1, round(length * Frequency))
   numControls = numSegments + 1  // e.g., 3 segments = 4 control points

7. Generate control points:
   for i = 0; i < numControls; i++:  // 0, 1, 2, ..., numControls-1
       t = i / (numControls - 1)     // 0.0 to 1.0
       basePos = lerp(p1, p2, t)

       // Noise offset (skip endpoints to maintain connectivity)
       if i == 0 or i == numControls - 1:
           offset = 0
       else:
           // Seed from edge key + point index for determinism
           seed = HashCombine(key.min, key.max, i)
           offset = NoiseUtils.HashToFloat(seed) * Amplitude

       controlPoints[i] = basePos + perp * offset

8. Smooth with Catmull-Rom:
   result = InterpolateCatmullRom(controlPoints, SamplesPerSegment)

9. Cache as RelaxedEdges[key] = result
10. Return new List(result) or new List(result.Reverse()) // Always return copy
```

**Key points:**
- Endpoints (first and last) have zero offset to ensure edges meet at vertices
- Noise seed is deterministic from vertex indices
- Always return a copy to prevent mutation of cached data
- `NoiseUtils.HashToFloat` - see section 1.3.1 below

### 1.3.1 NoiseUtils helper class

**New file:** `unity/Assets/Scripts/Renderer/NoiseUtils.cs`

```csharp
public static class NoiseUtils
{
    /// <summary>
    /// Hash single int to float in range [-1, 1]. Deterministic.
    /// </summary>
    public static float HashToFloat(int seed)
    {
        uint h = (uint)(seed * 374761393);
        h = (h ^ (h >> 13)) * 1274126177;
        h = h ^ (h >> 16);
        return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF * 2f - 1f;
    }

    /// <summary>
    /// Combine multiple ints into single hash seed.
    /// </summary>
    public static int HashCombine(int a, int b, int c)
    {
        return a * 374761393 + b * 668265263 + c * 1013904223;
    }
}
```

### 1.3.2 Short edge handling

Catmull-Rom splines need 4 control points for smooth interpolation. For short edges with fewer control points:
- 1 control point: Return just the two endpoints (straight line)
- 2 control points: Return endpoints + single midpoint
- 3+ control points: Full Catmull-Rom interpolation

The `InterpolateCatmullRom` utility from RiverRenderer handles this by duplicating endpoints as phantom control points.

### 1.4 Build cell polygons

**Include ALL cells (land and water)** for complete texture coverage.

For each cell:
```csharp
var polygon = new List<Vector2>();
var verts = cell.VertexIndices;
int n = verts.Count;

for (int i = 0; i < n; i++)
{
    int v1 = verts[i];
    int v2 = verts[(i + 1) % n];  // Wrap to first vertex

    List<Vector2> edge = GetEdge(v1, v2);

    if (i == 0) {
        // First edge: add all points
        polygon.AddRange(edge);
    } else {
        // Subsequent edges: skip first point (duplicate of previous last point)
        polygon.AddRange(edge.Skip(1));
    }
}

// Remove last point if it duplicates first (closed polygon)
if (polygon.Count > 1 && Vector2.Distance(polygon[0], polygon[^1]) < 0.001f)
{
    polygon.RemoveAt(polygon.Count - 1);
}

CellPolygons[cell.Id] = polygon;
```

**Vertex winding order handling:**

If `VertexIndices` is not in winding order (verify in 1.1), sort before building:
```csharp
var center = cell.Center;
var sorted = cell.VertexIndices
    .OrderBy(vi => {
        var v = mapData.Vertices[vi];
        return Mathf.Atan2(v.Y - center.Y, v.X - center.X);
    })
    .ToList();
```
This produces CCW order. Use for all cells consistently.

**Map edge handling:**
- Edges where both vertices are on map boundary: skip relaxation, keep straight
- `IsBoundaryEdge(v1, v2)`: true if both vertices within 0.1 of same map edge (x≈0, x≈width, y≈0, y≈height)
- Prevents relaxation from pushing geometry outside map bounds

### 1.5 Update BorderRenderer

**File:** `unity/Assets/Scripts/Renderer/BorderRenderer.cs`

**Changes to Initialize():**
```csharp
public void Initialize(MapData data, RelaxedCellGeometry relaxedGeometry, float cellScale, float heightScale)
```

**Note:** State borders remain shader-based (current implementation). Only province/county borders use relaxed mesh geometry.

**Updated approach for finding borders:**

```csharp
private void GenerateBorders()
{
    // For each cell pair that shares an edge and differs politically...
    foreach (var cell in mapData.Cells)
    {
        if (!cell.IsLand) continue;

        foreach (int neighborId in cell.NeighborIds)
        {
            var neighbor = mapData.CellById[neighborId];
            if (!neighbor.IsLand) continue;
            if (cell.Id > neighbor.Id) continue; // Process each pair once

            // Skip state borders (handled by shader)
            if (cell.StateId != neighbor.StateId) continue;

            // Find shared edge (reuse existing function)
            var sharedEdge = FindSharedEdge(cell, neighbor);
            if (sharedEdge == null) continue;

            int v1 = sharedEdge.Value.StartVertexIdx;
            int v2 = sharedEdge.Value.EndVertexIdx;

            // Get relaxed edge (2D map coords)
            List<Vector2> relaxedEdge2D = relaxedGeometry.GetEdge(v1, v2);

            // Convert to 3D world coords
            List<Vector3> relaxedEdge3D = relaxedEdge2D
                .Select(p => new Vector3(p.x * cellScale, 0f, -p.y * cellScale))
                .ToList();

            // Categorize by border type
            if (cell.ProvinceId != neighbor.ProvinceId) {
                AddProvinceBorderEdge(cell.StateId, relaxedEdge3D, v1, v2);
            }
            else if (cell.CountyId != neighbor.CountyId) {
                AddCountyBorderEdge(cell.StateId, relaxedEdge3D, v1, v2);
            }
        }
    }

    // Chain and generate meshes (updated for multi-point edges)
    ChainAndGenerateMesh(provinceBorderEdges, provinceBorderMesh);
    ChainAndGenerateMesh(countyBorderEdges, countyBorderMesh);
}
```

**Updated BorderEdge struct:**
```csharp
private struct BorderEdge
{
    public List<Vector3> Points;  // Full relaxed edge (multiple points)
    public int StartVertexIdx;    // For chaining
    public int EndVertexIdx;
}
```

**Updated chaining:**
- Chain by matching `StartVertexIdx`/`EndVertexIdx` (same as before)
- When concatenating, join the `Points` lists (skip duplicate at junction)

### 1.6 Caching

RelaxedCellGeometry is deterministic (same parameters + MapData = same result). Cache it alongside the spatial grid:

```csharp
// Cache key includes parameters that affect output
string cacheKey = $"relaxed_{mapData.Info.Seed}_{Amplitude}_{Frequency}_{SamplesPerSegment}";

// Serialize CellPolygons and RelaxedEdges to binary
// Load from cache on subsequent runs
```

Consider caching after Stage 2 is complete (when both borders and textures use relaxed geometry).

### 1.7 Integration point

**File:** `unity/Assets/Scripts/Renderer/MapView.cs`

In `InitializeMap()` or similar:
```csharp
// After loading mapData...
relaxedGeometry = new RelaxedCellGeometry
{
    Amplitude = 1.0f,      // Tunable
    Frequency = 0.3f,      // Tunable
    SamplesPerSegment = 4  // Tunable
};
relaxedGeometry.Build(mapData);

// Pass to BorderRenderer
borderRenderer.Initialize(mapData, relaxedGeometry, cellScale, heightScale);

// Store for MapOverlayManager (Stage 2)
this.relaxedGeometry = relaxedGeometry;
```

### 1.8 Visual checkpoint

**Tunable parameters:**
- `Amplitude` - start with 0.5-1.0 map units (perpendicular wobble distance)
- `Frequency` - start with 0.2-0.5 control points per map unit
- `SamplesPerSegment` - start with 4 (Catmull-Rom smoothness)

Run scene, verify:
- Borders have organic meandering appearance
- Borders meet cleanly at junctions (no gaps at vertices)
- Different border types (state/province/county) look appropriate
- Border line thickness appropriate

**Expected:** Texture boundaries still use straight Voronoi edges, so they will be misaligned with relaxed mesh borders. This is fine - ignore until Stage 2.

---

## Stage 2: Texture Rasterization from Relaxed Geometry

**Goal:** Replace domain warping with rasterization of relaxed cell polygons.

### 2.1 Update MapOverlayManager initialization

**File:** `unity/Assets/Scripts/Renderer/MapOverlayManager.cs`

```csharp
public void Initialize(MapData mapData, RelaxedCellGeometry relaxedGeometry, ...)
{
    this.relaxedGeometry = relaxedGeometry;
    // ... existing setup ...
    BuildSpatialGridFromRelaxedGeometry();
    // ... rest unchanged ...
}
```

### 2.2 Implement scanline polygon rasterization

**Coordinate conversion:** RelaxedCellGeometry stores map coords. Grid coords = map coords × scale.

**Parallelization:** Relaxed cell edges can slightly overlap at boundaries due to random offsets. Use last-write-wins semantics — the visual artifact is at most 1 pixel at cell boundaries and not noticeable in practice.

```csharp
private void BuildSpatialGridFromRelaxedGeometry()
{
    float scale = resolutionMultiplier;

    // Initialize grid to -1 (no cell)
    Array.Fill(spatialGrid, -1);

    // Rasterize each cell polygon (parallelized, last-write-wins at boundaries)
    var cellList = relaxedGeometry.CellPolygons.ToList();
    Parallel.ForEach(cellList, kvp =>
    {
        int cellId = kvp.Key;
        List<Vector2> polygon = kvp.Value;
        RasterizePolygon(polygon, cellId, scale);
    });
}

private void RasterizePolygon(List<Vector2> polygon, int cellId, float scale)
{
    // Convert to grid coordinates
    var gridPoly = polygon.Select(p => new Vector2(p.x * scale, p.y * scale)).ToList();

    // Find Y bounds
    float minYf = gridPoly.Min(p => p.y);
    float maxYf = gridPoly.Max(p => p.y);
    int minY = Mathf.Max(0, Mathf.FloorToInt(minYf));
    int maxY = Mathf.Min(gridHeight - 1, Mathf.CeilToInt(maxYf));

    // Scanline fill
    for (int y = minY; y <= maxY; y++)
    {
        float scanY = y + 0.5f;
        var intersections = FindScanlineIntersections(gridPoly, scanY);

        if (intersections.Count < 2) continue;
        intersections.Sort();

        // Fill between pairs of intersections
        for (int i = 0; i < intersections.Count - 1; i += 2)
        {
            int x1 = Mathf.Max(0, Mathf.CeilToInt(intersections[i]));
            int x2 = Mathf.Min(gridWidth - 1, Mathf.FloorToInt(intersections[i + 1]));

            for (int x = x1; x <= x2; x++)
            {
                spatialGrid[y * gridWidth + x] = cellId;
            }
        }
    }
}

private List<float> FindScanlineIntersections(List<Vector2> polygon, float y)
{
    var intersections = new List<float>();
    int n = polygon.Count;

    for (int i = 0; i < n; i++)
    {
        Vector2 p1 = polygon[i];
        Vector2 p2 = polygon[(i + 1) % n];

        // Check if edge crosses scanline
        if ((p1.y <= y && p2.y > y) || (p2.y <= y && p1.y > y))
        {
            // Compute x intersection
            float t = (y - p1.y) / (p2.y - p1.y);
            float x = p1.x + t * (p2.x - p1.x);
            intersections.Add(x);
        }
    }

    return intersections;
}
```

### 2.3 Replace spatial grid building

Domain warping code was already removed in Phase 10. Replace the existing `BuildSpatialGridFromScratch` method (which uses straight Voronoi edges) with `BuildSpatialGridFromRelaxedGeometry` from section 2.2.

### 2.4 Update callers

- `MapView.cs`: Pass `relaxedGeometry` to `MapOverlayManager.Initialize()`
- Remove spatial grid cache loading (format changed, needs regeneration)

### 2.5 Visual checkpoint

Run scene, verify:
- Texture boundaries align with mesh borders
- No gaps between cells (fully rasterized)
- Political colors correct in all map modes (1-4)
- Selection highlight works
- Performance acceptable (profile grid build time)

---

## Stage 3: Cleanup

1. Delete old `BuildSpatialGridFromScratch()` method (replaced by relaxed geometry version)
2. Delete or invalidate spatial grid cache files (format changed)
3. Update `CLAUDE.md`:
   - Document relaxed geometry system
   - Update coordinate system notes
4. Update `CHANGELOG.md`

---

## Files Modified

| File | Changes |
|------|---------|
| `NoiseUtils.cs` | **New** - shared noise/hash utilities |
| `RelaxedCellGeometry.cs` | **New** - relaxed polygon generation |
| `BorderRenderer.cs` | Accept RelaxedCellGeometry, use relaxed edges for province/county borders |
| `MapOverlayManager.cs` | Scanline rasterization from relaxed polygons |
| `MapView.cs` | Create RelaxedCellGeometry, wire to both renderers |
| `CLAUDE.md` | Update documentation |

**Unchanged:** State borders remain shader-based (MapOverlay.shader)

## Coordinate Systems Summary

| System | Format | Used By |
|--------|--------|---------|
| Map coords | Vector2(x, y) | RelaxedCellGeometry, MapData |
| World coords | Vector3(x×scale, 0, -y×scale) | BorderRenderer meshes |
| Grid coords | (x×resMultiplier, y×resMultiplier) | MapOverlayManager spatial grid |

## Utilities to Reuse

| Utility | Source | Purpose |
|---------|--------|---------|
| `CatmullRomPoint()` | `RiverRenderer.cs` | Spline interpolation |
| `InterpolateCatmullRom()` | `RiverRenderer.cs` | Full spline interpolation |
| `GenerateColoredPolylineMesh()` | `BorderRenderer.cs` | Mesh from polylines |

## Verification

### After Stage 1:
1. Run MainScene
2. Borders should look organic and meandering
3. Zoom in to verify borders meet cleanly at vertices
4. Texture will be misaligned (expected)

### After Stage 2:
1. Run MainScene
2. Borders and textures should align perfectly
3. Cycle map modes (1-4), verify colors correct
4. Click to select cells, verify highlight works
5. Check Unity console for errors
6. Compare spatial grid build time to previous (should be similar or faster)
