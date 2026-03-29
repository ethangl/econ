# WorldGen Feature Ideas

Brainstormed geological features to add to the pipeline.

## Data Features

Cell/edge attributes computed on the sphere mesh (`src/WorldGen/`).

### Mountain Range Asymmetry

Convergent ocean-continent boundaries should produce a steep subduction face and a gradual backslope. BFS propagation is currently symmetric — biasing it by plate type would make ranges more realistic.

### Rift Morphology

Divergent boundaries currently just lower elevation. Real rifts have a graben profile: a central low flanked by raised shoulders. A shaped profile function instead of flat depression.

### Cratons / Shields

Mark some interior continental cells as ancient stable cores: slightly flattened terrain, reduced noise amplitude. Gives continents internal structure beyond "flat plateau + boundary mountains."

### Sedimentary Basins

Identify enclosed low areas between mountain ranges on continental plates, flatten them further. These become plains, river basins, breadbaskets.

### Seafloor Age Gradient

Track distance from divergent boundaries across oceanic plates. Older crust = cooler = denser = deeper. Gives ocean floors a realistic depth gradient instead of flat base elevation.

### ~~Multi-step Plate Motion~~

~~Instead of a single static snapshot, run 3-5 time steps of drift + re-classify boundaries + re-compute elevation. Produces layered orogeny, closed basins, and more complex coastlines.~~

### Isostatic Adjustment

Thick crust floats higher (Airy isostasy). Estimate crustal thickness from plate type + boundary proximity, adjust elevation. Mountains sink slightly, continental interiors rise.

## Data + Rendering Features

Require both sphere-level data (placement, classification) and heightmap-level detail (shapes too fine for coarse cells).

### Hotspot Volcanism

Data: identify hotspot positions, trace drift trails across plates, mark affected cells. Rendering: stamp volcanic cone profiles onto the heightmap at trail points — individual peaks and calderas are sub-cell features.

### Volcanic Arcs

Data: identify convergent ocean-continent boundaries, place arc positions offset inland on the overriding plate. Rendering: stamp individual stratovolcano peaks along the arc at heightmap resolution.

### Seamounts / Abyssal Hills

Data: mark hotspot tracks and young oceanic crust regions on cells to guide placement. Rendering: scatter cone-shaped elevation bumps at pixel resolution across tagged ocean floor.

## Rendering Features

Heightmap-level processing during rasterization/post-processing (`cli/WorldGen.Lib/`).

### Hydraulic Erosion

Particle-based or flow-accumulation erosion on the rendered heightmap grid. Carves river valleys, creates alluvial plains, breaks up plateau uniformity. Works best on a regular grid rather than irregular sphere cells.

## Dependencies & Implementation Order

### Foundational Decision: Multi-step Plate Motion

Restructures the pipeline from single-shot to iterative. Most data features behave differently inside a multi-step loop:

- Hotspot volcanism becomes natural (plates drift over hotspots) vs faked (project backward along drift vector)
- Seafloor age becomes real accumulated age vs proxy BFS distance from ridges
- Mountain asymmetry and rift morphology apply per step, building layered terrain

Either commit to multi-step early and design features within it, or defer it and accept that features designed now may need retrofitting.

### Isolated (implement any time, no dependencies)

- **Sedimentary basins** — post-process on existing cell elevations
- **Cratons / shields** — tag interior cells, reduce noise
- **Rift morphology** — reshape divergent boundary elevation profile
- **Hydraulic erosion** — pure heightmap post-process

### Boundary Refinements (soft chain)

1. **Mountain range asymmetry** — biased BFS by plate type
2. **Volcanic arcs** — offset inland from convergent boundaries; easier to place correctly after asymmetry reshapes the overriding plate profile

### Volcanism + Ocean Floor (dependency chain)

- **Hotspot volcanism** → informs **seamount** placement (hotspot tracks)
- **Seafloor age gradient** → informs **seamount** placement (young crust regions)
- **Hotspots + volcanic arcs** share cone-stamping rendering infrastructure; implement one first, second reuses it

### Always Last

- **Isostatic adjustment** — post-process that rebalances all elevation. Everything that modifies cell elevation should run before it.
