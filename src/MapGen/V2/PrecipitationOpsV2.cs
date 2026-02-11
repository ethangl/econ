using System;

namespace MapGen.Core
{
    /// <summary>
    /// Precipitation via wind-band sweeps in V2, emitted as mm/year.
    /// </summary>
    public static class PrecipitationOpsV2
    {
        const float OceanHumidity = 0.9f;
        const float BasePrecipLoss = 0.025f;
        const float CoastalBonus = 0.05f;
        const float WaterPickupRate = 0.08f;
        const float PermafrostThreshold = -5f;
        const float PermafrostDamping = 0.1f;
        const float OrographicScale = 0.25f;

        public static void Compute(ClimateFieldV2 climate, ElevationFieldV2 elevation, MapGenV2Config config, WorldMetadata world)
        {
            var mesh = climate.Mesh;
            int n = mesh.CellCount;
            float[] totalPrecip = new float[n];

            for (int b = 0; b < config.WindBands.Length; b++)
            {
                WindBand band = config.WindBands[b];
                float overlapMin = Math.Max(band.LatMin, world.LatitudeSouth);
                float overlapMax = Math.Min(band.LatMax, world.LatitudeNorth);
                if (overlapMin >= overlapMax)
                    continue;

                float overlapFraction = (overlapMax - overlapMin) / (world.LatitudeNorth - world.LatitudeSouth);
                Vec2 windDir = band.WindVector;
                float[] bandPrecip = SweepBand(mesh, elevation, climate, windDir, config);

                for (int i = 0; i < n; i++)
                    totalPrecip[i] += bandPrecip[i] * overlapFraction;
            }

            float maxVal = 0f;
            for (int i = 0; i < n; i++)
            {
                if (totalPrecip[i] > maxVal)
                    maxVal = totalPrecip[i];
            }

            if (maxVal <= 1e-6f)
                return;

            const double exp = 0.225;
            float normMax = (float)Math.Pow(maxVal, exp);
            for (int i = 0; i < n; i++)
            {
                float norm = (float)Math.Pow(totalPrecip[i], exp) / normMax;
                climate.PrecipitationMmYear[i] = norm * config.MaxAnnualPrecipitationMm;
            }
        }

        static float[] SweepBand(CellMesh mesh, ElevationFieldV2 elevation, ClimateFieldV2 climate, Vec2 windDir, MapGenV2Config config)
        {
            int n = mesh.CellCount;
            float[] humidity = new float[n];
            float[] precip = new float[n];
            bool[] visited = new bool[n];

            float[] windPos = new float[n];
            int[] order = new int[n];
            for (int i = 0; i < n; i++)
            {
                windPos[i] = Vec2.Dot(mesh.CellCenters[i], windDir);
                order[i] = i;
            }

            Array.Sort(order, (a, b) => windPos[a].CompareTo(windPos[b]));

            bool[] coastal = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (!elevation.IsLand(i))
                    continue;

                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (nb >= 0 && nb < n && elevation.IsWater(nb))
                    {
                        coastal[i] = true;
                        break;
                    }
                }
            }

            for (int idx = 0; idx < n; idx++)
            {
                int i = order[idx];
                float cap = MoistureCapacity(climate.TemperatureC[i]);

                float gatheredHumidity = 0f;
                float totalWeight = 0f;
                bool hasUpwind = false;

                foreach (int nb in mesh.CellNeighbors[i])
                {
                    if (nb < 0 || nb >= n || !visited[nb])
                        continue;

                    Vec2 toCell = mesh.CellCenters[i] - mesh.CellCenters[nb];
                    float alignment = Vec2.Dot(toCell.Normalized, windDir);
                    if (alignment <= 0f)
                        continue;

                    float weight = alignment * alignment;
                    gatheredHumidity += humidity[nb] * weight;
                    totalWeight += weight;
                    hasUpwind = true;
                }

                humidity[i] = hasUpwind ? gatheredHumidity / totalWeight : OceanHumidity * cap;

                if (elevation.IsWater(i))
                {
                    humidity[i] += WaterPickupRate * cap;
                }
                else
                {
                    float baseLoss = humidity[i] * BasePrecipLoss;
                    float deposit = baseLoss;

                    if (coastal[i])
                        deposit += CoastalBonus * humidity[i];

                    float maxUphillMeters = 0f;
                    foreach (int nb in mesh.CellNeighbors[i])
                    {
                        if (nb < 0 || nb >= n)
                            continue;

                        float dh = elevation[i] - elevation[nb];
                        if (dh > maxUphillMeters)
                            maxUphillMeters = dh;
                    }

                    if (maxUphillMeters > 0f)
                    {
                        float slope = Math.Min(maxUphillMeters, 1000f) / 1000f;
                        float altFactor = elevation[i] > 0f
                            ? Math.Min(1f, elevation[i] / Math.Max(1f, config.MaxElevationMeters))
                            : 0f;
                        deposit += humidity[i] * OrographicScale * slope * (0.5f + altFactor);
                    }

                    if (deposit > humidity[i])
                        deposit = humidity[i];

                    precip[i] = deposit;
                    humidity[i] -= deposit;
                }

                if (humidity[i] > cap)
                    humidity[i] = cap;

                if (climate.TemperatureC[i] < PermafrostThreshold)
                    humidity[i] *= PermafrostDamping;

                visited[i] = true;
            }

            return precip;
        }

        static float MoistureCapacity(float tempC)
        {
            float raw = (float)Math.Pow(2.0, tempC / 10.0);
            if (raw < 0.05f) return 0.05f;
            if (raw > 4f) return 4f;
            return raw;
        }
    }
}
