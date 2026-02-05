# MapGen

Native map generation for EconSim, replacing Azgaar dependency.

## Status

- Phase A.1: Cell Mesh (Voronoi/Delaunay) — **complete**
- Phase A.2: Heightmap DSL — **complete**
- Phase A.3: Rivers — planned
- Phase A.4: Biomes — planned

## Quick Start

1. Open in Unity
2. Create empty GameObject, add `CellMeshGenerator`, `CellMeshVisualizer`, and `HeightmapGenerator`
3. Create Camera, add `MapCamera` component
4. Click "Generate" on CellMeshGenerator, then "Generate Heightmap" on HeightmapGenerator
5. Try different templates (LowIsland, Archipelago, Continents, etc.)

## Structure

```
Assets/
├── Scripts/
│   ├── Core/               # Engine-independent
│   │   ├── CellMesh.cs     # Cell data structure
│   │   ├── Delaunay.cs     # Triangulation
│   │   ├── VoronoiBuilder.cs
│   │   ├── PointGenerator.cs
│   │   ├── HeightGrid.cs   # Height values
│   │   ├── HeightmapOps.cs # DSL operations
│   │   ├── HeightmapDSL.cs # DSL parser
│   │   └── HeightmapTemplates.cs
│   ├── CellMeshGenerator.cs
│   ├── CellMeshVisualizer.cs
│   ├── HeightmapGenerator.cs
│   └── MapCamera.cs
├── Editor/                 # Inspector extensions
├── Shaders/
└── UI/
```

## Heightmap Templates

| Template      | Description                        |
| ------------- | ---------------------------------- |
| LowIsland     | Small landmass, minimal elevation  |
| Archipelago   | Scattered islands, lots of coast   |
| Continents    | Large landmasses with inland seas  |
| Pangaea       | Single supercontinent              |
| Highland      | Mountainous, dramatic terrain      |
| Atoll         | Ring islands around central lagoon |
| Peninsula     | Land extending into water          |
| Mediterranean | Inland sea with surrounding land   |

## Custom DSL

Write custom heightmap scripts in the `CustomScript` field:

```
Hill 1 90-99 50 50       # Main landmass at center
Range 2-3 40-60 20-80 20-80  # Mountain ridges
Pit 3-5 20-30 30-70 30-70    # Lakes
Mask 4                       # Island edge falloff
Smooth 2                     # Blend terrain
```

See `Assets/Scripts/Core/README.md` for full DSL documentation.

## Dependencies

- **DelaunatorSharp** — Fast Delaunay triangulation (vendored, MIT license)

## See Also

- `../docs/migration/` - Migration planning docs
- `../docs/migration/heightmap.md` - Heightmap DSL spec
- `../unity/` - Original Azgaar-based project (reference)
