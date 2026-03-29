using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WorldGen.Core
{
    /// <summary>
    /// Generates a dense terrain mesh from coarse tectonic data.
    /// Transfers elevation via nearest-neighbor mapping and adds fractal noise.
    /// Optionally tessellates the dense hull to produce an ultra-dense mesh (~4x cells).
    /// </summary>
    public static class DenseTerrainOps
    {
        const int NoiseOctaves = 8;
        const float NoiseFrequency = 8.0f;
        const float NoiseLacunarity = 2.0f;
        const float NoisePersistence = 0.5f;
        const float NoiseAmplitude = 0.5f;
        const float CoastDampingRange = 0.0f;
        const float SeaLevel = 0.5f;

        public static DenseTerrainData Generate(SphereMesh coarseMesh, TectonicData tectonics, WorldGenConfig config)
        {
            var timings = new DenseTerrainTimingData();
            var totalSw = Stopwatch.StartNew();
            var stepSw = Stopwatch.StartNew();

            // 1. Generate dense mesh
            Vec3[] densePoints = FibonacciSphere.Generate(config.DenseCellCount, config.Jitter, config.Seed + 100);
            timings.DensePointsSeconds = stepSw.Elapsed.TotalSeconds;

            stepSw.Restart();
            ConvexHull denseHull = ConvexHull.Build(densePoints);
            timings.DenseHullSeconds = stepSw.Elapsed.TotalSeconds;

            stepSw.Restart();
            SphereMesh denseMesh = SphericalVoronoiBuilder.Build(denseHull, config.Radius);
            timings.DenseVoronoiSeconds = stepSw.Elapsed.TotalSeconds;

            stepSw.Restart();
            denseMesh.ComputeAreas();
            timings.DenseAreaSeconds = stepSw.Elapsed.TotalSeconds;

            // 2. Map each dense cell to nearest coarse cell
            int denseCount = denseMesh.CellCount;
            int[] denseToCoarse = new int[denseCount];
            var coarseLookup = new NearestCellLookup(coarseMesh.CellCenters);

            stepSw.Restart();
            Parallel.For(0, denseCount, d =>
            {
                denseToCoarse[d] = coarseLookup.Nearest(denseMesh.CellCenters[d]);
            });
            timings.DenseMappingSeconds = stepSw.Elapsed.TotalSeconds;

            // 3. Transfer elevation + fractal noise for dense mesh
            stepSw.Restart();
            float[] elevation = ComputeElevation(denseMesh, denseToCoarse, tectonics, config);
            timings.DenseElevationSeconds = stepSw.Elapsed.TotalSeconds;

            var result = new DenseTerrainData
            {
                Mesh = denseMesh,
                DenseToCoarse = denseToCoarse,
                CellElevation = elevation,
                Timings = timings,
            };

            if (!config.EnableUltraDense)
            {
                result.UltraDenseMesh = denseMesh;
                result.UltraDenseToCoarse = denseToCoarse;
                result.UltraDenseCellElevation = elevation;
                timings.TotalSeconds = totalSw.Elapsed.TotalSeconds;
                return result;
            }

            // 4. Tessellate dense hull → ultra-dense mesh
            var rng = new Random(config.Seed + 300);

            stepSw.Restart();
            ConvexHull ultraHull = SubdivisionBuilder.Subdivide(denseHull, config.SubdivisionJitter, rng, timings);
            timings.UltraSubdivisionSeconds = stepSw.Elapsed.TotalSeconds;

            stepSw.Restart();
            SphereMesh ultraMesh = SphericalVoronoiBuilder.Build(ultraHull, config.Radius);
            timings.UltraVoronoiSeconds = stepSw.Elapsed.TotalSeconds;

            stepSw.Restart();
            ultraMesh.ComputeAreas();
            timings.UltraAreaSeconds = stepSw.Elapsed.TotalSeconds;

            // 5. Map ultra-dense → coarse via dense
            stepSw.Restart();
            int[] ultraToDense = SubdivisionBuilder.BuildParentMapping(denseHull, ultraHull);
            timings.UltraMappingSeconds = stepSw.Elapsed.TotalSeconds;
            int ultraCount = ultraMesh.CellCount;
            int[] ultraToCoarse = new int[ultraCount];
            for (int u = 0; u < ultraCount; u++)
                ultraToCoarse[u] = denseToCoarse[ultraToDense[u]];

            // 6. Transfer elevation + noise at ultra-dense resolution
            stepSw.Restart();
            float[] ultraElevation = ComputeElevation(ultraMesh, ultraToCoarse, tectonics, config);
            timings.UltraElevationSeconds = stepSw.Elapsed.TotalSeconds;

            result.UltraDenseMesh = ultraMesh;
            result.UltraDenseToCoarse = ultraToCoarse;
            result.UltraDenseCellElevation = ultraElevation;
            timings.TotalSeconds = totalSw.Elapsed.TotalSeconds;

            return result;
        }

        static float[] ComputeElevation(SphereMesh mesh, int[] toCoarse, TectonicData tectonics, WorldGenConfig config)
        {
            int count = mesh.CellCount;
            float[] elevation = new float[count];
            Parallel.For(
                0,
                count,
                () => new Noise3D(config.Seed + 200),
                (d, _, noise) =>
                {
                    float baseElev = tectonics.CellElevation[toCoarse[d]];

                    Vec3 p = mesh.CellCenters[d];
                    float nx = p.X / config.Radius * NoiseFrequency;
                    float ny = p.Y / config.Radius * NoiseFrequency;
                    float nz = p.Z / config.Radius * NoiseFrequency;
                    float noiseVal = noise.Fractal(nx, ny, nz, NoiseOctaves, NoiseLacunarity, NoisePersistence);

                    float distFromCoast = Math.Abs(baseElev - SeaLevel);
                    float dampFactor = Math.Min(distFromCoast / CoastDampingRange, 1.0f);

                    // Dampen noise on craton cells
                    float cratonDamp = 1f;
                    if (tectonics.CellCratonStrength != null)
                    {
                        float cratonStr = tectonics.CellCratonStrength[toCoarse[d]];
                        if (cratonStr > 0f)
                            cratonDamp = 1f - cratonStr * (1f - config.CratonNoiseMultiplier);
                    }

                    elevation[d] = baseElev + noiseVal * NoiseAmplitude * dampFactor * cratonDamp;
                    return noise;
                },
                _ => { });

            // Seamount cone bumps — applied after base noise so cones sit on top of ocean floor
            if (tectonics.Seamounts != null && tectonics.Seamounts.Length > 0)
            {
                float radiusSq = config.SeamountRadius * config.SeamountRadius;
                var peaks = tectonics.Seamounts;

                Parallel.For(0, count, d =>
                {
                    if (!tectonics.CellCrustOceanic[toCoarse[d]])
                        return;

                    Vec3 p = mesh.CellCenters[d];
                    for (int i = 0; i < peaks.Length; i++)
                    {
                        float dSq = Vec3.SqrDistance(p, peaks[i].Position);
                        if (dSq < radiusSq)
                        {
                            float t = 1f - (float)Math.Sqrt(dSq) / config.SeamountRadius;
                            elevation[d] += peaks[i].Height * t * t; // quadratic falloff
                        }
                    }
                });
            }

            // Final clamp
            for (int d = 0; d < count; d++)
                elevation[d] = Clamp01(elevation[d]);

            return elevation;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
