using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Biome/suitability/population/geography stage.
    /// </summary>
    public static class BiomeGenerationOps
    {
        public static void Compute(
            BiomeField biome,
            ElevationField elevation,
            ClimateField climate,
            RiverField rivers,
            MapGenConfig config)
        {
            ComputeLakeCells(biome, elevation, rivers);
            ComputeWaterFeatures(biome, elevation);
            ComputeCoastDistance(biome, elevation);
            ComputeSlope(biome, elevation);
            AssignBiomesAndSuitability(biome, elevation, climate, rivers, config);
            ComputePopulation(biome, elevation);
        }

        static void ComputeLakeCells(BiomeField biome, ElevationField elevation, RiverField rivers)
        {
            var mesh = biome.Mesh;
            ParallelOps.For(0, mesh.CellCount, i =>
            {
                if (elevation.IsLand(i))
                {
                    biome.IsLakeCell[i] = false;
                    return;
                }

                int[] verts = mesh.CellVertices[i];
                if (verts == null || verts.Length == 0)
                {
                    biome.IsLakeCell[i] = false;
                    return;
                }

                int lakeVerts = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    int vi = verts[v];
                    if (vi >= 0 && vi < mesh.VertexCount && rivers.IsLake(vi))
                        lakeVerts++;
                }

                biome.IsLakeCell[i] = lakeVerts * 2 >= verts.Length;
            });
        }

        static void ComputeWaterFeatures(BiomeField biome, ElevationField elevation)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            var visited = new bool[n];
            var features = new List<WaterFeature>();
            int nextId = 1;

            for (int i = 0; i < n; i++)
                biome.FeatureId[i] = 0;

            for (int start = 0; start < n; start++)
            {
                if (visited[start] || elevation.IsLand(start))
                    continue;

                bool touchesBorder = false;
                bool allLakeCells = true;
                int count = 0;
                var queue = new Queue<int>();
                queue.Enqueue(start);
                visited[start] = true;

                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    biome.FeatureId[c] = nextId;
                    count++;

                    if (mesh.CellIsBoundary[c])
                        touchesBorder = true;
                    if (!biome.IsLakeCell[c])
                        allLakeCells = false;

                    int[] neighbors = mesh.CellNeighbors[c];
                    for (int ni = 0; ni < neighbors.Length; ni++)
                    {
                        int nb = neighbors[ni];
                        if (nb < 0 || nb >= n || visited[nb] || elevation.IsLand(nb))
                            continue;

                        visited[nb] = true;
                        queue.Enqueue(nb);
                    }
                }

                WaterFeatureType type = (!touchesBorder && allLakeCells)
                    ? WaterFeatureType.Lake
                    : WaterFeatureType.Ocean;

                features.Add(new WaterFeature
                {
                    Id = nextId,
                    Type = type,
                    TouchesBorder = touchesBorder,
                    CellCount = count
                });

                nextId++;
            }

            biome.Features = features.ToArray();
        }

        static void ComputeCoastDistance(BiomeField biome, ElevationField elevation)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            var landDist = new int[n];
            var waterDist = new int[n];
            Array.Fill(landDist, -1);
            Array.Fill(waterDist, -1);

            var landQueue = new Queue<int>();
            var waterQueue = new Queue<int>();

            for (int i = 0; i < n; i++)
            {
                bool isLand = elevation.IsLand(i) && !biome.IsLakeCell[i];
                bool isWater = !isLand;

                bool hasOppositeNeighbor = false;
                int[] neighbors = mesh.CellNeighbors[i];
                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if (nb < 0 || nb >= n)
                        continue;

                    bool nbLand = elevation.IsLand(nb) && !biome.IsLakeCell[nb];
                    if (nbLand != isLand)
                    {
                        hasOppositeNeighbor = true;
                        break;
                    }
                }

                if (!hasOppositeNeighbor)
                    continue;

                if (isLand)
                {
                    landDist[i] = 0;
                    landQueue.Enqueue(i);
                }
                else if (isWater)
                {
                    waterDist[i] = 0;
                    waterQueue.Enqueue(i);
                }
            }

            while (landQueue.Count > 0)
            {
                int c = landQueue.Dequeue();
                int next = landDist[c] + 1;
                int[] neighbors = mesh.CellNeighbors[c];
                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if (nb < 0 || nb >= n || landDist[nb] >= 0)
                        continue;

                    if (!(elevation.IsLand(nb) && !biome.IsLakeCell[nb]))
                        continue;

                    landDist[nb] = next;
                    landQueue.Enqueue(nb);
                }
            }

            while (waterQueue.Count > 0)
            {
                int c = waterQueue.Dequeue();
                int next = waterDist[c] + 1;
                int[] neighbors = mesh.CellNeighbors[c];
                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if (nb < 0 || nb >= n || waterDist[nb] >= 0)
                        continue;

                    if (elevation.IsLand(nb) && !biome.IsLakeCell[nb])
                        continue;

                    waterDist[nb] = next;
                    waterQueue.Enqueue(nb);
                }
            }

            for (int i = 0; i < n; i++)
            {
                if (elevation.IsLand(i) && !biome.IsLakeCell[i])
                    biome.CoastDistance[i] = landDist[i] >= 0 ? landDist[i] : int.MaxValue / 4;
                else
                    biome.CoastDistance[i] = waterDist[i] >= 0 ? -waterDist[i] : int.MinValue / 4;
            }
        }

        static void ComputeSlope(BiomeField biome, ElevationField elevation)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            ParallelOps.For(0, n, i =>
            {
                float maxDh = 0f;
                int[] neighbors = mesh.CellNeighbors[i];
                for (int ni = 0; ni < neighbors.Length; ni++)
                {
                    int nb = neighbors[ni];
                    if (nb < 0 || nb >= n)
                        continue;

                    float dh = Math.Abs(elevation[i] - elevation[nb]);
                    if (dh > maxDh) maxDh = dh;
                }

                biome.Slope[i] = Clamp01(maxDh / 1000f);
            });
        }

        static void AssignBiomesAndSuitability(
            BiomeField biome,
            ElevationField elevation,
            ClimateField climate,
            RiverField rivers,
            MapGenConfig config)
        {
            bool[] cellHasRiver = ComputeCellHasRiver(biome.Mesh, rivers, config.EffectiveRiverTraceThreshold);
            float[] cellFlux = ComputeCellFluxFromVertices(biome.Mesh, rivers);
            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float coastSaltScale = profile != null ? profile.BiomeCoastSaltScale : 1f;
            float salineThresholdScale = profile != null ? profile.BiomeSalineThresholdScale : 1f;
            float slopeScale = profile != null ? profile.BiomeSlopeScale : 1f;
            float alluvialFluxThresholdScale = profile != null ? profile.BiomeAlluvialFluxThresholdScale : 1f;
            float alluvialMaxSlopeScale = profile != null ? profile.BiomeAlluvialMaxSlopeScale : 1f;
            float wetlandFluxThresholdScale = profile != null ? profile.BiomeWetlandFluxThresholdScale : 1f;
            float wetlandMaxSlopeScale = profile != null ? profile.BiomeWetlandMaxSlopeScale : 1f;
            float podzolTempMaxScale = profile != null ? profile.BiomePodzolTempMaxScale : 1f;
            float podzolPrecipThresholdScale = profile != null ? profile.BiomePodzolPrecipThresholdScale : 1f;
            float woodlandPrecipThresholdScale = profile != null ? profile.BiomeWoodlandPrecipThresholdScale : 1f;
            if (coastSaltScale <= 0f) coastSaltScale = 1f;
            if (salineThresholdScale <= 0f) salineThresholdScale = 1f;
            if (slopeScale <= 0f) slopeScale = 1f;
            if (alluvialFluxThresholdScale <= 0f) alluvialFluxThresholdScale = 1f;
            if (alluvialMaxSlopeScale <= 0f) alluvialMaxSlopeScale = 1f;
            if (wetlandFluxThresholdScale <= 0f) wetlandFluxThresholdScale = 1f;
            if (wetlandMaxSlopeScale <= 0f) wetlandMaxSlopeScale = 1f;
            if (podzolTempMaxScale <= 0f) podzolTempMaxScale = 1f;
            if (podzolPrecipThresholdScale <= 0f) podzolPrecipThresholdScale = 1f;
            if (woodlandPrecipThresholdScale <= 0f) woodlandPrecipThresholdScale = 1f;
            int n = biome.CellCount;

            ParallelOps.For(0, n, i =>
            {
                bool isLand = elevation.IsLand(i) && !biome.IsLakeCell[i];
                if (!isLand)
                {
                    biome.DebugCellFlux[i] = 0f;
                    biome.DebugSalineCandidate[i] = false;
                    biome.DebugAlluvialCandidate[i] = false;
                    biome.DebugLithosolCandidate[i] = false;
                    biome.DebugWetlandCandidate[i] = false;
                    biome.Biome[i] = biome.IsLakeCell[i] ? BiomeId.Lake : BiomeId.CoastalMarsh;
                    biome.Habitability[i] = 0f;
                    biome.MovementCost[i] = 100f;
                    biome.Suitability[i] = 0f;
                    return;
                }

                float temp = climate.TemperatureC[i];
                float precip = climate.PrecipitationMmYear[i];
                float alt = elevation[i];
                float precipPct = config.MaxAnnualPrecipitationMm > 1e-6f
                    ? (precip / config.MaxAnnualPrecipitationMm) * 100f
                    : 0f;
                float altPct = config.MaxElevationMeters > 1e-6f
                    ? (alt / config.MaxElevationMeters) * 100f
                    : 0f;
                float coastSaltProxy = biome.CoastDistance[i] <= 0 ? 1f
                    : (biome.CoastDistance[i] <= 1 ? 0.45f
                    : (biome.CoastDistance[i] <= 2 ? 0.25f : 0f));
                float slope = biome.Slope[i];
                float slopeForSoil = Clamp01(slope * slopeScale);
                float flux = cellFlux[i];
                // Keep soil/biome alluvial classification stable across mesh resolution.
                // River extraction thresholds scale with resolution, but biome flux bands should
                // remain anchored to world-scale behavior.
                float alluvialFluxThreshold = config.RiverThreshold * alluvialFluxThresholdScale;
                float alluvialMaxSlope = 0.15f * alluvialMaxSlopeScale;
                float wetlandFluxThreshold = 200f * wetlandFluxThresholdScale;
                float wetlandMaxSlope = 0.10f * wetlandMaxSlopeScale;
                float podzolTempMax = 20f * podzolTempMaxScale;
                float podzolPrecipThreshold = 30f * podzolPrecipThresholdScale;
                float woodlandPrecipThreshold = 45f * woodlandPrecipThresholdScale;
                float saltSignal = coastSaltProxy * coastSaltScale;
                float salineThreshold = 0.3f * salineThresholdScale;
                bool isPermafrost = temp < -5f;
                bool salineCandidate = !isPermafrost && saltSignal > salineThreshold;
                bool alluvialCandidate = !isPermafrost
                    && !salineCandidate
                    && flux > alluvialFluxThreshold
                    && slopeForSoil < alluvialMaxSlope;
                bool lithosolCandidate = !isPermafrost
                    && !salineCandidate
                    && !alluvialCandidate
                    && (slopeForSoil > 0.6f || altPct > 80f);
                SoilType soil = ClassifyPseudoSoil(
                    temp,
                    precipPct,
                    altPct,
                    slopeForSoil,
                    coastSaltProxy,
                    flux,
                    alluvialFluxThreshold,
                    alluvialMaxSlope,
                    coastSaltScale,
                    salineThresholdScale,
                    podzolTempMax,
                    podzolPrecipThreshold);

                BiomeId id = BiomeFromPseudoSoil(
                    soil,
                    temp,
                    precipPct,
                    altPct,
                    slopeForSoil,
                    flux,
                    wetlandFluxThreshold,
                    wetlandMaxSlope,
                    woodlandPrecipThreshold);
                bool wetlandCandidate = soil == SoilType.Alluvial
                    && flux > wetlandFluxThreshold
                    && slopeForSoil < wetlandMaxSlope;

                biome.DebugCellFlux[i] = flux;
                biome.DebugSalineCandidate[i] = salineCandidate;
                biome.DebugAlluvialCandidate[i] = alluvialCandidate;
                biome.DebugLithosolCandidate[i] = lithosolCandidate;
                biome.DebugWetlandCandidate[i] = wetlandCandidate;

                biome.Biome[i] = id;
                float habitability = BaseHabitability(id);
                float movement = BaseMovementCost(id);

                if (cellHasRiver[i])
                    habitability += 10f;
                if (biome.CoastDistance[i] == 0)
                    habitability += 8f;

                float slopePenalty = slope * 22f;
                float altitudePenalty = alt > 2600f ? (alt - 2600f) / 180f : 0f;
                float suitability = habitability - slopePenalty - altitudePenalty;

                biome.Habitability[i] = Clamp(habitability, 0f, 100f);
                biome.MovementCost[i] = movement + slope * 15f;
                biome.Suitability[i] = Clamp(suitability, 0f, 100f);
            });
        }

        static void ComputePopulation(BiomeField biome, ElevationField elevation)
        {
            var mesh = biome.Mesh;
            bool hasAreas = mesh.CellAreas != null && mesh.CellAreas.Length == mesh.CellCount;

            ParallelOps.For(0, mesh.CellCount, i =>
            {
                if (!(elevation.IsLand(i) && !biome.IsLakeCell[i]))
                {
                    biome.Population[i] = 0f;
                    return;
                }

                float area = hasAreas ? mesh.CellAreas[i] : 1f;
                if (area < 0.01f) area = 0.01f;
                biome.Population[i] = biome.Suitability[i] * area * 0.08f;
            });
        }

        static bool[] ComputeCellHasRiver(CellMesh mesh, RiverField rivers, float riverTraceThreshold)
        {
            var hasRiver = new bool[mesh.CellCount];
            for (int e = 0; e < mesh.EdgeCount; e++)
            {
                if (rivers.EdgeFlux[e] < riverTraceThreshold)
                    continue;

                var cells = mesh.EdgeCells[e];
                if (cells.C0 >= 0 && cells.C0 < mesh.CellCount)
                    hasRiver[cells.C0] = true;
                if (cells.C1 >= 0 && cells.C1 < mesh.CellCount)
                    hasRiver[cells.C1] = true;
            }

            return hasRiver;
        }

        static float[] ComputeCellFluxFromVertices(CellMesh mesh, RiverField rivers)
        {
            var cellFlux = new float[mesh.CellCount];
            ParallelOps.For(0, mesh.CellCount, i =>
            {
                int[] verts = mesh.CellVertices[i];
                if (verts == null || verts.Length == 0)
                    return;

                float sum = 0f;
                for (int v = 0; v < verts.Length; v++)
                {
                    int vi = verts[v];
                    if (vi < 0 || vi >= mesh.VertexCount)
                        continue;
                    sum += rivers.VertexFlux[vi];
                }

                cellFlux[i] = sum / verts.Length;
            });

            return cellFlux;
        }

        static SoilType ClassifyPseudoSoil(
            float tempC,
            float precipPct,
            float elevationPct,
            float slope,
            float coastSaltProxy,
            float cellFlux,
            float alluvialFluxThreshold,
            float alluvialMaxSlope,
            float coastSaltScale,
            float salineThresholdScale,
            float podzolTempMax,
            float podzolPrecipThreshold)
        {
            float saltSignal = coastSaltProxy * coastSaltScale;
            float salineThreshold = 0.3f * salineThresholdScale;
            if (tempC < -5f)
                return SoilType.Permafrost;
            if (saltSignal > salineThreshold)
                return SoilType.Saline;
            if (cellFlux > alluvialFluxThreshold && slope < alluvialMaxSlope)
                return SoilType.Alluvial;
            if (slope > 0.6f || elevationPct > 80f)
                return SoilType.Lithosol;
            if (precipPct < 15f)
                return SoilType.Aridisol;
            if (tempC > 20f && precipPct > 60f)
                return SoilType.Laterite;
            if (tempC < podzolTempMax && precipPct > podzolPrecipThreshold)
                return SoilType.Podzol;
            return SoilType.Chernozem;
        }

        static BiomeId BiomeFromPseudoSoil(
            SoilType soil,
            float tempC,
            float precipPct,
            float elevationPct,
            float slope,
            float cellFlux,
            float wetlandFluxThreshold,
            float wetlandMaxSlope,
            float woodlandPrecipThreshold)
        {
            switch (soil)
            {
                case SoilType.Permafrost:
                    return tempC < -10f ? BiomeId.Glacier : BiomeId.Tundra;
                case SoilType.Saline:
                    return precipPct < 15f ? BiomeId.SaltFlat : BiomeId.CoastalMarsh;
                case SoilType.Lithosol:
                    return (tempC < -3f || elevationPct > 85f) ? BiomeId.AlpineBarren : BiomeId.MountainShrub;
                case SoilType.Alluvial:
                    return (cellFlux > wetlandFluxThreshold && slope < wetlandMaxSlope) ? BiomeId.Wetland : BiomeId.Floodplain;
                case SoilType.Aridisol:
                    if (tempC > 25f) return BiomeId.HotDesert;
                    if (tempC < 5f) return BiomeId.ColdDesert;
                    return BiomeId.Scrubland;
                case SoilType.Laterite:
                    if (precipPct > 80f) return BiomeId.TropicalRainforest;
                    if (precipPct > 70f) return BiomeId.TropicalDryForest;
                    return BiomeId.Savanna;
                case SoilType.Podzol:
                    return tempC < 5f ? BiomeId.BorealForest : BiomeId.TemperateForest;
                default:
                    return precipPct > woodlandPrecipThreshold ? BiomeId.Woodland : BiomeId.Grassland;
            }
        }

        static float BaseHabitability(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.Glacier: return 0f;
                case BiomeId.Tundra: return 5f;
                case BiomeId.SaltFlat: return 2f;
                case BiomeId.CoastalMarsh: return 10f;
                case BiomeId.AlpineBarren: return 0f;
                case BiomeId.MountainShrub: return 8f;
                case BiomeId.Floodplain: return 90f;
                case BiomeId.Wetland: return 15f;
                case BiomeId.HotDesert: return 4f;
                case BiomeId.ColdDesert: return 5f;
                case BiomeId.Scrubland: return 15f;
                case BiomeId.TropicalRainforest: return 60f;
                case BiomeId.TropicalDryForest: return 50f;
                case BiomeId.Savanna: return 25f;
                case BiomeId.BorealForest: return 12f;
                case BiomeId.TemperateForest: return 70f;
                case BiomeId.Grassland: return 80f;
                case BiomeId.Woodland: return 85f;
                case BiomeId.Lake: return 0f;
                default: return 40f;
            }
        }

        static float BaseMovementCost(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.Glacier: return 95f;
                case BiomeId.Tundra: return 75f;
                case BiomeId.SaltFlat: return 85f;
                case BiomeId.CoastalMarsh: return 78f;
                case BiomeId.AlpineBarren: return 92f;
                case BiomeId.MountainShrub: return 74f;
                case BiomeId.Floodplain: return 40f;
                case BiomeId.Wetland: return 88f;
                case BiomeId.HotDesert:
                case BiomeId.ColdDesert: return 80f;
                case BiomeId.Scrubland: return 64f;
                case BiomeId.BorealForest: return 68f;
                case BiomeId.TropicalRainforest: return 72f;
                case BiomeId.TemperateForest:
                case BiomeId.TropicalDryForest: return 58f;
                case BiomeId.Woodland: return 52f;
                case BiomeId.Grassland:
                case BiomeId.Savanna: return 42f;
                default: return 55f;
            }
        }

        static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
        static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);
    }
}
