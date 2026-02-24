using System;
using DelaunatorSharp;

namespace MapGen.Core
{
    /// <summary>
    /// Point adapter for DelaunatorSharp.
    /// </summary>
    internal struct DelaunayPoint : DelaunatorSharp.IPoint
    {
        public double X { get; set; }
        public double Y { get; set; }

        public DelaunayPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public DelaunayPoint(Vec2 v)
        {
            X = v.X;
            Y = v.Y;
        }
    }

    /// <summary>
    /// Delaunay triangulation result using half-edge representation.
    /// Compatible with Delaunator library conventions.
    /// </summary>
    public class Delaunay
    {
        /// <summary>Input points</summary>
        public Vec2[] Points;

        /// <summary>
        /// Triangle vertex indices. Every 3 consecutive values form a triangle.
        /// triangles[e] = point index where half-edge e starts.
        /// </summary>
        public int[] Triangles;

        /// <summary>
        /// Half-edge opposites. halfedges[e] = opposite half-edge, or -1 if boundary.
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

        /// <summary>Get the 3 half-edges of a triangle</summary>
        public (int, int, int) EdgesOfTriangle(int t) => (3 * t, 3 * t + 1, 3 * t + 2);

        /// <summary>Get the 3 point indices of a triangle</summary>
        public (int, int, int) PointsOfTriangle(int t)
        {
            return (Triangles[3 * t], Triangles[3 * t + 1], Triangles[3 * t + 2]);
        }

        /// <summary>Get adjacent triangles (via opposite half-edges)</summary>
        public int[] AdjacentTriangles(int t)
        {
            int e0 = Halfedges[3 * t];
            int e1 = Halfedges[3 * t + 1];
            int e2 = Halfedges[3 * t + 2];

            int count = 0;
            if (e0 >= 0) count++;
            if (e1 >= 0) count++;
            if (e2 >= 0) count++;

            var result = new int[count];
            int idx = 0;
            if (e0 >= 0) result[idx++] = TriangleOfEdge(e0);
            if (e1 >= 0) result[idx++] = TriangleOfEdge(e1);
            if (e2 >= 0) result[idx++] = TriangleOfEdge(e2);
            return result;
        }

        /// <summary>
        /// Get all half-edges around a point (incoming edges).
        /// Returns edges in CCW order.
        /// </summary>
        public int[] EdgesAroundPoint(int start)
        {
            int count = 0;
            int incoming = start;

            do
            {
                count++;
                int outgoing = NextHalfedge(incoming);
                incoming = Halfedges[outgoing];
            }
            while (incoming >= 0 && incoming != start && count < 100);

            var result = new int[count];
            incoming = start;
            int idx = 0;
            do
            {
                result[idx++] = incoming;
                int outgoing = NextHalfedge(incoming);
                incoming = Halfedges[outgoing];
            }
            while (incoming >= 0 && incoming != start && idx < count);

            return result;
        }

        /// <summary>Calculate circumcenter of a triangle</summary>
        public Vec2 Circumcenter(int t)
        {
            var (p0, p1, p2) = PointsOfTriangle(t);
            return Circumcenter(Points[p0], Points[p1], Points[p2]);
        }

        /// <summary>Calculate circumcenter of three points</summary>
        public static Vec2 Circumcenter(Vec2 a, Vec2 b, Vec2 c)
        {
            float ax = a.X, ay = a.Y;
            float bx = b.X, by = b.Y;
            float cx = c.X, cy = c.Y;

            float d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

            if (Math.Abs(d) < 1e-10f)
            {
                // Degenerate triangle, return centroid
                return new Vec2((ax + bx + cx) / 3, (ay + by + cy) / 3);
            }

            float ad = ax * ax + ay * ay;
            float bd = bx * bx + by * by;
            float cd = cx * cx + cy * cy;

            float x = (ad * (by - cy) + bd * (cy - ay) + cd * (ay - by)) / d;
            float y = (ad * (cx - bx) + bd * (ax - cx) + cd * (bx - ax)) / d;

            return new Vec2(x, y);
        }
    }

    /// <summary>
    /// Builds Delaunay triangulation using DelaunatorSharp.
    /// </summary>
    public static class DelaunayBuilder
    {
        /// <summary>
        /// Build Delaunay triangulation from points.
        /// </summary>
        public static Delaunay Build(Vec2[] points)
        {
            if (points.Length < 3)
                throw new ArgumentException("Need at least 3 points");

            // Convert to DelaunatorSharp points
            var delaunayPoints = new DelaunatorSharp.IPoint[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                delaunayPoints[i] = new DelaunayPoint(points[i]);
            }

            // Run Delaunator
            var delaunator = new DelaunatorSharp.Delaunator(delaunayPoints);

            return new Delaunay
            {
                Points = points,
                Triangles = delaunator.Triangles,
                Halfedges = delaunator.Halfedges
            };
        }
    }
}
