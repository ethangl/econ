using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Post-biome geographic computations: coast distance BFS and water feature detection.
    /// </summary>
    public static class GeographyOps
    {
        /// <summary>
        /// BFS from coastline cells. Land cells get positive distance, water cells get negative.
        /// A coastline cell is any cell adjacent to a cell of the opposite land/water type.
        /// </summary>
        public static void ComputeCoastDistance(BiomeData biomes, HeightGrid heights)
        {
            var mesh = biomes.Mesh;
            int n = mesh.CellCount;
            var dist = new int[n];
            var queue = new Queue<int>();

            for (int i = 0; i < n; i++)
                dist[i] = int.MaxValue;

            // Seed: cells adjacent to a cell of opposite type
            for (int i = 0; i < n; i++)
            {
                bool iWater = heights.IsWater(i) || biomes.IsLakeCell[i];
                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (nb >= 0 && nb < n)
                    {
                        bool nbWater = heights.IsWater(nb) || biomes.IsLakeCell[nb];
                        if (nbWater != iWater)
                        {
                            dist[i] = 0;
                            queue.Enqueue(i);
                            break;
                        }
                    }
                }
            }

            // BFS outward
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int nextDist = dist[cur] + 1;
                foreach (int nb in mesh.CellNeighbors[cur])
                {
                    if (nb >= 0 && nb < n && dist[nb] == int.MaxValue)
                    {
                        dist[nb] = nextDist;
                        queue.Enqueue(nb);
                    }
                }
            }

            // Store: land = positive, water = negative
            for (int i = 0; i < n; i++)
            {
                int d = dist[i] == int.MaxValue ? 0 : dist[i];
                bool isWater = heights.IsWater(i) || biomes.IsLakeCell[i];
                biomes.CoastDistance[i] = isWater ? -d : d;
            }
        }

        /// <summary>
        /// Flood-fill water cells into distinct water bodies (oceans and lakes).
        /// Sets FeatureId per cell and populates the Features list.
        /// </summary>
        public static void ComputeWaterFeatures(BiomeData biomes, HeightGrid heights)
        {
            var mesh = biomes.Mesh;
            int n = mesh.CellCount;

            var featureId = new int[n];
            for (int i = 0; i < n; i++)
                featureId[i] = -1;

            var features = new List<WaterFeature>();
            int nextId = 1;

            for (int i = 0; i < n; i++)
            {
                bool isWater = heights.IsWater(i) || biomes.IsLakeCell[i];
                if (!isWater) continue;
                if (featureId[i] >= 0) continue;

                int fid = nextId++;
                var queue = new Queue<int>();
                queue.Enqueue(i);
                featureId[i] = fid;
                int count = 0;
                bool touchesBorder = false;
                bool hasLakeCell = false;

                while (queue.Count > 0)
                {
                    int cur = queue.Dequeue();
                    count++;
                    if (mesh.CellIsBoundary[cur])
                        touchesBorder = true;
                    if (biomes.IsLakeCell[cur])
                        hasLakeCell = true;

                    foreach (int nb in mesh.CellNeighbors[cur])
                    {
                        if (nb >= 0 && nb < n && featureId[nb] < 0)
                        {
                            bool nbWater = heights.IsWater(nb) || biomes.IsLakeCell[nb];
                            if (nbWater)
                            {
                                featureId[nb] = fid;
                                queue.Enqueue(nb);
                            }
                        }
                    }
                }

                var type = (hasLakeCell && !touchesBorder && count < 500)
                    ? WaterFeatureType.Lake
                    : WaterFeatureType.Ocean;

                features.Add(new WaterFeature
                {
                    Id = fid,
                    Type = type,
                    TouchesBorder = touchesBorder,
                    CellCount = count
                });
            }

            // Write to BiomeData
            for (int i = 0; i < n; i++)
                biomes.FeatureId[i] = featureId[i] >= 0 ? featureId[i] : 0;

            biomes.Features = features.ToArray();
        }
    }
}
