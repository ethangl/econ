using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Voronoi tessellation on a sphere. Spherical analog of MapGen's CellMesh.
    /// Every cell is interior (no boundary on a sphere).
    /// </summary>
    public class SphereMesh
    {
        /// <summary>Cell seed points on the sphere surface</summary>
        public Vec3[] CellCenters;

        /// <summary>For each cell: indices of vertices forming its polygon (CCW viewed from outside)</summary>
        public int[][] CellVertices;

        /// <summary>For each cell: indices of neighboring cells</summary>
        public int[][] CellNeighbors;

        /// <summary>For each cell: indices of edges forming its boundary</summary>
        public int[][] CellEdges;

        /// <summary>Voronoi vertex positions (circumcenters of Delaunay triangles, on sphere surface)</summary>
        public Vec3[] Vertices;

        /// <summary>For each vertex: indices of cells that meet at this vertex</summary>
        public int[][] VertexCells;

        /// <summary>For each vertex: indices of neighboring vertices</summary>
        public int[][] VertexNeighbors;

        /// <summary>Edge endpoints (pairs of vertex indices)</summary>
        public (int V0, int V1)[] EdgeVertices;

        /// <summary>For each edge: the two cells it separates</summary>
        public (int C0, int C1)[] EdgeCells;

        /// <summary>Area of each cell (units depend on radius)</summary>
        public float[] CellAreas;

        /// <summary>Sphere radius</summary>
        public float Radius;

        /// <summary>Number of cells</summary>
        public int CellCount => CellCenters?.Length ?? 0;

        /// <summary>Number of Voronoi vertices</summary>
        public int VertexCount => Vertices?.Length ?? 0;

        /// <summary>Number of edges</summary>
        public int EdgeCount => EdgeVertices?.Length ?? 0;

        /// <summary>
        /// Compute area of each cell using the spherical excess formula.
        /// Area = R^2 * (sum of interior angles - (n-2)*pi)
        /// </summary>
        public void ComputeAreas()
        {
            int n = CellCount;
            CellAreas = new float[n];
            float r2 = Radius * Radius;

            for (int c = 0; c < n; c++)
            {
                int[] verts = CellVertices[c];
                if (verts == null || verts.Length < 3)
                    continue;

                int len = verts.Length;
                float angleSum = 0f;

                for (int i = 0; i < len; i++)
                {
                    Vec3 prev = Vertices[verts[(i + len - 1) % len]];
                    Vec3 curr = Vertices[verts[i]];
                    Vec3 next = Vertices[verts[(i + 1) % len]];

                    // Compute interior angle at curr on the sphere
                    // Tangent vectors: project prev-curr and next-curr onto plane tangent to sphere at curr
                    Vec3 toPrev = prev - curr;
                    Vec3 toNext = next - curr;

                    // Project onto tangent plane (remove radial component)
                    Vec3 radial = curr.Normalized;
                    toPrev = toPrev - radial * Vec3.Dot(toPrev, radial);
                    toNext = toNext - radial * Vec3.Dot(toNext, radial);

                    float prevMag = toPrev.Magnitude;
                    float nextMag = toNext.Magnitude;
                    if (prevMag < 1e-10f || nextMag < 1e-10f)
                        continue;

                    float cosAngle = Vec3.Dot(toPrev, toNext) / (prevMag * nextMag);
                    cosAngle = Math.Max(-1f, Math.Min(1f, cosAngle));
                    angleSum += (float)Math.Acos(cosAngle);
                }

                // Spherical excess = sum of angles - (n-2)*pi
                float excess = angleSum - (len - 2) * (float)Math.PI;
                CellAreas[c] = r2 * Math.Max(0f, excess);
            }
        }
    }
}
