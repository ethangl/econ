using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Political assignment: landmasses -> realms -> provinces -> counties.
    /// </summary>
    public static class PoliticalGenerationOps
    {
        public static void Compute(
            PoliticalField political,
            BiomeField biomes,
            ElevationField elevation,
            MapGenConfig config)
        {
            DetectLandmasses(political, biomes, elevation);
            AssignRealms(political, biomes, elevation, config);
            AssignProvinces(political, biomes, elevation, config);
            AssignCounties(political, biomes, elevation, config);
        }

        static void DetectLandmasses(PoliticalField pol, BiomeField biomes, ElevationField elevation)
        {
            var mesh = pol.Mesh;
            int n = mesh.CellCount;
            var visited = new bool[n];
            int nextLandmassId = 1;

            for (int i = 0; i < n; i++)
            {
                bool land = elevation.IsLand(i) && !biomes.IsLakeCell[i];
                if (!land)
                {
                    pol.LandmassId[i] = -1;
                    continue;
                }

                if (visited[i])
                    continue;

                var queue = new Queue<int>();
                queue.Enqueue(i);
                visited[i] = true;
                pol.LandmassId[i] = nextLandmassId;

                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    int[] neighbors = mesh.CellNeighbors[c];
                    for (int ni = 0; ni < neighbors.Length; ni++)
                    {
                        int nb = neighbors[ni];
                        if (nb < 0 || nb >= n || visited[nb])
                            continue;

                        if (!(elevation.IsLand(nb) && !biomes.IsLakeCell[nb]))
                            continue;

                        visited[nb] = true;
                        pol.LandmassId[nb] = nextLandmassId;
                        queue.Enqueue(nb);
                    }
                }

                nextLandmassId++;
            }

            pol.LandmassCount = nextLandmassId - 1;
        }

        static void AssignRealms(PoliticalField pol, BiomeField biomes, ElevationField elevation, MapGenConfig config)
        {
            var mesh = pol.Mesh;
            var landCells = CollectLandCells(elevation, biomes);
            if (landCells.Count == 0)
            {
                pol.RealmCount = 0;
                pol.Capitals = Array.Empty<int>();
                return;
            }

            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float realmScale = profile != null ? profile.RealmTargetScale : 1f;
            if (realmScale <= 0f) realmScale = 1f;

            int targetRealms = Clamp((int)Math.Round((landCells.Count / 900f) * realmScale), 1, 24);
            var capitals = SelectSeeds(
                landCells,
                mesh,
                targetRealms,
                scoreCell: c => biomes.Suitability[c] + biomes.Population[c] * 0.02f,
                minSpacingKm: EstimateSpacing(mesh, targetRealms) * 0.35f);

            pol.RealmCount = capitals.Count;
            pol.Capitals = capitals.ToArray();

            AssignByNearestSeed(
                pol.RealmId,
                landCells,
                capitals,
                mesh,
                scoreBias: c => 1f + biomes.Suitability[c] * 0.01f);
        }

        static void AssignProvinces(PoliticalField pol, BiomeField biomes, ElevationField elevation, MapGenConfig config)
        {
            var mesh = pol.Mesh;
            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float provinceScale = profile != null ? profile.ProvinceTargetScale : 1f;
            if (provinceScale <= 0f) provinceScale = 1f;
            var provinceIds = new int[mesh.CellCount];
            int nextProvince = 1;

            for (int realm = 1; realm <= pol.RealmCount; realm++)
            {
                var realmCells = new List<int>();
                for (int i = 0; i < mesh.CellCount; i++)
                {
                    if (pol.RealmId[i] == realm)
                        realmCells.Add(i);
                }

                if (realmCells.Count == 0)
                    continue;

                int provinceTarget = Clamp((int)Math.Round((realmCells.Count / 450f) * provinceScale), 1, 18);
                var seeds = SelectSeeds(
                    realmCells,
                    mesh,
                    provinceTarget,
                    scoreCell: c => biomes.Suitability[c] + biomes.Population[c] * 0.015f,
                    minSpacingKm: EstimateSpacing(mesh, provinceTarget) * 0.25f);

                if (seeds.Count == 0)
                    seeds.Add(realmCells[0]);

                var localAssignment = new int[mesh.CellCount];
                AssignByNearestSeed(
                    localAssignment,
                    realmCells,
                    seeds,
                    mesh,
                    scoreBias: c => 1f + biomes.Suitability[c] * 0.005f);

                var remap = new Dictionary<int, int>();
                for (int s = 0; s < seeds.Count; s++)
                    remap[s + 1] = nextProvince++;

                for (int i = 0; i < realmCells.Count; i++)
                {
                    int c = realmCells[i];
                    int local = localAssignment[c];
                    provinceIds[c] = remap[local];
                }
            }

            pol.ProvinceId = provinceIds;
            pol.ProvinceCount = nextProvince - 1;
        }

        static void AssignCounties(PoliticalField pol, BiomeField biomes, ElevationField elevation, MapGenConfig config)
        {
            var mesh = pol.Mesh;
            HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(config.Template, config);
            float countyScale = profile != null ? profile.CountyTargetScale : 1f;
            if (countyScale <= 0f) countyScale = 1f;
            var countyIds = new int[mesh.CellCount];
            var countySeats = new List<int>();
            int nextCounty = 1;

            for (int province = 1; province <= pol.ProvinceCount; province++)
            {
                var provinceCells = new List<int>();
                for (int i = 0; i < mesh.CellCount; i++)
                {
                    if (pol.ProvinceId[i] == province)
                        provinceCells.Add(i);
                }

                if (provinceCells.Count == 0)
                    continue;

                int countyTarget = Clamp((int)Math.Round((provinceCells.Count / 120f) * countyScale), 1, 32);
                var seeds = SelectSeeds(
                    provinceCells,
                    mesh,
                    countyTarget,
                    scoreCell: c => biomes.Population[c] + biomes.Suitability[c],
                    minSpacingKm: EstimateSpacing(mesh, countyTarget) * 0.18f);

                if (seeds.Count == 0)
                    seeds.Add(provinceCells[0]);

                var localAssignment = new int[mesh.CellCount];
                AssignByNearestSeed(
                    localAssignment,
                    provinceCells,
                    seeds,
                    mesh,
                    scoreBias: c => 1f + biomes.Population[c] * 0.0002f);

                var remap = new Dictionary<int, int>();
                for (int s = 0; s < seeds.Count; s++)
                {
                    remap[s + 1] = nextCounty;
                    countySeats.Add(seeds[s]);
                    nextCounty++;
                }

                for (int i = 0; i < provinceCells.Count; i++)
                {
                    int c = provinceCells[i];
                    int local = localAssignment[c];
                    countyIds[c] = remap[local];
                }
            }

            pol.CountyId = countyIds;
            pol.CountySeats = countySeats.ToArray();
            pol.CountyCount = pol.CountySeats.Length;
        }

        static List<int> CollectLandCells(ElevationField elevation, BiomeField biomes)
        {
            var cells = new List<int>();
            for (int i = 0; i < elevation.CellCount; i++)
            {
                if (elevation.IsLand(i) && !biomes.IsLakeCell[i])
                    cells.Add(i);
            }

            return cells;
        }

        static List<int> SelectSeeds(
            List<int> cells,
            CellMesh mesh,
            int target,
            Func<int, float> scoreCell,
            float minSpacingKm)
        {
            var ranked = new List<int>(cells);
            ranked.Sort((a, b) => scoreCell(b).CompareTo(scoreCell(a)));

            var seeds = new List<int>();
            for (int i = 0; i < ranked.Count && seeds.Count < target; i++)
            {
                int c = ranked[i];
                bool farEnough = true;
                for (int s = 0; s < seeds.Count; s++)
                {
                    float d = Vec2.Distance(mesh.CellCenters[c], mesh.CellCenters[seeds[s]]);
                    if (d < minSpacingKm)
                    {
                        farEnough = false;
                        break;
                    }
                }

                if (farEnough)
                    seeds.Add(c);
            }

            for (int i = 0; i < ranked.Count && seeds.Count < target; i++)
            {
                int c = ranked[i];
                if (!seeds.Contains(c))
                    seeds.Add(c);
            }

            return seeds;
        }

        static void AssignByNearestSeed(
            int[] outAssignment,
            List<int> cells,
            List<int> seeds,
            CellMesh mesh,
            Func<int, float> scoreBias)
        {
            for (int ci = 0; ci < cells.Count; ci++)
            {
                int c = cells[ci];
                int best = 1;
                float bestScore = float.MaxValue;
                float bias = scoreBias(c);
                if (bias < 0.1f) bias = 0.1f;

                for (int s = 0; s < seeds.Count; s++)
                {
                    int seed = seeds[s];
                    float d = Vec2.Distance(mesh.CellCenters[c], mesh.CellCenters[seed]);
                    float score = d / bias;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = s + 1;
                    }
                }

                outAssignment[c] = best;
            }
        }

        static float EstimateSpacing(CellMesh mesh, int seedCount)
        {
            if (seedCount <= 0)
                return 1f;
            float area = mesh.Width * mesh.Height;
            if (area <= 1e-6f)
                return 1f;
            return (float)Math.Sqrt(area / seedCount);
        }

        static int Clamp(int x, int lo, int hi) => x < lo ? lo : (x > hi ? hi : x);
    }
}
