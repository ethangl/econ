using System;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2ClimateRiverMetricsTests
    {
        [Test]
        public void ClimateAndRivers_StayWithinBroadSanityBands()
        {
            foreach (HeightmapTemplateType template in Enum.GetValues(typeof(HeightmapTemplateType)))
            {
                var config = new MapGenV2Config
                {
                    Seed = 20260211,
                    CellCount = 5000,
                    Template = template,
                    MaxElevationMeters = 5000f,
                    MaxSeaDepthMeters = 1250f,
                    MaxAnnualPrecipitationMm = 3000f,
                    RiverThreshold = 180f,
                    RiverTraceThreshold = 10f,
                    MinRiverVertices = 8
                };

                MapGenV2Result result = MapGenPipelineV2.Generate(config);
                Assert.That(result.Climate, Is.Not.Null, $"Climate missing for {template}");
                Assert.That(result.Rivers, Is.Not.Null, $"Rivers missing for {template}");

                float minTemp = float.MaxValue;
                float maxTemp = float.MinValue;
                float minPrecip = float.MaxValue;
                float maxPrecip = float.MinValue;

                for (int i = 0; i < result.Climate.CellCount; i++)
                {
                    float t = result.Climate.TemperatureC[i];
                    float p = result.Climate.PrecipitationMmYear[i];

                    Assert.That(float.IsNaN(t) || float.IsInfinity(t), Is.False, $"Invalid temperature for {template} at cell {i}");
                    Assert.That(float.IsNaN(p) || float.IsInfinity(p), Is.False, $"Invalid precipitation for {template} at cell {i}");

                    if (t < minTemp) minTemp = t;
                    if (t > maxTemp) maxTemp = t;
                    if (p < minPrecip) minPrecip = p;
                    if (p > maxPrecip) maxPrecip = p;
                }

                Assert.That(minTemp, Is.GreaterThan(-80f), $"Unphysical cold floor for {template}");
                Assert.That(maxTemp, Is.LessThan(60f), $"Unphysical hot ceiling for {template}");
                Assert.That(minPrecip, Is.GreaterThanOrEqualTo(0f), $"Negative precipitation for {template}");
                Assert.That(maxPrecip, Is.GreaterThan(1f).And.LessThanOrEqualTo(config.MaxAnnualPrecipitationMm + 0.5f),
                    $"Precipitation range invalid for {template}");

                int riverCount = result.Rivers.Rivers.Length;
                int riverVertexTotal = 0;
                for (int i = 0; i < riverCount; i++)
                {
                    RiverV2 river = result.Rivers.Rivers[i];
                    Assert.That(river.Vertices, Is.Not.Null, $"River path missing for {template} river {river.Id}");
                    Assert.That(river.Vertices.Length, Is.GreaterThanOrEqualTo(config.MinRiverVertices),
                        $"River shorter than minimum for {template} river {river.Id}");
                    Assert.That(river.Discharge, Is.GreaterThanOrEqualTo(config.RiverTraceThreshold),
                        $"River discharge below trace threshold for {template} river {river.Id}");
                    riverVertexTotal += river.Vertices.Length;
                }

                float landRatio = result.Elevation.LandRatio();
                if (landRatio > 0.18f)
                    Assert.That(riverCount, Is.GreaterThan(0), $"Expected at least one river for land-rich template {template}");

                float riverCoverage = result.Mesh.VertexCount > 0 ? (float)riverVertexTotal / result.Mesh.VertexCount : 0f;
                Assert.That(riverCoverage, Is.LessThan(0.8f), $"River coverage implausibly high for {template}");
            }
        }
    }
}
