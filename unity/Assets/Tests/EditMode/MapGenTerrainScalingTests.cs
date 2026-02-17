using System;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGen")]
    public class MapGenTerrainScalingTests
    {
        const float Epsilon = 0.0001f;
        const float ReferenceSpanMeters = 6250f;

        [Test]
        public void HillOutput_IsStableAcrossSeaDepth_WhenReferenceSpanFixed()
        {
            CellMesh mesh = CreateMesh(seed: 101, cellCount: 1800, aspectRatio: 16f / 9f);
            var shallow = new ElevationField(mesh, maxSeaDepthMeters: 1250f, maxElevationMeters: 5000f, terrainShapeReferenceSpanMeters: ReferenceSpanMeters);
            var deep = new ElevationField(mesh, maxSeaDepthMeters: 5000f, maxElevationMeters: 5000f, terrainShapeReferenceSpanMeters: ReferenceSpanMeters);

            Fill(shallow, 0f);
            Fill(deep, 0f);

            var rngA = new Random(777);
            var rngB = new Random(777);

            bool placedA = HeightmapTerrainOps.Hill(shallow, 0.5f, 0.5f, 1800f, rngA, 0.2f, 0.8f, 0.2f, 0.8f, out float ax, out float ay);
            bool placedB = HeightmapTerrainOps.Hill(deep, 0.5f, 0.5f, 1800f, rngB, 0.2f, 0.8f, 0.2f, 0.8f, out float bx, out float by);

            Assert.That(placedA, Is.EqualTo(placedB));
            Assert.That(ax, Is.EqualTo(bx).Within(Epsilon));
            Assert.That(ay, Is.EqualTo(by).Within(Epsilon));
            AssertArraysEqual(shallow.ElevationMetersSigned, deep.ElevationMetersSigned, Epsilon);
        }

        [Test]
        public void StraitOutput_IsStableAcrossSeaDepth_WhenReferenceSpanFixed()
        {
            CellMesh mesh = CreateMesh(seed: 202, cellCount: 1800, aspectRatio: 16f / 9f);
            var shallow = new ElevationField(mesh, maxSeaDepthMeters: 1250f, maxElevationMeters: 5000f, terrainShapeReferenceSpanMeters: ReferenceSpanMeters);
            var deep = new ElevationField(mesh, maxSeaDepthMeters: 5000f, maxElevationMeters: 5000f, terrainShapeReferenceSpanMeters: ReferenceSpanMeters);

            Fill(shallow, -200f);
            Fill(deep, -200f);

            var rngA = new Random(888);
            var rngB = new Random(888);
            HeightmapTerrainOps.Strait(shallow, desiredWidth: 3, direction: 1, rng: rngA);
            HeightmapTerrainOps.Strait(deep, desiredWidth: 3, direction: 1, rng: rngB);

            AssertArraysEqual(shallow.ElevationMetersSigned, deep.ElevationMetersSigned, Epsilon);
        }

        static void Fill(ElevationField field, float value)
        {
            for (int i = 0; i < field.CellCount; i++)
                field[i] = value;
        }

        static void AssertArraysEqual(float[] expected, float[] actual, float epsilon)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(expected.Length, Is.EqualTo(actual.Length));
            for (int i = 0; i < expected.Length; i++)
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(epsilon), $"Mismatch at index {i}");
        }

        static CellMesh CreateMesh(int seed, int cellCount, float aspectRatio, float cellSizeKm = 2.5f)
        {
            float cellAreaKm2 = cellSizeKm * cellSizeKm;
            float mapAreaKm2 = cellCount * cellAreaKm2;
            float mapWidthKm = (float)Math.Sqrt(mapAreaKm2 * aspectRatio);
            float mapHeightKm = mapWidthKm / aspectRatio;

            var (gridPoints, spacing) = PointGenerator.JitteredGrid(mapWidthKm, mapHeightKm, cellCount, seed);
            Vec2[] boundaryPoints = PointGenerator.BoundaryPoints(mapWidthKm, mapHeightKm, spacing);
            CellMesh mesh = VoronoiBuilder.Build(mapWidthKm, mapHeightKm, gridPoints, boundaryPoints);
            mesh.ComputeAreas();
            return mesh;
        }
    }
}
