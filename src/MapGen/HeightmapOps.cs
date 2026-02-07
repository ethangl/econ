using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// Heightmap DSL operations ported from Azgaar's Fantasy Map Generator.
    /// Each operation modifies heights in-place.
    /// </summary>
    public static class HeightmapOps
    {
        // Blob power lookup by cell count (from Azgaar)
        private static float GetBlobPower(int cellCount)
        {
            if (cellCount <= 1000) return 0.93f;
            if (cellCount <= 2000) return 0.95f;
            if (cellCount <= 5000) return 0.97f;
            if (cellCount <= 10000) return 0.98f;
            if (cellCount <= 20000) return 0.99f;
            if (cellCount <= 30000) return 0.991f;
            if (cellCount <= 40000) return 0.993f;
            if (cellCount <= 50000) return 0.994f;
            if (cellCount <= 60000) return 0.995f;
            if (cellCount <= 70000) return 0.9955f;
            if (cellCount <= 80000) return 0.996f;
            if (cellCount <= 90000) return 0.9964f;
            return 0.9973f;
        }

        // Line power lookup by cell count (from Azgaar)
        private static float GetLinePower(int cellCount)
        {
            if (cellCount <= 1000) return 0.75f;
            if (cellCount <= 2000) return 0.77f;
            if (cellCount <= 5000) return 0.79f;
            if (cellCount <= 10000) return 0.81f;
            if (cellCount <= 20000) return 0.82f;
            if (cellCount <= 30000) return 0.83f;
            if (cellCount <= 40000) return 0.84f;
            if (cellCount <= 50000) return 0.86f;
            if (cellCount <= 60000) return 0.87f;
            if (cellCount <= 70000) return 0.88f;
            if (cellCount <= 80000) return 0.91f;
            if (cellCount <= 90000) return 0.92f;
            return 0.93f;
        }

        /// <summary>
        /// Hill: BFS blob growth with separate change array.
        /// Matches Azgaar's addHill — uses integer change values (Uint8Array in JS)
        /// to get correct falloff behavior.
        /// </summary>
        public static void Hill(HeightGrid grid, float seedX, float seedY, float height, Random rng)
        {
            int cellCount = grid.CellCount;
            if (cellCount == 0) return;

            float blobPower = GetBlobPower(cellCount);

            // Clamp height to [0,100] like Azgaar's lim()
            int h = (int)Math.Min(Math.Max(Math.Abs(height), 0), 100);

            // Retry loop: avoid placing hill where it would exceed 90 (Azgaar behavior)
            int seedCell = -1;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                seedCell = FindNearestCell(grid.Mesh, seedX * grid.Mesh.Width, seedY * grid.Mesh.Height);
                if (seedCell < 0) return;
                if (grid.Heights[seedCell] + h <= 90) break;
                // Jitter position slightly for retry
                seedX = seedX + ((float)rng.NextDouble() - 0.5f) * 0.1f;
                seedY = seedY + ((float)rng.NextDouble() - 0.5f) * 0.1f;
                seedX = Math.Max(0f, Math.Min(1f, seedX));
                seedY = Math.Max(0f, Math.Min(1f, seedY));
            }
            if (seedCell < 0) return;

            // Integer change array matches Azgaar's Uint8Array truncation behavior.
            // Float precision causes hills to spread much further than intended.
            var change = new int[cellCount];
            change[seedCell] = h;

            var queue = new Queue<int>();
            queue.Enqueue(seedCell);

            while (queue.Count > 0)
            {
                int q = queue.Dequeue();

                foreach (int c in grid.GetNeighbors(q))
                {
                    if (c < 0 || c >= cellCount || change[c] > 0) continue;

                    // Power decay, truncated to int (matches Uint8Array storage)
                    change[c] = (int)(Math.Pow(change[q], blobPower) * (0.9 + rng.NextDouble() * 0.2));
                    if (change[c] > 1)
                        queue.Enqueue(c);
                }
            }

            // Apply all changes at once
            for (int i = 0; i < cellCount; i++)
                grid.Heights[i] = HeightGrid.Clamp(grid.Heights[i] + change[i]);
        }

        /// <summary>
        /// Pit: BFS blob that subtracts height.
        /// Matches Azgaar's addPit.
        /// </summary>
        public static void Pit(HeightGrid grid, float seedX, float seedY, float height, Random rng)
        {
            int cellCount = grid.CellCount;
            if (cellCount == 0) return;

            float blobPower = GetBlobPower(cellCount);

            // Retry loop: prefer starting on land (Azgaar behavior)
            int seedCell = -1;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                seedCell = FindNearestCell(grid.Mesh, seedX * grid.Mesh.Width, seedY * grid.Mesh.Height);
                if (seedCell < 0) return;
                if (grid.Heights[seedCell] >= HeightGrid.SeaLevel) break;
                seedX = seedX + ((float)rng.NextDouble() - 0.5f) * 0.1f;
                seedY = seedY + ((float)rng.NextDouble() - 0.5f) * 0.1f;
                seedX = Math.Max(0f, Math.Min(1f, seedX));
                seedY = Math.Max(0f, Math.Min(1f, seedY));
            }
            if (seedCell < 0) return;

            float h = Math.Min(Math.Abs(height), 100f);
            var used = new bool[cellCount];

            var queue = new Queue<int>();
            queue.Enqueue(seedCell);

            while (queue.Count > 0)
            {
                int q = queue.Dequeue();
                h = (float)Math.Pow(h, blobPower) * (0.9f + (float)rng.NextDouble() * 0.2f);
                if (h < 1f) break;

                foreach (int c in grid.GetNeighbors(q))
                {
                    if (c < 0 || c >= cellCount || used[c]) continue;
                    grid.Heights[c] = HeightGrid.Clamp(grid.Heights[c] - h * (0.9f + (float)rng.NextDouble() * 0.2f));
                    used[c] = true;
                    queue.Enqueue(c);
                }
            }
        }

        /// <summary>
        /// Range: greedy path from A to B, then frontier-based height spread.
        /// Matches Azgaar's addRange.
        /// </summary>
        public static void Range(HeightGrid grid, float x1, float y1, float x2, float y2, float height, Random rng)
        {
            int cellCount = grid.CellCount;
            if (cellCount == 0) return;

            float linePower = GetLinePower(cellCount);
            float w = grid.Mesh.Width;
            float mapH = grid.Mesh.Height;

            int startCell = FindNearestCell(grid.Mesh, x1 * w, y1 * mapH);
            int endCell = FindNearestCell(grid.Mesh, x2 * w, y2 * mapH);
            if (startCell < 0 || endCell < 0 || startCell == endCell) return;

            var used = new bool[cellCount];

            // Greedy pathfinding from start to end (Azgaar approach)
            var ridge = new List<int>();
            int cur = startCell;
            used[cur] = true;
            ridge.Add(cur);

            while (cur != endCell)
            {
                float minDist = float.MaxValue;
                int next = -1;

                foreach (int c in grid.GetNeighbors(cur))
                {
                    if (c < 0 || c >= cellCount || used[c]) continue;
                    float dx = grid.Mesh.CellCenters[endCell].X - grid.Mesh.CellCenters[c].X;
                    float dy = grid.Mesh.CellCenters[endCell].Y - grid.Mesh.CellCenters[c].Y;
                    float dist = dx * dx + dy * dy;
                    // Random deviation (15% chance to halve distance = prefer this cell)
                    if (rng.NextDouble() > 0.85) dist *= 0.5f;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        next = c;
                    }
                }

                if (next < 0) break;
                cur = next;
                used[cur] = true;
                ridge.Add(cur);
            }

            // Frontier-based height spread from ridge outward
            float h = Math.Abs(height);
            var frontier = new List<int>(ridge);
            int depth = 0;

            while (frontier.Count > 0)
            {
                // Apply height to current frontier
                foreach (int cell in frontier)
                    grid.Heights[cell] = HeightGrid.Clamp(grid.Heights[cell] + h * (0.85f + (float)rng.NextDouble() * 0.3f));

                // Decay: h = h^linePower - 1 (Azgaar formula)
                h = (float)Math.Pow(h, linePower) - 1f;
                if (h < 2f) break;
                depth++;

                // Expand frontier
                var nextFrontier = new List<int>();
                foreach (int f in frontier)
                {
                    foreach (int c in grid.GetNeighbors(f))
                    {
                        if (c >= 0 && c < cellCount && !used[c])
                        {
                            used[c] = true;
                            nextFrontier.Add(c);
                        }
                    }
                }
                frontier = nextFrontier;
            }

            // Generate prominences (every 6th ridge cell, walk downhill)
            for (int d = 0; d < ridge.Count; d += 6)
            {
                int pCur = ridge[d];
                for (int step = 0; step < depth; step++)
                {
                    // Find lowest neighbor
                    int lowest = -1;
                    float lowestH = float.MaxValue;
                    foreach (int c in grid.GetNeighbors(pCur))
                    {
                        if (c >= 0 && c < cellCount && grid.Heights[c] < lowestH)
                        {
                            lowestH = grid.Heights[c];
                            lowest = c;
                        }
                    }
                    if (lowest < 0) break;
                    grid.Heights[lowest] = (grid.Heights[pCur] * 2f + grid.Heights[lowest]) / 3f;
                    pCur = lowest;
                }
            }
        }

        /// <summary>
        /// Trough: same as Range but subtracts height.
        /// </summary>
        public static void Trough(HeightGrid grid, float x1, float y1, float x2, float y2, float height, Random rng)
        {
            int cellCount = grid.CellCount;
            if (cellCount == 0) return;

            float linePower = GetLinePower(cellCount);
            float w = grid.Mesh.Width;
            float mapH = grid.Mesh.Height;

            int startCell = FindNearestCell(grid.Mesh, x1 * w, y1 * mapH);
            int endCell = FindNearestCell(grid.Mesh, x2 * w, y2 * mapH);
            if (startCell < 0 || endCell < 0 || startCell == endCell) return;

            var used = new bool[cellCount];

            // Greedy pathfinding
            var ridge = new List<int>();
            int cur = startCell;
            used[cur] = true;
            ridge.Add(cur);

            while (cur != endCell)
            {
                float minDist = float.MaxValue;
                int next = -1;

                foreach (int c in grid.GetNeighbors(cur))
                {
                    if (c < 0 || c >= cellCount || used[c]) continue;
                    float dx = grid.Mesh.CellCenters[endCell].X - grid.Mesh.CellCenters[c].X;
                    float dy = grid.Mesh.CellCenters[endCell].Y - grid.Mesh.CellCenters[c].Y;
                    float dist = dx * dx + dy * dy;
                    if (rng.NextDouble() > 0.8) dist *= 0.5f;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        next = c;
                    }
                }

                if (next < 0) break;
                cur = next;
                used[cur] = true;
                ridge.Add(cur);
            }

            // Frontier-based height subtraction
            float h = Math.Abs(height);
            var frontier = new List<int>(ridge);
            int depth = 0;

            while (frontier.Count > 0)
            {
                foreach (int cell in frontier)
                    grid.Heights[cell] = HeightGrid.Clamp(grid.Heights[cell] - h * (0.85f + (float)rng.NextDouble() * 0.3f));

                h = (float)Math.Pow(h, linePower) - 1f;
                if (h < 2f) break;
                depth++;

                var nextFrontier = new List<int>();
                foreach (int f in frontier)
                {
                    foreach (int c in grid.GetNeighbors(f))
                    {
                        if (c >= 0 && c < cellCount && !used[c])
                        {
                            used[c] = true;
                            nextFrontier.Add(c);
                        }
                    }
                }
                frontier = nextFrontier;
            }

            // Generate prominences (every 6th ridge cell, walk downhill)
            for (int d = 0; d < ridge.Count; d += 6)
            {
                int pCur = ridge[d];
                for (int step = 0; step < depth; step++)
                {
                    int lowest = -1;
                    float lowestH = float.MaxValue;
                    foreach (int c in grid.GetNeighbors(pCur))
                    {
                        if (c >= 0 && c < cellCount && grid.Heights[c] < lowestH)
                        {
                            lowestH = grid.Heights[c];
                            lowest = c;
                        }
                    }
                    if (lowest < 0) break;
                    grid.Heights[lowest] = (grid.Heights[pCur] * 2f + grid.Heights[lowest]) / 3f;
                    pCur = lowest;
                }
            }
        }

        /// <summary>
        /// Mask: distance-from-edge falloff with blending.
        /// Matches Azgaar's mask exactly: (h * (fr-1) + h * distance) / fr
        /// </summary>
        public static void Mask(HeightGrid grid, float fraction)
        {
            float fr = Math.Abs(fraction);
            if (fr < 1f) fr = 1f;
            float w = grid.Mesh.Width;
            float h = grid.Mesh.Height;

            for (int i = 0; i < grid.CellCount; i++)
            {
                Vec2 center = grid.Mesh.CellCenters[i];
                float nx = (2f * center.X) / w - 1f;
                float ny = (2f * center.Y) / h - 1f;
                float distance = (1f - nx * nx) * (1f - ny * ny);

                if (fraction < 0) distance = 1f - distance;

                float masked = grid.Heights[i] * distance;
                grid.Heights[i] = HeightGrid.Clamp((grid.Heights[i] * (fr - 1f) + masked) / fr);
            }
        }

        /// <summary>
        /// Add a constant to heights.
        /// For land range (min=20): clamps to stay above sea level.
        /// </summary>
        public static void Add(HeightGrid grid, float value, float minHeight = HeightGrid.MinHeight, float maxHeight = HeightGrid.MaxHeight)
        {
            bool isLand = minHeight == HeightGrid.SeaLevel;

            for (int i = 0; i < grid.CellCount; i++)
            {
                float h = grid.Heights[i];
                if (h >= minHeight && h <= maxHeight)
                {
                    float newH = h + value;
                    if (isLand) newH = Math.Max(newH, HeightGrid.SeaLevel);
                    grid.Heights[i] = HeightGrid.Clamp(newH);
                }
            }
        }

        /// <summary>
        /// Multiply heights by a factor.
        /// For land range (min=20): operates on height-above-sea-level.
        /// Azgaar formula: (h - 20) * factor + 20
        /// </summary>
        public static void Multiply(HeightGrid grid, float factor, float minHeight = HeightGrid.MinHeight, float maxHeight = HeightGrid.MaxHeight)
        {
            bool isLand = minHeight == HeightGrid.SeaLevel;

            for (int i = 0; i < grid.CellCount; i++)
            {
                float h = grid.Heights[i];
                if (h >= minHeight && h <= maxHeight)
                {
                    if (isLand)
                        grid.Heights[i] = HeightGrid.Clamp((h - HeightGrid.SeaLevel) * factor + HeightGrid.SeaLevel);
                    else
                        grid.Heights[i] = HeightGrid.Clamp(h * factor);
                }
            }
        }

        /// <summary>
        /// Smooth heights with weighted average.
        /// Azgaar formula: (h * (fr-1) + mean(neighbors)) / fr
        /// fr=1 is pure average, fr=2 favors current value, etc.
        /// </summary>
        public static void Smooth(HeightGrid grid, int fr = 2)
        {
            var temp = new float[grid.CellCount];

            for (int i = 0; i < grid.CellCount; i++)
            {
                var neighbors = grid.GetNeighbors(i);
                float sum = grid.Heights[i];
                int count = 1;

                foreach (int n in neighbors)
                {
                    if (n >= 0 && n < grid.CellCount)
                    {
                        sum += grid.Heights[n];
                        count++;
                    }
                }

                float mean = sum / count;

                if (fr <= 1)
                    temp[i] = mean;
                else
                    temp[i] = HeightGrid.Clamp((grid.Heights[i] * (fr - 1) + mean) / fr);
            }

            Array.Copy(temp, grid.Heights, grid.CellCount);
        }

        /// <summary>
        /// Strait: BFS path from one edge to another, then expand rings
        /// applying h^0.8 to reduce heights. Matches Azgaar's addStrait.
        /// </summary>
        /// <param name="desiredWidth">Number of expansion rings from the path</param>
        /// <param name="direction">1 = vertical (top-bottom), 0 = horizontal (left-right)</param>
        public static void Strait(HeightGrid grid, int desiredWidth, int direction, Random rng)
        {
            int cellCount = grid.CellCount;
            if (cellCount == 0 || desiredWidth < 1) return;

            float mapW = grid.Mesh.Width;
            float mapH = grid.Mesh.Height;
            bool vert = direction == 1;

            // Start and end positions (Azgaar approach)
            float startX = vert
                ? (float)(rng.NextDouble() * mapW * 0.4 + mapW * 0.3)
                : 5f;
            float startY = vert
                ? 5f
                : (float)(rng.NextDouble() * mapH * 0.4 + mapH * 0.3);
            float endX = vert
                ? (float)(mapW - startX - mapW * 0.1 + rng.NextDouble() * mapW * 0.2)
                : mapW - 5f;
            float endY = vert
                ? mapH - 5f
                : (float)(mapH - startY - mapH * 0.1 + rng.NextDouble() * mapH * 0.2);

            int startCell = FindNearestCell(grid.Mesh, startX, startY);
            int endCell = FindNearestCell(grid.Mesh, endX, endY);
            if (startCell < 0 || endCell < 0) return;

            // Greedy pathfinding from start to end
            var path = new List<int>();
            int cur = startCell;
            while (cur != endCell)
            {
                float minDist = float.MaxValue;
                int next = -1;
                foreach (int c in grid.GetNeighbors(cur))
                {
                    if (c < 0 || c >= cellCount) continue;
                    float dx = grid.Mesh.CellCenters[endCell].X - grid.Mesh.CellCenters[c].X;
                    float dy = grid.Mesh.CellCenters[endCell].Y - grid.Mesh.CellCenters[c].Y;
                    float dist = dx * dx + dy * dy;
                    if (rng.NextDouble() > 0.8) dist *= 0.5f;
                    if (dist < minDist) { minDist = dist; next = c; }
                }
                if (next < 0) break;
                cur = next;
                path.Add(cur);
            }

            if (path.Count == 0) return;

            // Expand rings and apply h **= 0.8 (Azgaar formula: exp = 0.9 - 0.1/w*w = 0.8)
            // Path cells are NOT marked used — they get modified via neighbor iteration
            // (matching Azgaar where path cells' heights are reduced by adjacent path cells)
            var used = new bool[cellCount];
            var ring = new List<int>(path);

            for (int i = 0; i < desiredWidth; i++)
            {
                var nextRing = new List<int>();
                foreach (int r in ring)
                {
                    foreach (int c in grid.GetNeighbors(r))
                    {
                        if (c < 0 || c >= cellCount || used[c]) continue;
                        used[c] = true;
                        nextRing.Add(c);
                        float h = grid.Heights[c];
                        h = (float)Math.Pow(h, 0.8);
                        if (h > 100f) h = 5f;
                        grid.Heights[c] = h;
                    }
                }
                ring = nextRing;
            }
        }

        /// <summary>
        /// Invert heightmap (mirror).
        /// </summary>
        public static void Invert(HeightGrid grid, int axis)
        {
            float width = grid.Mesh.Width;
            float height = grid.Mesh.Height;
            var original = (float[])grid.Heights.Clone();

            for (int i = 0; i < grid.CellCount; i++)
            {
                Vec2 center = grid.Mesh.CellCenters[i];
                float mx = axis == 0 || axis == 2 ? width - center.X : center.X;
                float my = axis == 1 || axis == 2 ? height - center.Y : center.Y;

                int mirrorCell = FindNearestCell(grid.Mesh, mx, my);
                if (mirrorCell >= 0)
                    grid.Heights[i] = original[mirrorCell];
            }
        }

        /// <summary>
        /// Find the cell nearest to a world position.
        /// </summary>
        internal static int FindNearestCell(CellMesh mesh, float x, float y)
        {
            Vec2 pos = new Vec2(x, y);
            int nearest = -1;
            float nearestDist = float.MaxValue;

            for (int i = 0; i < mesh.CellCount; i++)
            {
                float dist = Vec2.SqrDistance(mesh.CellCenters[i], pos);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }
    }
}
