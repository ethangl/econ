# EconSim CLI Tools

## WorldGen Heightmap

Generates a spherical world via tectonic simulation, then unwraps it to a 2D equirectangular heightmap PNG.

### Usage

```
dotnet run --project WorldGen.Cli -- [options]
```

### Options

| Option                     | Default       | Description                                                            |
| -------------------------- | ------------- | ---------------------------------------------------------------------- |
| `--seed <int>`             | 42            | Random seed                                                            |
| `--cells <int>`            | 20400         | Dense cell count (more cells = finer terrain)                          |
| `--ultra`                  | false         | Enable ultra-dense mesh (~4x cells via subdivision)                    |
| `--ocean <float>`          | 0.6           | Ocean fraction (0-1)                                                   |
| `--jitter <float>`         | 0.5           | Point distribution jitter (0-1)                                        |
| `--width <int>`            | 8192          | Output image width                                                     |
| `--height <int>`           | 4096          | Output image height                                                    |
| `--coast <float>`          | 0.25          | Coastal detail amplitude (fractal noise near sea level, 0-1)           |
| `--blur <float>`           | 1.0           | Blur strength (1.0 = 5px sigma at 8192w, scales with resolution)      |
| `--sharpen <float>`        | 0             | Unsharp mask amount (0=off, 1=normal, 2=strong; uses blur sigma)       |
| `--color`                  | false         | Output terrain-colored RGB instead of grayscale                        |
| `--cpu`                    | false         | Force CPU coast detail instead of the default Metal path on macOS      |
| `--output <path>`          | heightmap.png | Output file path                                                       |

### Examples

```bash
# Basic heightmap
dotnet run --project WorldGen.Cli -- --seed 123 --output world.png

# High-detail with ultra-dense mesh and light blur
dotnet run --project WorldGen.Cli -- --seed 7 --ultra --blur 2 --output world_hd.png

# More ocean, fewer cells for faster iteration
dotnet run --project WorldGen.Cli -- --seed 99 --ocean 0.75 --cells 5000 --output quick.png

# Force the exact CPU coast-detail path
dotnet run --project WorldGen.Cli -- --cpu --output world_cpu.png
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
11. **Color ramp** (optional) — maps elevation to terrain colors (ocean blue → green → brown → snow)

On macOS, coast detail uses an accelerated Metal path by default. Use `--cpu` to force the reference CPU implementation instead. The Metal result is not bit-exact with CPU output, but observed drift is only ±1 on the 16-bit height scale.


### Output format

- Sea level is at 0.5 (32768 in 16-bit)
