using System;
using System.Collections.Generic;

namespace MapGen.Core
{
    /// <summary>
    /// V2 heightmap operations in signed meters relative to sea level.
    /// </summary>
    public static class HeightmapOpsV2
    {
        static float GetBlobPower(int cellCount)
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

        static float GetLinePower(int cellCount)
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

        static float ShapeUnitMeters(ElevationFieldV2 field)
        {
            float unit = (field.MaxElevationMeters + field.MaxSeaDepthMeters) / 100f;
            if (unit <= 0f || float.IsNaN(unit) || float.IsInfinity(unit))
                return 1f;
            return unit;
        }

        static float ToShapeUnits(ElevationFieldV2 field, float meters) => Math.Abs(meters) / ShapeUnitMeters(field);

        static float FromShapeUnits(ElevationFieldV2 field, float units) => units * ShapeUnitMeters(field);

        public static void Hill(ElevationFieldV2 field, float seedX, float seedY, float heightMeters, Random rng)
        {
            int cellCount = field.CellCount;
            if (cellCount == 0)
                return;

            float blobPower = GetBlobPower(cellCount);
            int hUnits = (int)Math.Min(Math.Max(ToShapeUnits(field, heightMeters), 0f), 100f);

            int seedCell = -1;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                seedCell = FindNearestCell(field.Mesh, seedX * field.Mesh.Width, seedY * field.Mesh.Height);
                if (seedCell < 0)
                    return;

                if (field[seedCell] + FromShapeUnits(field, hUnits) <= field.MaxElevationMeters * 0.90f)
                    break;

                seedX = Clamp(seedX + ((float)rng.NextDouble() - 0.5f) * 0.1f, 0f, 1f);
                seedY = Clamp(seedY + ((float)rng.NextDouble() - 0.5f) * 0.1f, 0f, 1f);
            }

            if (seedCell < 0)
                return;

            var changeUnits = new int[cellCount];
            changeUnits[seedCell] = hUnits;

            var queue = new Queue<int>();
            queue.Enqueue(seedCell);

            while (queue.Count > 0)
            {
                int q = queue.Dequeue();
                foreach (int c in field.Mesh.CellNeighbors[q])
                {
                    if (c < 0 || c >= cellCount || changeUnits[c] > 0)
                        continue;

                    changeUnits[c] = (int)(Math.Pow(changeUnits[q], blobPower) * (0.9 + rng.NextDouble() * 0.2));
                    if (changeUnits[c] > 1)
                        queue.Enqueue(c);
                }
            }

            for (int i = 0; i < cellCount; i++)
                field[i] = field[i] + FromShapeUnits(field, changeUnits[i]);
        }

        public static void Pit(ElevationFieldV2 field, float seedX, float seedY, float depthMeters, Random rng)
        {
            int cellCount = field.CellCount;
            if (cellCount == 0)
                return;

            float blobPower = GetBlobPower(cellCount);
            int seedCell = -1;
            for (int attempt = 0; attempt < 50; attempt++)
            {
                seedCell = FindNearestCell(field.Mesh, seedX * field.Mesh.Width, seedY * field.Mesh.Height);
                if (seedCell < 0)
                    return;
                if (field.IsLand(seedCell))
                    break;

                seedX = Clamp(seedX + ((float)rng.NextDouble() - 0.5f) * 0.1f, 0f, 1f);
                seedY = Clamp(seedY + ((float)rng.NextDouble() - 0.5f) * 0.1f, 0f, 1f);
            }

            if (seedCell < 0)
                return;

            float hUnits = Math.Min(Math.Max(ToShapeUnits(field, depthMeters), 0f), 100f);
            var used = new bool[cellCount];
            var queue = new Queue<int>();
            queue.Enqueue(seedCell);

            while (queue.Count > 0)
            {
                int q = queue.Dequeue();
                hUnits = (float)(Math.Pow(hUnits, blobPower) * (0.9 + rng.NextDouble() * 0.2));
                if (hUnits < 1f)
                    break;

                foreach (int c in field.Mesh.CellNeighbors[q])
                {
                    if (c < 0 || c >= cellCount || used[c])
                        continue;

                    float deltaUnits = hUnits * (0.9f + (float)rng.NextDouble() * 0.2f);
                    field[c] = field[c] - FromShapeUnits(field, deltaUnits);
                    used[c] = true;
                    queue.Enqueue(c);
                }
            }
        }

        public static void Range(ElevationFieldV2 field, float x1, float y1, float x2, float y2, float heightMeters, Random rng)
        {
            int cellCount = field.CellCount;
            if (cellCount == 0)
                return;

            float linePower = GetLinePower(cellCount);
            float w = field.Mesh.Width;
            float mapH = field.Mesh.Height;

            int startCell = FindNearestCell(field.Mesh, x1 * w, y1 * mapH);
            int endCell = FindNearestCell(field.Mesh, x2 * w, y2 * mapH);
            if (startCell < 0 || endCell < 0 || startCell == endCell)
                return;

            var used = new bool[cellCount];
            var ridge = new List<int>();
            int cur = startCell;
            used[cur] = true;
            ridge.Add(cur);

            while (cur != endCell)
            {
                float minDist = float.MaxValue;
                int next = -1;

                foreach (int c in field.Mesh.CellNeighbors[cur])
                {
                    if (c < 0 || c >= cellCount || used[c])
                        continue;

                    float dx = field.Mesh.CellCenters[endCell].X - field.Mesh.CellCenters[c].X;
                    float dy = field.Mesh.CellCenters[endCell].Y - field.Mesh.CellCenters[c].Y;
                    float dist = dx * dx + dy * dy;
                    if (rng.NextDouble() > 0.85) dist *= 0.5f;

                    if (dist < minDist)
                    {
                        minDist = dist;
                        next = c;
                    }
                }

                if (next < 0)
                    break;

                cur = next;
                used[cur] = true;
                ridge.Add(cur);
            }

            float hUnits = ToShapeUnits(field, heightMeters);
            var frontier = new List<int>(ridge);
            int depth = 0;

            while (frontier.Count > 0)
            {
                foreach (int cell in frontier)
                {
                    float deltaUnits = hUnits * (0.85f + (float)rng.NextDouble() * 0.3f);
                    field[cell] = field[cell] + FromShapeUnits(field, deltaUnits);
                }

                hUnits = (float)Math.Pow(hUnits, linePower) - 1f;
                if (hUnits < 2f)
                    break;

                depth++;
                var nextFrontier = new List<int>();
                foreach (int f in frontier)
                {
                    foreach (int c in field.Mesh.CellNeighbors[f])
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

            for (int d = 0; d < ridge.Count; d += 6)
            {
                int pCur = ridge[d];
                for (int step = 0; step < depth; step++)
                {
                    int lowest = -1;
                    float lowestH = float.MaxValue;
                    foreach (int c in field.Mesh.CellNeighbors[pCur])
                    {
                        if (c >= 0 && c < cellCount && field[c] < lowestH)
                        {
                            lowestH = field[c];
                            lowest = c;
                        }
                    }

                    if (lowest < 0)
                        break;

                    field[lowest] = (field[pCur] * 2f + field[lowest]) / 3f;
                    pCur = lowest;
                }
            }
        }

        public static void Trough(ElevationFieldV2 field, float x1, float y1, float x2, float y2, float depthMeters, Random rng)
        {
            int cellCount = field.CellCount;
            if (cellCount == 0)
                return;

            float linePower = GetLinePower(cellCount);
            float w = field.Mesh.Width;
            float mapH = field.Mesh.Height;

            int startCell = FindNearestCell(field.Mesh, x1 * w, y1 * mapH);
            int endCell = FindNearestCell(field.Mesh, x2 * w, y2 * mapH);
            if (startCell < 0 || endCell < 0 || startCell == endCell)
                return;

            var used = new bool[cellCount];
            var ridge = new List<int>();
            int cur = startCell;
            used[cur] = true;
            ridge.Add(cur);

            while (cur != endCell)
            {
                float minDist = float.MaxValue;
                int next = -1;

                foreach (int c in field.Mesh.CellNeighbors[cur])
                {
                    if (c < 0 || c >= cellCount || used[c])
                        continue;

                    float dx = field.Mesh.CellCenters[endCell].X - field.Mesh.CellCenters[c].X;
                    float dy = field.Mesh.CellCenters[endCell].Y - field.Mesh.CellCenters[c].Y;
                    float dist = dx * dx + dy * dy;
                    if (rng.NextDouble() > 0.80) dist *= 0.5f;

                    if (dist < minDist)
                    {
                        minDist = dist;
                        next = c;
                    }
                }

                if (next < 0)
                    break;

                cur = next;
                used[cur] = true;
                ridge.Add(cur);
            }

            float hUnits = ToShapeUnits(field, depthMeters);
            var frontier = new List<int>(ridge);
            int depth = 0;

            while (frontier.Count > 0)
            {
                foreach (int cell in frontier)
                {
                    float deltaUnits = hUnits * (0.85f + (float)rng.NextDouble() * 0.3f);
                    field[cell] = field[cell] - FromShapeUnits(field, deltaUnits);
                }

                hUnits = (float)Math.Pow(hUnits, linePower) - 1f;
                if (hUnits < 2f)
                    break;

                depth++;
                var nextFrontier = new List<int>();
                foreach (int f in frontier)
                {
                    foreach (int c in field.Mesh.CellNeighbors[f])
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

            for (int d = 0; d < ridge.Count; d += 6)
            {
                int pCur = ridge[d];
                for (int step = 0; step < depth; step++)
                {
                    int lowest = -1;
                    float lowestH = float.MaxValue;
                    foreach (int c in field.Mesh.CellNeighbors[pCur])
                    {
                        if (c >= 0 && c < cellCount && field[c] < lowestH)
                        {
                            lowestH = field[c];
                            lowest = c;
                        }
                    }

                    if (lowest < 0)
                        break;

                    field[lowest] = (field[pCur] * 2f + field[lowest]) / 3f;
                    pCur = lowest;
                }
            }
        }

        public static void Mask(ElevationFieldV2 field, float fraction)
        {
            float fr = Math.Abs(fraction);
            if (fr < 1f) fr = 1f;

            float w = field.Mesh.Width;
            float h = field.Mesh.Height;

            for (int i = 0; i < field.CellCount; i++)
            {
                Vec2 center = field.Mesh.CellCenters[i];
                float nx = (2f * center.X) / w - 1f;
                float ny = (2f * center.Y) / h - 1f;
                float distance = (1f - nx * nx) * (1f - ny * ny);
                if (fraction < 0) distance = 1f - distance;

                float masked = field[i] * distance;
                field[i] = (field[i] * (fr - 1f) + masked) / fr;
            }
        }

        public static void Add(ElevationFieldV2 field, float valueMeters, float minHeightMeters, float maxHeightMeters)
        {
            bool isLandRange = Math.Abs(minHeightMeters) < 0.0001f && maxHeightMeters > 0f;

            for (int i = 0; i < field.CellCount; i++)
            {
                float h = field[i];
                if (h >= minHeightMeters && h <= maxHeightMeters)
                {
                    float next = h + valueMeters;
                    if (isLandRange && next < 0f)
                        next = 0f;

                    field[i] = next;
                }
            }
        }

        public static void Multiply(ElevationFieldV2 field, float factor, float minHeightMeters, float maxHeightMeters)
        {
            bool isLandRange = Math.Abs(minHeightMeters) < 0.0001f && maxHeightMeters > 0f;

            for (int i = 0; i < field.CellCount; i++)
            {
                float h = field[i];
                if (h >= minHeightMeters && h <= maxHeightMeters)
                {
                    if (isLandRange)
                        field[i] = h * factor;
                    else
                        field[i] = h * factor;
                }
            }
        }

        public static void Smooth(ElevationFieldV2 field, int fr)
        {
            if (fr < 1)
                fr = 1;

            var temp = new float[field.CellCount];

            for (int i = 0; i < field.CellCount; i++)
            {
                int[] neighbors = field.Mesh.CellNeighbors[i];
                float sum = field[i];
                int count = 1;

                foreach (int n in neighbors)
                {
                    if (n >= 0 && n < field.CellCount)
                    {
                        sum += field[n];
                        count++;
                    }
                }

                float mean = sum / count;
                temp[i] = fr <= 1 ? mean : ((field[i] * (fr - 1)) + mean) / fr;
            }

            Array.Copy(temp, field.ElevationMetersSigned, field.CellCount);
            field.ClampAll();
        }

        public static void Strait(ElevationFieldV2 field, int desiredWidth, int direction, Random rng)
        {
            int cellCount = field.CellCount;
            if (cellCount == 0 || desiredWidth < 1)
                return;

            float mapW = field.Mesh.Width;
            float mapH = field.Mesh.Height;
            bool vertical = direction == 1;

            float startX = vertical
                ? (float)(rng.NextDouble() * mapW * 0.4 + mapW * 0.3)
                : 5f;
            float startY = vertical
                ? 5f
                : (float)(rng.NextDouble() * mapH * 0.4 + mapH * 0.3);
            float endX = vertical
                ? (float)(mapW - startX - mapW * 0.1 + rng.NextDouble() * mapW * 0.2)
                : mapW - 5f;
            float endY = vertical
                ? mapH - 5f
                : (float)(mapH - startY - mapH * 0.1 + rng.NextDouble() * mapH * 0.2);

            int startCell = FindNearestCell(field.Mesh, startX, startY);
            int endCell = FindNearestCell(field.Mesh, endX, endY);
            if (startCell < 0 || endCell < 0)
                return;

            var path = new List<int>();
            int cur = startCell;
            int guard = cellCount * 2;

            while (cur != endCell && guard-- > 0)
            {
                float minDist = float.MaxValue;
                int next = -1;

                foreach (int c in field.Mesh.CellNeighbors[cur])
                {
                    if (c < 0 || c >= cellCount)
                        continue;

                    float dx = field.Mesh.CellCenters[endCell].X - field.Mesh.CellCenters[c].X;
                    float dy = field.Mesh.CellCenters[endCell].Y - field.Mesh.CellCenters[c].Y;
                    float dist = dx * dx + dy * dy;
                    if (rng.NextDouble() > 0.8) dist *= 0.5f;

                    if (dist < minDist)
                    {
                        minDist = dist;
                        next = c;
                    }
                }

                if (next < 0)
                    break;

                cur = next;
                path.Add(cur);
            }

            if (path.Count == 0)
                return;

            var used = new bool[cellCount];
            var ring = new List<int>(path);

            for (int i = 0; i < desiredWidth; i++)
            {
                var nextRing = new List<int>();
                foreach (int r in ring)
                {
                    foreach (int c in field.Mesh.CellNeighbors[r])
                    {
                        if (c < 0 || c >= cellCount || used[c])
                            continue;

                        used[c] = true;
                        nextRing.Add(c);

                        float h = field[c];
                        float carve = Math.Max(FromShapeUnits(field, 6f), Math.Abs(h) * 0.55f);
                        h -= carve;
                        h -= field.MaxSeaDepthMeters * 0.03f;
                        field[c] = h;
                    }
                }

                ring = nextRing;
                if (ring.Count == 0)
                    break;
            }
        }

        public static void Invert(ElevationFieldV2 field, int axis)
        {
            float width = field.Mesh.Width;
            float height = field.Mesh.Height;
            var original = (float[])field.ElevationMetersSigned.Clone();

            for (int i = 0; i < field.CellCount; i++)
            {
                Vec2 center = field.Mesh.CellCenters[i];
                float mx = (axis == 0 || axis == 2) ? width - center.X : center.X;
                float my = (axis == 1 || axis == 2) ? height - center.Y : center.Y;

                int mirrorCell = FindNearestCell(field.Mesh, mx, my);
                if (mirrorCell >= 0)
                    field[i] = original[mirrorCell];
            }
        }

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

        static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);
    }
}
