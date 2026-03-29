using NUnit.Framework;
using WorldGen.Core;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("WorldGen")]
    public class AtmosphereTests
    {
        [Test]
        public void Generate_PopulatesAtmosphericFields()
        {
            var config = new WorldGenConfig
            {
                CoarseCellCount = 256,
                DenseCellCount = 256,
                Seed = 42,
                Radius = 1f,
                TectonicSteps = 1,
            };

            var result = WorldGenPipeline.Generate(config);
            var mesh = result.Mesh;
            var tectonics = result.Tectonics;

            Assert.NotNull(tectonics.CellWind);
            Assert.NotNull(tectonics.CellWindSpeed);
            Assert.NotNull(tectonics.CellHumidity);
            Assert.NotNull(tectonics.CellPrecipitation);

            Assert.AreEqual(mesh.CellCount, tectonics.CellWind.Length);
            Assert.AreEqual(mesh.CellCount, tectonics.CellWindSpeed.Length);
            Assert.AreEqual(mesh.CellCount, tectonics.CellHumidity.Length);
            Assert.AreEqual(mesh.CellCount, tectonics.CellPrecipitation.Length);

            bool foundWind = false;
            bool foundPrecip = false;

            for (int i = 0; i < mesh.CellCount; i++)
            {
                Assert.GreaterOrEqual(tectonics.CellWindSpeed[i], 0f);
                Assert.LessOrEqual(tectonics.CellWindSpeed[i], 1f);
                Assert.GreaterOrEqual(tectonics.CellHumidity[i], 0f);
                Assert.LessOrEqual(tectonics.CellHumidity[i], 4f);
                Assert.GreaterOrEqual(tectonics.CellPrecipitation[i], 0f);
                Assert.LessOrEqual(tectonics.CellPrecipitation[i], 1f);

                Vec3 normal = mesh.CellCenters[i].Normalized;
                float tangentDot = System.Math.Abs(Vec3.Dot(tectonics.CellWind[i], normal));
                Assert.LessOrEqual(tangentDot, 1e-3f);

                if (tectonics.CellWindSpeed[i] > 1e-3f)
                    foundWind = true;
                if (tectonics.CellPrecipitation[i] > 1e-3f)
                    foundPrecip = true;
            }

            Assert.IsTrue(foundWind, "Expected at least one non-zero wind cell.");
            Assert.IsTrue(foundPrecip, "Expected at least one non-zero precipitation cell.");
        }
    }
}
