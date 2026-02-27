using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// 3D convex hull using incremental algorithm.
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

    /// <summary>
    /// Builds 3D convex hull using incremental algorithm.
    /// Optimized for points on a sphere (all points are on the hull).
    /// </summary>
    public static class ConvexHullBuilder
    {
        // Working face during construction
        struct Face
        {
            public int V0, V1, V2;     // Vertex indices (CCW when viewed from outside)
            public Vec3 Normal;         // Outward-pointing normal
            public bool Alive;          // False if deleted
        }

        public static ConvexHull Build(Vec3[] points)
        {
            if (points.Length < 4)
                throw new ArgumentException("Need at least 4 points", nameof(points));

            int n = points.Length;

            // Phase 1: Find initial tetrahedron
            int i0, i1, i2, i3;
            FindInitialTetrahedron(points, out i0, out i1, out i2, out i3);

            // Working storage
            var faces = new List<Face>();
            // halfEdgeMap: (minPt, maxPt, face with edge going minPt->maxPt or maxPt->minPt)
            // We need to track which face owns which directed edge.
            // Key: encoded directed edge (v_from, v_to), Value: (faceIndex, edgeIndexInFace 0/1/2)
            var edgeToFace = new Dictionary<long, int>(); // directed edge -> half-edge index (face*3 + localEdge)

            // Create initial tetrahedron (4 faces)
            // Ensure all faces point outward
            Vec3 center = (points[i0] + points[i1] + points[i2] + points[i3]) * 0.25f;

            AddFace(faces, edgeToFace, points, i0, i1, i2, center);
            AddFace(faces, edgeToFace, points, i0, i2, i3, center);
            AddFace(faces, edgeToFace, points, i0, i3, i1, center);
            AddFace(faces, edgeToFace, points, i1, i3, i2, center);

            // Track which points are already in the hull
            var inHull = new bool[n];
            inHull[i0] = true;
            inHull[i1] = true;
            inHull[i2] = true;
            inHull[i3] = true;

            // Phase 2: Insert remaining points
            var visibleFaces = new List<int>();
            var horizonEdges = new List<(int From, int To)>();

            for (int pi = 0; pi < n; pi++)
            {
                if (inHull[pi]) continue;

                Vec3 p = points[pi];

                // Find all faces visible from p
                visibleFaces.Clear();
                for (int fi = 0; fi < faces.Count; fi++)
                {
                    if (!faces[fi].Alive) continue;
                    // Face is visible if point is above its plane
                    Face f = faces[fi];
                    Vec3 facePoint = points[f.V0];
                    if (Vec3.Dot(f.Normal, p - facePoint) > 1e-8f)
                        visibleFaces.Add(fi);
                }

                if (visibleFaces.Count == 0)
                    continue; // Point is inside hull (shouldn't happen for sphere points)

                // Find horizon edges: edges of visible faces whose opposite face is NOT visible
                horizonEdges.Clear();
                var visibleSet = new HashSet<int>(visibleFaces);

                for (int vi = 0; vi < visibleFaces.Count; vi++)
                {
                    int fi = visibleFaces[vi];
                    Face f = faces[fi];
                    int[] verts = { f.V0, f.V1, f.V2 };

                    for (int ei = 0; ei < 3; ei++)
                    {
                        int from = verts[ei];
                        int to = verts[(ei + 1) % 3];

                        // Find the face on the other side of this edge
                        long oppositeKey = DirectedEdgeKey(to, from);
                        if (edgeToFace.TryGetValue(oppositeKey, out int oppositeHe))
                        {
                            int oppositeFace = oppositeHe / 3;
                            if (!visibleSet.Contains(oppositeFace))
                            {
                                // This is a horizon edge (from the perspective of the visible face)
                                // The horizon edge goes from->to in the visible face,
                                // so in the new fan it should go to->from (to maintain winding)
                                horizonEdges.Add((to, from));
                            }
                        }
                    }
                }

                // Remove visible faces
                foreach (int fi in visibleFaces)
                {
                    Face f = faces[fi];
                    RemoveFaceEdges(edgeToFace, f.V0, f.V1, f.V2, fi);
                    f.Alive = false;
                    faces[fi] = f;
                }

                // Create fan of new triangles from horizon edges to new point
                // Need to order horizon edges into a loop
                var orderedHorizon = OrderHorizonLoop(horizonEdges);

                for (int hi = 0; hi < orderedHorizon.Count; hi++)
                {
                    var (from, to) = orderedHorizon[hi];
                    AddFace(faces, edgeToFace, points, from, to, pi, center);
                }

                inHull[pi] = true;
            }

            // Phase 3: Compact into flat arrays
            return Compact(points, faces, edgeToFace);
        }

        static void FindInitialTetrahedron(Vec3[] points, out int i0, out int i1, out int i2, out int i3)
        {
            int n = points.Length;

            // Pick first point: the one with max X
            i0 = 0;
            for (int i = 1; i < n; i++)
                if (points[i].X > points[i0].X) i0 = i;

            // Pick second point: farthest from i0
            i1 = 0;
            float maxDist = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == i0) continue;
                float d = Vec3.SqrDistance(points[i], points[i0]);
                if (d > maxDist) { maxDist = d; i1 = i; }
            }

            // Pick third point: maximizing triangle area (cross product magnitude)
            i2 = 0;
            float maxCross = 0;
            Vec3 d01 = points[i1] - points[i0];
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1) continue;
                Vec3 d02 = points[i] - points[i0];
                float crossMag = Vec3.Cross(d01, d02).SqrMagnitude;
                if (crossMag > maxCross) { maxCross = crossMag; i2 = i; }
            }

            // Pick fourth point: maximizing tetrahedron volume (scalar triple product)
            i3 = 0;
            float maxVol = 0;
            Vec3 normal012 = Vec3.Cross(points[i1] - points[i0], points[i2] - points[i0]);
            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1 || i == i2) continue;
                float vol = Math.Abs(Vec3.Dot(normal012, points[i] - points[i0]));
                if (vol > maxVol) { maxVol = vol; i3 = i; }
            }

            if (maxVol < 1e-10f)
                throw new InvalidOperationException("Points are coplanar, cannot build tetrahedron");
        }

        static long DirectedEdgeKey(int from, int to)
        {
            return ((long)from << 32) | (uint)to;
        }

        static void AddFace(List<Face> faces, Dictionary<long, int> edgeToFace,
            Vec3[] points, int v0, int v1, int v2, Vec3 interiorPoint)
        {
            Vec3 a = points[v0], b = points[v1], c = points[v2];
            Vec3 normal = Vec3.Cross(b - a, c - a);

            // Ensure normal points outward (away from interior point)
            Vec3 centroid = (a + b + c) * (1f / 3f);
            if (Vec3.Dot(normal, centroid - interiorPoint) < 0)
            {
                // Flip winding
                int tmp = v1;
                v1 = v2;
                v2 = tmp;
                normal = -normal;
            }

            normal = normal.Normalized;

            int fi = faces.Count;
            faces.Add(new Face { V0 = v0, V1 = v1, V2 = v2, Normal = normal, Alive = true });

            // Register directed edges
            edgeToFace[DirectedEdgeKey(v0, v1)] = fi * 3 + 0;
            edgeToFace[DirectedEdgeKey(v1, v2)] = fi * 3 + 1;
            edgeToFace[DirectedEdgeKey(v2, v0)] = fi * 3 + 2;
        }

        static void RemoveFaceEdges(Dictionary<long, int> edgeToFace, int v0, int v1, int v2, int fi)
        {
            edgeToFace.Remove(DirectedEdgeKey(v0, v1));
            edgeToFace.Remove(DirectedEdgeKey(v1, v2));
            edgeToFace.Remove(DirectedEdgeKey(v2, v0));
        }

        static List<(int, int)> OrderHorizonLoop(List<(int From, int To)> edges)
        {
            if (edges.Count == 0) return new List<(int, int)>();

            var result = new List<(int, int)>(edges.Count);
            var remaining = new Dictionary<int, (int From, int To)>(edges.Count);

            foreach (var e in edges)
                remaining[e.From] = e;

            var first = edges[0];
            result.Add(first);
            remaining.Remove(first.From);

            int current = first.To;
            while (remaining.Count > 0)
            {
                if (!remaining.TryGetValue(current, out var next))
                    throw new InvalidOperationException(
                        $"Horizon loop broken at vertex {current}, {remaining.Count} edges remaining");

                result.Add(next);
                remaining.Remove(current);
                current = next.To;
            }

            return result;
        }

        static ConvexHull Compact(Vec3[] points, List<Face> faces, Dictionary<long, int> edgeToFace)
        {
            // Count alive faces
            int aliveCount = 0;
            for (int i = 0; i < faces.Count; i++)
                if (faces[i].Alive) aliveCount++;

            var triangles = new int[aliveCount * 3];
            var halfedges = new int[aliveCount * 3];

            // Map old face index -> new face index
            var faceMap = new int[faces.Count];
            int newIdx = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                if (faces[i].Alive)
                {
                    faceMap[i] = newIdx;
                    Face f = faces[i];
                    triangles[newIdx * 3 + 0] = f.V0;
                    triangles[newIdx * 3 + 1] = f.V1;
                    triangles[newIdx * 3 + 2] = f.V2;
                    newIdx++;
                }
                else
                {
                    faceMap[i] = -1;
                }
            }

            // Build halfedge links
            for (int i = 0; i < aliveCount; i++)
            {
                int v0 = triangles[i * 3 + 0];
                int v1 = triangles[i * 3 + 1];
                int v2 = triangles[i * 3 + 2];

                // Edge 0: v0->v1, opposite is v1->v0
                halfedges[i * 3 + 0] = FindOpposite(edgeToFace, faceMap, v1, v0);
                // Edge 1: v1->v2, opposite is v2->v1
                halfedges[i * 3 + 1] = FindOpposite(edgeToFace, faceMap, v2, v1);
                // Edge 2: v2->v0, opposite is v0->v2
                halfedges[i * 3 + 2] = FindOpposite(edgeToFace, faceMap, v0, v2);
            }

            return new ConvexHull
            {
                Points = points,
                Triangles = triangles,
                Halfedges = halfedges
            };
        }

        static int FindOpposite(Dictionary<long, int> edgeToFace, int[] faceMap, int from, int to)
        {
            long key = DirectedEdgeKey(from, to);
            if (edgeToFace.TryGetValue(key, out int oldHe))
            {
                int oldFace = oldHe / 3;
                int localEdge = oldHe % 3;
                int newFace = faceMap[oldFace];
                if (newFace >= 0)
                    return newFace * 3 + localEdge;
            }
            return -1; // Should not happen on a closed hull
        }
    }
}
