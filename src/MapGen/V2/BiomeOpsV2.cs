using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Biome/suitability/population/geography stage for V2.
    /// </summary>
    public static class BiomeOpsV2
    {
        public static void Compute(
            BiomeFieldV2 biome,
            ElevationFieldV2 elevation,
            ClimateFieldV2 climate,
            RiverFieldV2 rivers,
            MapGenV2Config config)
        {
            ComputeLakeCells(biome, elevation, rivers);
            ComputeWaterFeatures(biome, elevation);
            ComputeCoastDistance(biome, elevation);
            ComputeSlope(biome, elevation);
            AssignBiomesAndSuitability(biome, elevation, climate, rivers, config);
            ComputePopulation(biome, elevation);
        }

        static void ComputeLakeCells(BiomeFieldV2 biome, ElevationFieldV2 elevation, RiverFieldV2 rivers)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (elevation.IsLand(i))
                {
                    biome.IsLakeCell[i] = false;
                    continue;
                }

                int[] verts = mesh.CellVertices[i];
                if (verts == null || verts.Length == 0)
                {
                    biome.IsLakeCell[i] = false;
                    continue;
                }

                int lakeVerts = 0;
                for (int v = 0; v < verts.Length; v++)
                {
                    int vi = verts[v];
                    if (vi >= 0 && vi < mesh.VertexCount && rivers.IsLake(vi))
                        lakeVerts++;
                }

                biome.IsLakeCell[i] = lakeVerts * 2 >= verts.Length;
            }
        }

        static void ComputeWaterFeatures(BiomeFieldV2 biome, ElevationFieldV2 elevation)
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

        static void ComputeCoastDistance(BiomeFieldV2 biome, ElevationFieldV2 elevation)
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

        static void ComputeSlope(BiomeFieldV2 biome, ElevationFieldV2 elevation)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            for (int i = 0; i < n; i++)
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
            }
        }

        static void AssignBiomesAndSuitability(
            BiomeFieldV2 biome,
            ElevationFieldV2 elevation,
            ClimateFieldV2 climate,
            RiverFieldV2 rivers,
            MapGenV2Config config)
        {
            bool[] cellHasRiver = ComputeCellHasRiver(biome.Mesh, rivers, config.EffectiveRiverTraceThreshold);
            int n = biome.CellCount;

            for (int i = 0; i < n; i++)
            {
                bool isLand = elevation.IsLand(i) && !biome.IsLakeCell[i];
                if (!isLand)
                {
                    biome.Biome[i] = biome.IsLakeCell[i] ? BiomeId.Lake : BiomeId.CoastalMarsh;
                    biome.Habitability[i] = 0f;
                    biome.MovementCost[i] = 100f;
                    biome.Suitability[i] = 0f;
                    continue;
                }

                float temp = climate.TemperatureC[i];
                float precip = climate.PrecipitationMmYear[i];
                float alt = elevation[i];

                BiomeId id;
                if (alt > 4300f || temp < -12f)
                    id = BiomeId.Glacier;
                else if (temp < -2f)
                    id = BiomeId.Tundra;
                else if (precip < 180f)
                    id = temp > 12f ? BiomeId.HotDesert : BiomeId.ColdDesert;
                else if (precip < 350f)
                    id = BiomeId.Scrubland;
                else if (precip < 700f)
                    id = temp > 20f ? BiomeId.Savanna : (temp > 8f ? BiomeId.Grassland : BiomeId.BorealForest);
                else if (precip < 1400f)
                    id = temp > 24f ? BiomeId.TropicalDryForest : (temp > 12f ? BiomeId.TemperateForest : BiomeId.BorealForest);
                else
                    id = temp > 22f ? BiomeId.TropicalRainforest : (temp > 8f ? BiomeId.TemperateForest : BiomeId.BorealForest);

                biome.Biome[i] = id;
                float habitability = BaseHabitability(id);
                float movement = BaseMovementCost(id);

                if (cellHasRiver[i])
                    habitability += 10f;
                if (biome.CoastDistance[i] == 0)
                    habitability += 8f;

                float slopePenalty = biome.Slope[i] * 22f;
                float altitudePenalty = alt > 2600f ? (alt - 2600f) / 180f : 0f;
                float suitability = habitability - slopePenalty - altitudePenalty;

                biome.Habitability[i] = Clamp(habitability, 0f, 100f);
                biome.MovementCost[i] = movement + biome.Slope[i] * 15f;
                biome.Suitability[i] = Clamp(suitability, 0f, 100f);
            }
        }

        static void ComputePopulation(BiomeFieldV2 biome, ElevationFieldV2 elevation)
        {
            var mesh = biome.Mesh;
            bool hasAreas = mesh.CellAreas != null && mesh.CellAreas.Length == mesh.CellCount;

            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (!(elevation.IsLand(i) && !biome.IsLakeCell[i]))
                {
                    biome.Population[i] = 0f;
                    continue;
                }

                float area = hasAreas ? mesh.CellAreas[i] : 1f;
                if (area < 0.01f) area = 0.01f;
                biome.Population[i] = biome.Suitability[i] * area * 0.08f;
            }
        }

        static bool[] ComputeCellHasRiver(CellMesh mesh, RiverFieldV2 rivers, float riverTraceThreshold)
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

        static float BaseHabitability(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.Glacier: return 0f;
                case BiomeId.Tundra: return 15f;
                case BiomeId.ColdDesert: return 22f;
                case BiomeId.HotDesert: return 20f;
                case BiomeId.Scrubland: return 38f;
                case BiomeId.Grassland: return 62f;
                case BiomeId.Savanna: return 58f;
                case BiomeId.BorealForest: return 45f;
                case BiomeId.TemperateForest: return 66f;
                case BiomeId.TropicalDryForest: return 62f;
                case BiomeId.TropicalRainforest: return 54f;
                default: return 40f;
            }
        }

        static float BaseMovementCost(BiomeId biome)
        {
            switch (biome)
            {
                case BiomeId.Glacier: return 95f;
                case BiomeId.Tundra: return 75f;
                case BiomeId.HotDesert:
                case BiomeId.ColdDesert: return 80f;
                case BiomeId.BorealForest: return 68f;
                case BiomeId.TropicalRainforest: return 72f;
                case BiomeId.TemperateForest:
                case BiomeId.TropicalDryForest: return 58f;
                case BiomeId.Grassland:
                case BiomeId.Savanna: return 42f;
                default: return 55f;
            }
        }

        static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
        static float Clamp(float x, float lo, float hi) => x < lo ? lo : (x > hi ? hi : x);
    }
}
