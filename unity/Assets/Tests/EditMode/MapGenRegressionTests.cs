using System.Collections.Generic;
using System.IO;
using System;
using EconSim.Core.Import;
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
            return new BaselineCase(
                obj.seed,
                (HeightmapTemplateType)Enum.Parse(typeof(HeightmapTemplateType), obj.template),
                obj.cellCount,
                obj.biomeDefCount,
                obj.landRatio.min,
                obj.landRatio.max,
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
}
