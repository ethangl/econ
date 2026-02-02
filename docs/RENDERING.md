# Map Rendering Roadmap

Visual improvements and rendering features for the economic simulator map.

## Current State (Post-Phase 7.5)

- Palette-based rendering with data textures
- Shader-based borders with 16-sample anti-aliasing
- Heightmap integration (vertex displacement, normals)
- Map modes: political (1), province (2), county (3), market (4), terrain (5), height (0)
- Separate line renderers for rivers and roads
- GPU-based selection highlight (mode-aware: state/province/market/cell)

### Data Texture Format (RGBAFloat)

```
R: StateId / 65535
G: ProvinceId / 65535
B: (BiomeId + WaterFlag) / 65535  -- water flag adds 32768 if cell is water
A: CellId / 65535                 -- enables per-cell coloring (32-bit for precision)
```

### Color Derivation (Shader-Based Cascade)

State colors are stored in a 256-entry palette. Province and county colors are computed in the shader:

```
State:    Palette lookup by StateId (S base 0.52, V base 0.70)
Province: DeriveProvinceColor(stateColor, provinceId)  -- ±0.05 H, ±0.07 S/V
County:   DeriveCountyColor(provinceColor, cellId)     -- ±0.05 H, ±0.07 S/V
```

HSV clamping: S [0.15, 0.95], V [0.25, 0.95] (allows ± variance around parent)

### Market Zone Texture

Dynamic cell-to-market mapping stored in separate texture:
- `cellToMarketTexture` (16384x1, RHalf)
- Maps CellId → MarketId
- Updated when economy state changes
- Allows market zones to change without regenerating main data texture

### Selection Highlight (Shader-Based)

GPU-based selection using same anti-aliasing technique as borders:
- `_SelectedStateId`, `_SelectedProvinceId`, `_SelectedCellId`, `_SelectedMarketId` (normalized, -1 = none)
- Only one selection level active at a time (priority: state → province → market → cell)
- Mode-aware: political mode selects state, province mode selects province, etc.
- Configurable border color, width (1-6px), and fill alpha (0-0.5)
- Market selection excludes water cells
- Selection cleared on game start (prevents persistence across play sessions)

## Phase 7: Rendering Refinement

Core focus: polish existing systems, add water rendering, improve selection feedback.

### 7.1 Color Generation ✓

- [x] State colors: even hue distribution, hash-based S/V variance
- [x] Province colors: shader-derived from state with HSV variance
- [x] County colors: shader-derived from province with smaller variance
- [x] Data texture restructure: CellId in A channel (was MarketId)
- [x] Separate cell-to-market texture for dynamic market zones
- [x] HSV clamping to avoid UI/border conflicts

### 7.2 Border Rendering

- Refine border thickness and anti-aliasing
- Hierarchy visualization (state borders thicker than province)
- Border style options (solid, dashed for contested?)

### 7.3 Water Shader

- Dedicated water rendering for ocean/lakes/rivers
- Animated waves or subtle movement
- Depth-based color gradient (shallow → deep)
- Foam/shore effects at coastline

### 7.4 Elevation Tinting

- Apply elevation-based color modification to all map modes
- Toggle with H key
- Subtle darkening in valleys, lightening on peaks
- Works alongside existing map mode colors

### 7.5 Selection Shader ✓

- [x] GPU-based selection highlight (replaced misaligned yellow outline)
- [x] Anti-aliased selection border using same technique as other borders
- [x] Fill tint for selected region (configurable alpha)
- [x] Mode-aware selection: country/province/market/cell based on current map mode
- [ ] Pulsing or animated border effect (future enhancement)

### 7.6 Coastline Enhancement

- Distinct land/water boundary treatment
- Beach gradient or shore line
- Pairs with water shader work

### 7.7 Cleanup

- Remove heightmap map mode (3) — redundant with elevation tinting
- Consolidate map mode numbering

## Future Phases

### Visual Polish

| Feature | Description | Effort |
|---------|-------------|--------|
| Terrain noise overlay | Procedural detail to break up flat colors | Low |
| Ambient occlusion | Darken valleys, lighten ridges from heightmap | Medium |
| Normal mapping | Apparent surface detail without geometry | Medium |
| Map mode transitions | Smooth crossfade when switching modes | Low |
| Seasonal tinting | Palette variations for seasons/weather | Low |

### Practical Features

| Feature | Description | Effort |
|---------|-------------|--------|
| Minimap | Corner overview with viewport indicator | Medium |
| Province/state labels | Text rendering on map | High |
| Grid overlay toggle | Show cell boundaries for debug | Low |
| Fog of war | Shader support for hidden/unexplored regions | Medium |
| Trade route visualization | Animated flow lines for active trade | Medium |
| Terrain icons | Trees for forests, mountain peaks, etc. | High |

### Technical Improvements

| Feature | Description | Effort |
|---------|-------------|--------|
| Unified river/road shader | Move line renderers into main shader | Medium |
| LOD system | Reduce mesh detail at distance | Medium |
| Instanced rendering | For repeated elements (icons, labels) | Medium |

## Technical Notes

### Data Texture Format (Current)

```
R: StateId / 65535
G: ProvinceId / 65535
B: (BiomeId + WaterFlag) / 65535  // water flag adds 32768
A: CellId / 65535                 // 32-bit float for precision
```

### Coordinate Systems

- Azgaar: Y=0 at top (screen coordinates)
- Unity: Y=0 at bottom for textures
- Y-flip happens at texture generation in MapOverlayManager

### Key Files

- `Assets/Shaders/MapOverlay.shader` — main map shader
- `Assets/Scripts/Renderer/MapOverlayManager.cs` — data texture generation
- `Assets/Scripts/Renderer/MapView.cs` — mesh generation, map modes
