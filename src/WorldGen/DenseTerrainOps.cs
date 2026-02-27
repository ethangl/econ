using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Generates a dense terrain mesh from coarse tectonic data.
    /// Transfers elevation via nearest-neighbor mapping and adds fractal noise.
    /// </summary>
    public static class DenseTerrainOps
    {
        const int NoiseOctaves = 6;
        const float NoiseFrequency = 8.0f;
        const float NoiseLacunarity = 2.0f;
        const float NoisePersistence = 0.5f;
        const float NoiseAmplitude = 0.15f;
        const float CoastDampingRange = 0.06f;
        const float SeaLevel = 0.4f;

        public static DenseTerrainData Generate(SphereMesh coarseMesh, TectonicData tectonics, WorldGenConfig config)
        {
            // 1. Generate dense mesh
            Vec3[] densePoints = FibonacciSphere.Generate(config.DenseCellCount, config.Jitter, config.Seed + 100);
            ConvexHull denseHull = ConvexHullBuilder.Build(densePoints);
            SphereMesh denseMesh = SphericalVoronoiBuilder.Build(denseHull, config.Radius);
            denseMesh.ComputeAreas();

            // 2. Map each dense cell to nearest coarse cell
            int denseCount = denseMesh.CellCount;
            int coarseCount = coarseMesh.CellCount;
            int[] denseToCoarse = new int[denseCount];

            for (int d = 0; d < denseCount; d++)
            {
                Vec3 dp = denseMesh.CellCenters[d];
                float bestDist = float.MaxValue;
                int bestCoarse = 0;

                for (int c = 0; c < coarseCount; c++)
                {
                    float dist = Vec3.SqrDistance(dp, coarseMesh.CellCenters[c]);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestCoarse = c;
                    }
                }

                denseToCoarse[d] = bestCoarse;
            }

            // 3. Transfer elevation + fractal noise
            float[] elevation = new float[denseCount];
            var noise = new Noise3D(config.Seed + 200);

            for (int d = 0; d < denseCount; d++)
            {
                float baseElev = tectonics.CellElevation[denseToCoarse[d]];

                // Sample 3D fractal noise at cell center position (on unit sphere, scaled by frequency)
                Vec3 p = denseMesh.CellCenters[d];
                float nx = p.X / config.Radius * NoiseFrequency;
                float ny = p.Y / config.Radius * NoiseFrequency;
                float nz = p.Z / config.Radius * NoiseFrequency;
                float noiseVal = noise.Fractal(nx, ny, nz, NoiseOctaves, NoiseLacunarity, NoisePersistence);

                // Coast damping: attenuate noise near sea level to preserve tectonic coastlines
                float distFromCoast = Math.Abs(baseElev - SeaLevel);
                float dampFactor = Math.Min(distFromCoast / CoastDampingRange, 1.0f);

                elevation[d] = Clamp01(baseElev + noiseVal * NoiseAmplitude * dampFactor);
            }

            return new DenseTerrainData
            {
                Mesh = denseMesh,
                DenseToCoarse = denseToCoarse,
                CellElevation = elevation,
            };
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
