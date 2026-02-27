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
            if (config.CellCount < 4)
                throw new ArgumentException("Need at least 4 cells", nameof(config));

            // 1. Generate points on unit sphere
            Vec3[] points = FibonacciSphere.Generate(config.CellCount, config.Jitter, config.Seed);

            // 2. Build convex hull (= spherical Delaunay triangulation)
            ConvexHull hull = ConvexHullBuilder.Build(points);

            // 3. Build Voronoi dual
            SphereMesh mesh = SphericalVoronoiBuilder.Build(hull, config.Radius);

            // 4. Compute cell areas
            mesh.ComputeAreas();

            // 5. Generate tectonic plates
            TectonicData tectonics = TectonicOps.Generate(mesh, config.PlateCount, config.Seed);

            // 6. Compute tectonic elevation
            ElevationOps.Generate(mesh, tectonics, config.OceanFraction, config.Seed + 1);

            return new WorldGenResult
            {
                Mesh = mesh,
                Tectonics = tectonics,
            };
        }
    }
}
