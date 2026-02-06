# Migration from Azgaar

This directory documents our plan to migrate away from Azgaar's Fantasy Map Generator toward our own world generation systems.

## Contents

| Document                                | Topic                                    |
| --------------------------------------- | ---------------------------------------- |
| [Philosophy & Overview](./overview.md)  | Goals, dependencies, migration boundary  |
| [Persistence](./persistence.md)         | Caching MapData, file formats            |
| [Heightmap](./heightmap.md)             | Terrain generation, template DSL         |
| [Rivers](./rivers.md)                   | Flow accumulation, edge-based design     |
| [Climate & Biomes](./climate-biomes.md) | Temperature, precipitation, biome matrix |
| [Settlements](./settlements.md)         | Suitability scoring, population          |
| [Cultures](./cultures.md)               | Culture types, expansion, naming         |
| [Political](./political.md)             | States, provinces, borders               |
| [Shortcomings](./shortcomings.md)       | Azgaar issues we want to address         |

## Quick Reference

### Dependency Chain

```
Seed → Cell Mesh → Heightmap
                       ↓
         ┌─────────────┼─────────────┐
         ↓             ↓             ↓
      Rivers       Climate      Resources
         └─────────────┼─────────────┘
                       ↓
                    Biomes
                       ↓
                  RankCells (suitability)
                       ↓
                  Population
                       ↓
                   Cultures
                       ↓
              States (expansion)
                       ↓
                  Provinces
                       ↓
              CountyGrouper (ours)
                       ↓
                Markets (ours)
```

### Migration Boundary

```
Azgaar JSON ──► AzgaarParser ──► MapConverter ──► MapData ──► [Our Systems]
                                                    ▲
                                                    │
                                            Replace everything
                                            upstream of MapData
```

### Project Structure

We maintain parallel Unity projects during migration:

```
econ/
├── unity/           # Azgaar-based (reference, frozen)
├── unity-mapgen/    # New native generation
└── src/EconSim.Core/  # Forked into new project as needed
```

**Rationale:** MapData's structure is shaped by Azgaar's output — trying to maintain compatibility between both systems would constrain the new design and litter the codebase with adapter code. A clean separation lets us:

- Design MapData around what we actually need (e.g., edge-based rivers for transport modeling)
- Keep the old project as a working reference
- Cherry-pick simulation code (economy, trade) forward when ready

### Proposed Order

**Phase A: Foundation**

[x] Cell Mesh (Voronoi/Delaunay)
[x] Heightmap (noise + templates)
[x] Climate Model
[x] Rivers (flow accumulation)

**Phase B: Economic Geography**

[ ] Resource Placement
[ ] Settlement/Population
[ ] Political Boundaries

**Phase C: Polish**

[ ] Biome Refinement
[ ] Naming

## References

- Red Blob Games — terrain generation, Voronoi/Delaunay
- Azgaar's source code — `reference/azgaar/`
- "Polygonal Map Generation" (Amit Patel)
- Flow accumulation / D8 algorithm (standard GIS technique)

---

_Last updated: 2026-02-05_
