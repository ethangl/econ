using System;

namespace MapGen.Core
{
    /// <summary>
    /// MapGen V2 pipeline scaffolding.
    /// Generates mesh + V2 elevation field + world metadata.
    /// </summary>
    public static class MapGenPipelineV2
    {
        public static MapGenV2Result Generate(MapGenV2Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();

            CellMesh mesh = GenerateMesh(config);
            WorldMetadata world = BuildWorldMetadata(config, mesh);
            var elevation = new ElevationFieldV2(mesh, config.MaxSeaDepthMeters, config.MaxElevationMeters);
            GenerateElevationFromDsl(elevation, config);
            EnsureNonDegenerateLandWater(elevation);
            var climate = new ClimateFieldV2(mesh);
            TemperatureOpsV2.Compute(climate, elevation, config, world);
            PrecipitationOpsV2.Compute(climate, elevation, config, world);
            var rivers = new RiverFieldV2(mesh);
            FlowOpsV2.Compute(rivers, elevation, climate, config);
            var biomes = new BiomeFieldV2(mesh);
            BiomeOpsV2.Compute(biomes, elevation, climate, rivers, config);
            var political = new PoliticalFieldV2(mesh);
            PoliticalOpsV2.Compute(political, biomes, elevation);

            return new MapGenV2Result
            {
                Mesh = mesh,
                Elevation = elevation,
                Climate = climate,
                Rivers = rivers,
                Biomes = biomes,
                Political = political,
                World = world
            };
        }

        static CellMesh GenerateMesh(MapGenV2Config config)
        {
            float cellAreaKm2 = config.CellSizeKm * config.CellSizeKm;
            float mapAreaKm2 = config.CellCount * cellAreaKm2;
            float mapWidthKm = (float)Math.Sqrt(mapAreaKm2 * config.AspectRatio);
            float mapHeightKm = mapWidthKm / config.AspectRatio;

            var (gridPoints, spacing) = PointGenerator.JitteredGrid(
                mapWidthKm,
                mapHeightKm,
                config.CellCount,
                config.MeshSeed);

            var boundaryPoints = PointGenerator.BoundaryPoints(mapWidthKm, mapHeightKm, spacing);
            var mesh = VoronoiBuilder.Build(mapWidthKm, mapHeightKm, gridPoints, boundaryPoints);
            mesh.ComputeAreas();
            return mesh;
        }

        static void GenerateElevationFromDsl(ElevationFieldV2 elevation, MapGenV2Config config)
        {
            string script = HeightmapTemplatesV2.GetTemplate(config.Template, config);
            if (string.IsNullOrWhiteSpace(script))
                throw new InvalidOperationException($"No V2 template found for {config.Template}.");

            HeightmapDslV2.Execute(elevation, script, config.ElevationSeed);
            elevation.ClampAll();
        }

        static void EnsureNonDegenerateLandWater(ElevationFieldV2 elevation)
        {
            var (land, water) = elevation.CountLandWater();
            if (land > 0 && water > 0)
                return;

            int minIndex = 0;
            int maxIndex = 0;
            float minValue = elevation.ElevationMetersSigned[0];
            float maxValue = minValue;

            for (int i = 1; i < elevation.ElevationMetersSigned.Length; i++)
            {
                float h = elevation.ElevationMetersSigned[i];
                if (h < minValue)
                {
                    minValue = h;
                    minIndex = i;
                }

                if (h > maxValue)
                {
                    maxValue = h;
                    maxIndex = i;
                }
            }

            elevation[minIndex] = -0.1f * elevation.MaxSeaDepthMeters;
            elevation[maxIndex] = 0.1f * elevation.MaxElevationMeters;
        }

        static WorldMetadata BuildWorldMetadata(MapGenV2Config config, CellMesh mesh)
        {
            float latitudeNorth = config.LatitudeSouth + mesh.Height / 111f;

            return new WorldMetadata
            {
                CellSizeKm = config.CellSizeKm,
                MapWidthKm = mesh.Width,
                MapHeightKm = mesh.Height,
                MapAreaKm2 = mesh.Width * mesh.Height,
                LatitudeSouth = config.LatitudeSouth,
                LatitudeNorth = latitudeNorth,
                MinHeight = -config.MaxSeaDepthMeters,
                SeaLevelHeight = 0f,
                MaxHeight = config.MaxElevationMeters,
                MaxElevationMeters = config.MaxElevationMeters,
                MaxSeaDepthMeters = config.MaxSeaDepthMeters
            };
        }
    }
}
