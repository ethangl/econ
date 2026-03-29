using System;
using System.Diagnostics;

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

            var timings = new WorldGenTimingData();
            var totalSw = Stopwatch.StartNew();
            var stepSw = Stopwatch.StartNew();

            // 1. Generate points on unit sphere (coarse tectonic mesh)
            Vec3[] points = FibonacciSphere.Generate(config.CoarseCellCount, config.Jitter, config.Seed);
            timings.CoarsePointsSeconds = stepSw.Elapsed.TotalSeconds;

            // 2. Build convex hull (= spherical Delaunay triangulation)
            stepSw.Restart();
            ConvexHull hull = ConvexHull.Build(points);
            timings.CoarseHullSeconds = stepSw.Elapsed.TotalSeconds;

            // 3. Build Voronoi dual
            stepSw.Restart();
            SphereMesh mesh = SphericalVoronoiBuilder.Build(hull, config.Radius);
            timings.CoarseVoronoiSeconds = stepSw.Elapsed.TotalSeconds;

            // 4. Compute cell areas
            stepSw.Restart();
            mesh.ComputeAreas();
            timings.CoarseAreaSeconds = stepSw.Elapsed.TotalSeconds;

            // 5. Generate tectonic plates
            stepSw.Restart();
            TectonicData tectonics = TectonicOps.Generate(mesh, config.MajorPlateCount, config.MinorPlateCount,
                config.MajorHeadStartRounds, config.Seed, config.PolarCapLatitude);
            timings.TectonicsSeconds = stepSw.Elapsed.TotalSeconds;

            // 6. Compute tectonic elevation
            stepSw.Restart();
            if (config.TectonicSteps > 1)
                ElevationOps.GenerateMultiStep(mesh, tectonics, config.OceanFraction,
                    config.Seed + 1, config.TectonicSteps, config.InterStepErosion);
            else
                ElevationOps.Generate(mesh, tectonics, config.OceanFraction, config.Seed + 1);
            timings.ElevationSeconds = stepSw.Elapsed.TotalSeconds;

            // 7. Volcanic hotspots (after elevation, before dense terrain)
            if (config.HotspotCount > 0)
            {
                stepSw.Restart();
                HotspotOps.Generate(mesh, tectonics, config);
                timings.HotspotsSeconds = stepSw.Elapsed.TotalSeconds;
            }

            // 8. Volcanic arcs (after elevation + hotspots, before dense terrain)
            stepSw.Restart();
            VolcanicArcOps.Generate(mesh, tectonics, config);
            timings.VolcanicArcsSeconds = stepSw.Elapsed.TotalSeconds;

            // 9. Generate dense terrain mesh with fractal noise
            stepSw.Restart();
            DenseTerrainData denseTerrain = DenseTerrainOps.Generate(mesh, tectonics, config);
            timings.DenseTerrainSeconds = stepSw.Elapsed.TotalSeconds;

            // 9. Select candidate sites for flat map generation
            var partialResult = new WorldGenResult
            {
                Mesh = mesh,
                Tectonics = tectonics,
                DenseTerrain = denseTerrain,
                Timings = timings,
            };

            stepSw.Restart();
            partialResult.Sites = SiteSelector.SelectMultiple(partialResult, config);
            timings.SiteSelectionSeconds = stepSw.Elapsed.TotalSeconds;
            partialResult.Site = partialResult.Sites.Count > 0 ? partialResult.Sites[0] : null;
            timings.TotalSeconds = totalSw.Elapsed.TotalSeconds;

            return partialResult;
        }
    }
}
