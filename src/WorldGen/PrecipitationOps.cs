using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Computes steady-state humidity and normalized precipitation on the coarse sphere mesh.
    /// </summary>
    public static class PrecipitationOps
    {
        const float SeaLevel = 0.5f;
        const float PermafrostHumidityCap = 0.1f;
        const float ConvergenceThreshold = 0.001f;
        const int MaxPasses = 50;
        const double NormalizationExponent = 0.225;

        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;
            if (tectonics.CellWind == null || tectonics.CellWindSpeed == null)
                throw new InvalidOperationException("Wind must be generated before precipitation.");

            var isOcean = new bool[cellCount];
            var elevationKm = new float[cellCount];
            var tempC = new float[cellCount];
            var moistureCapacity = new float[cellCount];
            var incomingDirs = new Vec3[cellCount][];
            var oroFactor = new float[cellCount];

            PrecomputeCellFields(mesh, tectonics, config, isOcean, elevationKm, tempC, moistureCapacity);
            PrecomputeIncomingDirections(mesh, incomingDirs);
            PrecomputeOrographicFactors(mesh, tectonics, incomingDirs, elevationKm, isOcean, oroFactor);

            float[] prev = new float[cellCount];
            float[] next = new float[cellCount];

            for (int c = 0; c < cellCount; c++)
            {
                prev[c] = isOcean[c] ? Math.Min(config.OceanBaseHumidity, moistureCapacity[c]) : 0f;
            }

            int passCount = 0;
            for (; passCount < MaxPasses; passCount++)
            {
                float maxDelta = 0f;
                for (int c = 0; c < cellCount; c++)
                {
                    float nextValue;
                    if (isOcean[c])
                    {
                        nextValue = prev[c];
                    }
                    else
                    {
                        float gathered = GatherHumidity(c, mesh, tectonics, prev, incomingDirs);
                        float baseLoss = config.BasePrecipitationRate * gathered;
                        float oroLoss = config.OrographicScale * oroFactor[c] * gathered;
                        nextValue = Math.Clamp(gathered - baseLoss - oroLoss, 0f, moistureCapacity[c]);
                        if (tempC[c] < config.PermafrostThresholdC && nextValue > PermafrostHumidityCap)
                            nextValue = PermafrostHumidityCap;
                    }

                    next[c] = nextValue;
                    float delta = Math.Abs(nextValue - prev[c]);
                    if (delta > maxDelta)
                        maxDelta = delta;
                }

                var tmp = prev;
                prev = next;
                next = tmp;

                if (maxDelta < ConvergenceThreshold)
                {
                    passCount++;
                    break;
                }
            }

            var precipitation = new float[cellCount];
            float maxPrecip = 0f;
            float totalHumidity = 0f;

            for (int c = 0; c < cellCount; c++)
            {
                totalHumidity += prev[c];
                if (isOcean[c])
                    continue;

                float gathered = GatherHumidity(c, mesh, tectonics, prev, incomingDirs);
                float baseLoss = config.BasePrecipitationRate * gathered;
                float oroLoss = config.OrographicScale * oroFactor[c] * gathered;
                float remainder = gathered - baseLoss - oroLoss;
                float capExcess = Math.Max(0f, remainder - moistureCapacity[c]);
                float afterCap = remainder - capExcess;
                float permafrostLoss = tempC[c] < config.PermafrostThresholdC
                    ? Math.Max(0f, afterCap - PermafrostHumidityCap)
                    : 0f;
                float precip = baseLoss + oroLoss + capExcess + permafrostLoss;
                precipitation[c] = precip;
                if (precip > maxPrecip)
                    maxPrecip = precip;
            }

            if (maxPrecip > 1e-6f)
            {
                float normMax = (float)Math.Pow(maxPrecip, NormalizationExponent);
                for (int c = 0; c < cellCount; c++)
                {
                    if (precipitation[c] <= 0f)
                        continue;
                    precipitation[c] = (float)Math.Pow(precipitation[c], NormalizationExponent) / normMax;
                }
            }

            tectonics.CellHumidity = prev;
            tectonics.CellPrecipitation = precipitation;

            float avgHumidity = cellCount > 0 ? totalHumidity / cellCount : 0f;
            Console.WriteLine($"    Precipitation: {passCount} passes, avg humidity {avgHumidity:F2}, max precip {maxPrecip:F2}");
        }

        static void PrecomputeCellFields(
            SphereMesh mesh,
            TectonicData tectonics,
            WorldGenConfig config,
            bool[] isOcean,
            float[] elevationKm,
            float[] tempC,
            float[] moistureCapacity)
        {
            for (int c = 0; c < mesh.CellCount; c++)
            {
                float elevation = tectonics.CellElevation[c];
                bool ocean = elevation <= SeaLevel;
                isOcean[c] = ocean;

                float elevKm = 0f;
                if (!ocean)
                    elevKm = ((elevation - SeaLevel) / (1f - SeaLevel)) * config.MaxLandElevationKm;
                elevationKm[c] = elevKm;

                Vec3 normal = mesh.CellCenters[c].Normalized;
                float latDeg = (float)(Math.Asin(Math.Clamp(normal.Y, -1f, 1f)) * 180.0 / Math.PI);
                float seaLevelTemp = config.EquatorTempC
                    - (config.EquatorTempC - config.PoleTempC) * Math.Abs(latDeg) / 90f;
                float temperature = seaLevelTemp - config.LapseRateCPerKm * elevKm;
                tempC[c] = temperature;

                float cap = (float)Math.Pow(2.0, temperature / 10.0);
                moistureCapacity[c] = Math.Clamp(cap, 0.05f, 4f);
            }
        }

        static void PrecomputeIncomingDirections(SphereMesh mesh, Vec3[][] incomingDirs)
        {
            for (int c = 0; c < mesh.CellCount; c++)
            {
                int[] neighbors = mesh.CellNeighbors[c];
                var dirs = new Vec3[neighbors.Length];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    Vec3 normalNb = mesh.CellCenters[nb].Normalized;
                    Vec3 chord = mesh.CellCenters[c] - mesh.CellCenters[nb];
                    Vec3 tangent = chord - normalNb * Vec3.Dot(chord, normalNb);
                    dirs[i] = tangent.Magnitude > 1e-6f ? tangent.Normalized : new Vec3(0f, 0f, 0f);
                }
                incomingDirs[c] = dirs;
            }
        }

        static void PrecomputeOrographicFactors(
            SphereMesh mesh,
            TectonicData tectonics,
            Vec3[][] incomingDirs,
            float[] elevationKm,
            bool[] isOcean,
            float[] oroFactor)
        {
            for (int c = 0; c < mesh.CellCount; c++)
            {
                if (isOcean[c])
                    continue;

                int[] neighbors = mesh.CellNeighbors[c];
                float best = 0f;
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    float alignment = Vec3.Dot(incomingDirs[c][i], tectonics.CellWind[nb].Normalized);
                    if (alignment <= 0f)
                        continue;

                    float riseKm = Math.Max(0f, elevationKm[c] - elevationKm[nb]);
                    if (riseKm <= 0f)
                        continue;

                    float dot = Math.Clamp(Vec3.Dot(mesh.CellCenters[c].Normalized, mesh.CellCenters[nb].Normalized), -1f, 1f);
                    float distanceKm = (float)Math.Acos(dot) * mesh.Radius;
                    if (distanceKm <= 1e-6f)
                        continue;

                    float slope = riseKm / distanceKm;
                    if (slope > best)
                        best = slope;
                }

                oroFactor[c] = best;
            }
        }

        static float GatherHumidity(int cell, SphereMesh mesh, TectonicData tectonics, float[] humidity, Vec3[][] incomingDirs)
        {
            int[] neighbors = mesh.CellNeighbors[cell];
            float gathered = 0f;
            float totalAlignment = 0f;

            for (int i = 0; i < neighbors.Length; i++)
            {
                int nb = neighbors[i];
                float alignment = Vec3.Dot(incomingDirs[cell][i], tectonics.CellWind[nb].Normalized);
                if (alignment <= 0f)
                    continue;

                float alignmentWeight = alignment * alignment;
                if (alignmentWeight <= 1e-6f)
                    continue;

                float speed = tectonics.CellWindSpeed[nb];
                if (speed <= 1e-6f)
                    continue;

                // Normalize by directional support so mesh valence does not dominate,
                // but keep wind speed as an absolute attenuation term on transport.
                gathered += humidity[nb] * alignmentWeight * speed;
                totalAlignment += alignmentWeight;
            }

            return totalAlignment > 1e-6f ? gathered / totalAlignment : 0f;
        }
    }
}
