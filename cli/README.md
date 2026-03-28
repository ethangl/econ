# EconSim CLI Tools

## WorldGen Heightmap

Generates a spherical world via tectonic simulation, then unwraps it to a 2D equirectangular heightmap. The primary output is a raw little-endian `.r16` height buffer, and the CLI also writes a viewable preview PNG by default.

### Usage

```
dotnet run --project WorldGen.Cli -- [options]
```

### Options

| Option                    | Default       | Description                                                                 |
| ------------------------- | ------------- | --------------------------------------------------------------------------- |
| `--seed <int>`            | 42            | Random seed                                                                 |
| `--cells <int>`           | 20400         | Dense cell count (more cells = finer terrain)                               |
| `--ultra`                 | false         | Enable ultra-dense mesh (~4x cells via subdivision)                         |
| `--ocean <float>`         | 0.6           | Ocean fraction (0-1)                                                        |
| `--jitter <float>`        | 0.5           | Point distribution jitter (0-1)                                             |
| `--width <int>`           | 8192          | Heightmap width                                                             |
| `--height <int>`          | 4096          | Heightmap height                                                            |
| `--coast <float>`         | 0.25          | Coastal detail amplitude (fractal noise near sea level, 0-1)                |
| `--blur <float>`          | 1.0           | Blur strength (1.0 = 5px sigma at 8192w, scales with resolution)            |
| `--sharpen <float>`       | 0             | Unsharp mask amount (0=off, 1=normal, 2=strong; uses blur sigma)            |
| `--color`                 | true          | Apply the terrain color ramp to the preview PNG                             |
| `--cpu`                   | false         | Force the CPU heightmap pipeline instead of the default Metal path on macOS |
| `--output <path>`         | heightmap.r16 | Raw little-endian 16-bit heightmap output path                              |
| `--preview-output <path>` | auto          | Preview PNG path; defaults to a sibling `.preview.png` file                 |
| `--preview-width <int>`   | 2048          | Maximum width for the preview PNG                                           |

### Examples

```bash
# Basic heightmap export
dotnet run --project WorldGen.Cli -- --seed 123 --output world.r16

# High-detail with ultra-dense mesh and light blur
dotnet run --project WorldGen.Cli -- --seed 7 --ultra --blur 2 --output world_hd.r16

# More ocean, fewer cells for faster iteration
dotnet run --project WorldGen.Cli -- --seed 99 --ocean 0.75 --cells 5000 --output quick.r16

# Force the CPU heightmap path
dotnet run --project WorldGen.Cli -- --cpu --output world_cpu.r16

# Override the default preview path and enable preview coloring
dotnet run --project WorldGen.Cli -- --output world.r16 --preview-output world.thumb.png --color
```

### Pipeline

1. **Fibonacci sphere** — distributes points uniformly on a unit sphere
2. **Convex hull + Voronoi** — builds a spherical Voronoi tessellation
3. **Tectonics** — seeds major/minor plates, classifies boundaries (convergent/divergent/transform)
4. **Elevation** — assigns base elevation from plate type, applies boundary effects, BFS propagation, smoothing
5. **Dense terrain** — generates a higher-resolution mesh, transfers elevation via nearest-neighbor, adds fractal noise
6. **Ultra-dense** (optional) — midpoint subdivision with Delaunay restoration for ~4x cell count
7. **Projection** — equirectangular unwrap with spatial-hash nearest-cell lookup
8. **Blur** (optional) — Gaussian blur with horizontal wrapping to avoid tiling seams
9. **Sharpen** (optional) — unsharp mask (also wraps horizontally) to recover terrain edges after blur
10. **Coast detail** — fractal 3D Perlin noise near sea level creates islands, inlets, and irregular coastlines; frequency scales with image resolution so features are consistent pixel size
11. **Color ramp** (optional) — maps elevation to terrain colors (ocean blue → green → brown → snow) for the preview PNG only

On macOS, the heightmap rasterization, blur, coast-detail, and optional preview color-ramp stages use Metal by default. Sharpen and preview PNG encoding still run on CPU. Use `--cpu` to force the reference CPU implementation instead. The Metal result is not bit-exact with CPU output.

### Output format

- Sea level is at 0.5 (32768 in 16-bit)
- `.r16` output is raw little-endian unsigned 16-bit grayscale with no header
- The preview PNG defaults to `<output-stem>.preview.png`
