using System;
using System.Threading.Tasks;

namespace MapGen.Core
{
    /// <summary>
    /// Core data structure for Voronoi cell mesh.
    /// Stores cells, vertices, and edges with full adjacency information.
    /// </summary>
    public class CellMesh
    {
        /// <summary>Cell seed points (Voronoi generators)</summary>
        public Vec2[] CellCenters;

        /// <summary>For each cell: indices of vertices forming its polygon (CCW order)</summary>
        public int[][] CellVertices;

        /// <summary>For each cell: indices of neighboring cells</summary>
        public int[][] CellNeighbors;

        /// <summary>For each cell: indices of edges forming its boundary</summary>
        public int[][] CellEdges;

        /// <summary>Is this cell on the map boundary?</summary>
        public bool[] CellIsBoundary;

        /// <summary>Voronoi vertex positions (circumcenters of Delaunay triangles)</summary>
        public Vec2[] Vertices;

        /// <summary>For each vertex: indices of cells that meet at this vertex</summary>
        public int[][] VertexCells;

        /// <summary>For each vertex: indices of neighboring vertices</summary>
        public int[][] VertexNeighbors;

        /// <summary>Edge endpoints (pairs of vertex indices)</summary>
        public (int V0, int V1)[] EdgeVertices;

        /// <summary>For each edge: the two cells it separates (C1 may be -1 for boundary)</summary>
        public (int C0, int C1)[] EdgeCells;

        /// <summary>Area of each cell polygon (km²). Call ComputeAreas() to populate.</summary>
        public float[] CellAreas;

        /// <summary>Map dimensions</summary>
        public float Width;
        public float Height;

        /// <summary>Number of cells (excluding boundary padding)</summary>
        public int CellCount => CellCenters?.Length ?? 0;

        /// <summary>Number of vertices</summary>
        public int VertexCount => Vertices?.Length ?? 0;

        /// <summary>Number of edges</summary>
        public int EdgeCount => EdgeVertices?.Length ?? 0;

        /// <summary>
        /// Compute area of each cell polygon using the shoelace formula.
        /// Populates the CellAreas array.
        /// </summary>
        public void ComputeAreas()
        {
            int n = CellCount;
            CellAreas = new float[n];

            ParallelOps.For(0, n, c =>
            {
                int[] verts = CellVertices[c];
                if (verts == null || verts.Length < 3)
                    return;

                float area = 0f;
                int len = verts.Length;
                for (int i = 0; i < len; i++)
                {
                    var a = Vertices[verts[i]];
                    var b = Vertices[verts[(i + 1) % len]];
                    area += a.X * b.Y - b.X * a.Y;
                }
                CellAreas[c] = System.Math.Abs(area) * 0.5f;
            });
        }
    }

    /// <summary>
    /// Simple 2D vector for engine-independent code.
    /// </summary>
    public struct Vec2
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 v, float s) => new Vec2(v.X * s, v.Y * s);
        public static Vec2 operator *(float s, Vec2 v) => new Vec2(v.X * s, v.Y * s);

        public float SqrMagnitude => X * X + Y * Y;
        public float Magnitude => (float)System.Math.Sqrt(SqrMagnitude);

        public Vec2 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec2(X / m, Y / m) : new Vec2(0, 0);
            }
        }

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrMagnitude;

        public override string ToString() => $"({X:F2}, {Y:F2})";
    }

    internal static class ParallelOps
    {
        const int MinParallelBatchSize = 4096;

        public static void For(int fromInclusive, int toExclusive, Action<int> body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));

            int length = toExclusive - fromInclusive;
            if (length <= 0)
                return;

            if (length < MinParallelBatchSize || Environment.ProcessorCount < 2)
            {
                for (int i = fromInclusive; i < toExclusive; i++)
                    body(i);
                return;
            }

            Parallel.For(fromInclusive, toExclusive, body);
        }
    }
}
