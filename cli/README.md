# EconSim CLI Tools

## WorldGen Heightmap

Generates a spherical world via tectonic simulation, then unwraps it to a 2D equirectangular heightmap PNG.

### Usage

```
dotnet run --project WorldGen.Cli -- [options]
```

### Options

| Option             | Default       | Description                                                            |
| ------------------ | ------------- | ---------------------------------------------------------------------- |
| `--seed <int>`     | 42            | Random seed                                                            |
| `--cells <int>`    | 20400         | Dense cell count (more cells = finer terrain)                          |
| `--ultra`          | false         | Enable ultra-dense mesh (~4x cells via subdivision)                    |
| `--ocean <float>`  | 0.6           | Ocean fraction (0-1)                                                   |
| `--jitter <float>` | 0.5           | Point distribution jitter (0-1)                                        |
| `--width <int>`    | 8192          | Output image width                                                     |
| `--height <int>`   | 4096          | Output image height                                                    |
| `--16bit`          | true          | 16-bit grayscale output                                                |
| `--blur <float>`   | 0             | Gaussian blur sigma in pixels (wraps horizontally for seamless tiling) |
| `--output <path>`  | heightmap.png | Output file path                                                       |

### Examples

```bash
# Basic heightmap
dotnet run --project WorldGen.Cli -- --seed 123 --output world.png

# High-detail with ultra-dense mesh and light blur
dotnet run --project WorldGen.Cli -- --seed 7 --ultra --blur 2 --output world_hd.png

# More ocean, fewer cells for faster iteration
dotnet run --project WorldGen.Cli -- --seed 99 --ocean 0.75 --cells 5000 --output quick.png
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

### Output format

- **16-bit** (default): elevation 0.0-1.0 mapped to grayscale 0-65535
- **8-bit** (`--16bit false`): elevation 0.0-1.0 mapped to grayscale 0-255
- Sea level is at 0.5 (128 in 8-bit, 32768 in 16-bit)
