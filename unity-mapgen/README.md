# MapGen

Native map generation for EconSim, replacing Azgaar dependency.

## Status

Phase A.1: Cell Mesh (Voronoi/Delaunay) — in progress

## Quick Start

1. Open in Unity
2. Create empty GameObject, add `CellMeshGenerator` and `CellMeshVisualizer`
3. Create Camera, add `MapCamera` component
4. Click "Generate" in inspector (or enter Play mode)

## Structure

```
Assets/
├── Scripts/
│   ├── Core/           # Engine-independent (CellMesh, Delaunay, Voronoi)
│   ├── CellMeshGenerator.cs
│   ├── CellMeshVisualizer.cs
│   └── MapCamera.cs
├── Editor/             # Inspector extensions
├── Shaders/
└── UI/
```

## Dependencies

- **DelaunatorSharp** — Fast Delaunay triangulation (vendored, MIT license)

## See Also

- `../docs/migration/` - Migration planning docs
- `../unity/` - Original Azgaar-based project (reference)
