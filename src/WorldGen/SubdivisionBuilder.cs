using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WorldGen.Core
{
    /// <summary>
    /// Midpoint subdivision of a convex hull on the unit sphere.
    /// Produces 4x triangles per level with jittered midpoints to break regularity.
    /// After subdivision, edge flips restore the Delaunay property so that
    /// SphericalVoronoiBuilder produces non-overlapping Voronoi cells.
    /// </summary>
    public static class SubdivisionBuilder
    {
        /// <summary>
        /// Subdivide a convex hull once, producing ~4x triangles.
        /// Each triangle becomes 4 sub-triangles via midpoint insertion on edges.
        /// Midpoints are jittered in the tangent plane then re-projected to the unit sphere.
        /// Edge flips restore the Delaunay property after subdivision.
        /// </summary>
        public static ConvexHull Subdivide(ConvexHull hull, float jitter, Random rng, DenseTerrainTimingData timings = null)
        {
            var stepSw = Stopwatch.StartNew();
            int origVertCount = hull.Points.Length;
            int origTriCount = hull.TriangleCount;
            int origEdgeCount = hull.Triangles.Length; // half-edge count

            // 1. Identify unique edges and assign midpoint vertex indices.
            int[] midpointIndex = new int[origEdgeCount];
            for (int i = 0; i < origEdgeCount; i++)
                midpointIndex[i] = -1;

            int midpointCount = 0;
            for (int e = 0; e < origEdgeCount; e++)
            {
                int opp = hull.Halfedges[e];
                int canonical = Math.Min(e, opp);
                if (midpointIndex[canonical] < 0)
                {
                    midpointIndex[canonical] = origVertCount + midpointCount;
                    midpointCount++;
                }
                midpointIndex[e] = midpointIndex[canonical];
            }

            // 2. Build new points array: original vertices + midpoints
            int newVertCount = origVertCount + midpointCount;
            var newPoints = new Vec3[newVertCount];
            Array.Copy(hull.Points, newPoints, origVertCount);

            var processed = new bool[origEdgeCount];
            for (int e = 0; e < origEdgeCount; e++)
            {
                int opp = hull.Halfedges[e];
                int canonical = Math.Min(e, opp);
                if (processed[canonical]) continue;
                processed[canonical] = true;

                int p0 = hull.Triangles[e];
                int p1 = hull.Triangles[hull.NextHalfedge(e)];
                Vec3 a = hull.Points[p0];
                Vec3 b = hull.Points[p1];

                Vec3 mid = (a + b).Normalized;

                if (jitter > 0f && rng != null)
                {
                    float edgeLen = Vec3.Distance(a, b);
                    float rx = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float ry = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float rz = (float)(rng.NextDouble() * 2.0 - 1.0);
                    Vec3 randVec = new Vec3(rx, ry, rz);
                    Vec3 tangent = randVec - mid * Vec3.Dot(randVec, mid);
                    float tangentMag = tangent.Magnitude;
                    if (tangentMag > 1e-6f)
                    {
                        tangent = tangent * (1f / tangentMag);
                        float displacement = jitter * edgeLen * (float)rng.NextDouble();
                        mid = (mid + tangent * displacement).Normalized;
                    }
                }

                newPoints[midpointIndex[canonical]] = mid;
            }

            // 3. Build sub-triangles (4 per original triangle)
            int newTriCount = origTriCount * 4;
            int newEdgeCount = newTriCount * 3;
            var newTriangles = new int[newEdgeCount];
            var newHalfedges = new int[newEdgeCount];

            for (int t = 0; t < origTriCount; t++)
            {
                int v0 = hull.Triangles[3 * t + 0];
                int v1 = hull.Triangles[3 * t + 1];
                int v2 = hull.Triangles[3 * t + 2];
                int m0 = midpointIndex[3 * t + 0];
                int m1 = midpointIndex[3 * t + 1];
                int m2 = midpointIndex[3 * t + 2];

                int st0 = 4 * t + 0; // (v0, m0, m2)
                newTriangles[3 * st0 + 0] = v0;
                newTriangles[3 * st0 + 1] = m0;
                newTriangles[3 * st0 + 2] = m2;

                int st1 = 4 * t + 1; // (m0, v1, m1)
                newTriangles[3 * st1 + 0] = m0;
                newTriangles[3 * st1 + 1] = v1;
                newTriangles[3 * st1 + 2] = m1;

                int st2 = 4 * t + 2; // (m2, m1, v2)
                newTriangles[3 * st2 + 0] = m2;
                newTriangles[3 * st2 + 1] = m1;
                newTriangles[3 * st2 + 2] = v2;

                int st3 = 4 * t + 3; // (m0, m1, m2)
                newTriangles[3 * st3 + 0] = m0;
                newTriangles[3 * st3 + 1] = m1;
                newTriangles[3 * st3 + 2] = m2;
            }

            // 4. Half-edge connectivity
            for (int i = 0; i < newEdgeCount; i++)
                newHalfedges[i] = -1;

            for (int t = 0; t < origTriCount; t++)
            {
                int st0 = 4 * t + 0;
                int st1 = 4 * t + 1;
                int st2 = 4 * t + 2;
                int st3 = 4 * t + 3;

                // Interior: center ↔ corners
                newHalfedges[3 * st0 + 1] = 3 * st3 + 2;
                newHalfedges[3 * st3 + 2] = 3 * st0 + 1;
                newHalfedges[3 * st1 + 2] = 3 * st3 + 0;
                newHalfedges[3 * st3 + 0] = 3 * st1 + 2;
                newHalfedges[3 * st2 + 0] = 3 * st3 + 1;
                newHalfedges[3 * st3 + 1] = 3 * st2 + 0;

                // Exterior: cross original boundaries
                for (int j = 0; j < 3; j++)
                {
                    int origEdge = 3 * t + j;
                    int opp = hull.Halfedges[origEdge];
                    if (opp < 0 || origEdge > opp) continue;

                    int tPrime = opp / 3;
                    int jPrime = opp % 3;

                    int thisFirst = 3 * (4 * t + j) + j;
                    int oppSecond = 3 * (4 * tPrime + (jPrime + 1) % 3) + jPrime;
                    newHalfedges[thisFirst] = oppSecond;
                    newHalfedges[oppSecond] = thisFirst;

                    int thisSecond = 3 * (4 * t + (j + 1) % 3) + j;
                    int oppFirst = 3 * (4 * tPrime + jPrime) + jPrime;
                    newHalfedges[thisSecond] = oppFirst;
                    newHalfedges[oppFirst] = thisSecond;
                }
            }

            var result = new ConvexHull
            {
                Points = newPoints,
                Triangles = newTriangles,
                Halfedges = newHalfedges,
            };
            if (timings != null)
                timings.UltraSubdivisionSetupSeconds = stepSw.Elapsed.TotalSeconds;

            // 5. Restore Delaunay property via edge flips
            stepSw.Restart();
            RestoreDelaunay(result);
            if (timings != null)
                timings.UltraSubdivisionRestoreSeconds = stepSw.Elapsed.TotalSeconds;

            return result;
        }

        /// <summary>
        /// Sweep all edges and flip those that violate the spherical Delaunay condition.
        /// Repeat until no flips occur (convergence) or max passes reached.
        /// </summary>
        static void RestoreDelaunay(ConvexHull hull)
        {
            Vec3[] points = hull.Points;
            int[] triangles = hull.Triangles;
            int[] halfedges = hull.Halfedges;
            int edgeCount = triangles.Length;
            const int maxPasses = 100;

            for (int pass = 0; pass < maxPasses; pass++)
            {
                bool flipped = false;

                for (int e = 0; e < edgeCount; e++)
                {
                    int opp = halfedges[e];
                    if (opp < 0 || e > opp) continue;

                    // Vertices: edge e goes A→B, triangle is (A, B, C)
                    // Opposite triangle has vertex D opposite the shared edge
                    int a = triangles[e];
                    int b = triangles[NextHE(e)];
                    int c = triangles[PrevHE(e)];
                    int d = triangles[PrevHE(opp)];

                    if (InCircle(points[a], points[b], points[c], points[d]))
                    {
                        FlipEdge(hull, e, opp);
                        flipped = true;
                    }
                }

                if (!flipped) break;
            }
        }

        /// <summary>
        /// Spherical InCircle test: is point d inside the circumcircle of triangle (a, b, c)?
        /// Uses the circumcenter — d is inside iff it's angularly closer to the circumcenter
        /// than the triangle's own vertices.
        /// </summary>
        static bool InCircle(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            Vec3 normal = Vec3.Cross(b - a, c - a);
            if (Vec3.Dot(normal, a + b + c) < 0)
                normal = -normal;

            float normalMag = normal.Magnitude;
            float cosThreshold = Vec3.Dot(normal, a);
            return Vec3.Dot(normal, d) > cosThreshold + 1e-10f * normalMag;
        }

        /// <summary>
        /// Flip edge shared by half-edges e and opp (Delaunator convention).
        /// Replaces the diagonal of the quadrilateral formed by the two adjacent triangles.
        /// </summary>
        static void FlipEdge(ConvexHull hull, int e, int opp)
        {
            int e2 = PrevHE(e);
            int e5 = PrevHE(opp);

            int ext2 = hull.Halfedges[e2];
            int ext5 = hull.Halfedges[e5];

            // Rewrite triangle vertices
            hull.Triangles[e] = hull.Triangles[e5];   // D replaces A
            hull.Triangles[opp] = hull.Triangles[e2];  // C replaces B

            // Rewrite halfedge links
            hull.Halfedges[e] = ext5;
            hull.Halfedges[opp] = ext2;
            hull.Halfedges[e2] = e5;
            hull.Halfedges[e5] = e2;

            // Fix external back-references
            if (ext5 >= 0) hull.Halfedges[ext5] = e;
            if (ext2 >= 0) hull.Halfedges[ext2] = opp;
        }

        static int NextHE(int e) => (e % 3 == 2) ? e - 2 : e + 1;
        static int PrevHE(int e) => (e % 3 == 0) ? e + 2 : e - 1;

        /// <summary>
        /// Build a per-point mapping from subdivided hull points back to original hull points.
        /// Original vertices (0..V-1) map to themselves.
        /// Midpoint vertices map to the nearer of their two parent edge endpoints.
        /// </summary>
        public static int[] BuildParentMapping(ConvexHull original, ConvexHull subdivided)
        {
            int origCount = original.Points.Length;
            int subCount = subdivided.Points.Length;
            var mapping = new int[subCount];
            var lookup = new NearestCellLookup(original.Points);

            for (int i = 0; i < origCount; i++)
                mapping[i] = i;

            Parallel.For(origCount, subCount, i =>
            {
                mapping[i] = lookup.Nearest(subdivided.Points[i]);
            });

            return mapping;
        }
    }
}
