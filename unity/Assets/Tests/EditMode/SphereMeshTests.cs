using System;
using NUnit.Framework;
using WorldGen.Core;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("WorldGen")]
    public class SphereMeshTests
    {
        [TestCase(100)]
        [TestCase(1000)]
        [TestCase(10000)]
        public void Generate_ProducesValidMesh(int cellCount)
        {
            var config = new WorldGenConfig { CellCount = cellCount, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config);

            Assert.AreEqual(cellCount, mesh.CellCount);
            Assert.Greater(mesh.VertexCount, 0);
            Assert.Greater(mesh.EdgeCount, 0);
        }

        [Test]
        public void AllPoints_OnUnitSphere()
        {
            var config = new WorldGenConfig { CellCount = 1000, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config);

            foreach (var center in mesh.CellCenters)
            {
                float mag = center.Magnitude;
                Assert.AreEqual(1f, mag, 0.01f, $"Cell center not on sphere: magnitude = {mag}");
            }
        }

        [Test]
        public void EulerFormula_HullFaces()
        {
            // For convex hull of N points on sphere: faces = 2*N - 4
            int cellCount = 1000;
            var points = FibonacciSphere.Generate(cellCount);
            var hull = ConvexHullBuilder.Build(points);

            int expectedFaces = 2 * cellCount - 4;
            Assert.AreEqual(expectedFaces, hull.TriangleCount,
                $"Euler formula: expected {expectedFaces} faces, got {hull.TriangleCount}");
        }

        [Test]
        public void AllHalfedges_HaveValidOpposites()
        {
            var points = FibonacciSphere.Generate(1000);
            var hull = ConvexHullBuilder.Build(points);

            for (int e = 0; e < hull.Halfedges.Length; e++)
            {
                Assert.AreNotEqual(-1, hull.Halfedges[e],
                    $"Half-edge {e} has no opposite (closed hull should have none)");

                int opp = hull.Halfedges[e];
                Assert.AreEqual(e, hull.Halfedges[opp],
                    $"Half-edge symmetry broken: halfedges[halfedges[{e}]] != {e}");
            }
        }

        [Test]
        public void CellNeighbors_AreSymmetric()
        {
            var config = new WorldGenConfig { CellCount = 500, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config);

            for (int c = 0; c < mesh.CellCount; c++)
            {
                Assert.IsNotNull(mesh.CellNeighbors[c],
                    $"Cell {c} has null neighbors");

                foreach (int neighbor in mesh.CellNeighbors[c])
                {
                    Assert.IsNotNull(mesh.CellNeighbors[neighbor],
                        $"Neighbor {neighbor} of cell {c} has null neighbors");

                    bool found = Array.IndexOf(mesh.CellNeighbors[neighbor], c) >= 0;
                    Assert.IsTrue(found,
                        $"Cell {c} lists {neighbor} as neighbor, but not vice versa");
                }
            }
        }

        [Test]
        public void CellAreas_SumToSphereArea()
        {
            float radius = 1f;
            var config = new WorldGenConfig { CellCount = 1000, Seed = 42, Radius = radius };
            var mesh = WorldGenPipeline.Generate(config);

            float totalArea = 0f;
            for (int i = 0; i < mesh.CellCount; i++)
                totalArea += mesh.CellAreas[i];

            float expectedArea = 4f * (float)Math.PI * radius * radius;
            float tolerance = expectedArea * 0.02f; // 2% tolerance
            Assert.AreEqual(expectedArea, totalArea, tolerance,
                $"Total area {totalArea:F4} should be ~{expectedArea:F4} (4*pi*r^2)");
        }

        [Test]
        public void Vertices_AreOnSphere()
        {
            var config = new WorldGenConfig { CellCount = 500, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config);

            foreach (var vertex in mesh.Vertices)
            {
                float mag = vertex.Magnitude;
                Assert.AreEqual(1f, mag, 0.01f,
                    $"Voronoi vertex not on sphere: magnitude = {mag}");
            }
        }

        [TestCase(100)]
        [TestCase(1000)]
        public void AllCells_HaveVerticesAndNeighbors(int cellCount)
        {
            var config = new WorldGenConfig { CellCount = cellCount, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config);

            for (int c = 0; c < mesh.CellCount; c++)
            {
                Assert.IsNotNull(mesh.CellVertices[c], $"Cell {c} has null vertices");
                Assert.GreaterOrEqual(mesh.CellVertices[c].Length, 3,
                    $"Cell {c} has fewer than 3 vertices");

                Assert.IsNotNull(mesh.CellNeighbors[c], $"Cell {c} has null neighbors");
                Assert.GreaterOrEqual(mesh.CellNeighbors[c].Length, 3,
                    $"Cell {c} has fewer than 3 neighbors");
            }
        }
    }
}
