using System;

namespace MapGen.Core
{
    /// <summary>
    /// Precipitation via wind sweep: sort cells by projection onto wind vector,
    /// propagate humidity upwind-to-downwind through neighbor graph.
    /// </summary>
    public static class PrecipitationOps
    {
        const float OceanHumidity = 0.9f;
        const float BasePrecipLoss = 0.025f;
        const float CoastalBonus = 0.05f;
        const float WaterPickupRate = 0.08f;
        const float PermafrostThreshold = -5f;
        const float PermafrostDamping = 0.1f;
        const float OrographicScale = 0.25f; // multiplier for orographic lift

        /// <summary>
        /// Compute precipitation for all cells using wind sweep across all bands.
        /// </summary>
        public static void Compute(ClimateData climate, HeightGrid heights, WorldConfig config)
        {
            var mesh = climate.Mesh;
            int n = mesh.CellCount;

            // Accumulator for precipitation across all wind bands
            float[] totalPrecip = new float[n];

            for (int b = 0; b < config.WindBands.Length; b++)
            {
                var band = config.WindBands[b];

                // Determine overlap of this wind band with the map's latitude range
                float overlapMin = Math.Max(band.LatMin, config.LatitudeSouth);
                float overlapMax = Math.Min(band.LatMax, config.LatitudeNorth);
                if (overlapMin >= overlapMax) continue;

                float overlapFraction = (overlapMax - overlapMin) / (config.LatitudeNorth - config.LatitudeSouth);

                Vec2 windDir = band.WindVector;
                float[] bandPrecip = SweepBand(mesh, heights, climate, windDir, band, config);

                for (int i = 0; i < n; i++)
                    totalPrecip[i] += bandPrecip[i] * overlapFraction;
            }

            // Normalize to 0-100 with fourth root to spread the skewed distribution
            float maxVal = 0f;
            for (int i = 0; i < n; i++)
                if (totalPrecip[i] > maxVal) maxVal = totalPrecip[i];

            if (maxVal > 1e-6f)
            {
                const double exp = 0.225;
                float normMax = (float)Math.Pow(maxVal, exp);
                for (int i = 0; i < n; i++)
                    climate.Precipitation[i] = ((float)Math.Pow(totalPrecip[i], exp) / normMax) * 100f;
            }
        }

        /// <summary>
        /// Run one wind band sweep: sort by projection, propagate humidity, deposit rain.
        /// </summary>
        static float[] SweepBand(CellMesh mesh, HeightGrid heights, ClimateData climate,
            Vec2 windDir, WindBand band, WorldConfig config)
        {
            int n = mesh.CellCount;
            float[] humidity = new float[n];
            float[] precip = new float[n];
            bool[] visited = new bool[n];

            // Project cells onto wind direction
            float[] windPos = new float[n];
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                windPos[i] = Vec2.Dot(mesh.CellCenters[i], windDir);
                order[i] = i;
            }

            // Sort upwind-first (ascending projection)
            Array.Sort(order, (a, b) => windPos[a].CompareTo(windPos[b]));

            // Pre-check which cells are adjacent to water
            bool[] coastalCell = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (heights.IsLand(i))
                {
                    foreach (int nb in mesh.CellNeighbors[i])
                    {
                        if (heights.IsWater(nb))
                        {
                            coastalCell[i] = true;
                            break;
                        }
                    }
                }
            }

            // Sweep cells in upwind-to-downwind order
            for (int idx = 0; idx < n; idx++)
            {
                int i = order[idx];
                float cap = MoistureCapacity(climate.Temperature[i]);

                // Gather humidity from upwind neighbors
                float gatheredHumidity = 0f;
                float totalWeight = 0f;
                bool hasUpwindNeighbor = false;

                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (!visited[nb]) continue;

                    // Direction from neighbor to this cell
                    Vec2 toCell = mesh.CellCenters[i] - mesh.CellCenters[nb];
                    float alignment = Vec2.Dot(toCell.Normalized, windDir);
                    if (alignment <= 0f) continue; // not upwind

                    float weight = alignment * alignment;
                    gatheredHumidity += humidity[nb] * weight;
                    totalWeight += weight;
                    hasUpwindNeighbor = true;
                }

                if (hasUpwindNeighbor)
                {
                    humidity[i] = gatheredHumidity / totalWeight;
                }
                else
                {
                    // Boundary cell on windward edge — seed with ocean humidity
                    humidity[i] = OceanHumidity * cap;
                }

                // Process the cell
                if (heights.IsWater(i))
                {
                    // Water: pick up moisture
                    humidity[i] += WaterPickupRate * cap;
                }
                else
                {
                    // Land: deposit precipitation
                    float baseLoss = humidity[i] * BasePrecipLoss;
                    float deposit = baseLoss;

                    // Coastal bonus
                    if (coastalCell[i])
                        deposit += CoastalBonus * humidity[i];

                    // Orographic effect: slope-driven, linear height scaling, capped
                    float maxUphill = 0f;
                    foreach (int nb in mesh.CellNeighbors[i])
                    {
                        float dh = heights.Heights[i] - heights.Heights[nb];
                        if (dh > maxUphill) maxUphill = dh;
                    }
                    if (maxUphill > 0f)
                    {
                        float slope = Math.Min(maxUphill, 20f) / 20f; // cap slope contribution
                        float altFactor = (heights.Heights[i] - HeightGrid.SeaLevel) /
                                          (HeightGrid.MaxHeight - HeightGrid.SeaLevel);
                        if (altFactor < 0f) altFactor = 0f;
                        deposit += humidity[i] * OrographicScale * slope * (0.5f + altFactor);
                    }

                    // Cap deposit at available humidity
                    if (deposit > humidity[i]) deposit = humidity[i];
                    precip[i] = deposit;
                    humidity[i] -= deposit;
                }

                // Cap humidity at temperature-scaled capacity
                if (humidity[i] > cap)
                    humidity[i] = cap;

                // Permafrost damping
                if (climate.Temperature[i] < PermafrostThreshold)
                    humidity[i] *= PermafrostDamping;

                visited[i] = true;
            }

            return precip;
        }

        /// <summary>
        /// Temperature-scaled moisture capacity: warm air holds more moisture.
        /// Clausius-Clapeyron approximation: capacity ~ 2^(temp/10), clamped [0.05, 4.0].
        /// </summary>
        static float MoistureCapacity(float tempC)
        {
            float raw = (float)Math.Pow(2.0, tempC / 10.0);
            return raw < 0.05f ? 0.05f : (raw > 4f ? 4f : raw);
        }
    }
}
