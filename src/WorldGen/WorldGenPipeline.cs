using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Entry point for spherical world generation.
    /// Generates points → convex hull → Voronoi → SphereMesh → tectonics.
    /// </summary>
    public static class WorldGenPipeline
    {
        /// <summary>
        /// Generate a WorldGenResult from configuration.
        /// </summary>
        public static WorldGenResult Generate(WorldGenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.CoarseCellCount < 4)
                throw new ArgumentException("Need at least 4 coarse cells", nameof(config));
            if (config.DenseCellCount < config.CoarseCellCount)
                throw new ArgumentException("DenseCellCount must be >= CoarseCellCount", nameof(config));

            // 1. Generate points on unit sphere (coarse tectonic mesh)
            Vec3[] points = FibonacciSphere.Generate(config.CoarseCellCount, config.Jitter, config.Seed);

            // 2. Build convex hull (= spherical Delaunay triangulation)
            ConvexHull hull = ConvexHull.Build(points);

            // 3. Build Voronoi dual
            SphereMesh mesh = SphericalVoronoiBuilder.Build(hull, config.Radius);

            // 4. Compute cell areas
            mesh.ComputeAreas();

            // 5. Generate tectonic plates
            TectonicData tectonics = TectonicOps.Generate(mesh, config.MajorPlateCount, config.MinorPlateCount,
                config.MajorHeadStartRounds, config.Seed, config.PolarCapLatitude);

            // 6. Compute tectonic elevation
            ElevationOps.Generate(mesh, tectonics, config.OceanFraction, config.Seed + 1);

            // 7. Generate dense terrain mesh with fractal noise
            DenseTerrainData denseTerrain = DenseTerrainOps.Generate(mesh, tectonics, config);

            // 8. Select site for flat map generation
            var siteRng = new Random(config.Seed + 2);
            var partialResult = new WorldGenResult
            {
                Mesh = mesh,
                Tectonics = tectonics,
                DenseTerrain = denseTerrain,
            };
            partialResult.Site = SiteSelector.Select(partialResult, config, siteRng);

            return partialResult;
        }
    }
}
