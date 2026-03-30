using System;
using System.Collections.Generic;

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
        const float DryThreshold = 0.20f;
        const float WetThreshold = 0.60f;
        const int InlandThresholdHops = 3;
        static readonly (string Label, int Min, int Max)[] CoastBands = new[]
        {
            ("coast", 1, 1),
            ("near", 2, 3),
            ("mid", 4, 6),
            ("deep", 7, int.MaxValue),
        };

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
                float transportHumidity = GatherHumidity(c, mesh, tectonics, prev, incomingDirs);
                float availableHumidity = isOcean[c]
                    ? Math.Max(prev[c], transportHumidity)
                    : transportHumidity;

                float baseLoss = config.BasePrecipitationRate * availableHumidity;
                float oroLoss = isOcean[c] ? 0f : config.OrographicScale * oroFactor[c] * availableHumidity;
                float remainder = availableHumidity - baseLoss - oroLoss;
                float capExcess = isOcean[c] ? 0f : Math.Max(0f, remainder - moistureCapacity[c]);
                float afterCap = remainder - capExcess;
                float permafrostLoss = !isOcean[c] && tempC[c] < config.PermafrostThresholdC
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
            EmitDiagnostics(mesh, isOcean, prev, precipitation, passCount, avgHumidity, maxPrecip);
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

        static void EmitDiagnostics(
            SphereMesh mesh,
            bool[] isOcean,
            float[] humidity,
            float[] precipitation,
            int passCount,
            float avgHumidity,
            float maxPrecip)
        {
            int[] coastDist = ComputeCoastDistance(mesh, isOcean);

            int landCount = 0;
            int oceanCount = 0;
            int dryCount = 0;
            int wetCount = 0;
            int coastalCount = 0;
            int inlandCount = 0;
            float totalLandArea = 0f;
            float totalOceanArea = 0f;
            float dryArea = 0f;
            float wetArea = 0f;
            float landHumiditySum = 0f;
            float landPrecipSum = 0f;
            float oceanHumiditySum = 0f;
            float oceanPrecipSum = 0f;
            float coastalPrecipSum = 0f;
            float inlandPrecipSum = 0f;
            float weightedLandPrecipSum = 0f;
            float weightedLandHumiditySum = 0f;
            float weightedOceanHumiditySum = 0f;
            float weightedOceanPrecipSum = 0f;
            float weightedCoastalPrecipSum = 0f;
            float weightedInlandPrecipSum = 0f;
            float coastalArea = 0f;
            float inlandArea = 0f;
            float totalArea = 0f;
            float weightedTotalPrecipSum = 0f;
            var bandArea = new float[CoastBands.Length];
            var bandPrecip = new float[CoastBands.Length];
            var bandCount = new int[CoastBands.Length];

            for (int c = 0; c < mesh.CellCount; c++)
            {
                float precip = precipitation[c];
                float area = mesh.CellAreas != null && c < mesh.CellAreas.Length ? mesh.CellAreas[c] : 1f;
                totalArea += area;
                weightedTotalPrecipSum += precip * area;

                if (isOcean[c])
                {
                    oceanCount++;
                    totalOceanArea += area;
                    oceanHumiditySum += humidity[c];
                    oceanPrecipSum += precip;
                    weightedOceanHumiditySum += humidity[c] * area;
                    weightedOceanPrecipSum += precip * area;
                    continue;
                }

                landCount++;
                totalLandArea += area;
                landHumiditySum += humidity[c];
                landPrecipSum += precip;
                weightedLandHumiditySum += humidity[c] * area;
                weightedLandPrecipSum += precip * area;

                if (precip <= DryThreshold)
                {
                    dryCount++;
                    dryArea += area;
                }
                if (precip >= WetThreshold)
                {
                    wetCount++;
                    wetArea += area;
                }

                if (coastDist[c] == 1)
                {
                    coastalCount++;
                    coastalPrecipSum += precip;
                    coastalArea += area;
                    weightedCoastalPrecipSum += precip * area;
                }
                else if (coastDist[c] >= InlandThresholdHops)
                {
                    inlandCount++;
                    inlandPrecipSum += precip;
                    inlandArea += area;
                    weightedInlandPrecipSum += precip * area;
                }

                for (int b = 0; b < CoastBands.Length; b++)
                {
                    var band = CoastBands[b];
                    if (coastDist[c] < band.Min || coastDist[c] > band.Max)
                        continue;
                    bandArea[b] += area;
                    bandPrecip[b] += precip * area;
                    bandCount[b]++;
                    break;
                }
            }

            float avgOceanHumidity = oceanCount > 0 ? oceanHumiditySum / oceanCount : 0f;
            float avgOceanPrecip = oceanCount > 0 ? oceanPrecipSum / oceanCount : 0f;
            float avgLandHumidity = landCount > 0 ? landHumiditySum / landCount : 0f;
            float avgLandPrecip = landCount > 0 ? landPrecipSum / landCount : 0f;
            float weightedAvgPrecip = totalArea > 0f ? weightedTotalPrecipSum / totalArea : 0f;
            float dryFraction = landCount > 0 ? (float)dryCount / landCount : 0f;
            float wetFraction = landCount > 0 ? (float)wetCount / landCount : 0f;
            float coastalAvg = coastalCount > 0 ? coastalPrecipSum / coastalCount : 0f;
            float inlandAvg = inlandCount > 0 ? inlandPrecipSum / inlandCount : 0f;
            float weightedOceanPrecip = totalOceanArea > 0f ? weightedOceanPrecipSum / totalOceanArea : 0f;
            float weightedOceanHumidity = totalOceanArea > 0f ? weightedOceanHumiditySum / totalOceanArea : 0f;
            float weightedLandHumidity = totalLandArea > 0f ? weightedLandHumiditySum / totalLandArea : 0f;
            float weightedLandPrecip = totalLandArea > 0f ? weightedLandPrecipSum / totalLandArea : 0f;
            float weightedDryFraction = totalLandArea > 0f ? dryArea / totalLandArea : 0f;
            float weightedWetFraction = totalLandArea > 0f ? wetArea / totalLandArea : 0f;
            float weightedCoastalAvg = coastalArea > 0f ? weightedCoastalPrecipSum / coastalArea : 0f;
            float weightedInlandAvg = inlandArea > 0f ? weightedInlandPrecipSum / inlandArea : 0f;

            Console.WriteLine($"    Precipitation: {passCount} passes, avg humidity {avgHumidity:F2}, avg precip {weightedAvgPrecip:F2}, avg land humidity {avgLandHumidity:F2}, avg land precip {avgLandPrecip:F2}, avg ocean humidity {avgOceanHumidity:F2}, avg ocean precip {avgOceanPrecip:F2}, max precip {maxPrecip:F2}");
            Console.WriteLine($"      Land climate: dry<{DryThreshold:F2} {dryFraction:P0}, wet>{WetThreshold:F2} {wetFraction:P0}, coastal avg {coastalAvg:F2}, inland avg {inlandAvg:F2}");
            Console.WriteLine($"      Area-weighted: ocean humidity {weightedOceanHumidity:F2}, ocean precip {weightedOceanPrecip:F2}, land humidity {weightedLandHumidity:F2}, land precip {weightedLandPrecip:F2}, dry {weightedDryFraction:P0}, wet {weightedWetFraction:P0}, coastal {weightedCoastalAvg:F2}, inland {weightedInlandAvg:F2}");

            for (int b = 0; b < CoastBands.Length; b++)
            {
                float areaFraction = totalLandArea > 0f ? bandArea[b] / totalLandArea : 0f;
                float bandAvg = bandArea[b] > 0f ? bandPrecip[b] / bandArea[b] : 0f;
                Console.WriteLine($"      Coast band {CoastBands[b].Label,-5}: area {areaFraction:P0}, avg precip {bandAvg:F2}, cells {bandCount[b]}");
            }
        }

        static int[] ComputeCoastDistance(SphereMesh mesh, bool[] isOcean)
        {
            int cellCount = mesh.CellCount;
            var dist = new int[cellCount];
            Array.Fill(dist, -1);
            var queue = new Queue<int>();

            for (int c = 0; c < cellCount; c++)
            {
                if (!isOcean[c])
                    continue;
                dist[c] = 0;
                queue.Enqueue(c);
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int nextDist = dist[cell] + 1;
                int[] neighbors = mesh.CellNeighbors[cell];
                for (int i = 0; i < neighbors.Length; i++)
                {
                    int nb = neighbors[i];
                    if (dist[nb] >= 0)
                        continue;
                    dist[nb] = nextDist;
                    queue.Enqueue(nb);
                }
            }

            return dist;
        }
    }
}
