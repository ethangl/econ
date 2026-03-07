using System;

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
            // 1. Generate dense mesh
            Vec3[] densePoints = FibonacciSphere.Generate(config.DenseCellCount, config.Jitter, config.Seed + 100);
            ConvexHull denseHull = ConvexHull.Build(densePoints);
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

            // 3. Transfer elevation + fractal noise for dense mesh
            float[] elevation = ComputeElevation(denseMesh, denseToCoarse, tectonics, config);

            var result = new DenseTerrainData
            {
                Mesh = denseMesh,
                DenseToCoarse = denseToCoarse,
                CellElevation = elevation,
            };

            // Skip tessellation for now (slow) — reuse dense as ultra-dense
            result.UltraDenseMesh = denseMesh;
            result.UltraDenseToCoarse = denseToCoarse;
            result.UltraDenseCellElevation = elevation;
            return result;

            // 4. Tessellate dense hull → ultra-dense mesh
            var rng = new Random(config.Seed + 300);
            ConvexHull ultraHull = SubdivisionBuilder.Subdivide(denseHull, config.SubdivisionJitter, rng);
            SphereMesh ultraMesh = SphericalVoronoiBuilder.Build(ultraHull, config.Radius);
            ultraMesh.ComputeAreas();

            // 5. Map ultra-dense → coarse via dense
            int[] ultraToDense = SubdivisionBuilder.BuildParentMapping(denseHull, ultraHull);
            int ultraCount = ultraMesh.CellCount;
            int[] ultraToCoarse = new int[ultraCount];
            for (int u = 0; u < ultraCount; u++)
                ultraToCoarse[u] = denseToCoarse[ultraToDense[u]];

            // 6. Transfer elevation + noise at ultra-dense resolution
            float[] ultraElevation = ComputeElevation(ultraMesh, ultraToCoarse, tectonics, config);

            result.UltraDenseMesh = ultraMesh;
            result.UltraDenseToCoarse = ultraToCoarse;
            result.UltraDenseCellElevation = ultraElevation;

            return result;
        }

        static float[] ComputeElevation(SphereMesh mesh, int[] toCoarse, TectonicData tectonics, WorldGenConfig config)
        {
            int count = mesh.CellCount;
            float[] elevation = new float[count];
            var noise = new Noise3D(config.Seed + 200);

            for (int d = 0; d < count; d++)
            {
                float baseElev = tectonics.CellElevation[toCoarse[d]];

                Vec3 p = mesh.CellCenters[d];
                float nx = p.X / config.Radius * NoiseFrequency;
                float ny = p.Y / config.Radius * NoiseFrequency;
                float nz = p.Z / config.Radius * NoiseFrequency;
                float noiseVal = noise.Fractal(nx, ny, nz, NoiseOctaves, NoiseLacunarity, NoisePersistence);

                float distFromCoast = Math.Abs(baseElev - SeaLevel);
                float dampFactor = Math.Min(distFromCoast / CoastDampingRange, 1.0f);

                elevation[d] = Clamp01(baseElev + noiseVal * NoiseAmplitude * dampFactor);
            }

            return elevation;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
