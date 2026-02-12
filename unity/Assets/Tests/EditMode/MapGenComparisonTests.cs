using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Import;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGen")]
    public class MapGenComparisonTests
    {
        [Test]
        public void ComparisonRunner_ProducesFiniteMetricsAndReport()
        {
            var config = new MapGenConfig
            {
                Seed = 71023,
                CellCount = 3500,
                Template = HeightmapTemplateType.Continents,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            MapGenComparisonCase result = MapGenComparison.Compare(config);

            Assert.That(result.CellCount, Is.EqualTo(config.CellCount));
            Assert.That(result.Baseline.LandRatio, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(result.Candidate.LandRatio, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(float.IsNaN(result.Baseline.ElevationP50Meters) || float.IsInfinity(result.Baseline.ElevationP50Meters), Is.False);
            Assert.That(float.IsNaN(result.Candidate.ElevationP50Meters) || float.IsInfinity(result.Candidate.ElevationP50Meters), Is.False);
            Assert.That(result.Baseline.BiomeCounts, Is.Not.Null);
            Assert.That(result.Candidate.BiomeCounts, Is.Not.Null);

            string report = MapGenComparison.BuildReport(new List<MapGenComparisonCase> { result });
            Assert.That(report, Does.Contain("MapGen Baseline vs Candidate Comparison"));
            Assert.That(report, Does.Contain("Biome overlap="));
            Assert.That(report, Does.Contain("rivers="));
        }

        [Test]
        public void Adapter_CanConvertMapGenResult_IntoRuntimeMapData()
        {
            var config = new MapGenConfig
            {
                Seed = 987654,
                CellCount = 3500,
                Template = HeightmapTemplateType.LowIsland,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            MapGenResult result = MapGenPipeline.Generate(config);
            MapData data = MapGenAdapter.Convert(result);

            Assert.That(data, Is.Not.Null);
            Assert.That(data.Cells.Count, Is.GreaterThan(0));
            Assert.That(data.Info.World, Is.Not.Null);
            Assert.That(data.Info.World.SeaLevelHeight, Is.EqualTo(result.World.SeaLevelHeight).Within(0.0001f));
            Assert.That(data.Info.World.MinHeight, Is.EqualTo(result.World.MinHeight).Within(0.0001f));
            Assert.That(data.Info.World.MaxHeight, Is.EqualTo(result.World.MaxHeight).Within(0.0001f));
            data.AssertElevationInvariants();
            data.AssertWorldInvariants();
        }
    }
}
