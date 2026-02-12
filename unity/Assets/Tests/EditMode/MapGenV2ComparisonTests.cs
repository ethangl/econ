using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Import;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2ComparisonTests
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
            Assert.That(result.V1.LandRatio, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(result.V2.LandRatio, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(float.IsNaN(result.V1.ElevationP50Meters) || float.IsInfinity(result.V1.ElevationP50Meters), Is.False);
            Assert.That(float.IsNaN(result.V2.ElevationP50Meters) || float.IsInfinity(result.V2.ElevationP50Meters), Is.False);
            Assert.That(result.V1.BiomeCounts, Is.Not.Null);
            Assert.That(result.V2.BiomeCounts, Is.Not.Null);

            string report = MapGenComparison.BuildReport(new List<MapGenComparisonCase> { result });
            Assert.That(report, Does.Contain("MapGen V1 vs V2 Comparison"));
            Assert.That(report, Does.Contain("Biome overlap="));
            Assert.That(report, Does.Contain("rivers="));
        }

        [Test]
        public void Adapter_CanConvertV2Result_IntoRuntimeMapData()
        {
            var config = new MapGenV2Config
            {
                Seed = 987654,
                CellCount = 3500,
                Template = HeightmapTemplateType.LowIsland,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            MapGenV2Result result = MapGenPipelineV2.Generate(config);
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
