using System.Collections.Generic;
using System.IO;
using System;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Import;
using EconSim.Renderer;
using MapGen.Core;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("M3Regression")]
    public class MapGenRegressionTests
    {
        private readonly struct Snapshot
        {
            public readonly int CellCount;
            public readonly int LandCellCount;
            public readonly int RealmCount;
            public readonly int ProvinceCount;
            public readonly int CountyCount;
            public readonly int RiverCount;
            public readonly int BiomeDefCount;
            public readonly float LandRatio;

            public Snapshot(int cellCount, int landCellCount, int realmCount, int provinceCount, int countyCount, int riverCount, int biomeDefCount)
            {
                CellCount = cellCount;
                LandCellCount = landCellCount;
                RealmCount = realmCount;
                ProvinceCount = provinceCount;
                CountyCount = countyCount;
                RiverCount = riverCount;
                BiomeDefCount = biomeDefCount;
                LandRatio = cellCount > 0 ? (float)landCellCount / cellCount : 0f;
            }
        }

        private readonly struct DistributionMetrics
        {
            public readonly float LandRatio;
            public readonly float WaterRatio;
            public readonly float RiverCellRatio;
            public readonly float ElevationP10;
            public readonly float ElevationP50;
            public readonly float ElevationP90;

            public DistributionMetrics(
                float landRatio,
                float waterRatio,
                float riverCellRatio,
                float elevationP10,
                float elevationP50,
                float elevationP90)
            {
                LandRatio = landRatio;
                WaterRatio = waterRatio;
                RiverCellRatio = riverCellRatio;
                ElevationP10 = elevationP10;
                ElevationP50 = elevationP50;
                ElevationP90 = elevationP90;
            }
        }

        private const string BaselineFileRelativePath = "Tests/EditMode/MapGenRegressionBaselines.json";

        [Test]
        public void Pipeline_IsDeterministic_ForFixedSeedAndTemplate()
        {
            var baselines = LoadBaselines();
            foreach (var baseline in baselines)
            {
                var runA = RunSnapshot(baseline.Seed, baseline.Template);
                var runB = RunSnapshot(baseline.Seed, baseline.Template);

                AssertSnapshotsEqual(runA, runB, baseline.Seed, baseline.Template);
            }
        }

        [Test]
        public void Pipeline_Output_RespectsBasicInvariants()
        {
            var baselines = LoadBaselines();
            foreach (var baseline in baselines)
            {
                var config = CreateConfig(baseline.Seed, baseline.Template);
                var result = MapGenPipeline.Generate(config);
                var mapData = MapGenAdapter.Convert(result);

                // MapGen CellCount is a target. Final Voronoi count may vary slightly.
                int generatedCellCount = result.Mesh.CellCount;
                int requestedMin = (int)(config.CellCount * 0.95f);
                int requestedMax = (int)(config.CellCount * 1.05f);

                Assert.That(generatedCellCount, Is.InRange(requestedMin, requestedMax), "Generated cell count out of request tolerance");
                Assert.That(mapData.Info.TotalCells, Is.EqualTo(generatedCellCount), "TotalCells mismatch vs generated mesh");
                Assert.That(mapData.Cells.Count, Is.EqualTo(generatedCellCount), "Cells count mismatch vs generated mesh");

                Assert.That(mapData.LandCellCount(), Is.EqualTo(mapData.Info.LandCells), "LandCells metadata mismatch");
                Assert.That(mapData.Info.LandCells, Is.GreaterThan(0), "Map has no land cells");
                Assert.That(mapData.Info.LandCells, Is.LessThan(mapData.Info.TotalCells), "Map is all land");

                Assert.That(mapData.CellById, Is.Not.Null);
                Assert.That(mapData.ProvinceById, Is.Not.Null);
                Assert.That(mapData.RealmById, Is.Not.Null);
                Assert.That(mapData.CountyById, Is.Not.Null);

                Assert.That(mapData.CellById.Count, Is.EqualTo(mapData.Cells.Count), "CellById count mismatch");
                Assert.That(mapData.ProvinceById.Count, Is.EqualTo(mapData.Provinces.Count), "ProvinceById count mismatch");
                Assert.That(mapData.RealmById.Count, Is.EqualTo(mapData.Realms.Count), "RealmById count mismatch");
                Assert.That(mapData.CountyById.Count, Is.EqualTo(mapData.Counties.Count), "CountyById count mismatch");

                Assert.That(mapData.Realms.Count, Is.InRange(baseline.RealmCountMin, baseline.RealmCountMax), "Realm count out of baseline band");
                Assert.That(mapData.Provinces.Count, Is.InRange(baseline.ProvinceCountMin, baseline.ProvinceCountMax), "Province count out of baseline band");
                Assert.That(mapData.Counties.Count, Is.InRange(baseline.CountyCountMin, baseline.CountyCountMax), "County count out of baseline band");
                Assert.That(mapData.Rivers.Count, Is.InRange(baseline.RiverCountMin, baseline.RiverCountMax), "River count out of baseline band");
                Assert.That(mapData.Biomes.Count, Is.EqualTo(baseline.BiomeDefCount), "Biome definition table size changed unexpectedly");

                float landRatio = mapData.Info.LandCells / (float)mapData.Info.TotalCells;
                Assert.That(landRatio, Is.GreaterThanOrEqualTo(baseline.LandRatioMin).And.LessThanOrEqualTo(baseline.LandRatioMax),
                    $"Land ratio out of baseline band for seed={baseline.Seed}, template={baseline.Template}");
            }
        }

        [Test]
        public void Pipeline_Output_RespectsDistributionBaselines()
        {
            var baselines = LoadBaselines();
            var failures = new List<string>();

            foreach (var baseline in baselines)
            {
                var result = MapGenPipeline.Generate(CreateConfig(baseline.Seed, baseline.Template));
                var metrics = ComputeDistributionMetrics(result);
                string report = BuildDistributionDebugReport(baseline, metrics);
                TestContext.WriteLine(report);

                var caseFailures = new List<string>();
                CheckRange(caseFailures, "landRatio", metrics.LandRatio, baseline.LandRatioMin, baseline.LandRatioMax);
                CheckRange(caseFailures, "waterRatio", metrics.WaterRatio, baseline.WaterRatioMin, baseline.WaterRatioMax);
                CheckRange(caseFailures, "riverCellRatio", metrics.RiverCellRatio, baseline.RiverCellRatioMin, baseline.RiverCellRatioMax);
                CheckRange(caseFailures, "elevationP10", metrics.ElevationP10, baseline.ElevationP10Min, baseline.ElevationP10Max);
                CheckRange(caseFailures, "elevationP50", metrics.ElevationP50, baseline.ElevationP50Min, baseline.ElevationP50Max);
                CheckRange(caseFailures, "elevationP90", metrics.ElevationP90, baseline.ElevationP90Min, baseline.ElevationP90Max);

                if (caseFailures.Count > 0)
                {
                    failures.Add(
                        $"Distribution baseline mismatch (seed={baseline.Seed}, template={baseline.Template})\n" +
                        string.Join("\n", caseFailures) +
                        "\n" +
                        report);
                }
            }

            if (failures.Count > 0)
                Assert.Fail(string.Join("\n\n", failures));
        }

        [Test]
        public void Pipeline_EmitsWorldMetadata_AndAdapterPreservesIt()
        {
            var baselines = LoadBaselines();
            foreach (var baseline in baselines)
            {
                var config = CreateConfig(baseline.Seed, baseline.Template);
                var result = MapGenPipeline.Generate(config);
                Assert.That(result.World, Is.Not.Null, "MapGenResult.World metadata must be present.");

                var world = result.World;
                Assert.That(world.CellSizeKm, Is.GreaterThan(0f));
                Assert.That(world.MapWidthKm, Is.GreaterThan(0f));
                Assert.That(world.MapHeightKm, Is.GreaterThan(0f));
                Assert.That(world.MapAreaKm2, Is.GreaterThan(0f));
                Assert.That(world.LatitudeNorth, Is.GreaterThan(world.LatitudeSouth));
                Assert.That(world.MaxElevationMeters, Is.GreaterThan(0f));
                Assert.That(world.MaxSeaDepthMeters, Is.GreaterThan(0f));
                Assert.That(world.SeaLevelHeight, Is.GreaterThan(world.MinHeight).And.LessThan(world.MaxHeight));

                var mapData = MapGenAdapter.Convert(result);
                Assert.That(mapData.Info.World, Is.Not.Null, "MapInfo.World must be populated from mapgen metadata.");
                Assert.That(mapData.Info.SeaLevel, Is.EqualTo(world.SeaLevelHeight).Within(0.0001f),
                    "MapInfo.SeaLevel should be sourced from world metadata.");

                var infoWorld = mapData.Info.World;
                Assert.That(infoWorld.CellSizeKm, Is.EqualTo(world.CellSizeKm).Within(0.0001f));
                Assert.That(infoWorld.MapWidthKm, Is.EqualTo(world.MapWidthKm).Within(0.0001f));
                Assert.That(infoWorld.MapHeightKm, Is.EqualTo(world.MapHeightKm).Within(0.0001f));
                Assert.That(infoWorld.MapAreaKm2, Is.EqualTo(world.MapAreaKm2).Within(0.001f));
                Assert.That(infoWorld.LatitudeSouth, Is.EqualTo(world.LatitudeSouth).Within(0.0001f));
                Assert.That(infoWorld.LatitudeNorth, Is.EqualTo(world.LatitudeNorth).Within(0.0001f));
                Assert.That(infoWorld.MaxElevationMeters, Is.EqualTo(world.MaxElevationMeters).Within(0.0001f));
                Assert.That(infoWorld.MaxSeaDepthMeters, Is.EqualTo(world.MaxSeaDepthMeters).Within(0.0001f));
            }
        }

        private static MapGenConfig CreateConfig(int seed, HeightmapTemplateType template)
        {
            return new MapGenConfig
            {
                Seed = seed,
                Template = template,
                CellCount = 5000
            };
        }

        private static Snapshot RunSnapshot(int seed, HeightmapTemplateType template)
        {
            var result = MapGenPipeline.Generate(CreateConfig(seed, template));
            var mapData = MapGenAdapter.Convert(result);

            return new Snapshot(
                mapData.Cells.Count,
                mapData.Info.LandCells,
                mapData.Realms.Count,
                mapData.Provinces.Count,
                mapData.Counties.Count,
                mapData.Rivers.Count,
                mapData.Biomes.Count
            );
        }

        private static DistributionMetrics ComputeDistributionMetrics(MapGenResult result)
        {
            int cellCount = result.Mesh.CellCount;
            if (cellCount <= 0)
                return new DistributionMetrics(0f, 0f, 0f, 0f, 0f, 0f);

            int landCells = 0;
            for (int i = 0; i < cellCount; i++)
            {
                if (!result.Heights.IsWater(i) && !result.Biomes.IsLakeCell[i])
                    landCells++;
            }

            float landRatio = landCells / (float)cellCount;
            float waterRatio = 1f - landRatio;
            float riverCellRatio = ComputeRiverCellRatio(result);

            var sortedHeights = (float[])result.Heights.Heights.Clone();
            Array.Sort(sortedHeights);

            return new DistributionMetrics(
                landRatio,
                waterRatio,
                riverCellRatio,
                Percentile(sortedHeights, 0.10f),
                Percentile(sortedHeights, 0.50f),
                Percentile(sortedHeights, 0.90f));
        }

        private static float ComputeRiverCellRatio(MapGenResult result)
        {
            int cellCount = result.Mesh.CellCount;
            int vertexCount = result.Mesh.VertexCount;
            if (cellCount <= 0 || vertexCount <= 0)
                return 0f;

            bool[] riverVertices = new bool[vertexCount];
            var rivers = result.Rivers.Rivers;
            for (int i = 0; i < rivers.Length; i++)
            {
                int[] vertices = rivers[i].Vertices;
                if (vertices == null)
                    continue;

                for (int v = 0; v < vertices.Length; v++)
                {
                    int vertexId = vertices[v];
                    if (vertexId >= 0 && vertexId < vertexCount)
                        riverVertices[vertexId] = true;
                }
            }

            int riverCells = 0;
            for (int i = 0; i < cellCount; i++)
            {
                int[] cellVertices = result.Mesh.CellVertices[i];
                bool hasRiverVertex = false;
                for (int v = 0; v < cellVertices.Length; v++)
                {
                    int vertexId = cellVertices[v];
                    if (vertexId >= 0 && vertexId < vertexCount && riverVertices[vertexId])
                    {
                        hasRiverVertex = true;
                        break;
                    }
                }

                if (hasRiverVertex)
                    riverCells++;
            }

            return riverCells / (float)cellCount;
        }

        private static float Percentile(float[] sortedValues, float p)
        {
            if (sortedValues == null || sortedValues.Length == 0)
                return 0f;

            float clampedP = Mathf.Clamp01(p);
            float index = (sortedValues.Length - 1) * clampedP;
            int lo = (int)Math.Floor(index);
            int hi = (int)Math.Ceiling(index);
            if (lo == hi)
                return sortedValues[lo];

            float t = index - lo;
            return Mathf.Lerp(sortedValues[lo], sortedValues[hi], t);
        }

        private static void CheckRange(List<string> failures, string name, float actual, float min, float max)
        {
            if (actual < min || actual > max)
            {
                failures.Add(
                    $"{name} out of range: actual={actual:F4}, expected=[{min:F4}, {max:F4}], delta={DistanceToRange(actual, min, max):F4}");
            }
        }

        private static float DistanceToRange(float value, float min, float max)
        {
            if (value < min) return min - value;
            if (value > max) return value - max;
            return 0f;
        }

        private static string BuildDistributionDebugReport(BaselineCase baseline, DistributionMetrics metrics)
        {
            var sb = new StringBuilder();
            sb.Append("distribution seed=").Append(baseline.Seed)
                .Append(" template=").Append(baseline.Template)
                .Append(" cellCount=").Append(baseline.CellCount)
                .AppendLine();
            AppendMetric(sb, "landRatio", metrics.LandRatio, baseline.LandRatioMin, baseline.LandRatioMax);
            AppendMetric(sb, "waterRatio", metrics.WaterRatio, baseline.WaterRatioMin, baseline.WaterRatioMax);
            AppendMetric(sb, "riverCellRatio", metrics.RiverCellRatio, baseline.RiverCellRatioMin, baseline.RiverCellRatioMax);
            AppendMetric(sb, "elevationP10", metrics.ElevationP10, baseline.ElevationP10Min, baseline.ElevationP10Max);
            AppendMetric(sb, "elevationP50", metrics.ElevationP50, baseline.ElevationP50Min, baseline.ElevationP50Max);
            AppendMetric(sb, "elevationP90", metrics.ElevationP90, baseline.ElevationP90Min, baseline.ElevationP90Max);
            return sb.ToString();
        }

        private static void AppendMetric(StringBuilder sb, string name, float actual, float min, float max)
        {
            sb.Append("  ").Append(name)
                .Append(": actual=").Append(actual.ToString("F4"))
                .Append(" expected=[").Append(min.ToString("F4"))
                .Append(", ").Append(max.ToString("F4")).Append("]")
                .Append(" delta=").Append(DistanceToRange(actual, min, max).ToString("F4"))
                .AppendLine();
        }

        private static void AssertSnapshotsEqual(Snapshot a, Snapshot b, int seed, HeightmapTemplateType template)
        {
            string context = $"(seed={seed}, template={template})";
            Assert.That(a.CellCount, Is.EqualTo(b.CellCount), $"CellCount mismatch {context}");
            Assert.That(a.LandCellCount, Is.EqualTo(b.LandCellCount), $"LandCellCount mismatch {context}");
            Assert.That(a.RealmCount, Is.EqualTo(b.RealmCount), $"RealmCount mismatch {context}");
            Assert.That(a.ProvinceCount, Is.EqualTo(b.ProvinceCount), $"ProvinceCount mismatch {context}");
            Assert.That(a.CountyCount, Is.EqualTo(b.CountyCount), $"CountyCount mismatch {context}");
            Assert.That(a.RiverCount, Is.EqualTo(b.RiverCount), $"RiverCount mismatch {context}");
            Assert.That(a.BiomeDefCount, Is.EqualTo(b.BiomeDefCount), $"BiomeDefCount mismatch {context}");
            Assert.That(a.LandRatio, Is.EqualTo(b.LandRatio).Within(0.000001f), $"LandRatio mismatch {context}");
        }

        private static List<BaselineCase> LoadBaselines()
        {
            string fullPath = Path.Combine(Application.dataPath, BaselineFileRelativePath);
            Assert.That(File.Exists(fullPath), Is.True, $"Missing baseline file at: {fullPath}");

            string json = File.ReadAllText(fullPath);
            var root = JsonUtility.FromJson<BaselineFile>(json);
            Assert.That(root, Is.Not.Null, "Failed to parse baseline JSON");
            Assert.That(root.cases, Is.Not.Null, "Baseline JSON must contain 'cases' array");
            Assert.That(root.cases.Length, Is.GreaterThan(0), "Baseline JSON has no cases");

            var cases = new List<BaselineCase>(root.cases.Length);
            foreach (var item in root.cases)
            {
                cases.Add(BaselineCase.FromJson(item));
            }
            return cases;
        }
    }

    internal readonly struct BaselineCase
    {
        public readonly int Seed;
        public readonly HeightmapTemplateType Template;
        public readonly int CellCount;
        public readonly int BiomeDefCount;
        public readonly float LandRatioMin;
        public readonly float LandRatioMax;
        public readonly float WaterRatioMin;
        public readonly float WaterRatioMax;
        public readonly float RiverCellRatioMin;
        public readonly float RiverCellRatioMax;
        public readonly float ElevationP10Min;
        public readonly float ElevationP10Max;
        public readonly float ElevationP50Min;
        public readonly float ElevationP50Max;
        public readonly float ElevationP90Min;
        public readonly float ElevationP90Max;
        public readonly int RealmCountMin;
        public readonly int RealmCountMax;
        public readonly int ProvinceCountMin;
        public readonly int ProvinceCountMax;
        public readonly int CountyCountMin;
        public readonly int CountyCountMax;
        public readonly int RiverCountMin;
        public readonly int RiverCountMax;

        private BaselineCase(
            int seed,
            HeightmapTemplateType template,
            int cellCount,
            int biomeDefCount,
            float landRatioMin,
            float landRatioMax,
            float waterRatioMin,
            float waterRatioMax,
            float riverCellRatioMin,
            float riverCellRatioMax,
            float elevationP10Min,
            float elevationP10Max,
            float elevationP50Min,
            float elevationP50Max,
            float elevationP90Min,
            float elevationP90Max,
            int realmCountMin,
            int realmCountMax,
            int provinceCountMin,
            int provinceCountMax,
            int countyCountMin,
            int countyCountMax,
            int riverCountMin,
            int riverCountMax)
        {
            Seed = seed;
            Template = template;
            CellCount = cellCount;
            BiomeDefCount = biomeDefCount;
            LandRatioMin = landRatioMin;
            LandRatioMax = landRatioMax;
            WaterRatioMin = waterRatioMin;
            WaterRatioMax = waterRatioMax;
            RiverCellRatioMin = riverCellRatioMin;
            RiverCellRatioMax = riverCellRatioMax;
            ElevationP10Min = elevationP10Min;
            ElevationP10Max = elevationP10Max;
            ElevationP50Min = elevationP50Min;
            ElevationP50Max = elevationP50Max;
            ElevationP90Min = elevationP90Min;
            ElevationP90Max = elevationP90Max;
            RealmCountMin = realmCountMin;
            RealmCountMax = realmCountMax;
            ProvinceCountMin = provinceCountMin;
            ProvinceCountMax = provinceCountMax;
            CountyCountMin = countyCountMin;
            CountyCountMax = countyCountMax;
            RiverCountMin = riverCountMin;
            RiverCountMax = riverCountMax;
        }

        public static BaselineCase FromJson(BaselineJsonCase obj)
        {
            var landRatio = ResolveFloatRange(obj.landRatio, 0f, 1f);
            var waterRatio = ResolveFloatRange(obj.waterRatio, 1f - landRatio.max, 1f - landRatio.min);
            var riverCellRatio = ResolveFloatRange(obj.riverCellRatio, 0f, 1f);
            var elevationP10 = ResolveFloatRange(obj.elevationP10, 0f, 100f);
            var elevationP50 = ResolveFloatRange(obj.elevationP50, 0f, 100f);
            var elevationP90 = ResolveFloatRange(obj.elevationP90, 0f, 100f);

            return new BaselineCase(
                obj.seed,
                (HeightmapTemplateType)Enum.Parse(typeof(HeightmapTemplateType), obj.template),
                obj.cellCount,
                obj.biomeDefCount,
                landRatio.min,
                landRatio.max,
                waterRatio.min,
                waterRatio.max,
                riverCellRatio.min,
                riverCellRatio.max,
                elevationP10.min,
                elevationP10.max,
                elevationP50.min,
                elevationP50.max,
                elevationP90.min,
                elevationP90.max,
                obj.realmCount.min,
                obj.realmCount.max,
                obj.provinceCount.min,
                obj.provinceCount.max,
                obj.countyCount.min,
                obj.countyCount.max,
                obj.riverCount.min,
                obj.riverCount.max
            );
        }

        private static (float min, float max) ResolveFloatRange(BaselineRangeFloat range, float fallbackMin, float fallbackMax)
        {
            if (range == null)
                return (fallbackMin, fallbackMax);

            return (range.min, range.max);
        }
    }

    [Serializable]
    internal class BaselineFile
    {
        public BaselineJsonCase[] cases;
    }

    [Serializable]
    internal class BaselineJsonCase
    {
        public int seed;
        public string template;
        public int cellCount;
        public int biomeDefCount;
        public BaselineRangeFloat landRatio;
        public BaselineRangeFloat waterRatio;
        public BaselineRangeFloat riverCellRatio;
        public BaselineRangeFloat elevationP10;
        public BaselineRangeFloat elevationP50;
        public BaselineRangeFloat elevationP90;
        public BaselineRangeInt realmCount;
        public BaselineRangeInt provinceCount;
        public BaselineRangeInt countyCount;
        public BaselineRangeInt riverCount;
    }

    [Serializable]
    internal class BaselineRangeInt
    {
        public int min;
        public int max;
    }

    [Serializable]
    internal class BaselineRangeFloat
    {
        public float min;
        public float max;
    }

    internal static class MapDataTestExtensions
    {
        public static int LandCellCount(this EconSim.Core.Data.MapData mapData)
        {
            int count = 0;
            List<EconSim.Core.Data.Cell> cells = mapData.Cells;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsLand)
                    count++;
            }
            return count;
        }
    }

    [TestFixture]
    [Category("Elevation")]
    public class ElevationConsistencyTests
    {
        private static readonly Regex RawCellHeightPattern =
            new Regex(@"\b(?:cell|[A-Za-z_][A-Za-z0-9_]*Cell)\.Height\b", RegexOptions.Compiled);
        private static readonly Regex RawMapInfoSeaLevelPattern =
            new Regex(@"\b(?:[A-Za-z_][A-Za-z0-9_]*\.Info|[A-Za-z_][A-Za-z0-9_]*Info|info|Info)\.SeaLevel\b", RegexOptions.Compiled);

        [Test]
        public void ElevationHelpers_SupportCanonicalAndLegacyRoundTrip()
        {
            const float seaLevel = 20f;
            const float absolute = 42.75f;

            float seaRelative = Elevation.SeaRelativeFromAbsolute(absolute, seaLevel);
            float roundTrip = Elevation.AbsoluteFromSeaRelative(seaRelative, seaLevel);
            Assert.That(roundTrip, Is.EqualTo(absolute).Within(0.0001f));

            var info = new MapInfo { SeaLevel = seaLevel };

            var canonicalCell = new Cell
            {
                Height = 43,
                SeaRelativeElevation = 22.75f,
                HasSeaRelativeElevation = true
            };

            Assert.That(Elevation.GetSeaRelativeHeight(canonicalCell, info), Is.EqualTo(22.75f).Within(0.0001f));
            Assert.That(Elevation.GetAbsoluteHeight(canonicalCell, info), Is.EqualTo(42.75f).Within(0.0001f));

            var legacyCell = new Cell
            {
                Height = 12,
                HasSeaRelativeElevation = false
            };

            Assert.That(Elevation.GetSeaRelativeHeight(legacyCell, info), Is.EqualTo(-8f).Within(0.0001f));
            Assert.That(Elevation.GetAbsoluteHeight(legacyCell, info), Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void ElevationWorldUnitConversions_RoundTripAboveSeaLevelAndSigned()
        {
            var info = new MapInfo
            {
                SeaLevel = 20f,
                World = new WorldInfo
                {
                    MaxElevationMeters = 6000f,
                    MaxSeaDepthMeters = 1500f
                }
            };

            float seaAbsolute = Elevation.ResolveSeaLevel(info);
            Assert.That(Elevation.AbsoluteToMetersAboveSeaLevel(seaAbsolute, info), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Elevation.MetersAboveSeaLevelToAbsolute(0f, info), Is.EqualTo(seaAbsolute).Within(0.0001f));

            float peakMetersAboveSeaLevel = Elevation.AbsoluteToMetersAboveSeaLevel(Elevation.LegacyMaxHeight, info);
            Assert.That(peakMetersAboveSeaLevel, Is.EqualTo(6000f).Within(0.0001f));
            Assert.That(Elevation.MetersAboveSeaLevelToAbsolute(6000f, info), Is.EqualTo(Elevation.LegacyMaxHeight).Within(0.0001f));

            float signedUp = Elevation.SeaRelativeToSignedMeters(40f, info);
            float roundTripUp = Elevation.SignedMetersToSeaRelative(signedUp, info);
            Assert.That(roundTripUp, Is.EqualTo(40f).Within(0.0001f));

            float signedDown = Elevation.SeaRelativeToSignedMeters(-10f, info);
            float roundTripDown = Elevation.SignedMetersToSeaRelative(signedDown, info);
            Assert.That(roundTripDown, Is.EqualTo(-10f).Within(0.0001f));
        }

        [Test]
        public void ElevationCellMetersHelpers_SupportCanonicalAndLegacyCells()
        {
            var info = new MapInfo
            {
                SeaLevel = 20f,
                World = new WorldInfo
                {
                    MaxElevationMeters = 6000f,
                    MaxSeaDepthMeters = 1500f
                }
            };

            var canonicalCell = new Cell
            {
                Height = 60,
                SeaRelativeElevation = 40f,
                HasSeaRelativeElevation = true
            };
            Assert.That(Elevation.GetMetersAboveSeaLevel(canonicalCell, info), Is.EqualTo(3000f).Within(0.0001f));
            Assert.That(Elevation.GetSignedMeters(canonicalCell, info), Is.EqualTo(3000f).Within(0.0001f));

            var legacyCell = new Cell
            {
                Height = 10,
                HasSeaRelativeElevation = false
            };
            Assert.That(Elevation.GetMetersAboveSeaLevel(legacyCell, info), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(Elevation.GetSignedMeters(legacyCell, info), Is.EqualTo(-750f).Within(0.0001f));
        }

        [Test]
        public void ElevationNormalizedSignedAndDepthHelpers_AreWorldScaleConsistent()
        {
            var info = new MapInfo
            {
                SeaLevel = 20f,
                World = new WorldInfo
                {
                    MaxElevationMeters = 6000f,
                    MaxSeaDepthMeters = 1500f
                }
            };

            var landCell = new Cell
            {
                Height = 60,
                SeaRelativeElevation = 40f,
                HasSeaRelativeElevation = true
            };
            Assert.That(Elevation.GetNormalizedSignedHeight(landCell, info), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(Elevation.GetNormalizedDepth01(landCell, info), Is.EqualTo(0f).Within(0.0001f));

            var midDepthCell = new Cell
            {
                Height = 10,
                HasSeaRelativeElevation = false
            };
            Assert.That(Elevation.GetNormalizedSignedHeight(midDepthCell, info), Is.EqualTo(-0.125f).Within(0.0001f));
            Assert.That(Elevation.GetNormalizedDepth01(midDepthCell, info), Is.EqualTo(0.5f).Within(0.0001f));

            var maxDepthCell = new Cell
            {
                Height = 0,
                HasSeaRelativeElevation = false
            };
            Assert.That(Elevation.GetNormalizedSignedHeight(maxDepthCell, info), Is.EqualTo(-0.25f).Within(0.0001f));
            Assert.That(Elevation.GetNormalizedDepth01(maxDepthCell, info), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void ElevationHelpers_RejectOutOfRangeAbsoluteValues()
        {
            var info = new MapInfo { SeaLevel = 20f };
            Assert.Throws<InvalidOperationException>(() => Elevation.NormalizeAbsolute01(101f));

            var legacyCell = new Cell
            {
                Id = 1001,
                Height = 120,
                HasSeaRelativeElevation = false
            };

            Assert.Throws<InvalidOperationException>(() => Elevation.GetAbsoluteHeight(legacyCell, info));

            var canonicalCell = new Cell
            {
                Id = 1002,
                SeaRelativeElevation = 90f, // absolute = 110 with sea level 20
                HasSeaRelativeElevation = true
            };

            Assert.Throws<InvalidOperationException>(() => Elevation.GetAbsoluteHeight(canonicalCell, info));
        }

        [Test]
        public void ResolveSeaLevel_PrefersWorldMetadata_WhenLegacySeaLevelIsUnset()
        {
            var info = new MapInfo
            {
                SeaLevel = 0f, // legacy unset/invalid
                World = new WorldInfo
                {
                    SeaLevelHeight = 25f
                }
            };

            Assert.That(Elevation.ResolveSeaLevel(info), Is.EqualTo(25f).Within(0.0001f));
        }

        [Test]
        public void MapGenAdapter_ProducesCanonicalElevationForEveryCell()
        {
            var config = new MapGenConfig
            {
                Seed = 12345,
                Template = HeightmapTemplateType.LowIsland,
                CellCount = 5000
            };

            var mapResult = MapGenPipeline.Generate(config);
            var mapData = MapGenAdapter.Convert(mapResult);

            Assert.DoesNotThrow(() => mapData.AssertElevationInvariants(requireCanonical: true));
            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                Assert.That(mapData.Cells[i].HasSeaRelativeElevation, Is.True, $"Cell {mapData.Cells[i].Id} is missing canonical elevation.");
            }
        }

        [Test]
        public void SeaLevelClassification_IsConsistent_FromMapGenToRendererAndEconomy()
        {
            var config = new MapGenConfig
            {
                Seed = 12345,
                Template = HeightmapTemplateType.LowIsland,
                CellCount = 5000
            };

            var mapResult = MapGenPipeline.Generate(config);
            var mapData = MapGenAdapter.Convert(mapResult);

            var economy = new EconomyState();
            economy.InitializeFromMap(mapData);

            int expectedEconomyCellMappings = 0;

            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                Cell cell = mapData.Cells[i];
                bool isSeaLevelLandFromMapGen = !mapResult.Heights.IsWater(i);
                bool isSeaLevelLandFromCell = Elevation.GetSeaRelativeHeight(cell, mapData.Info) > 0f;
                Assert.That(isSeaLevelLandFromCell, Is.EqualTo(isSeaLevelLandFromMapGen),
                    $"Sea-level classification mismatch for cell {i}");

                bool expectedLand = isSeaLevelLandFromMapGen && !mapResult.Biomes.IsLakeCell[i];
                Assert.That(cell.IsLand, Is.EqualTo(expectedLand),
                    $"Land/water mismatch for cell {i}");

                if (cell.IsLand && cell.CountyId > 0)
                    expectedEconomyCellMappings++;
            }

            Assert.That(economy.CellToCounty.Count, Is.EqualTo(expectedEconomyCellMappings),
                "Economy land/county mapping count mismatch");

            foreach (var kvp in economy.CellToCounty)
            {
                Assert.That(mapData.CellById[kvp.Key].IsLand, Is.True, $"Economy mapped water cell {kvp.Key}");
                Assert.That(mapData.CellById[kvp.Key].CountyId, Is.EqualTo(kvp.Value),
                    $"Economy county mismatch for cell {kvp.Key}");
            }

            var shader = Shader.Find("EconSim/MapOverlay");
            Assert.That(shader, Is.Not.Null, "Shader EconSim/MapOverlay not found");

            var material = new Material(shader);
            var overlayManager = new MapOverlayManager(mapData, material, 1);

            try
            {
                var geographyTexture = material.GetTexture("_GeographyBaseTex") as Texture2D;
                Assert.That(geographyTexture, Is.Not.Null, "Missing geography texture from overlay material");

                var pixels = geographyTexture.GetPixels();
                var spatialGrid = GetPrivateField<int[]>(overlayManager, "spatialGrid");

                Assert.That(spatialGrid, Is.Not.Null);
                Assert.That(spatialGrid.Length, Is.EqualTo(pixels.Length),
                    "Spatial grid length must match geography texture length");

                for (int i = 0; i < spatialGrid.Length; i++)
                {
                    int cellId = spatialGrid[i];
                    if (cellId < 0 || !mapData.CellById.TryGetValue(cellId, out var cell))
                        continue;

                    bool rendererWater = pixels[i].a >= 0.5f;
                    bool mapDataWater = !cell.IsLand;
                    Assert.That(rendererWater, Is.EqualTo(mapDataWater),
                        $"Renderer water flag mismatch for cell {cellId} at pixel {i}");
                }
            }
            finally
            {
                overlayManager.Dispose();
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void OverlayHeightTexture_UsesCanonicalElevationInsteadOfLegacyHeightField()
        {
            var config = new MapGenConfig
            {
                Seed = 12345,
                Template = HeightmapTemplateType.LowIsland,
                CellCount = 5000
            };

            var mapResult = MapGenPipeline.Generate(config);
            var mapData = MapGenAdapter.Convert(mapResult);
            Assert.That(mapData.Cells.Count, Is.GreaterThan(0), "Map must contain at least one cell.");

            // Force legacy and canonical values to disagree so this test catches raw Height usage.
            var targetCell = mapData.Cells[0];
            targetCell.Height = 0;
            targetCell.SeaRelativeElevation = 25f; // absolute ~= 45 with default sea level 20
            targetCell.HasSeaRelativeElevation = true;

            float expectedNormalizedHeight = Elevation.NormalizeAbsolute01(
                Elevation.GetAbsoluteHeight(targetCell, mapData.Info));

            var shader = Shader.Find("EconSim/MapOverlay");
            Assert.That(shader, Is.Not.Null, "Shader EconSim/MapOverlay not found");

            var material = new Material(shader);
            var overlayManager = new MapOverlayManager(mapData, material, 1);

            try
            {
                var heightTexture = material.GetTexture("_HeightmapTex") as Texture2D;
                Assert.That(heightTexture, Is.Not.Null, "Missing heightmap texture from overlay material");

                var heightPixels = heightTexture.GetPixels();
                var spatialGrid = GetPrivateField<int[]>(overlayManager, "spatialGrid");

                Assert.That(spatialGrid, Is.Not.Null);
                Assert.That(spatialGrid.Length, Is.EqualTo(heightPixels.Length),
                    "Spatial grid length must match height texture length");

                int firstPixelIndex = -1;
                for (int i = 0; i < spatialGrid.Length; i++)
                {
                    if (spatialGrid[i] == targetCell.Id)
                    {
                        firstPixelIndex = i;
                        break;
                    }
                }

                Assert.That(firstPixelIndex, Is.GreaterThanOrEqualTo(0),
                    $"Could not find a sampled pixel for target cell {targetCell.Id}");

                float actualNormalizedHeight = heightPixels[firstPixelIndex].r;
                float legacyRawNormalized = targetCell.Height / 100f;

                Assert.That(actualNormalizedHeight, Is.EqualTo(expectedNormalizedHeight).Within(0.001f),
                    "Overlay height texture should use canonical absolute elevation conversion.");
                Assert.That(actualNormalizedHeight, Is.Not.EqualTo(legacyRawNormalized).Within(0.001f),
                    "Overlay height texture appears to be reading legacy cell.Height directly.");
            }
            finally
            {
                overlayManager.Dispose();
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void OverlaySetSeaLevel_NormalizesAbsoluteUnitsForShader()
        {
            var config = new MapGenConfig
            {
                Seed = 12345,
                Template = HeightmapTemplateType.LowIsland,
                CellCount = 5000
            };

            var mapResult = MapGenPipeline.Generate(config);
            var mapData = MapGenAdapter.Convert(mapResult);

            var shader = Shader.Find("EconSim/MapOverlay");
            Assert.That(shader, Is.Not.Null, "Shader EconSim/MapOverlay not found");

            var material = new Material(shader);
            var overlayManager = new MapOverlayManager(mapData, material, 1);

            try
            {
                overlayManager.SetSeaLevel(20f);
                Assert.That(material.GetFloat("_SeaLevel"), Is.EqualTo(0.2f).Within(0.0001f),
                    "SetSeaLevel(20) should normalize to shader value 0.2.");

                overlayManager.SetSeaLevel(100f);
                Assert.That(material.GetFloat("_SeaLevel"), Is.EqualTo(1.0f).Within(0.0001f),
                    "SetSeaLevel(100) should normalize to shader value 1.0.");
            }
            finally
            {
                overlayManager.Dispose();
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static T GetPrivateField<T>(object instance, string fieldName) where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}' on {instance.GetType().Name}");
            return field.GetValue(instance) as T;
        }

        [Test]
        [Category("M3Regression")]
        public void ProductionCode_DoesNotReadRawCellHeightOutsideElevationHelpers()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string srcRoot = Path.Combine(repoRoot, "src");
            string unityScriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            var roots = new[] { srcRoot, unityScriptsRoot };
            var violations = new List<string>();

            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root))
                    continue;

                string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
                for (int f = 0; f < files.Length; f++)
                {
                    string file = Path.GetFullPath(files[f]);
                    string normalizedFile = file.Replace('\\', '/');
                    if (normalizedFile.EndsWith("/Data/MapData.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string source = File.ReadAllText(file);
                    MatchCollection matches = RawCellHeightPattern.Matches(source);
                    for (int m = 0; m < matches.Count; m++)
                    {
                        Match match = matches[m];
                        int line = 1;
                        for (int i = 0; i < match.Index; i++)
                        {
                            if (source[i] == '\n')
                                line++;
                        }

                        string relative = file.StartsWith(repoRoot, StringComparison.Ordinal)
                            ? file.Substring(repoRoot.Length + 1)
                            : file;
                        violations.Add($"{relative}:{line}: {match.Value}");
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "Raw cell.Height usage is forbidden outside Elevation helpers. Use Elevation.GetAbsoluteHeight/GetSeaRelativeHeight.\n" +
                string.Join("\n", violations));
        }

        [Test]
        [Category("M3Regression")]
        public void ProductionCode_DoesNotReadMapInfoSeaLevelOutsideElevationHelpers()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string srcRoot = Path.Combine(repoRoot, "src");
            string unityScriptsRoot = Path.Combine(Application.dataPath, "Scripts");
            var roots = new[] { srcRoot, unityScriptsRoot };
            var violations = new List<string>();

            for (int r = 0; r < roots.Length; r++)
            {
                string root = roots[r];
                if (!Directory.Exists(root))
                    continue;

                string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
                for (int f = 0; f < files.Length; f++)
                {
                    string file = Path.GetFullPath(files[f]);
                    string normalizedFile = file.Replace('\\', '/');
                    if (normalizedFile.EndsWith("/Data/MapData.cs", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string source = File.ReadAllText(file);
                    MatchCollection matches = RawMapInfoSeaLevelPattern.Matches(source);
                    for (int m = 0; m < matches.Count; m++)
                    {
                        Match match = matches[m];
                        int line = 1;
                        for (int i = 0; i < match.Index; i++)
                        {
                            if (source[i] == '\n')
                                line++;
                        }

                        string relative = file.StartsWith(repoRoot, StringComparison.Ordinal)
                            ? file.Substring(repoRoot.Length + 1)
                            : file;
                        violations.Add($"{relative}:{line}: {match.Value}");
                    }
                }
            }

            Assert.That(violations, Is.Empty,
                "Direct MapInfo.SeaLevel reads are forbidden outside Elevation helpers. Use Elevation.ResolveSeaLevel.\n" +
                string.Join("\n", violations));
        }
    }
}
