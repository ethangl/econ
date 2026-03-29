using System;
using System.Reflection;
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
                Radius = 6371f,
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

        [Test]
        public void GatherHumidity_ScalesWithAbsoluteWindSpeed()
        {
            var mesh = new SphereMesh
            {
                CellCenters = new[]
                {
                    new Vec3(1f, 0f, 0f),
                    new Vec3(0f, 1f, 0f),
                },
                CellNeighbors = new[]
                {
                    new[] { 1 },
                    Array.Empty<int>(),
                },
            };

            var tectonics = new TectonicData
            {
                CellWind = new[]
                {
                    new Vec3(0f, 0f, 0f),
                    new Vec3(1f, 0f, 0f),
                },
                CellWindSpeed = new[] { 0f, 1f },
            };

            var humidity = new[] { 0f, 1f };
            var incomingDirs = new[]
            {
                new[] { new Vec3(1f, 0f, 0f) },
                Array.Empty<Vec3>(),
            };

            float fastTransport = InvokeGatherHumidity(mesh, tectonics, humidity, incomingDirs);

            tectonics.CellWindSpeed[1] = 0.1f;
            float slowTransport = InvokeGatherHumidity(mesh, tectonics, humidity, incomingDirs);

            Assert.Greater(fastTransport, 0.9f);
            Assert.Less(slowTransport, 0.2f);
        }

        [Test]
        public void BuildZonalWind_TransitionsThroughCalmFlow()
        {
            Vec3 east = new Vec3(1f, 0f, 0f);
            Vec3 north = new Vec3(0f, 1f, 0f);

            Vec3 subtropicalTransition = InvokeBuildZonalWind(30f, east, north);
            Vec3 polarTransition = InvokeBuildZonalWind(60f, east, north);

            Assert.Less(subtropicalTransition.Magnitude, 1e-3f);
            Assert.Less(polarTransition.Magnitude, 1e-3f);
        }

        static float InvokeGatherHumidity(SphereMesh mesh, TectonicData tectonics, float[] humidity, Vec3[][] incomingDirs)
        {
            var method = typeof(PrecipitationOps).GetMethod("GatherHumidity", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method, "Expected GatherHumidity private helper to exist.");
            return (float)method!.Invoke(null, new object[] { 0, mesh, tectonics, humidity, incomingDirs })!;
        }

        static Vec3 InvokeBuildZonalWind(float latitudeDeg, Vec3 east, Vec3 north)
        {
            var method = typeof(WindOps).GetMethod("BuildZonalWind", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method, "Expected BuildZonalWind private helper to exist.");
            return (Vec3)method!.Invoke(null, new object[] { latitudeDeg, east, north })!;
        }
    }
}
