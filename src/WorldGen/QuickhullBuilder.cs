using System;
using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// Builds 3D convex hull using Quickhull algorithm (O(n log n) average).
    /// Uses conflict lists to partition unassigned points across faces,
    /// and a max-heap for O(log n) face selection instead of linear scan.
    /// </summary>
    public static class QuickhullBuilder
    {
        class QFace
        {
            public int V0, V1, V2;
            public Vec3 Normal;
            public float Dist; // dot(Normal, points[V0])
            public bool Alive;
            public List<int> ConflictList;
            public float FarthestDist;
            public int FarthestPoint;
        }

        // Max-heap keyed by farthest distance, with lazy deletion of stale entries
        struct HeapEntry
        {
            public float Dist;
            public int FaceIndex;
        }

        public static ConvexHull Build(Vec3[] points)
        {
            if (points.Length < 4)
                throw new ArgumentException("Need at least 4 points", nameof(points));

            int n = points.Length;
            int expectedFaceCount = Math.Max(4, 2 * n - 4);
            int expectedHalfedgeCount = expectedFaceCount * 3;

            // Phase 1: Find initial tetrahedron
            FindInitialTetrahedron(points, out int i0, out int i1, out int i2, out int i3);

            Vec3 center = (points[i0] + points[i1] + points[i2] + points[i3]) * 0.25f;

            var faces = new List<QFace>(expectedFaceCount);
            var edgeToFace = new Dictionary<long, int>(expectedHalfedgeCount);

            AddFace(faces, edgeToFace, points, i0, i1, i2, center);
            AddFace(faces, edgeToFace, points, i0, i2, i3, center);
            AddFace(faces, edgeToFace, points, i0, i3, i1, center);
            AddFace(faces, edgeToFace, points, i1, i3, i2, center);

            // Phase 2: Assign all remaining points to conflict lists
            var inHull = new bool[n];
            inHull[i0] = true;
            inHull[i1] = true;
            inHull[i2] = true;
            inHull[i3] = true;

            for (int pi = 0; pi < n; pi++)
            {
                if (inHull[pi]) continue;

                Vec3 p = points[pi];
                float bestDist = -1f;
                int bestFace = -1;

                for (int fi = 0; fi < faces.Count; fi++)
                {
                    QFace f = faces[fi];
                    float dist = Vec3.Dot(f.Normal, p) - f.Dist;
                    if (dist > bestDist)
                    {
                        bestDist = dist;
                        bestFace = fi;
                    }
                }

                if (bestFace >= 0 && bestDist > -1e-8f)
                {
                    QFace f = faces[bestFace];
                    f.ConflictList.Add(pi);
                    if (bestDist > f.FarthestDist)
                    {
                        f.FarthestDist = bestDist;
                        f.FarthestPoint = pi;
                    }
                }
            }

            // Seed the heap with initial faces that have conflict points
            int heapCount = 0;
            var heap = new HeapEntry[Math.Max(16, n / 2)];
            for (int fi = 0; fi < faces.Count; fi++)
            {
                if (faces[fi].ConflictList.Count > 0)
                    HeapPush(ref heap, ref heapCount, faces[fi].FarthestDist, fi);
            }

            // Phase 3: Main loop — process faces with non-empty conflict lists
            var visibleFaces = new List<int>(64);
            var horizonEdges = new List<(int From, int To)>(64);
            var bfsQueue = new Queue<int>(64);
            var orphanPoints = new List<int>(Math.Max(16, n / 4));

            // Reusable bool[] for visible set (grows as faces list grows)
            bool[] isVisible = new bool[Math.Max(16, expectedFaceCount)];

            while (heapCount > 0)
            {
                // Pop face with globally farthest point (skip stale entries)
                int bestFaceIdx;
                while (true)
                {
                    if (heapCount == 0) goto done;
                    var entry = HeapPop(heap, ref heapCount);
                    bestFaceIdx = entry.FaceIndex;
                    QFace candidate = faces[bestFaceIdx];
                    // Valid if face is alive, has conflict points, and dist matches (not stale)
                    if (candidate.Alive && candidate.ConflictList.Count > 0
                        && candidate.FarthestDist == entry.Dist)
                        break;
                }

                int apex = faces[bestFaceIdx].FarthestPoint;
                Vec3 apexPos = points[apex];

                // BFS to find all faces visible from apex
                visibleFaces.Clear();

                // Grow isVisible if needed
                if (faces.Count > isVisible.Length)
                    Array.Resize(ref isVisible, faces.Count * 2);

                bfsQueue.Clear();
                bfsQueue.Enqueue(bestFaceIdx);
                isVisible[bestFaceIdx] = true;

                while (bfsQueue.Count > 0)
                {
                    int fi = bfsQueue.Dequeue();
                    visibleFaces.Add(fi);
                    QFace f = faces[fi];

                    CheckNeighbor(faces, edgeToFace, bfsQueue, isVisible, apexPos, f.V0, f.V1);
                    CheckNeighbor(faces, edgeToFace, bfsQueue, isVisible, apexPos, f.V1, f.V2);
                    CheckNeighbor(faces, edgeToFace, bfsQueue, isVisible, apexPos, f.V2, f.V0);
                }

                // Find horizon edges
                horizonEdges.Clear();
                for (int vi = 0; vi < visibleFaces.Count; vi++)
                {
                    int fi = visibleFaces[vi];
                    QFace f = faces[fi];
                    CollectHorizonEdge(edgeToFace, horizonEdges, isVisible, f.V0, f.V1);
                    CollectHorizonEdge(edgeToFace, horizonEdges, isVisible, f.V1, f.V2);
                    CollectHorizonEdge(edgeToFace, horizonEdges, isVisible, f.V2, f.V0);
                }

                // Collect orphan points from visible faces
                orphanPoints.Clear();
                foreach (int fi in visibleFaces)
                {
                    QFace f = faces[fi];
                    foreach (int pi in f.ConflictList)
                    {
                        if (pi != apex)
                            orphanPoints.Add(pi);
                    }
                }

                // Remove visible faces and clear isVisible
                foreach (int fi in visibleFaces)
                {
                    QFace f = faces[fi];
                    RemoveFaceEdges(edgeToFace, f.V0, f.V1, f.V2);
                    f.Alive = false;
                    f.ConflictList.Clear();
                    isVisible[fi] = false;
                }

                // Create fan of new triangles
                var orderedHorizon = OrderHorizonLoop(horizonEdges);
                int newFaceStart = faces.Count;

                for (int hi = 0; hi < orderedHorizon.Count; hi++)
                {
                    var (from, to) = orderedHorizon[hi];
                    AddFace(faces, edgeToFace, points, from, to, apex, center);
                }

                inHull[apex] = true;

                // Redistribute orphan points to new faces
                int newFaceEnd = faces.Count;
                for (int oi = 0; oi < orphanPoints.Count; oi++)
                {
                    int pi = orphanPoints[oi];
                    Vec3 p = points[pi];
                    float bestDist = -1e-8f;
                    int bestFace = -1;

                    for (int fi = newFaceStart; fi < newFaceEnd; fi++)
                    {
                        QFace f = faces[fi];
                        float dist = Vec3.Dot(f.Normal, p) - f.Dist;
                        if (dist > bestDist)
                        {
                            bestDist = dist;
                            bestFace = fi;
                        }
                    }

                    if (bestFace >= 0)
                    {
                        QFace f = faces[bestFace];
                        f.ConflictList.Add(pi);
                        if (bestDist > f.FarthestDist)
                        {
                            f.FarthestDist = bestDist;
                            f.FarthestPoint = pi;
                        }
                    }
                }

                // Push new faces with conflict points to heap
                for (int fi = newFaceStart; fi < newFaceEnd; fi++)
                {
                    if (faces[fi].ConflictList.Count > 0)
                        HeapPush(ref heap, ref heapCount, faces[fi].FarthestDist, fi);
                }
            }

            done:

            // Phase 4: Compact into flat arrays
            return Compact(points, faces, edgeToFace);
        }

        static void CheckNeighbor(List<QFace> faces, Dictionary<long, int> edgeToFace,
            Queue<int> bfsQueue, bool[] isVisible, Vec3 apexPos, int edgeFrom, int edgeTo)
        {
            long oppositeKey = DirectedEdgeKey(edgeTo, edgeFrom);
            if (edgeToFace.TryGetValue(oppositeKey, out int oppositeHe))
            {
                int oppFace = oppositeHe / 3;
                if (!isVisible[oppFace] && faces[oppFace].Alive)
                {
                    QFace opp = faces[oppFace];
                    float dist = Vec3.Dot(opp.Normal, apexPos) - opp.Dist;
                    if (dist > 1e-8f)
                    {
                        isVisible[oppFace] = true;
                        bfsQueue.Enqueue(oppFace);
                    }
                }
            }
        }

        static void CollectHorizonEdge(Dictionary<long, int> edgeToFace,
            List<(int, int)> horizonEdges, bool[] isVisible, int edgeFrom, int edgeTo)
        {
            long oppositeKey = DirectedEdgeKey(edgeTo, edgeFrom);
            if (edgeToFace.TryGetValue(oppositeKey, out int oppositeHe))
            {
                int oppFace = oppositeHe / 3;
                if (!isVisible[oppFace])
                    horizonEdges.Add((edgeTo, edgeFrom));
            }
        }

        // --- Max-heap operations (array-based, no allocations) ---

        static void HeapPush(ref HeapEntry[] heap, ref int count, float dist, int faceIndex)
        {
            if (count == heap.Length)
                Array.Resize(ref heap, heap.Length * 2);

            heap[count] = new HeapEntry { Dist = dist, FaceIndex = faceIndex };
            int i = count;
            count++;

            // Sift up
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (heap[i].Dist <= heap[parent].Dist) break;
                var tmp = heap[i];
                heap[i] = heap[parent];
                heap[parent] = tmp;
                i = parent;
            }
        }

        static HeapEntry HeapPop(HeapEntry[] heap, ref int count)
        {
            var top = heap[0];
            count--;
            heap[0] = heap[count];

            // Sift down
            int i = 0;
            while (true)
            {
                int left = 2 * i + 1;
                int right = 2 * i + 2;
                int largest = i;
                if (left < count && heap[left].Dist > heap[largest].Dist) largest = left;
                if (right < count && heap[right].Dist > heap[largest].Dist) largest = right;
                if (largest == i) break;
                var tmp = heap[i];
                heap[i] = heap[largest];
                heap[largest] = tmp;
                i = largest;
            }

            return top;
        }

        // --- Shared utilities ---

        static void FindInitialTetrahedron(Vec3[] points, out int i0, out int i1, out int i2, out int i3)
        {
            int n = points.Length;

            // Pick first point: max X
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

            // Pick third point: maximizing triangle area
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

            // Pick fourth point: maximizing tetrahedron volume
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

        static void AddFace(List<QFace> faces, Dictionary<long, int> edgeToFace,
            Vec3[] points, int v0, int v1, int v2, Vec3 interiorPoint)
        {
            Vec3 a = points[v0], b = points[v1], c = points[v2];
            Vec3 normal = Vec3.Cross(b - a, c - a);

            // Ensure normal points outward (away from interior point)
            Vec3 centroid = (a + b + c) * (1f / 3f);
            if (Vec3.Dot(normal, centroid - interiorPoint) < 0)
            {
                int tmp = v1;
                v1 = v2;
                v2 = tmp;
                normal = -normal;
            }

            normal = normal.Normalized;
            float dist = Vec3.Dot(normal, points[v0]);

            int fi = faces.Count;
            faces.Add(new QFace
            {
                V0 = v0, V1 = v1, V2 = v2,
                Normal = normal, Dist = dist,
                Alive = true,
                ConflictList = new List<int>(4),
                FarthestDist = -1f,
                FarthestPoint = -1,
            });

            edgeToFace[DirectedEdgeKey(v0, v1)] = fi * 3 + 0;
            edgeToFace[DirectedEdgeKey(v1, v2)] = fi * 3 + 1;
            edgeToFace[DirectedEdgeKey(v2, v0)] = fi * 3 + 2;
        }

        static void RemoveFaceEdges(Dictionary<long, int> edgeToFace, int v0, int v1, int v2)
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

        static ConvexHull Compact(Vec3[] points, List<QFace> faces, Dictionary<long, int> edgeToFace)
        {
            int aliveCount = 0;
            for (int i = 0; i < faces.Count; i++)
                if (faces[i].Alive) aliveCount++;

            var triangles = new int[aliveCount * 3];
            var halfedges = new int[aliveCount * 3];

            var faceMap = new int[faces.Count];
            int newIdx = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                if (faces[i].Alive)
                {
                    faceMap[i] = newIdx;
                    QFace f = faces[i];
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

            for (int i = 0; i < aliveCount; i++)
            {
                int v0 = triangles[i * 3 + 0];
                int v1 = triangles[i * 3 + 1];
                int v2 = triangles[i * 3 + 2];

                halfedges[i * 3 + 0] = FindOpposite(edgeToFace, faceMap, v1, v0);
                halfedges[i * 3 + 1] = FindOpposite(edgeToFace, faceMap, v2, v1);
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
            return -1;
        }
    }
}
