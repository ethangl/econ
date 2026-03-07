using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Builds SphereMesh (spherical Voronoi) from a convex hull (spherical Delaunay).
    /// Follows the pattern of MapGen's VoronoiBuilder.BuildFromDelaunay.
    /// </summary>
    public static class SphericalVoronoiBuilder
    {
        /// <summary>
        /// Build a SphereMesh from a convex hull of points on a sphere.
        /// </summary>
        public static SphereMesh Build(ConvexHull hull, float radius)
        {
            int numPoints = hull.Points.Length;
            int numTriangles = hull.TriangleCount;

            var mesh = new SphereMesh
            {
                Radius = radius,
                CellCenters = new Vec3[numPoints],
            };

            // Scale cell centers to radius
            for (int i = 0; i < numPoints; i++)
                mesh.CellCenters[i] = hull.Points[i] * radius;

            // Voronoi vertices = circumcenters of Delaunay triangles, scaled to sphere surface
            mesh.Vertices = new Vec3[numTriangles];
            mesh.VertexCells = new int[numTriangles][];
            mesh.VertexNeighbors = new int[numTriangles][];

            for (int t = 0; t < numTriangles; t++)
            {
                mesh.Vertices[t] = hull.Circumcenter(t) * radius;

                // Cells adjacent to this vertex = points of the triangle
                var (p0, p1, p2) = hull.PointsOfTriangle(t);
                mesh.VertexCells[t] = new int[] { p0, p1, p2 };

                // Neighboring vertices = adjacent triangles
                mesh.VertexNeighbors[t] = hull.AdjacentTriangles(t);
            }

            // Build cell data by walking half-edges around each point
            mesh.CellVertices = new int[numPoints][];
            mesh.CellNeighbors = new int[numPoints][];
            mesh.CellEdges = new int[numPoints][];

            // Find one incoming half-edge per point
            var pointToEdge = new int[numPoints];
            for (int i = 0; i < numPoints; i++)
                pointToEdge[i] = -1;

            for (int e = 0; e < hull.Triangles.Length; e++)
            {
                int p = hull.Triangles[hull.NextHalfedge(e)];
                if (pointToEdge[p] < 0)
                    pointToEdge[p] = e;
            }

            for (int p = 0; p < numPoints; p++)
            {
                if (pointToEdge[p] < 0)
                    continue;

                var edges = hull.EdgesAroundPoint(pointToEdge[p]);
                var cellVerts = new int[edges.Length];
                var cellNeighbors = new int[edges.Length];

                for (int i = 0; i < edges.Length; i++)
                {
                    // Voronoi vertex = triangle containing this half-edge
                    cellVerts[i] = hull.TriangleOfEdge(edges[i]);
                    // Neighbor = point at the start of this half-edge
                    cellNeighbors[i] = hull.Triangles[edges[i]];
                }

                mesh.CellVertices[p] = cellVerts;
                mesh.CellNeighbors[p] = cellNeighbors;
            }

            // Build explicit edge list
            BuildEdges(mesh, hull);

            return mesh;
        }

        static void BuildEdges(SphereMesh mesh, ConvexHull hull)
        {
            int numPoints = hull.Points.Length;
            var edges = new List<(int V0, int V1)>();
            var edgeCells = new List<(int C0, int C1)>();
            var cellEdgeLists = new List<int>[numPoints];
            for (int i = 0; i < numPoints; i++)
                cellEdgeLists[i] = new List<int>();

            for (int e = 0; e < hull.Triangles.Length; e++)
            {
                int opposite = hull.Halfedges[e];

                // Only process each edge once (lower half-edge index)
                if (opposite >= 0 && opposite < e)
                    continue;

                // Voronoi edge connects circumcenters of adjacent triangles
                int t0 = hull.TriangleOfEdge(e);
                int t1 = opposite >= 0 ? hull.TriangleOfEdge(opposite) : -1;

                if (t1 < 0)
                    continue; // Shouldn't happen on closed hull

                // The two cells separated by this Delaunay edge
                int c0 = hull.Triangles[e];
                int c1 = hull.Triangles[hull.NextHalfedge(e)];

                int edgeIndex = edges.Count;
                edges.Add((t0, t1));
                edgeCells.Add((c0, c1));

                cellEdgeLists[c0].Add(edgeIndex);
                cellEdgeLists[c1].Add(edgeIndex);
            }

            mesh.EdgeVertices = edges.ToArray();
            mesh.EdgeCells = edgeCells.ToArray();

            for (int i = 0; i < numPoints; i++)
                mesh.CellEdges[i] = cellEdgeLists[i].ToArray();
        }
    }
}
