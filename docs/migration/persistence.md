# Persistence Strategy

Currently: Azgaar JSON → parse → MapData (rebuilt every launch)

Problem: Map generation will get expensive. Don't want to regenerate on every game start.

## What to Persist

Three layers, different trade-offs:

| Layer                | Contents                        | Size       | Regen Cost          |
| -------------------- | ------------------------------- | ---------- | ------------------- |
| **World seed**       | Just the seed string            | ~32 bytes  | Full regen          |
| **MapData**          | All cells, rivers, states, etc. | ~5-20 MB   | Fast (load only)    |
| **Derived textures** | Data textures, palettes, meshes | ~50-200 MB | Medium (GPU upload) |

**Recommendation:** Persist MapData as primary cache. Regenerate textures on load (fast, GPU-bound).

## Format Options

| Format                     | Pros                           | Cons                    |
| -------------------------- | ------------------------------ | ----------------------- |
| **JSON**                   | Human-readable, debuggable     | Large files, slow parse |
| **MessagePack**            | Fast, compact, schema-flexible | Binary, needs library   |
| **Unity ScriptableObject** | Editor integration, inspector  | Unity-coupled           |
| **Custom binary**          | Maximum control, smallest      | Maintenance burden      |

**Recommendation:** MessagePack or similar binary format. MapData is already `[Serializable]`.

## Proposed File Structure

```
saves/
├── worlds/
│   ├── {seed}.world          # Serialized MapData (binary)
│   └── {seed}.world.meta     # Metadata: seed, gen version, timestamp
└── games/
    └── {save-name}/
        ├── world-ref.txt     # Points to worlds/{seed}.world
        ├── economy.dat       # EconomyState snapshot
        └── simulation.dat    # Tick count, RNG state
```

**Key insight:** Separate world data from game save. Same world seed = same geography. Multiple saves can share a world file.

## Generation Pipeline

```
New Game:
  seed ──► [World exists?] ──yes──► Load MapData
                │
                no
                │
                ▼
          Generate MapData ──► Save to worlds/{seed}.world
                │
                ▼
          Initialize EconomyState
                │
                ▼
          Generate textures (GPU)
                │
                ▼
          Ready to play

Load Game:
  save ──► Read world-ref ──► Load MapData from worlds/
                │
                ▼
          Load EconomyState from save
                │
                ▼
          Generate textures (GPU)
                │
                ▼
          Ready to play
```

## Texture Caching (Optional)

For large maps or complex derived data, consider caching GPU textures too:

```
worlds/
├── {seed}.world              # MapData
├── {seed}.textures/          # Pre-baked textures (optional)
│   ├── spatial-grid.raw      # Cell ownership texture
│   ├── river-mask.raw        # River knockout mask
│   └── manifest.json         # Dimensions, format, version
```

Trade-off: Disk space vs. load time. Measure before committing.

## Development Workflow

Fast load times directly improve iteration speed in Unity:

- Play mode entry should be <2 seconds ideally
- Texture generation currently dominates startup
- Caching MapData alone helps, but texture caching may be worth it

Consider: **Editor-only aggressive caching** that persists across play mode cycles. Production builds can regenerate textures if disk space matters more than load time.

## Cache Invalidation

World files should include generation version. If generator algorithm changes, old caches are invalid:

```json
{
  "seed": "1234",
  "generator_version": "0.1.0",
  "generated_at": "2026-02-03T12:00:00Z",
  "cell_count": 10000
}
```

On load: if `generator_version` doesn't match current, regenerate.
