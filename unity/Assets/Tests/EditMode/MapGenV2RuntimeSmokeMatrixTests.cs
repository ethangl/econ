using System;
using EconSim.Core.Data;
using EconSim.Core.Import;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2RuntimeSmokeMatrixTests
    {
        readonly SmokeCase[] _cases =
        {
            new SmokeCase(1101, HeightmapTemplateType.LowIsland, 5000, 0.15f, 0.60f, 0, 120, -700f, 900f),
            new SmokeCase(2202, HeightmapTemplateType.Continents, 5000, 0.30f, 0.82f, 5, 250, -500f, 1800f),
            new SmokeCase(3303, HeightmapTemplateType.Volcano, 5000, 0.20f, 0.72f, 0, 150, -900f, 1500f),
            new SmokeCase(4404, HeightmapTemplateType.Isthmus, 5000, 0.20f, 0.78f, 1, 200, -300f, 2200f),
            new SmokeCase(5505, HeightmapTemplateType.Taklamakan, 5000, 0.20f, 0.86f, 0, 260, -200f, 2600f),
        };

        [Test]
        public void RuntimeSmokeMatrix_StaysWithinDeterministicBands()
        {
            foreach (SmokeCase c in _cases)
            {
                var config = new MapGenV2Config
                {
                    Seed = c.Seed,
                    CellCount = c.CellCount,
                    Template = c.Template,
                    RiverThreshold = 180f,
                    RiverTraceThreshold = 10f,
                    MinRiverVertices = 8
                };

                MapGenV2Result result = MapGenPipelineV2.Generate(config);
                MapData runtimeMap = MapGenAdapter.Convert(result);

                runtimeMap.AssertElevationInvariants();
                runtimeMap.AssertWorldInvariants();

                float landRatio = result.Elevation.LandRatio();
                int riverCount = result.Rivers.Rivers.Length;
                float p50Meters = Percentile(result.Elevation.ElevationMetersSigned, 0.50f);

                int runtimeLandCells = 0;
                for (int i = 0; i < runtimeMap.Cells.Count; i++)
                {
                    if (runtimeMap.Cells[i].IsLand)
                        runtimeLandCells++;
                }

                float runtimeLandRatio = runtimeMap.Cells.Count > 0
                    ? runtimeLandCells / (float)runtimeMap.Cells.Count
                    : 0f;

                string label = $"{c.Template} seed={c.Seed} cells={c.CellCount}";
                Assert.That(landRatio, Is.InRange(c.LandRatioMin, c.LandRatioMax),
                    $"{label}: V2 land ratio out of band.");
                Assert.That(runtimeLandRatio, Is.InRange(c.LandRatioMin, c.LandRatioMax),
                    $"{label}: runtime land ratio out of band.");
                Assert.That(Math.Abs(runtimeLandRatio - landRatio), Is.LessThanOrEqualTo(0.08f),
                    $"{label}: runtime/V2 land ratio drift too high.");

                Assert.That(riverCount, Is.InRange(c.RiverCountMin, c.RiverCountMax),
                    $"{label}: river count out of band.");
                Assert.That(runtimeMap.Rivers.Count, Is.EqualTo(riverCount),
                    $"{label}: converted river count mismatch.");

                Assert.That(p50Meters, Is.InRange(c.P50MinMeters, c.P50MaxMeters),
                    $"{label}: p50 signed elevation out of band.");

                Assert.That(runtimeMap.Info.World.SeaLevelHeight, Is.EqualTo(result.World.SeaLevelHeight).Within(0.0001f),
                    $"{label}: runtime sea level anchor changed.");
                Assert.That(runtimeMap.Info.LandCells, Is.EqualTo(runtimeLandCells),
                    $"{label}: MapInfo land cell count mismatch.");
            }
        }

        static float Percentile(float[] values, float q)
        {
            if (values == null || values.Length == 0)
                return 0f;

            var sorted = (float[])values.Clone();
            Array.Sort(sorted);

            if (q <= 0f) return sorted[0];
            if (q >= 1f) return sorted[sorted.Length - 1];

            float index = q * (sorted.Length - 1);
            int lo = (int)Math.Floor(index);
            int hi = (int)Math.Ceiling(index);
            if (lo == hi)
                return sorted[lo];

            float t = index - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }

        readonly struct SmokeCase
        {
            public readonly int Seed;
            public readonly HeightmapTemplateType Template;
            public readonly int CellCount;
            public readonly float LandRatioMin;
            public readonly float LandRatioMax;
            public readonly int RiverCountMin;
            public readonly int RiverCountMax;
            public readonly float P50MinMeters;
            public readonly float P50MaxMeters;

            public SmokeCase(
                int seed,
                HeightmapTemplateType template,
                int cellCount,
                float landRatioMin,
                float landRatioMax,
                int riverCountMin,
                int riverCountMax,
                float p50MinMeters,
                float p50MaxMeters)
            {
                Seed = seed;
                Template = template;
                CellCount = cellCount;
                LandRatioMin = landRatioMin;
                LandRatioMax = landRatioMax;
                RiverCountMin = riverCountMin;
                RiverCountMax = riverCountMax;
                P50MinMeters = p50MinMeters;
                P50MaxMeters = p50MaxMeters;
            }
        }
    }
}
