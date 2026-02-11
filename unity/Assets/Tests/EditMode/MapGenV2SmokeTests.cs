using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2SmokeTests
    {
        [Test]
        public void PipelineV2_IsDeterministic_AndProducesSignedElevationDomain()
        {
            var config = new MapGenV2Config
            {
                Seed = 424242,
                CellCount = 3000,
                AspectRatio = 16f / 9f,
                CellSizeKm = 2.5f,
                Template = HeightmapTemplateType.Continents,
                LatitudeSouth = 30f,
                MaxElevationMeters = 5000f,
                MaxSeaDepthMeters = 1500f
            };

            MapGenV2Result runA = MapGenPipelineV2.Generate(config);
            MapGenV2Result runB = MapGenPipelineV2.Generate(config);

            Assert.That(runA, Is.Not.Null);
            Assert.That(runA.Mesh, Is.Not.Null);
            Assert.That(runA.Elevation, Is.Not.Null);
            Assert.That(runA.Climate, Is.Not.Null);
            Assert.That(runA.Rivers, Is.Not.Null);
            Assert.That(runA.World, Is.Not.Null);

            int requestedMin = (int)(config.CellCount * 0.95f);
            int requestedMax = (int)(config.CellCount * 1.05f);
            Assert.That(runA.Mesh.CellCount, Is.InRange(requestedMin, requestedMax), "Generated cell count out of request tolerance.");
            Assert.That(runA.Elevation.CellCount, Is.EqualTo(runA.Mesh.CellCount));

            var (land, water) = runA.Elevation.CountLandWater();
            Assert.That(land, Is.GreaterThan(0), "V2 terrain should include land.");
            Assert.That(water, Is.GreaterThan(0), "V2 terrain should include water.");

            Assert.That(runA.World.SeaLevelHeight, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(runA.World.MinHeight, Is.EqualTo(-config.MaxSeaDepthMeters).Within(0.0001f));
            Assert.That(runA.World.MaxHeight, Is.EqualTo(config.MaxElevationMeters).Within(0.0001f));

            Assert.That(runA.Elevation.ElevationMetersSigned.Length, Is.EqualTo(runB.Elevation.ElevationMetersSigned.Length));
            for (int i = 0; i < runA.Elevation.ElevationMetersSigned.Length; i++)
            {
                float a = runA.Elevation.ElevationMetersSigned[i];
                float b = runB.Elevation.ElevationMetersSigned[i];

                Assert.That(float.IsNaN(a), Is.False);
                Assert.That(float.IsInfinity(a), Is.False);
                Assert.That(a, Is.InRange(-config.MaxSeaDepthMeters, config.MaxElevationMeters));
                Assert.That(a, Is.EqualTo(b).Within(0.0001f), $"V2 pipeline must be deterministic at cell {i}.");
            }

            Assert.That(runA.Climate.TemperatureC.Length, Is.EqualTo(runB.Climate.TemperatureC.Length));
            Assert.That(runA.Climate.PrecipitationMmYear.Length, Is.EqualTo(runB.Climate.PrecipitationMmYear.Length));
            for (int i = 0; i < runA.Climate.CellCount; i++)
            {
                Assert.That(runA.Climate.TemperatureC[i], Is.EqualTo(runB.Climate.TemperatureC[i]).Within(0.0001f),
                    $"Temperature determinism failed at cell {i}.");
                Assert.That(runA.Climate.PrecipitationMmYear[i], Is.EqualTo(runB.Climate.PrecipitationMmYear[i]).Within(0.0001f),
                    $"Precipitation determinism failed at cell {i}.");
            }

            Assert.That(runA.Rivers.Rivers.Length, Is.EqualTo(runB.Rivers.Rivers.Length),
                "River extraction determinism failed.");
        }
    }
}
