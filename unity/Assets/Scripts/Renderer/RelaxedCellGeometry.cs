using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Generates relaxed (organically curved) polygon boundaries for all cells.
    /// Provides the single source of truth for cell boundaries used by both
    /// border rendering and texture rasterization.
    /// </summary>
    public class RelaxedCellGeometry
    {
        /// <summary>Relaxed polygon for each cell (closed polyline in 2D map coords).</summary>
        public Dictionary<int, List<Vector2>> CellPolygons { get; private set; }

        /// <summary>
        /// Relaxed edges keyed by sorted vertex pair.
        /// Key: (min(v1,v2), max(v1,v2)) ensures symmetry.
        /// </summary>
        public Dictionary<(int, int), List<Vector2>> RelaxedEdges { get; private set; }

        /// <summary>Perpendicular displacement amplitude in map units.</summary>
        public float Amplitude { get; set; } = 1.0f;

        /// <summary>Control points per map unit of edge length.</summary>
        public float Frequency { get; set; } = 0.3f;

        /// <summary>Catmull-Rom samples between each pair of control points.</summary>
        public int SamplesPerSegment { get; set; } = 4;

        private MapData mapData;

        /// <summary>
        /// Build relaxed geometry for all cells.
        /// </summary>
        public void Build(MapData data)
        {
            mapData = data;
            RelaxedEdges = new Dictionary<(int, int), List<Vector2>>();
            CellPolygons = new Dictionary<int, List<Vector2>>();

            // Build all cell polygons (land and water for complete coverage)
            foreach (var cell in mapData.Cells)
            {
                var polygon = BuildCellPolygon(cell);
                if (polygon.Count >= 3)
                {
                    CellPolygons[cell.Id] = polygon;
                }
            }

            Debug.Log($"RelaxedCellGeometry: Built {CellPolygons.Count} polygons, {RelaxedEdges.Count} unique edges");
        }

        /// <summary>
        /// Get a relaxed edge between two vertices, reversed if necessary
        /// to match the requested direction (v1 → v2).
        /// Always returns a copy to prevent mutation of cached data.
        /// </summary>
        public List<Vector2> GetEdge(int v1, int v2)
        {
            var key = GetEdgeKey(v1, v2);

            if (!RelaxedEdges.TryGetValue(key, out var cachedEdge))
            {
                cachedEdge = BuildRelaxedEdge(key.Item1, key.Item2);
                RelaxedEdges[key] = cachedEdge;
            }

            // Return copy, reversed if requested direction is opposite to stored direction
            if (v1 > v2)
            {
                var reversed = new List<Vector2>(cachedEdge);
                reversed.Reverse();
                return reversed;
            }
            return new List<Vector2>(cachedEdge);
        }

        private (int, int) GetEdgeKey(int v1, int v2)
        {
            return v1 < v2 ? (v1, v2) : (v2, v1);
        }

        private List<Vector2> BuildRelaxedEdge(int v1, int v2)
        {
            // v1 < v2 guaranteed by GetEdgeKey
            Vector2 p1 = mapData.Vertices[v1].ToUnity();
            Vector2 p2 = mapData.Vertices[v2].ToUnity();

            // Check for map boundary edge (skip relaxation)
            if (IsBoundaryEdge(p1, p2))
            {
                return new List<Vector2> { p1, p2 };
            }

            Vector2 dir = p2 - p1;
            float length = dir.magnitude;

            if (length < 0.001f)
            {
                return new List<Vector2> { p1, p2 };
            }

            Vector2 perp = new Vector2(-dir.y, dir.x).normalized;

            // Determine control point count based on edge length
            int numSegments = Mathf.Max(1, Mathf.RoundToInt(length * Frequency));
            int numControls = numSegments + 1;

            var controlPoints = new List<Vector2>(numControls);

            for (int i = 0; i < numControls; i++)
            {
                float t = (numControls == 1) ? 0.5f : (float)i / (numControls - 1);
                Vector2 basePos = Vector2.Lerp(p1, p2, t);

                float offset = 0f;
                // Skip endpoints to maintain connectivity
                if (i != 0 && i != numControls - 1)
                {
                    int seed = NoiseUtils.HashCombine(v1, v2, i);
                    offset = NoiseUtils.HashToFloat(seed) * Amplitude;
                }

                controlPoints.Add(basePos + perp * offset);
            }

            // Smooth with Catmull-Rom spline
            return InterpolateCatmullRom(controlPoints, SamplesPerSegment);
        }

        private bool IsBoundaryEdge(Vector2 p1, Vector2 p2)
        {
            // Edge is on map boundary if both vertices are within threshold of same edge
            const float threshold = 0.1f;
            float width = mapData.Info.Width;
            float height = mapData.Info.Height;

            // Left edge
            if (p1.x < threshold && p2.x < threshold) return true;
            // Right edge
            if (p1.x > width - threshold && p2.x > width - threshold) return true;
            // Top edge (Azgaar Y=0 at top)
            if (p1.y < threshold && p2.y < threshold) return true;
            // Bottom edge
            if (p1.y > height - threshold && p2.y > height - threshold) return true;

            return false;
        }

        private List<Vector2> BuildCellPolygon(Cell cell)
        {
            var verts = cell.VertexIndices;
            if (verts == null || verts.Count < 3)
            {
                return new List<Vector2>();
            }

            // Sort vertices by angle from cell center to ensure proper winding order
            var center = cell.Center.ToUnity();
            var sortedVerts = verts.OrderBy(vi =>
            {
                var v = mapData.Vertices[vi].ToUnity();
                return Mathf.Atan2(v.y - center.y, v.x - center.x);
            }).ToList();

            var polygon = new List<Vector2>();
            int n = sortedVerts.Count;

            for (int i = 0; i < n; i++)
            {
                int v1 = sortedVerts[i];
                int v2 = sortedVerts[(i + 1) % n];

                List<Vector2> edge = GetEdge(v1, v2);

                if (i == 0)
                {
                    // First edge: add all points
                    polygon.AddRange(edge);
                }
                else
                {
                    // Subsequent edges: skip first point (duplicate of previous last)
                    polygon.AddRange(edge.Skip(1));
                }
            }

            // Remove last point if it duplicates first (closed polygon)
            if (polygon.Count > 1 && Vector2.Distance(polygon[0], polygon[polygon.Count - 1]) < 0.001f)
            {
                polygon.RemoveAt(polygon.Count - 1);
            }

            return polygon;
        }

        /// <summary>
        /// Catmull-Rom spline interpolation with automatic phantom point handling.
        /// </summary>
        private List<Vector2> InterpolateCatmullRom(List<Vector2> points, int samples)
        {
            if (points.Count < 2)
                return new List<Vector2>(points);

            // For 2 points, just return them (straight line)
            if (points.Count == 2)
                return new List<Vector2>(points);

            var result = new List<Vector2>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                // Catmull-Rom needs 4 control points (P0, P1, P2, P3)
                // to interpolate between P1 and P2.
                // Use endpoint duplication for phantom points.
                Vector2 p0 = points[Mathf.Max(0, i - 1)];
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = points[Mathf.Min(points.Count - 1, i + 2)];

                for (int s = 0; s < samples; s++)
                {
                    float t = (float)s / samples;
                    result.Add(CatmullRomPoint(p0, p1, p2, p3, t));
                }
            }

            // Add the final point
            result.Add(points[points.Count - 1]);

            return result;
        }

        /// <summary>
        /// Evaluates a point on a Catmull-Rom spline segment (uniform parameterization).
        /// </summary>
        private Vector2 CatmullRomPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }
    }
}
