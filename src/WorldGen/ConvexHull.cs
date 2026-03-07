using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// 3D convex hull result with half-edge representation.
    /// For points on a unit sphere, this produces the spherical Delaunay triangulation.
    /// Output uses half-edge representation matching MapGen's Delaunay convention.
    /// </summary>
    public class ConvexHull
    {
        /// <summary>Input points</summary>
        public Vec3[] Points;

        /// <summary>
        /// Triangle vertex indices. Every 3 consecutive values form a triangle.
        /// Triangles[e] = point index where half-edge e starts.
        /// Faces are oriented outward (normals point away from origin).
        /// </summary>
        public int[] Triangles;

        /// <summary>
        /// Half-edge opposites. Halfedges[e] = opposite half-edge.
        /// On a closed convex hull, all values are valid (no -1 boundary edges).
        /// </summary>
        public int[] Halfedges;

        /// <summary>Number of triangles</summary>
        public int TriangleCount => Triangles.Length / 3;

        /// <summary>Get the triangle index for a half-edge</summary>
        public int TriangleOfEdge(int e) => e / 3;

        /// <summary>Get next half-edge in triangle (CCW)</summary>
        public int NextHalfedge(int e) => (e % 3 == 2) ? e - 2 : e + 1;

        /// <summary>Get previous half-edge in triangle (CW)</summary>
        public int PrevHalfedge(int e) => (e % 3 == 0) ? e + 2 : e - 1;

        /// <summary>Get the 3 point indices of a triangle</summary>
        public (int, int, int) PointsOfTriangle(int t)
        {
            return (Triangles[3 * t], Triangles[3 * t + 1], Triangles[3 * t + 2]);
        }

        /// <summary>Get adjacent triangles (via opposite half-edges)</summary>
        public int[] AdjacentTriangles(int t)
        {
            int t0 = TriangleOfEdge(Halfedges[3 * t]);
            int t1 = TriangleOfEdge(Halfedges[3 * t + 1]);
            int t2 = TriangleOfEdge(Halfedges[3 * t + 2]);
            return new int[] { t0, t1, t2 };
        }

        /// <summary>
        /// Get all half-edges around a point (incoming edges).
        /// On a closed hull, this always forms a complete loop.
        /// </summary>
        public int[] EdgesAroundPoint(int start)
        {
            var result = new List<int>();
            int incoming = start;
            do
            {
                result.Add(incoming);
                int outgoing = NextHalfedge(incoming);
                incoming = Halfedges[outgoing];
            }
            while (incoming != start && result.Count < 1000);

            return result.ToArray();
        }

        /// <summary>
        /// Build a 3D convex hull from the given points using Quickhull.
        /// </summary>
        public static ConvexHull Build(Vec3[] points) => QuickhullBuilder.Build(points);

        /// <summary>
        /// Circumcenter of a hull triangle = outward face normal, normalized to unit sphere.
        /// This is the Voronoi vertex on the sphere.
        /// </summary>
        public Vec3 Circumcenter(int t)
        {
            var (p0, p1, p2) = PointsOfTriangle(t);
            Vec3 a = Points[p0], b = Points[p1], c = Points[p2];
            Vec3 normal = Vec3.Cross(b - a, c - a);
            // Ensure outward-facing (dot with centroid should be positive)
            Vec3 centroid = (a + b + c) * (1f / 3f);
            if (Vec3.Dot(normal, centroid) < 0)
                normal = -normal;
            return normal.Normalized;
        }
    }
}
