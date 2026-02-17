using System;

namespace MapGen.Core
{
    /// <summary>
    /// Canonical world-unit map pipeline implementation.
    /// </summary>
    public static class MapGenPipelineCore
    {
        public static MapGenResult Generate(MapGenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();

            CellMesh mesh = GenerateMesh(config);
            WorldMetadata world = BuildWorldMetadata(config, mesh);
            float terrainDomainMaxElevation = ResolveTerrainDomainMaxElevation(config);
            float terrainDomainMaxSeaDepth = ResolveTerrainDomainMaxSeaDepth(config);
            var elevation = new ElevationField(
                mesh,
                terrainDomainMaxSeaDepth,
                terrainDomainMaxElevation,
                config.TerrainShapeReferenceSpanMeters,
                config.TerrainShapeInitialSeaDepthMeters);
            float depthRemapExponent = GenerateElevationFromDsl(elevation, config);
            EnsureNonDegenerateLandWater(elevation);
            var climate = new ClimateField(mesh);
            TemperatureModelOps.Compute(climate, elevation, config, world);
            PrecipitationModelOps.Compute(climate, elevation, config, world);
            var rivers = new RiverField(mesh);
            RiverFlowOps.Compute(rivers, elevation, climate, config);
            var biomes = new BiomeField(mesh);
            BiomeGenerationOps.Compute(biomes, elevation, climate, rivers, config);
            var political = new PoliticalField(mesh);
            PoliticalGenerationOps.Compute(political, biomes, rivers, elevation, config);
            RemapElevationToWorldEnvelope(
                elevation,
                terrainDomainMaxSeaDepth,
                terrainDomainMaxElevation,
                config.MaxSeaDepthMeters,
                config.MaxElevationMeters,
                depthRemapExponent);

            return new MapGenResult
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

        static CellMesh GenerateMesh(MapGenConfig config)
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

        static float GenerateElevationFromDsl(ElevationField elevation, MapGenConfig config)
        {
            string script = HeightmapTemplateCompiler.GetTemplate(config.Template, config);
            if (string.IsNullOrWhiteSpace(script))
                throw new InvalidOperationException($"No map template found for {config.Template}.");

            var dslContext = new HeightmapDslExecutionContext();
            HeightmapDsl.Execute(elevation, script, config.ElevationSeed, context: dslContext);
            ConstrainLandRatioBand(elevation, config.Template);
            elevation.ClampAll();

            if (dslContext.DepthRemapExponentOverride.HasValue)
                return dslContext.DepthRemapExponentOverride.Value;

            return config.TerrainDepthRemapExponent;
        }

        static void ConstrainLandRatioBand(ElevationField elevation, HeightmapTemplateType template)
        {
            var (minLand, maxLand) = HeightmapTemplateCompiler.GetLandRatioBand(template);
            for (int iter = 0; iter < 3; iter++)
            {
                float current = elevation.LandRatio();
                if (current >= minLand && current <= maxLand)
                    return;

                float target = current > maxLand ? maxLand : minLand;
                float shift = ComputeSeaShiftForTargetLandRatio(elevation.ElevationMetersSigned, target);
                if (float.IsNaN(shift) || float.IsInfinity(shift))
                    return;

                if (current > maxLand)
                    shift -= 0.001f;
                else
                    shift += 0.001f;

                for (int i = 0; i < elevation.CellCount; i++)
                    elevation[i] = elevation[i] + shift;

                elevation.ClampAll();
            }
        }

        static float ComputeSeaShiftForTargetLandRatio(float[] elevations, float targetLandRatio)
        {
            if (elevations == null || elevations.Length == 0)
                return 0f;

            float t = targetLandRatio;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            float waterRatio = 1f - t;
            int n = elevations.Length;
            var sorted = (float[])elevations.Clone();
            Array.Sort(sorted);

            int idx = (int)Math.Floor(waterRatio * (n - 1));
            if (idx < 0) idx = 0;
            if (idx >= n) idx = n - 1;

            float cutoff = sorted[idx];
            return -cutoff;
        }

        static void EnsureNonDegenerateLandWater(ElevationField elevation)
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

        static float ResolveTerrainDomainMaxElevation(MapGenConfig config)
        {
            float domain = config.TerrainShapeDomainMaxElevationMeters;
            if (float.IsNaN(domain) || float.IsInfinity(domain) || domain <= 0f)
                domain = config.MaxElevationMeters;
            return Math.Min(config.MaxElevationMeters, domain);
        }

        static float ResolveTerrainDomainMaxSeaDepth(MapGenConfig config)
        {
            float domain = config.TerrainShapeDomainMaxSeaDepthMeters;
            if (float.IsNaN(domain) || float.IsInfinity(domain) || domain <= 0f)
                domain = config.MaxSeaDepthMeters;
            return Math.Min(config.MaxSeaDepthMeters, domain);
        }

        static void RemapElevationToWorldEnvelope(
            ElevationField elevation,
            float fromSeaDepthMeters,
            float fromMaxElevationMeters,
            float toSeaDepthMeters,
            float toMaxElevationMeters,
            float depthRemapExponent)
        {
            if (Math.Abs(fromSeaDepthMeters - toSeaDepthMeters) <= 0.0001f
                && Math.Abs(fromMaxElevationMeters - toMaxElevationMeters) <= 0.0001f)
            {
                return;
            }

            float invFromSea = fromSeaDepthMeters > 0f ? 1f / fromSeaDepthMeters : 0f;
            float invFromLand = fromMaxElevationMeters > 0f ? 1f / fromMaxElevationMeters : 0f;
            float exponent = depthRemapExponent;
            if (float.IsNaN(exponent) || float.IsInfinity(exponent) || exponent <= 0f)
                exponent = 1f;

            for (int i = 0; i < elevation.CellCount; i++)
            {
                float h = elevation.ElevationMetersSigned[i];
                if (h > 0f)
                {
                    float t = h * invFromLand;
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;
                    elevation.ElevationMetersSigned[i] = t * toMaxElevationMeters;
                }
                else if (h < 0f)
                {
                    float t = -h * invFromSea;
                    if (t < 0f) t = 0f;
                    if (t > 1f) t = 1f;
                    t = (float)Math.Pow(t, exponent);
                    elevation.ElevationMetersSigned[i] = -t * toSeaDepthMeters;
                }
            }
        }

        static WorldMetadata BuildWorldMetadata(MapGenConfig config, CellMesh mesh)
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
