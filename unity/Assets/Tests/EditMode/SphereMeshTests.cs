using System;
using System.Diagnostics;
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
            var config = new WorldGenConfig { CoarseCellCount = cellCount, DenseCellCount = cellCount, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config).Mesh;

            Assert.AreEqual(cellCount, mesh.CellCount);
            Assert.Greater(mesh.VertexCount, 0);
            Assert.Greater(mesh.EdgeCount, 0);
        }

        [Test]
        public void AllPoints_OnUnitSphere()
        {
            var config = new WorldGenConfig { CoarseCellCount = 1000, DenseCellCount = 1000, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config).Mesh;

            foreach (var center in mesh.CellCenters)
            {
                float mag = center.Magnitude;
                Assert.AreEqual(1f, mag, 0.01f, $"Cell center not on sphere: magnitude = {mag}");
            }
        }

        [TestCase(ConvexHullAlgorithm.Quickhull)]
        [TestCase(ConvexHullAlgorithm.Incremental)]
        public void EulerFormula_HullFaces(ConvexHullAlgorithm algorithm)
        {
            // For convex hull of N points on sphere: faces = 2*N - 4
            int cellCount = 1000;
            var points = FibonacciSphere.Generate(cellCount);
            var hull = ConvexHull.Build(points, algorithm);

            int expectedFaces = 2 * cellCount - 4;
            Assert.AreEqual(expectedFaces, hull.TriangleCount,
                $"Euler formula: expected {expectedFaces} faces, got {hull.TriangleCount}");
        }

        [TestCase(ConvexHullAlgorithm.Quickhull)]
        [TestCase(ConvexHullAlgorithm.Incremental)]
        public void AllHalfedges_HaveValidOpposites(ConvexHullAlgorithm algorithm)
        {
            var points = FibonacciSphere.Generate(1000);
            var hull = ConvexHull.Build(points, algorithm);

            for (int e = 0; e < hull.Halfedges.Length; e++)
            {
                Assert.AreNotEqual(-1, hull.Halfedges[e],
                    $"Half-edge {e} has no opposite (closed hull should have none)");

                int opp = hull.Halfedges[e];
                Assert.AreEqual(e, hull.Halfedges[opp],
                    $"Half-edge symmetry broken: halfedges[halfedges[{e}]] != {e}");
            }
        }

        [TestCase(ConvexHullAlgorithm.Quickhull)]
        [TestCase(ConvexHullAlgorithm.Incremental)]
        public void OutwardNormals_PointAwayFromOrigin(ConvexHullAlgorithm algorithm)
        {
            var points = FibonacciSphere.Generate(1000);
            var hull = ConvexHull.Build(points, algorithm);

            for (int t = 0; t < hull.TriangleCount; t++)
            {
                var (p0, p1, p2) = hull.PointsOfTriangle(t);
                Vec3 a = hull.Points[p0], b = hull.Points[p1], c = hull.Points[p2];
                Vec3 normal = Vec3.Cross(b - a, c - a);
                Vec3 centroid = (a + b + c) * (1f / 3f);

                Assert.Greater(Vec3.Dot(normal, centroid), 0f,
                    $"Triangle {t} normal points inward");
            }
        }

        [TestCase(ConvexHullAlgorithm.Quickhull)]
        [TestCase(ConvexHullAlgorithm.Incremental)]
        public void AllPoints_UsedInHull(ConvexHullAlgorithm algorithm)
        {
            int cellCount = 500;
            var points = FibonacciSphere.Generate(cellCount);
            var hull = ConvexHull.Build(points, algorithm);

            var used = new bool[cellCount];
            for (int i = 0; i < hull.Triangles.Length; i++)
                used[hull.Triangles[i]] = true;

            for (int i = 0; i < cellCount; i++)
                Assert.IsTrue(used[i], $"Point {i} not used in any triangle");
        }

        [Test]
        public void BothAlgorithms_ProduceSameFaceCount()
        {
            var points = FibonacciSphere.Generate(2000);
            var quickhull = ConvexHull.Build(points, ConvexHullAlgorithm.Quickhull);
            var incremental = ConvexHull.Build(points, ConvexHullAlgorithm.Incremental);

            Assert.AreEqual(incremental.TriangleCount, quickhull.TriangleCount,
                "Quickhull and Incremental should produce same face count");
        }

        [Test]
        public void CellNeighbors_AreSymmetric()
        {
            var config = new WorldGenConfig { CoarseCellCount = 500, DenseCellCount = 500, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config).Mesh;

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
            var config = new WorldGenConfig { CoarseCellCount = 1000, DenseCellCount = 1000, Seed = 42, Radius = radius };
            var mesh = WorldGenPipeline.Generate(config).Mesh;

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
            var config = new WorldGenConfig { CoarseCellCount = 500, DenseCellCount = 500, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config).Mesh;

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
            var config = new WorldGenConfig { CoarseCellCount = cellCount, DenseCellCount = cellCount, Seed = 42, Radius = 1f };
            var mesh = WorldGenPipeline.Generate(config).Mesh;

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

        [Test]
        [Explicit("Performance comparison — run manually")]
        public void Performance_QuickhullVsIncremental()
        {
            foreach (int n in new[] { 2000, 20000 })
            {
                var points = FibonacciSphere.Generate(n);

                var sw = Stopwatch.StartNew();
                ConvexHull.Build(points, ConvexHullAlgorithm.Quickhull);
                sw.Stop();
                long quickMs = sw.ElapsedMilliseconds;

                sw.Restart();
                ConvexHull.Build(points, ConvexHullAlgorithm.Incremental);
                sw.Stop();
                long incrMs = sw.ElapsedMilliseconds;

                float speedup = incrMs > 0 ? (float)incrMs / quickMs : 0;
                UnityEngine.Debug.Log(
                    $"[ConvexHull {n} pts] Quickhull: {quickMs}ms, Incremental: {incrMs}ms, Speedup: {speedup:F1}x");
            }
        }
    }
}
