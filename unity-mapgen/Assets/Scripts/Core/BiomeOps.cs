using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Static operations for the biome pipeline.
    /// All computation is engine-independent (no UnityEngine).
    /// </summary>
    public static class BiomeOps
    {
        /// <summary>
        /// Compute slope for each cell: max height gradient to any neighbor, normalized 0-1.
        /// </summary>
        public static void ComputeSlope(BiomeData biome, HeightGrid heights)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;

            // First pass: compute raw max gradient per cell
            float globalMax = 0f;
            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i)) continue;

                float maxGrad = 0f;
                Vec2 ci = mesh.CellCenters[i];

                foreach (int nb in mesh.CellNeighbors[i])
                {
                    float dh = Math.Abs(heights.Heights[i] - heights.Heights[nb]);
                    float dist = Vec2.Distance(ci, mesh.CellCenters[nb]);
                    if (dist < 1e-6f) continue;
                    float grad = dh / dist;
                    if (grad > maxGrad) maxGrad = grad;
                }

                biome.Slope[i] = maxGrad;
                if (maxGrad > globalMax) globalMax = maxGrad;
            }

            // Second pass: normalize to 0-1
            if (globalMax > 1e-6f)
            {
                for (int i = 0; i < n; i++)
                {
                    if (heights.IsWater(i)) continue;
                    biome.Slope[i] /= globalMax;
                }
            }
        }

        /// <summary>
        /// BFS salt proximity from ocean cells.
        /// saltEffect decays with cell-hop distance and elevation above sea level.
        /// </summary>
        public static void ComputeSaltProximity(BiomeData biome, HeightGrid heights)
        {
            const int maxSaltReach = 5;
            const float saltElevCutoff = 20f;

            var mesh = biome.Mesh;
            int n = mesh.CellCount;

            // BFS distance from any ocean cell
            int[] dist = new int[n];
            for (int i = 0; i < n; i++)
                dist[i] = int.MaxValue;

            var queue = new Queue<int>();

            // Seed: all water cells at distance 0
            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i))
                {
                    dist[i] = 0;
                    queue.Enqueue(i);
                }
            }

            // BFS outward across land cells up to maxSaltReach
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int nextDist = dist[c] + 1;
                if (nextDist > maxSaltReach) continue;

                foreach (int nb in mesh.CellNeighbors[c])
                {
                    if (dist[nb] <= nextDist) continue;
                    dist[nb] = nextDist;
                    queue.Enqueue(nb);
                }
            }

            // Compute salt effect
            for (int i = 0; i < n; i++)
            {
                if (heights.IsWater(i) || dist[i] == int.MaxValue)
                {
                    biome.SaltEffect[i] = 0f;
                    continue;
                }

                float distFactor = Math.Max(0f, 1f - (float)dist[i] / maxSaltReach);
                float elevAboveSea = heights.Heights[i] - HeightGrid.SeaLevel;
                float elevFactor = Math.Max(0f, 1f - elevAboveSea / saltElevCutoff);
                biome.SaltEffect[i] = distFactor * elevFactor;
            }
        }

        /// <summary>
        /// Average vertex flux onto cells via CellMesh.CellVertices.
        /// </summary>
        public static void ComputeCellFlux(BiomeData biome, RiverData rivers)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;

            for (int i = 0; i < n; i++)
            {
                int[] verts = mesh.CellVertices[i];
                if (verts == null || verts.Length == 0) continue;

                float sum = 0f;
                for (int v = 0; v < verts.Length; v++)
                    sum += rivers.VertexFlux[verts[v]];

                biome.CellFlux[i] = sum / verts.Length;
            }
        }

        // ── Rock Type ───────────────────────────────────────────────────────

        const float RockNoiseFrequency = 0.003f; // low frequency = large geological regions

        /// <summary>
        /// Assign rock type per cell using Perlin noise thresholds.
        /// Granite &lt; 0.25, Sedimentary 0.25-0.50, Limestone 0.50-0.75, Volcanic &gt; 0.75.
        /// </summary>
        public static void ComputeRockType(BiomeData biome, int seed)
        {
            var mesh = biome.Mesh;
            var noise = new Noise(seed);

            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 c = mesh.CellCenters[i];
                float v = noise.Sample01(c.X * RockNoiseFrequency, c.Y * RockNoiseFrequency);

                if (v > 0.75f)      biome.Rock[i] = RockType.Volcanic;
                else if (v > 0.50f) biome.Rock[i] = RockType.Limestone;
                else if (v > 0.25f) biome.Rock[i] = RockType.Sedimentary;
                else                biome.Rock[i] = RockType.Granite;
            }
        }

        // ── Loess Deposition ────────────────────────────────────────────────

        const float LoessSourceStrength = 0.3f;
        const float LoessOrographicCapture = 0.5f;
        const float LoessDepositRate = 0.1f;

        /// <summary>
        /// Wind-deposited silt from bare arid/frozen source areas.
        /// Reuses the same wind-sweep pattern as PrecipitationOps.
        /// </summary>
        public static void ComputeLoess(BiomeData biome, HeightGrid heights,
            ClimateData climate, WorldConfig config)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            float[] loessAccum = new float[n];

            for (int b = 0; b < config.WindBands.Length; b++)
            {
                var band = config.WindBands[b];
                float overlapMin = Math.Max(band.LatMin, config.LatitudeSouth);
                float overlapMax = Math.Min(band.LatMax, config.LatitudeNorth);
                if (overlapMin >= overlapMax) continue;

                float overlapFraction = (overlapMax - overlapMin) /
                                        (config.LatitudeNorth - config.LatitudeSouth);

                float[] bandLoess = SweepLoessBand(mesh, heights, climate, band.WindVector);

                for (int i = 0; i < n; i++)
                    loessAccum[i] += bandLoess[i] * overlapFraction;
            }

            // Normalize to 0-1
            float maxVal = 0f;
            for (int i = 0; i < n; i++)
                if (loessAccum[i] > maxVal) maxVal = loessAccum[i];

            if (maxVal > 1e-6f)
            {
                for (int i = 0; i < n; i++)
                    biome.Loess[i] = Math.Min(loessAccum[i] / maxVal, 1f);
            }
        }

        static float[] SweepLoessBand(CellMesh mesh, HeightGrid heights,
            ClimateData climate, Vec2 windDir)
        {
            int n = mesh.CellCount;
            float[] carry = new float[n];
            float[] deposit = new float[n];
            bool[] visited = new bool[n];

            // Sort cells by wind projection (upwind first)
            float[] windPos = new float[n];
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                windPos[i] = Vec2.Dot(mesh.CellCenters[i], windDir);
                order[i] = i;
            }
            Array.Sort(order, (a, b) => windPos[a].CompareTo(windPos[b]));

            for (int idx = 0; idx < n; idx++)
            {
                int i = order[idx];

                // Gather carry from upwind neighbors
                float gatheredCarry = 0f;
                float totalWeight = 0f;
                bool hasUpwind = false;

                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (!visited[nb]) continue;
                    Vec2 toCell = mesh.CellCenters[i] - mesh.CellCenters[nb];
                    float alignment = Vec2.Dot(toCell.Normalized, windDir);
                    if (alignment <= 0f) continue;

                    float weight = alignment * alignment;
                    gatheredCarry += carry[nb] * weight;
                    totalWeight += weight;
                    hasUpwind = true;
                }

                if (hasUpwind)
                    carry[i] = gatheredCarry / totalWeight;

                // Skip water cells
                if (heights.IsWater(i))
                {
                    visited[i] = true;
                    continue;
                }

                // Source emission: bare arid/frozen ground
                if (climate.Precipitation[i] < 15f || climate.Temperature[i] < -5f)
                    carry[i] += LoessSourceStrength;

                // Orographic capture: rising terrain traps silt
                float maxUphill = 0f;
                foreach (int nb in mesh.CellNeighbors[i])
                {
                    float dh = heights.Heights[i] - heights.Heights[nb];
                    if (dh > maxUphill) maxUphill = dh;
                }
                if (maxUphill > 0f)
                {
                    float slopeFactor = Math.Min(maxUphill, 20f) / 20f;
                    float captured = carry[i] * LoessOrographicCapture * slopeFactor;
                    deposit[i] += captured;
                    carry[i] -= captured;
                    if (carry[i] < 0f) carry[i] = 0f;
                }

                // Distance decay: silt settles
                float settled = carry[i] * LoessDepositRate;
                deposit[i] += settled;
                carry[i] -= settled;
                if (carry[i] < 0f) carry[i] = 0f;

                visited[i] = true;
            }

            return deposit;
        }

        // ── Soil Classification ─────────────────────────────────────────────

        static readonly float[] BaseFertility = { 0.05f, 0.10f, 0.10f, 0.90f, 0.15f, 0.45f, 0.35f, 0.70f };
        static readonly float[] RockFertilityModifier = { 0.7f, 1.0f, 1.1f, 1.4f }; // Granite, Sedimentary, Limestone, Volcanic

        /// <summary>
        /// Priority cascade soil classification. First match wins.
        /// </summary>
        public static void ClassifySoil(BiomeData biome, HeightGrid heights, ClimateData climate)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                float temp = climate.Temperature[i];
                float precip = climate.Precipitation[i];
                float elev = heights.Heights[i];
                float slope = biome.Slope[i];
                float salt = biome.SaltEffect[i];
                float flux = biome.CellFlux[i];

                // Priority cascade
                if (temp < -5f)
                    biome.Soil[i] = SoilType.Permafrost;
                else if (salt > 0.3f)
                    biome.Soil[i] = SoilType.Saline;
                else if (slope > 0.6f || elev > 80f)
                    biome.Soil[i] = SoilType.Lithosol;
                else if (flux > 200f && slope < 0.15f)
                    biome.Soil[i] = SoilType.Alluvial;
                else if (precip < 15f)
                    biome.Soil[i] = SoilType.Aridisol;
                else if (temp > 20f && precip > 60f)
                    biome.Soil[i] = SoilType.Laterite;
                else if (temp < 20f && precip > 30f)
                    biome.Soil[i] = SoilType.Podzol;
                else
                    biome.Soil[i] = SoilType.Chernozem;
            }
        }

        /// <summary>
        /// Fertility = baseFertility * rockModifier * (1 + loess) * drainageModifier.
        /// </summary>
        public static void ComputeFertility(BiomeData biome, HeightGrid heights, ClimateData climate)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                float baseFert = BaseFertility[(int)biome.Soil[i]];
                float rockMod = RockFertilityModifier[(int)biome.Rock[i]];
                float loessMod = 1f + biome.Loess[i];

                // Drainage modifier: penalizes moisture extremes for non-alluvial soils
                float drainageMod;
                if (biome.Soil[i] == SoilType.Alluvial)
                {
                    drainageMod = 1f;
                }
                else
                {
                    float precipNorm = climate.Precipitation[i] / 100f;
                    float dryPenalty = precipNorm < 0.3f
                        ? (0.3f - precipNorm) / 0.3f * 0.3f
                        : 0f;
                    float wetPenalty = precipNorm > 0.7f
                        ? (precipNorm - 0.7f) / 0.3f * 0.2f
                        : 0f;
                    drainageMod = 1f - dryPenalty - wetPenalty;
                }

                float fertility = baseFert * rockMod * loessMod * drainageMod;
                biome.Fertility[i] = fertility < 0f ? 0f : (fertility > 1f ? 1f : fertility);
            }
        }

        // ── Biome Assignment ────────────────────────────────────────────────

        // Base habitability per biome (index = BiomeId)
        static readonly float[] BiomeHabitability =
        {
            0f,  // Glacier
            5f,  // Tundra
            2f,  // SaltFlat
            10f, // CoastalMarsh
            0f,  // AlpineBarren
            8f,  // MountainShrub
            90f, // Floodplain
            15f, // Wetland
            4f,  // HotDesert
            5f,  // ColdDesert
            15f, // Scrubland
            60f, // TropicalRainforest
            50f, // TropicalDryForest
            25f, // Savanna
            12f, // BorealForest
            70f, // TemperateForest
            80f, // Grassland
            85f, // Woodland
        };

        const float RiverBonusMax = 15f;
        const float ReferenceFlux = 1000f;

        /// <summary>
        /// Assign biome from soil type + climate conditions. See docs/biomes/biomes.md.
        /// </summary>
        public static void AssignBiomes(BiomeData biome, HeightGrid heights, ClimateData climate)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                float temp = climate.Temperature[i];
                float precip = climate.Precipitation[i];
                float elev = heights.Heights[i];
                float slope = biome.Slope[i];
                float flux = biome.CellFlux[i];

                switch (biome.Soil[i])
                {
                    case SoilType.Permafrost:
                        biome.Biome[i] = temp < -10f ? BiomeId.Glacier : BiomeId.Tundra;
                        break;
                    case SoilType.Saline:
                        biome.Biome[i] = precip < 15f ? BiomeId.SaltFlat : BiomeId.CoastalMarsh;
                        break;
                    case SoilType.Lithosol:
                        biome.Biome[i] = (temp < -3f || elev > 85f)
                            ? BiomeId.AlpineBarren : BiomeId.MountainShrub;
                        break;
                    case SoilType.Alluvial:
                        biome.Biome[i] = (flux > 500f && slope < 0.1f)
                            ? BiomeId.Wetland : BiomeId.Floodplain;
                        break;
                    case SoilType.Aridisol:
                        if (temp > 25f)      biome.Biome[i] = BiomeId.HotDesert;
                        else if (temp < 5f)  biome.Biome[i] = BiomeId.ColdDesert;
                        else                 biome.Biome[i] = BiomeId.Scrubland;
                        break;
                    case SoilType.Laterite:
                        if (precip > 80f)      biome.Biome[i] = BiomeId.TropicalRainforest;
                        else if (precip > 70f) biome.Biome[i] = BiomeId.TropicalDryForest;
                        else                   biome.Biome[i] = BiomeId.Savanna;
                        break;
                    case SoilType.Podzol:
                        biome.Biome[i] = temp < 5f ? BiomeId.BorealForest : BiomeId.TemperateForest;
                        break;
                    case SoilType.Chernozem:
                        biome.Biome[i] = precip > 45f ? BiomeId.Woodland : BiomeId.Grassland;
                        break;
                }
            }
        }

        /// <summary>
        /// Habitability = biome base + river adjacency bonus.
        /// River bonus uses max edge flux across the cell's Voronoi edges.
        /// </summary>
        public static void ComputeHabitability(BiomeData biome, HeightGrid heights, RiverData rivers)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                float baseHab = BiomeHabitability[(int)biome.Biome[i]];

                // River adjacency bonus: max edge flux across cell's edges
                float maxEdgeFlux = 0f;
                int[] edges = mesh.CellEdges[i];
                if (edges != null)
                {
                    for (int e = 0; e < edges.Length; e++)
                    {
                        float ef = rivers.EdgeFlux[edges[e]];
                        if (ef > maxEdgeFlux) maxEdgeFlux = ef;
                    }
                }

                float riverBonus = 0f;
                if (maxEdgeFlux > 0f)
                {
                    float fluxNorm = maxEdgeFlux / ReferenceFlux;
                    if (fluxNorm > 1f) fluxNorm = 1f;
                    riverBonus = RiverBonusMax * (float)Math.Sqrt(fluxNorm);
                }

                biome.Habitability[i] = baseHab + riverBonus;
            }
        }

        // ── Vegetation ──────────────────────────────────────────────────────

        // Per-biome: dominant vegetation type, density min/max, precip range for density interpolation
        struct VegInfo
        {
            public VegetationType Type;
            public float DensityMin, DensityMax;
            public float PrecipMin, PrecipMax;
        }

        static readonly VegInfo[] BiomeVeg =
        {
            // Glacier
            new VegInfo { Type = VegetationType.None,             DensityMin = 0f,   DensityMax = 0f,   PrecipMin = 0f,  PrecipMax = 100f },
            // Tundra
            new VegInfo { Type = VegetationType.LichenMoss,       DensityMin = 0.1f, DensityMax = 0.3f, PrecipMin = 0f,  PrecipMax = 100f },
            // SaltFlat
            new VegInfo { Type = VegetationType.None,             DensityMin = 0f,   DensityMax = 0f,   PrecipMin = 0f,  PrecipMax = 15f  },
            // CoastalMarsh
            new VegInfo { Type = VegetationType.Grass,            DensityMin = 0.3f, DensityMax = 0.6f, PrecipMin = 15f, PrecipMax = 100f },
            // AlpineBarren
            new VegInfo { Type = VegetationType.None,             DensityMin = 0f,   DensityMax = 0.1f, PrecipMin = 0f,  PrecipMax = 100f },
            // MountainShrub
            new VegInfo { Type = VegetationType.Shrub,            DensityMin = 0.2f, DensityMax = 0.5f, PrecipMin = 0f,  PrecipMax = 100f },
            // Floodplain
            new VegInfo { Type = VegetationType.Grass,            DensityMin = 0.5f, DensityMax = 0.8f, PrecipMin = 0f,  PrecipMax = 100f },
            // Wetland
            new VegInfo { Type = VegetationType.Grass,            DensityMin = 0.6f, DensityMax = 0.9f, PrecipMin = 0f,  PrecipMax = 100f },
            // HotDesert
            new VegInfo { Type = VegetationType.None,             DensityMin = 0f,   DensityMax = 0.1f, PrecipMin = 0f,  PrecipMax = 15f  },
            // ColdDesert
            new VegInfo { Type = VegetationType.None,             DensityMin = 0f,   DensityMax = 0.1f, PrecipMin = 0f,  PrecipMax = 15f  },
            // Scrubland
            new VegInfo { Type = VegetationType.Shrub,            DensityMin = 0.2f, DensityMax = 0.4f, PrecipMin = 0f,  PrecipMax = 15f  },
            // TropicalRainforest
            new VegInfo { Type = VegetationType.BroadleafForest,  DensityMin = 0.8f, DensityMax = 1.0f, PrecipMin = 80f, PrecipMax = 100f },
            // TropicalDryForest
            new VegInfo { Type = VegetationType.BroadleafForest,  DensityMin = 0.5f, DensityMax = 0.7f, PrecipMin = 70f, PrecipMax = 80f  },
            // Savanna
            new VegInfo { Type = VegetationType.Grass,            DensityMin = 0.3f, DensityMax = 0.5f, PrecipMin = 60f, PrecipMax = 70f  },
            // BorealForest
            new VegInfo { Type = VegetationType.ConiferousForest, DensityMin = 0.5f, DensityMax = 0.8f, PrecipMin = 30f, PrecipMax = 100f },
            // TemperateForest
            new VegInfo { Type = VegetationType.DeciduousForest,  DensityMin = 0.5f, DensityMax = 0.8f, PrecipMin = 30f, PrecipMax = 100f },
            // Grassland
            new VegInfo { Type = VegetationType.Grass,            DensityMin = 0.4f, DensityMax = 0.7f, PrecipMin = 15f, PrecipMax = 45f  },
            // Woodland
            new VegInfo { Type = VegetationType.DeciduousForest,  DensityMin = 0.3f, DensityMax = 0.5f, PrecipMin = 45f, PrecipMax = 100f },
        };

        // Temperature thresholds for vegetation type downgrade
        const float TreelineTemp = -3f;
        const float ShrubMinTemp = -5f;
        const float GrassMinTemp = -8f;
        const float LichenMinTemp = -10f;
        const float TreelineFadeRange = 13f;

        /// <summary>
        /// Assign vegetation type and density per cell.
        /// Type from biome, density from precipitation * elevation * salinity factors.
        /// Treeline gate downgrades vegetation type at cold temperatures.
        /// </summary>
        public static void ComputeVegetation(BiomeData biome, HeightGrid heights, ClimateData climate)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                int biomeIdx = (int)biome.Biome[i];
                var veg = BiomeVeg[biomeIdx];
                float temp = climate.Temperature[i];
                float precip = climate.Precipitation[i];

                // Start with biome's dominant vegetation type
                VegetationType vegType = veg.Type;

                // Treeline gate: downgrade type if too cold
                vegType = ApplyTreeline(vegType, temp);

                // Precipitation factor: interpolate density within biome's range
                float precipRange = veg.PrecipMax - veg.PrecipMin;
                float precipNorm = precipRange > 0.01f
                    ? Clamp01((precip - veg.PrecipMin) / precipRange)
                    : 0.5f;
                float precipFactor = veg.DensityMin + (veg.DensityMax - veg.DensityMin) * precipNorm;

                // Elevation factor: density fades as temp approaches treeline
                float elevationFactor = Clamp01((temp - TreelineTemp) / TreelineFadeRange);

                // Salinity factor
                float salinityFactor = 1f - 0.5f * biome.SaltEffect[i];

                float density = precipFactor * elevationFactor * salinityFactor;
                if (density < 0f) density = 0f;
                if (density > 1f) density = 1f;

                biome.Vegetation[i] = vegType;
                biome.VegetationDensity[i] = density;
            }
        }

        static VegetationType ApplyTreeline(VegetationType type, float temp)
        {
            // Forest types require treeline temp
            if (type >= VegetationType.DeciduousForest && temp < TreelineTemp)
                type = VegetationType.Shrub;
            // Shrub requires -5
            if (type == VegetationType.Shrub && temp < ShrubMinTemp)
                type = VegetationType.Grass;
            // Grass requires -8
            if (type == VegetationType.Grass && temp < GrassMinTemp)
                type = VegetationType.LichenMoss;
            // Lichen/moss requires -10
            if (type == VegetationType.LichenMoss && temp < LichenMinTemp)
                type = VegetationType.None;
            return type;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        // ── Fauna ───────────────────────────────────────────────────────────

        // Per-biome base values: [game, waterfowl, fur]
        static readonly float[,] BiomeFaunaBase = new float[18, 3]
        {
            { 0f,   0f,   0f   }, // Glacier
            { 0f,   0f,   0.3f }, // Tundra
            { 0f,   0f,   0f   }, // SaltFlat
            { 0f,   0.8f, 0f   }, // CoastalMarsh
            { 0f,   0f,   0f   }, // AlpineBarren
            { 0.3f, 0f,   0.3f }, // MountainShrub
            { 0.5f, 0.5f, 0f   }, // Floodplain
            { 0f,   0.8f, 0f   }, // Wetland
            { 0f,   0f,   0f   }, // HotDesert
            { 0f,   0f,   0f   }, // ColdDesert
            { 0.3f, 0f,   0f   }, // Scrubland
            { 0.5f, 0f,   0f   }, // TropicalRainforest
            { 0.5f, 0f,   0f   }, // TropicalDryForest
            { 0.8f, 0f,   0f   }, // Savanna
            { 0.5f, 0f,   0.8f }, // BorealForest
            { 0.8f, 0f,   0.5f }, // TemperateForest
            { 0.8f, 0f,   0f   }, // Grassland
            { 0.5f, 0f,   0.3f }, // Woodland
        };

        // Fish parameters
        const float RiverFishBase = 0.6f;
        const float CoastalFishBase = 0.4f;
        const float LakeFishBase = 0.3f;
        const float EstuaryFishBonus = 0.3f;
        // ReferenceFlux already defined above (1000)

        // Fur cold bonus
        const float ColdBonusThreshold = 5f;
        const float ColdBonusRange = 20f;

        // Waterfowl water proximity
        const float WaterProximityScale = 3f;

        // Subsistence food values per fauna type
        const float FishFoodValue = 0.25f;
        const float GameFoodValue = 0.20f;
        const float WaterfowlFoodValue = 0.10f;
        const float FurFoodValue = 0.05f;

        // Subsistence climate penalty
        const float ColdPenaltyRate = 0.02f;
        const float HeatPenaltyRate = 0.02f;

        // Vegetation base food values
        static readonly float[] VegFoodBase =
        {
            0f,    // None
            0.02f, // LichenMoss
            0.15f, // Grass (scaled by fertility)
            0.08f, // Shrub
            0.12f, // DeciduousForest (scaled by density)
            0.06f, // ConiferousForest (scaled by density)
            0.10f, // BroadleafForest (scaled by density)
        };

        /// <summary>
        /// Compute all 4 fauna abundances per cell.
        /// </summary>
        public static void ComputeFauna(BiomeData biome, HeightGrid heights,
            ClimateData climate, RiverData rivers)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                int biomeIdx = (int)biome.Biome[i];
                float temp = climate.Temperature[i];
                float density = biome.VegetationDensity[i];

                // Game: base * vegetation density
                biome.GameAbundance[i] = Clamp01(BiomeFaunaBase[biomeIdx, 0] * density);

                // Fur: base * density * cold bonus
                float coldBonus = 1f + Math.Max(0f, ColdBonusThreshold - temp) / ColdBonusRange;
                biome.FurAbundance[i] = Clamp01(BiomeFaunaBase[biomeIdx, 2] * density * coldBonus);

                // Waterfowl: base * water proximity
                float waterEdgeCount = 0f;
                int[] edges = mesh.CellEdges[i];
                if (edges != null)
                {
                    for (int e = 0; e < edges.Length; e++)
                    {
                        // Edge has water if it has river flux or borders a water cell
                        if (rivers.EdgeFlux[edges[e]] > 0f)
                        {
                            waterEdgeCount += 1f;
                            continue;
                        }
                        var (c0, c1) = mesh.EdgeCells[edges[e]];
                        if ((c0 >= 0 && heights.IsWater(c0)) || (c1 >= 0 && heights.IsWater(c1)))
                            waterEdgeCount += 1f;
                    }
                }
                float waterProximity = Clamp01(waterEdgeCount / WaterProximityScale);
                biome.WaterfowlAbundance[i] = Clamp01(BiomeFaunaBase[biomeIdx, 1] * waterProximity);

                // Fish: river + coastal + lake + estuary
                float maxAdjEdgeFlux = 0f;
                if (edges != null)
                {
                    for (int e = 0; e < edges.Length; e++)
                    {
                        if (rivers.EdgeFlux[edges[e]] > maxAdjEdgeFlux)
                            maxAdjEdgeFlux = rivers.EdgeFlux[edges[e]];
                    }
                }
                float riverFish = Clamp01(maxAdjEdgeFlux / ReferenceFlux) * RiverFishBase;

                bool hasCoast = false;
                bool hasLake = false;
                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (heights.IsWater(nb))
                        hasCoast = true;
                    // Lakes: check vertex-level lake detection
                }
                // Check for lake vertices among cell's vertices
                int[] verts = mesh.CellVertices[i];
                if (verts != null)
                {
                    for (int v = 0; v < verts.Length; v++)
                    {
                        if (rivers.IsLake(verts[v]))
                        {
                            hasLake = true;
                            break;
                        }
                    }
                }

                float coastalFish = hasCoast ? CoastalFishBase : 0f;
                float lakeFish = hasLake ? LakeFishBase : 0f;
                float estuaryBonus = (coastalFish > 0f && riverFish > 0f) ? EstuaryFishBonus : 0f;

                biome.FishAbundance[i] = Clamp01(riverFish + coastalFish + lakeFish + estuaryBonus);
            }
        }

        /// <summary>
        /// Subsistence = vegetation food + fauna food - climate penalty.
        /// </summary>
        public static void ComputeSubsistence(BiomeData biome, HeightGrid heights, ClimateData climate)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                // Vegetation food
                int vegIdx = (int)biome.Vegetation[i];
                float vegFood = VegFoodBase[vegIdx];
                // Grass scales with fertility; forest scales with density
                if (biome.Vegetation[i] == VegetationType.Grass)
                    vegFood *= biome.Fertility[i];
                else if (vegIdx >= (int)VegetationType.DeciduousForest)
                    vegFood *= biome.VegetationDensity[i];

                // Fauna food
                float faunaFood = biome.FishAbundance[i] * FishFoodValue
                                + biome.GameAbundance[i] * GameFoodValue
                                + biome.WaterfowlAbundance[i] * WaterfowlFoodValue
                                + biome.FurAbundance[i] * FurFoodValue;

                // Climate penalty
                float temp = climate.Temperature[i];
                float penalty = 0f;
                if (temp < 0f) penalty = -temp * ColdPenaltyRate;
                else if (temp > 35f) penalty = (temp - 35f) * HeatPenaltyRate;

                biome.Subsistence[i] = Clamp01(vegFood + faunaFood - penalty);
            }
        }

        // ── Movement Cost ───────────────────────────────────────────────────

        const float SlopeWeight = 4f;
        const float AltitudeThreshold = 50f;
        const float AltitudeWeight = 2f;

        // Ground cost per soil type (index = SoilType)
        static readonly float[] SoilGroundCost =
            { 3.0f, 1.5f, 2.0f, 0.5f, 1.0f, 1.0f, 1.2f, 0.5f };

        // Vegetation movement weight per type (index = VegetationType)
        static readonly float[] VegMovementWeight =
            { 0f, 0.2f, 0f, 0.5f, 1.5f, 2.0f, 3.0f };

        /// <summary>
        /// Per-cell movement cost: slope + altitude + ground + vegetation.
        /// Wetland biome overrides alluvial ground cost to 2.5.
        /// </summary>
        public static void ComputeMovementCost(BiomeData biome, HeightGrid heights)
        {
            var mesh = biome.Mesh;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                float slope = biome.Slope[i];
                float height = heights.Heights[i];

                float slopeCost = 1f + slope * SlopeWeight;

                float altNorm = (height - AltitudeThreshold) /
                                (HeightGrid.MaxHeight - AltitudeThreshold);
                float altitudeCost = altNorm > 0f ? altNorm * AltitudeWeight : 0f;

                float groundCost = SoilGroundCost[(int)biome.Soil[i]];
                // Wetland override
                if (biome.Biome[i] == BiomeId.Wetland)
                    groundCost = 2.5f;

                float vegCost = VegMovementWeight[(int)biome.Vegetation[i]] *
                                biome.VegetationDensity[i];

                biome.MovementCost[i] = slopeCost + altitudeCost + groundCost + vegCost;
            }
        }

        // ── Geological Resources ────────────────────────────────────────────

        const float OreNoiseFrequency = 0.004f;
        const float OreHeightGate = 50f;
        const float IronThreshold = 0.6f;
        const float GoldThreshold = 0.7f;
        const float LeadThreshold = 0.65f;

        /// <summary>
        /// Place iron, gold, and lead ore deposits using independent Perlin noise fields.
        /// </summary>
        public static void ComputeGeologicalResources(BiomeData biome, HeightGrid heights,
            int ironSeed, int goldSeed, int leadSeed)
        {
            var mesh = biome.Mesh;
            var ironNoise = new Noise(ironSeed);
            var goldNoise = new Noise(goldSeed);
            var leadNoise = new Noise(leadSeed);

            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                Vec2 c = mesh.CellCenters[i];
                float height = heights.Heights[i];

                if (height <= OreHeightGate) continue;

                float cx = c.X * OreNoiseFrequency;
                float cy = c.Y * OreNoiseFrequency;

                // Iron
                float ironVal = ironNoise.Sample01(cx, cy);
                if (ironVal > IronThreshold)
                    biome.IronAbundance[i] = (ironVal - IronThreshold) / (1f - IronThreshold);

                // Gold
                float goldVal = goldNoise.Sample01(cx, cy);
                if (goldVal > GoldThreshold)
                    biome.GoldAbundance[i] = (goldVal - GoldThreshold) / (1f - GoldThreshold);

                // Lead
                float leadVal = leadNoise.Sample01(cx, cy);
                if (leadVal > LeadThreshold)
                    biome.LeadAbundance[i] = (leadVal - LeadThreshold) / (1f - LeadThreshold);
            }
        }

        /// <summary>
        /// Compute salt abundance from saline soil and salt effect proximity.
        /// </summary>
        public static void ComputeSaltResource(BiomeData biome, HeightGrid heights)
        {
            for (int i = 0; i < biome.Mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                // Salt only in saline soil biomes (SaltFlat, CoastalMarsh)
                if (biome.Soil[i] != SoilType.Saline) continue;

                // Abundance scales with salt effect (ocean proximity decay)
                biome.SaltAbundance[i] = biome.SaltEffect[i];
            }
        }

        /// <summary>
        /// Compute stone abundance from rock type and slope (exposed rock = quarry sites).
        /// </summary>
        public static void ComputeStoneResource(BiomeData biome, HeightGrid heights)
        {
            for (int i = 0; i < biome.Mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;

                // Stone from hard rock types or thin soil exposing bedrock
                float rockBonus = 0f;
                switch (biome.Rock[i])
                {
                    case RockType.Granite:   rockBonus = 0.7f; break;
                    case RockType.Limestone:  rockBonus = 0.5f; break;
                    case RockType.Volcanic:   rockBonus = 0.3f; break;
                    case RockType.Sedimentary: rockBonus = 0.1f; break;
                }

                // Lithosol (thin/rocky soil) exposes more bedrock
                if (biome.Soil[i] == SoilType.Lithosol)
                    rockBonus += 0.3f;

                // Slope boosts exposure (steep terrain = exposed rock faces)
                float slopeBonus = biome.Slope[i] * 0.4f;

                biome.StoneAbundance[i] = Clamp01(rockBonus + slopeBonus);
            }
        }
    }
}
