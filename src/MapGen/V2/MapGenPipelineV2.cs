using System;

namespace MapGen.Core
{
    /// <summary>
    /// Phase-A MapGen V2 pipeline.
    /// Produces deterministic mesh + signed-meter elevation + world metadata.
    /// </summary>
    public static class MapGenPipelineV2
    {
        public static MapGenV2Result Generate(MapGenV2Config config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            config.Validate();

            CellMesh mesh = GenerateMesh(config);
            var elevation = new ElevationFieldV2(mesh, config.MaxSeaDepthMeters, config.MaxElevationMeters);
            GenerateElevation(elevation, config);
            WorldMetadata world = BuildWorldMetadata(config, mesh);

            return new MapGenV2Result
            {
                Mesh = mesh,
                Elevation = elevation,
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

        static void GenerateElevation(ElevationFieldV2 elevation, MapGenV2Config config)
        {
            int cellCount = elevation.CellCount;
            if (cellCount == 0)
                return;

            float[] terrainScore = new float[cellCount];
            var noise = new Noise(config.ElevationSeed);

            ResolveTemplateShape(config.Template, out float bias, out float radialWeight);
            float widthInv = elevation.Mesh.Width > 1e-6f ? 1f / elevation.Mesh.Width : 0f;
            float heightInv = elevation.Mesh.Height > 1e-6f ? 1f / elevation.Mesh.Height : 0f;

            for (int i = 0; i < cellCount; i++)
            {
                Vec2 c = elevation.Mesh.CellCenters[i];
                float nx = c.X * widthInv;
                float ny = c.Y * heightInv;

                float macro = noise.Sample(nx * 2.1f + 11.7f, ny * 2.1f - 3.4f);
                float detail = noise.Sample(nx * 6.9f - 7.8f, ny * 6.9f + 5.2f);
                float radial = RadialCore(nx, ny);
                float radialSignal = radial * 2f - 1f;

                terrainScore[i] = macro * 0.72f + detail * 0.22f + radialSignal * radialWeight + bias;
            }

            float targetLandRatio = ResolveTargetLandRatio(config.Template);
            float seaThreshold = Quantile(terrainScore, 1f - targetLandRatio);

            float maxAboveSea = 1e-5f;
            float maxBelowSea = 1e-5f;
            for (int i = 0; i < cellCount; i++)
            {
                float centered = terrainScore[i] - seaThreshold;
                terrainScore[i] = centered;

                if (centered > 0f)
                {
                    if (centered > maxAboveSea) maxAboveSea = centered;
                }
                else if (centered < 0f)
                {
                    float depth = -centered;
                    if (depth > maxBelowSea) maxBelowSea = depth;
                }
            }

            for (int i = 0; i < cellCount; i++)
            {
                float centered = terrainScore[i];
                float meters = centered >= 0f
                    ? centered / maxAboveSea * config.MaxElevationMeters
                    : centered / maxBelowSea * config.MaxSeaDepthMeters;
                elevation[i] = meters;
            }

            EnsureNonDegenerateLandWater(elevation);
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

        static float RadialCore(float nx, float ny)
        {
            float dx = nx - 0.5f;
            float dy = ny - 0.5f;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy) / 0.70710677f;
            float clamped = Clamp(dist, 0f, 1f);
            float falloff = 1f - clamped;
            return falloff * falloff * (3f - 2f * falloff); // smoothstep
        }

        static void ResolveTemplateShape(HeightmapTemplateType template, out float bias, out float radialWeight)
        {
            switch (template)
            {
                case HeightmapTemplateType.LowIsland:
                case HeightmapTemplateType.HighIsland:
                case HeightmapTemplateType.Volcano:
                case HeightmapTemplateType.Atoll:
                    bias = -0.08f;
                    radialWeight = 0.60f;
                    return;
                case HeightmapTemplateType.Pangea:
                case HeightmapTemplateType.OldWorld:
                    bias = 0.18f;
                    radialWeight = 0.15f;
                    return;
                case HeightmapTemplateType.Archipelago:
                case HeightmapTemplateType.Shattered:
                case HeightmapTemplateType.Fractious:
                    bias = -0.12f;
                    radialWeight = 0.05f;
                    return;
                case HeightmapTemplateType.Mediterranean:
                case HeightmapTemplateType.Isthmus:
                case HeightmapTemplateType.Peninsula:
                    bias = 0.05f;
                    radialWeight = 0.30f;
                    return;
                case HeightmapTemplateType.Continents:
                    bias = 0.10f;
                    radialWeight = 0.18f;
                    return;
                case HeightmapTemplateType.Taklamakan:
                    bias = 0.00f;
                    radialWeight = 0.25f;
                    return;
                default:
                    bias = 0f;
                    radialWeight = 0.20f;
                    return;
            }
        }

        static float ResolveTargetLandRatio(HeightmapTemplateType template)
        {
            switch (template)
            {
                case HeightmapTemplateType.LowIsland:
                case HeightmapTemplateType.Atoll:
                    return 0.30f;
                case HeightmapTemplateType.HighIsland:
                case HeightmapTemplateType.Volcano:
                    return 0.42f;
                case HeightmapTemplateType.Archipelago:
                case HeightmapTemplateType.Shattered:
                case HeightmapTemplateType.Fractious:
                    return 0.38f;
                case HeightmapTemplateType.Continents:
                case HeightmapTemplateType.Mediterranean:
                    return 0.52f;
                case HeightmapTemplateType.Peninsula:
                case HeightmapTemplateType.Isthmus:
                    return 0.47f;
                case HeightmapTemplateType.Pangea:
                case HeightmapTemplateType.OldWorld:
                    return 0.62f;
                case HeightmapTemplateType.Taklamakan:
                    return 0.50f;
                default:
                    return 0.50f;
            }
        }

        static float Quantile(float[] data, float q)
        {
            if (data == null || data.Length == 0)
                return 0f;

            if (q <= 0f) return Min(data);
            if (q >= 1f) return Max(data);

            var sorted = (float[])data.Clone();
            Array.Sort(sorted);
            float index = q * (sorted.Length - 1);
            int lo = (int)Math.Floor(index);
            int hi = (int)Math.Ceiling(index);
            if (lo == hi)
                return sorted[lo];

            float t = index - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }

        static float Min(float[] values)
        {
            float min = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] < min) min = values[i];
            }
            return min;
        }

        static float Max(float[] values)
        {
            float max = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] > max) max = values[i];
            }
            return max;
        }

        static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);
    }
}
