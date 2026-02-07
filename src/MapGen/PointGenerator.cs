using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Generates seed points for Voronoi tessellation.
    /// </summary>
    public static class PointGenerator
    {
        /// <summary>
        /// Generate points on a jittered grid (Azgaar's approach).
        /// Points are placed on a regular grid then randomly offset.
        /// </summary>
        /// <param name="width">Map width</param>
        /// <param name="height">Map height</param>
        /// <param name="cellCount">Approximate number of cells desired</param>
        /// <param name="seed">Random seed</param>
        /// <returns>Grid points and computed spacing</returns>
        public static (Vec2[] Points, float Spacing) JitteredGrid(
            float width, float height, int cellCount, int seed)
        {
            var rng = new Random(seed);

            // Calculate spacing to achieve target cell count
            float spacing = (float)Math.Sqrt((width * height) / cellCount);
            float radius = spacing / 2f;
            float jitter = radius * 0.9f; // Max deviation (90% of half-spacing)

            var points = new List<Vec2>();

            for (float y = radius; y < height; y += spacing)
            {
                for (float x = radius; x < width; x += spacing)
                {
                    float jx = x + (float)(rng.NextDouble() * 2 - 1) * jitter;
                    float jy = y + (float)(rng.NextDouble() * 2 - 1) * jitter;

                    // Clamp to bounds
                    jx = Math.Max(0, Math.Min(width, jx));
                    jy = Math.Max(0, Math.Min(height, jy));

                    points.Add(new Vec2(jx, jy));
                }
            }

            return (points.ToArray(), spacing);
        }

        /// <summary>
        /// Generate boundary points for Voronoi clipping.
        /// These create cells at the map edge that extend to infinity,
        /// preventing interior cells from having unbounded polygons.
        /// </summary>
        /// <param name="width">Map width</param>
        /// <param name="height">Map height</param>
        /// <param name="spacing">Grid spacing (from JitteredGrid)</param>
        /// <returns>Boundary points</returns>
        public static Vec2[] BoundaryPoints(float width, float height, float spacing)
        {
            float offset = -spacing;
            float boundarySpacing = spacing * 2;

            int countX = (int)Math.Ceiling(width / boundarySpacing);
            int countY = (int)Math.Ceiling(height / boundarySpacing);

            var points = new List<Vec2>();

            // Top and bottom edges
            for (int i = 0; i < countX; i++)
            {
                float x = (i + 0.5f) * width / countX;
                points.Add(new Vec2(x, offset));           // Top (outside)
                points.Add(new Vec2(x, height - offset));  // Bottom (outside)
            }

            // Left and right edges
            for (int i = 0; i < countY; i++)
            {
                float y = (i + 0.5f) * height / countY;
                points.Add(new Vec2(offset, y));           // Left (outside)
                points.Add(new Vec2(width - offset, y));   // Right (outside)
            }

            return points.ToArray();
        }

        /// <summary>
        /// Combine grid and boundary points for Delaunay input.
        /// </summary>
        /// <param name="gridPoints">Interior points from JitteredGrid</param>
        /// <param name="boundaryPoints">Boundary points from BoundaryPoints</param>
        /// <returns>Combined array (grid points first, then boundary)</returns>
        public static Vec2[] CombinePoints(Vec2[] gridPoints, Vec2[] boundaryPoints)
        {
            var combined = new Vec2[gridPoints.Length + boundaryPoints.Length];
            Array.Copy(gridPoints, 0, combined, 0, gridPoints.Length);
            Array.Copy(boundaryPoints, 0, combined, gridPoints.Length, boundaryPoints.Length);
            return combined;
        }
    }
}
