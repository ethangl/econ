using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Builds CellMesh (Voronoi diagram) from Delaunay triangulation.
    /// </summary>
    public static class VoronoiBuilder
    {
        /// <summary>
        /// Build Voronoi mesh from points.
        /// </summary>
        /// <param name="width">Map width</param>
        /// <param name="height">Map height</param>
        /// <param name="gridPoints">Interior cell seed points</param>
        /// <param name="boundaryPoints">Boundary padding points</param>
        public static CellMesh Build(float width, float height, Vec2[] gridPoints, Vec2[] boundaryPoints)
        {
            int interiorCount = gridPoints.Length;
            var allPoints = PointGenerator.CombinePoints(gridPoints, boundaryPoints);

            // Build Delaunay triangulation
            var delaunay = DelaunayBuilder.Build(allPoints);

            // Build Voronoi from Delaunay
            return BuildFromDelaunay(width, height, delaunay, interiorCount);
        }

        /// <summary>
        /// Build Voronoi mesh from existing Delaunay triangulation.
        /// </summary>
        /// <param name="width">Map width</param>
        /// <param name="height">Map height</param>
        /// <param name="delaunay">Delaunay triangulation</param>
        /// <param name="interiorCount">Number of interior points (rest are boundary)</param>
        public static CellMesh BuildFromDelaunay(float width, float height, Delaunay delaunay, int interiorCount)
        {
            var mesh = new CellMesh
            {
                Width = width,
                Height = height
            };

            // Vertices are circumcenters of Delaunay triangles
            int vertexCount = delaunay.TriangleCount;
            mesh.Vertices = new Vec2[vertexCount];
            mesh.VertexCells = new int[vertexCount][];
            mesh.VertexNeighbors = new int[vertexCount][];

            for (int t = 0; t < vertexCount; t++)
            {
                mesh.Vertices[t] = delaunay.Circumcenter(t);

                // Cells adjacent to this vertex = points of the triangle
                var (p0, p1, p2) = delaunay.PointsOfTriangle(t);
                int cellCount = 0;
                if (p0 < interiorCount) cellCount++;
                if (p1 < interiorCount) cellCount++;
                if (p2 < interiorCount) cellCount++;

                var cells = new int[cellCount];
                int ci = 0;
                if (p0 < interiorCount) cells[ci++] = p0;
                if (p1 < interiorCount) cells[ci++] = p1;
                if (p2 < interiorCount) cells[ci++] = p2;
                mesh.VertexCells[t] = cells;

                // Neighboring vertices = adjacent triangles
                mesh.VertexNeighbors[t] = delaunay.AdjacentTriangles(t);
            }

            // Build cell data (only for interior points)
            mesh.CellCenters = new Vec2[interiorCount];
            mesh.CellVertices = new int[interiorCount][];
            mesh.CellNeighbors = new int[interiorCount][];
            mesh.CellEdges = new int[interiorCount][];
            mesh.CellIsBoundary = new bool[interiorCount];

            Array.Copy(delaunay.Points, mesh.CellCenters, interiorCount);

            // For each interior point, find its Voronoi cell
            // Process half-edges to build cell data
            var processedPoints = new bool[delaunay.Points.Length];

            for (int e = 0; e < delaunay.Triangles.Length; e++)
            {
                int p = delaunay.Triangles[delaunay.NextHalfedge(e)];

                if (p >= interiorCount || processedPoints[p])
                    continue;

                processedPoints[p] = true;

                // Get all edges around this point
                var edges = delaunay.EdgesAroundPoint(e);
                var cellVertices = new int[edges.Length];
                int neighborCount = 0;

                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    int edge = edges[edgeIndex];
                    // Vertex = triangle containing this edge
                    int vertex = delaunay.TriangleOfEdge(edge);
                    cellVertices[edgeIndex] = vertex;

                    // Neighbor = point at start of this edge
                    int neighbor = delaunay.Triangles[edge];
                    if (neighbor < interiorCount)
                        neighborCount++;
                }

                var cellNeighbors = new int[neighborCount];
                int neighborIdx = 0;
                for (int edgeIndex = 0; edgeIndex < edges.Length; edgeIndex++)
                {
                    int neighbor = delaunay.Triangles[edges[edgeIndex]];
                    if (neighbor < interiorCount)
                        cellNeighbors[neighborIdx++] = neighbor;
                }

                // Check if cell is on boundary (has fewer neighbors than vertices)
                // or if loop didn't close
                mesh.CellVertices[p] = cellVertices;
                mesh.CellNeighbors[p] = cellNeighbors;
                mesh.CellIsBoundary[p] = edges.Length > neighborCount;
            }

            // Build explicit edge list
            BuildEdges(mesh, delaunay, interiorCount);

            return mesh;
        }

        /// <summary>
        /// Build explicit edge list from Delaunay half-edges.
        /// Each edge is a Voronoi edge (between two circumcenters).
        /// </summary>
        private static void BuildEdges(CellMesh mesh, Delaunay delaunay, int interiorCount)
        {
            var edges = new List<(int V0, int V1)>(delaunay.Triangles.Length / 2);
            var edgeCells = new List<(int C0, int C1)>(delaunay.Triangles.Length / 2);

            // Cell edge lists
            var cellEdgeLists = new List<int>[interiorCount];
            for (int i = 0; i < interiorCount; i++)
                cellEdgeLists[i] = new List<int>();

            // Each half-edge in Delaunay corresponds to a Voronoi edge
            for (int e = 0; e < delaunay.Triangles.Length; e++)
            {
                int opposite = delaunay.Halfedges[e];

                // Only process each edge once (use the lower half-edge index)
                if (opposite >= 0 && opposite < e)
                    continue;

                // Voronoi edge connects circumcenters of adjacent triangles
                int t0 = delaunay.TriangleOfEdge(e);
                int t1 = opposite >= 0 ? delaunay.TriangleOfEdge(opposite) : -1;

                // Skip boundary edges (no second triangle)
                if (t1 < 0)
                    continue;

                // The two cells separated by this edge
                int p0 = delaunay.Triangles[e];
                int p1 = delaunay.Triangles[delaunay.NextHalfedge(e)];

                // Normalize cell indices (-1 for boundary cells)
                int c0 = p0 < interiorCount ? p0 : -1;
                int c1 = p1 < interiorCount ? p1 : -1;

                // Skip edges that only touch boundary cells
                if (c0 < 0 && c1 < 0)
                    continue;

                int edgeIndex = edges.Count;
                edges.Add((t0, t1));
                edgeCells.Add((c0, c1));

                // Add to cell edge lists
                if (c0 >= 0)
                    cellEdgeLists[c0].Add(edgeIndex);
                if (c1 >= 0)
                    cellEdgeLists[c1].Add(edgeIndex);
            }

            mesh.EdgeVertices = edges.ToArray();
            mesh.EdgeCells = edgeCells.ToArray();

            for (int i = 0; i < interiorCount; i++)
                mesh.CellEdges[i] = cellEdgeLists[i].ToArray();
        }
    }
}
