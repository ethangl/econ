# Map Texture Architecture

## Current State

Single `_CellDataTex` (RGBAFloat, gridWidth × gridHeight):
- R: RealmId / 65535
- G: ProvinceId / 65535
- B: (BiomeId*8 + SoilId + WaterFlag) / 65535
- A: CountyId / 65535

Plus ~12 supporting textures (heightmap, river mask, palettes, border distance maps, road mask, cell-to-market).

Total sampler count: ~13 of 16 (shader target 3.5).

### Problems
- B channel packs biome + soil via `biomeId*8 + soilId`. Fragile, hard to debug (caused significant issues during soil map mode implementation).
- No room for additional per-cell data without more sub-channel packing or new textures.
- Monolithic shader (~700 lines) handles all map modes in one fragment function. Works fine for GPU perf but painful to iterate on.

## CK3-Scale Data Requirements

### Per-Cell Data Textures Needed

**Political (2 textures, 8 channels)**
- Realm, kingdom, duchy, county, barony IDs
- De jure vs de facto territory
- Occupation/control during wars
- Diplomatic state (truces, alliances, etc.)

**Geography/Geology (1-2 textures, 4-8 channels)**
- Biome, soil type, rock type, terrain type
- Vegetation density, fertility
- (Elevation already has dedicated heightmap)

**Climate (1 texture, 4 channels)**
- Temperature, precipitation, moisture, seasonal modifier

**Demographics (1-2 textures, 4-8 channels)**
- Population, culture ID, religion ID, development level
- Disease, unrest, migration pressure

**Economic (1-2 textures, 4-8 channels)**
- Market zone, trade route membership, wealth, supply level
- Building/holding type, resource availability

**Military (1 texture, dynamic, 4 channels)**
- Control level, siege progress, supply lines, fortification

**Overlays (1-2 textures, dynamic)**
- Fog of war, war score, selected realm highlight, alert regions

**Total: ~8-12 data textures + palettes + border distance maps = 20-30 samplers**

### Supporting Textures (Already Exist or Needed)
- Heightmap (RFloat)
- River mask (R8)
- Border distance maps (realm, province, county, market — R8 each)
- Road mask (R8, dynamic)
- Palette textures (realm, market, culture, religion — 256x1 each)
- Biome-elevation matrix (64x64)

## Architecture Options

### Option 1: Bump Shader Model (Simplest)
- Target SM 4.5+ → 32+ samplers
- Keep current approach: one texture per data domain, one sampler each
- Modern GPUs all support this; no mobile concern for this project
- **Pro:** Simple, each texture is clean RGBA with one ID per channel
- **Con:** Still many texture fetches per pixel (cache pressure with 20+ textures)

### Option 2: Texture Arrays
- Pack related data into `Texture2DArray` slices (e.g., all political layers as one array)
- Costs 1 sampler binding per array regardless of slice count
- **Pro:** Fewer sampler slots used, good for related data
- **Con:** All slices must share format and resolution; more complex API

### Option 3: Atlasing
- Tile multiple data maps into one large texture (e.g., 4 maps in a 2×2 atlas)
- UV math to select the right quadrant
- **Pro:** Fewer bindings
- **Con:** Wastes texture memory if maps have different ideal resolutions; UV math adds complexity

### Option 4: Compute Pre-Pass (CK3 Approach)
- Each frame, a compute shader (or C# job) resolves the active map mode into a single "display color" texture
- Fragment shader just samples that resolved texture + does water/selection/hover
- Only the active mode's data textures need to be bound during the resolve pass
- **Pro:** Fragment shader stays trivial; sampler count is always low; easy to add new modes
- **Con:** Extra GPU pass per frame; mode transitions need re-resolve; loses ability to blend between modes

### Option 5: Hybrid
- Keep 2-3 "always needed" data textures (political IDs, water flags, heightmap)
- Use compute pre-pass for the active map mode's specialized data
- Fragment shader handles compositing (terrain + resolved mode color + water + selection)
- **Pro:** Best of both worlds; core data always available for selection/hover, mode-specific data resolved efficiently
- **Con:** More architectural complexity

## Recommendation

**Short term:** Option 1 (bump SM target to 4.5, add a second geology texture for soil/rock/vegetation/fertility). Unblock current work without refactoring.

**Medium term:** Option 5 (hybrid). Keep a small set of always-bound core textures (political, heightmap, water). Add a compute pre-pass that resolves the active map mode into a display texture. This scales to arbitrary map modes without sampler pressure.

**The monolithic shader is fine.** GPU branching on `_MapMode` is effectively free since all pixels take the same branch (uniform control flow). Splitting into separate shaders would require multiple materials or passes, which is worse for performance. The shader is hard to read at 700 lines but that's a code organization problem, not a performance one — `#include` or `CGINCLUDE` blocks can help.

## EU5 Reference

EU5 (Clausewitz/Jomini engine) uses a **baked decal system**: 16 decals at 8192×4096, each with 16 grayscale mask textures controlling terrain material blending (climate × topography × vegetation). These are pre-composited offline into cached `.bin` files. Province data is a simple color-coded lookup image (`locations.png`, 16384×8192 RGB — one unique color per province).

Key difference: EU5's map is a fixed asset. They can spend arbitrary time baking optimized textures offline. We generate maps from a seed at runtime, so offline baking isn't available.

**Implication for us:** The hybrid approach (Option 5) fits our constraints best. After `MapGenAdapter.Convert` finishes, run a one-time "resolve" step that composites clean per-domain textures from cell data. This is still at runtime but only once per map generation, not per frame. The fragment shader then samples pre-resolved textures — similar to EU5's baked approach but triggered by generation instead of an editor tool.

This also opens the door to caching resolved textures to disk alongside saved maps, getting closer to EU5's model for subsequent loads.

## Migration Path

1. Extract soil from B channel packing back to a clean channel in a new geology texture
2. Add rock type, vegetation density, fertility to remaining channels
3. Bump shader target to 4.5
4. When sampler count approaches 32, implement compute pre-pass for mode-specific rendering
5. Keep political + geology + heightmap as always-bound core textures

## Future Textures

### Relief Texture (cosmetic)

Generate a procedural relief texture from the heightmap for visual terrain detail (hillshading, surface roughness) without affecting gameplay elevation data. Generated once after map gen.

- **Blur**: Gaussian blur on heightmap (radius ~3-5 texels) to soften cell boundaries
- **Noise**: Layered Perlin/simplex at 2-3 octaves, amplitude ~5-10% of height range
- **Combine**: `blurred_height + noise * scale`
- **Shader use**: Normal-map-style hillshading, or direct multiply with terrain color for subtle relief

Reference: CK3/EU5 terrain detail uses baked normal maps derived from heightmap + artist-placed detail. Our version would be fully procedural.

### Elevation Range Refactor (econ-hzr)

Current 0-100 range (from Azgaar) gives only 80 integer land levels. Bump to 0-255 for 204 land levels and natural byte mapping. Well-abstracted via `HeightGrid.SeaLevel`/`MaxHeight` constants — mostly a constants + threshold update.
