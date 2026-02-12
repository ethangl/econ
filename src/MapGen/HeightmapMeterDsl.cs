using System;
using System.Globalization;

namespace MapGen.Core
{
    /// <summary>
    /// DSL parser for map terrain templates.
    /// Elevation magnitudes are specified in meters (suffix: m).
    /// Coordinates are percentages (0..100) for template authoring convenience.
    /// </summary>
    public static class HeightmapDsl
    {
        public static void Execute(ElevationField field, string script, int seed, HeightmapDslDiagnostics diagnostics = null)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (script == null) throw new ArgumentNullException(nameof(script));

            var rng = new Random(seed);
            var lines = script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int logicalLine = 0;
            foreach (string raw in lines)
            {
                logicalLine++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                float[] before = null;
                float beforeLand = 0f;
                float beforeEdgeLand = 0f;
                if (diagnostics != null)
                {
                    before = (float[])field.ElevationMetersSigned.Clone();
                    beforeLand = field.LandRatio();
                    beforeEdgeLand = ComputeEdgeLandRatio(field);
                }

                OpTrace trace = ExecuteLine(field, line, rng);
                field.ClampAll();

                if (diagnostics != null)
                {
                    ComputeDeltaStats(before, field, out float changedRatio, out float meanAbsDelta, out float maxRaise, out float maxLower);
                    diagnostics.Add(new HeightmapDslOpMetrics
                    {
                        LineNumber = logicalLine,
                        Operation = trace.Operation,
                        RawLine = line,
                        BeforeLandRatio = beforeLand,
                        AfterLandRatio = field.LandRatio(),
                        BeforeEdgeLandRatio = beforeEdgeLand,
                        AfterEdgeLandRatio = ComputeEdgeLandRatio(field),
                        ChangedCellRatio = changedRatio,
                        MeanAbsDeltaMeters = meanAbsDelta,
                        MaxRaiseMeters = maxRaise,
                        MaxLowerMeters = maxLower,
                        PlacementCount = trace.PlacementCount,
                        RequestedXMinPercent = trace.RequestedXMinPercent,
                        RequestedXMaxPercent = trace.RequestedXMaxPercent,
                        RequestedYMinPercent = trace.RequestedYMinPercent,
                        RequestedYMaxPercent = trace.RequestedYMaxPercent,
                        SeedXMinPercent = trace.SeedXMinPercent,
                        SeedXMaxPercent = trace.SeedXMaxPercent,
                        SeedYMinPercent = trace.SeedYMinPercent,
                        SeedYMaxPercent = trace.SeedYMaxPercent,
                        EndXMinPercent = trace.EndXMinPercent,
                        EndXMaxPercent = trace.EndXMaxPercent,
                        EndYMinPercent = trace.EndYMinPercent,
                        EndYMaxPercent = trace.EndYMaxPercent
                    });
                }
            }
        }

        static OpTrace ExecuteLine(ElevationField field, string line, Random rng)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return new OpTrace("noop");

            string op = parts[0].ToLowerInvariant();
            switch (op)
            {
                case "hill":
                    return ExecuteBlob(field, parts, positive: true, rng, op);
                case "pit":
                    return ExecuteBlob(field, parts, positive: false, rng, op);
                case "range":
                    return ExecuteLinear(field, parts, positive: true, rng, op);
                case "trough":
                    return ExecuteLinear(field, parts, positive: false, rng, op);
                case "mask":
                    ExecuteMask(field, parts);
                    return new OpTrace(op);
                case "strait":
                    ExecuteStrait(field, parts, rng);
                    return new OpTrace(op);
                case "add":
                    ExecuteAdd(field, parts);
                    return new OpTrace(op);
                case "multiply":
                    ExecuteMultiply(field, parts);
                    return new OpTrace(op);
                case "smooth":
                    ExecuteSmooth(field, parts);
                    return new OpTrace(op);
                case "invert":
                    ExecuteInvert(field, parts, rng);
                    return new OpTrace(op);
                default:
                    throw new ArgumentException($"Unknown heightmap operation: {op}");
            }
        }

        static OpTrace ExecuteBlob(ElevationField field, string[] parts, bool positive, Random rng, string operation)
        {
            if (parts.Length < 5)
                throw new ArgumentException("Blob operation requires: count height_m x% y%.");

            int count = ParseCountRange(parts[1], rng);
            float heightMeters = ParseMeterRange(parts[2], rng);
            var xRange = ParsePercentRange(parts[3]);
            var yRange = ParsePercentRange(parts[4]);
            NormalizeRange(xRange.min, xRange.max, out float xMinPercent, out float xMaxPercent);
            NormalizeRange(yRange.min, yRange.max, out float yMinPercent, out float yMaxPercent);
            float xMin = xMinPercent / 100f;
            float xMax = xMaxPercent / 100f;
            float yMin = yMinPercent / 100f;
            float yMax = yMaxPercent / 100f;
            // DSL x/y bounds are hard placement bounds for blob seeds. Terrain spread may extend beyond them.
            var trace = new OpTrace(operation)
            {
                RequestedXMinPercent = xMinPercent,
                RequestedXMaxPercent = xMaxPercent,
                RequestedYMinPercent = yMinPercent,
                RequestedYMaxPercent = yMaxPercent
            };

            for (int i = 0; i < count; i++)
            {
                float x = RandInRange(xMin, xMax, rng);
                float y = RandInRange(yMin, yMax, rng);
                bool placed;
                float acceptedX;
                float acceptedY;

                if (positive)
                {
                    placed = HeightmapTerrainOps.Hill(
                        field,
                        x,
                        y,
                        heightMeters,
                        rng,
                        xMin,
                        xMax,
                        yMin,
                        yMax,
                        out acceptedX,
                        out acceptedY);
                }
                else
                {
                    placed = HeightmapTerrainOps.Pit(
                        field,
                        x,
                        y,
                        heightMeters,
                        rng,
                        xMin,
                        xMax,
                        yMin,
                        yMax,
                        out acceptedX,
                        out acceptedY);
                }

                if (placed)
                {
                    trace.PlacementCount++;
                    trace.AddSeed(acceptedX * 100f, acceptedY * 100f);
                }
            }

            return trace;
        }

        static OpTrace ExecuteLinear(ElevationField field, string[] parts, bool positive, Random rng, string operation)
        {
            if (parts.Length < 5)
                throw new ArgumentException("Linear operation requires: count height_m x% y%.");

            int count = ParseCountRange(parts[1], rng);
            float heightMeters = ParseMeterRange(parts[2], rng);
            var xRange = ParsePercentRange(parts[3]);
            var yRange = ParsePercentRange(parts[4]);
            NormalizeRange(xRange.min, xRange.max, out float xMinPercent, out float xMaxPercent);
            NormalizeRange(yRange.min, yRange.max, out float yMinPercent, out float yMaxPercent);
            float xMin = xMinPercent / 100f;
            float xMax = xMaxPercent / 100f;
            float yMin = yMinPercent / 100f;
            float yMax = yMaxPercent / 100f;

            float maxDistFraction = positive ? 3f : 2f;
            float mapW = field.Mesh.Width;
            float mapH = field.Mesh.Height;
            float minDist = mapW / 8f;
            float maxDist = mapW / maxDistFraction;
            // DSL x/y bounds are hard placement bounds for line start/end points. Terrain spread may extend beyond them.
            var trace = new OpTrace(operation)
            {
                RequestedXMinPercent = xMinPercent,
                RequestedXMaxPercent = xMaxPercent,
                RequestedYMinPercent = yMinPercent,
                RequestedYMaxPercent = yMaxPercent
            };

            for (int i = 0; i < count; i++)
            {
                float x1 = RandInRange(xMin, xMax, rng);
                float y1 = RandInRange(yMin, yMax, rng);

                if (!positive)
                {
                    int limit = 0;
                    while (limit < 50)
                    {
                        int startCell = HeightmapTerrainOps.FindNearestCell(field.Mesh, x1 * mapW, y1 * mapH);
                        if (startCell >= 0 && field.IsLand(startCell)) break;
                        x1 = RandInRange(xMin, xMax, rng);
                        y1 = RandInRange(yMin, yMax, rng);
                        limit++;
                    }
                }

                ChooseEndpointWithinBounds(x1, y1, xMin, xMax, yMin, yMax, minDist, maxDist, mapW, mapH, rng, out float x2, out float y2);
                trace.PlacementCount++;
                trace.AddSeed(x1 * 100f, y1 * 100f);
                trace.AddEnd(x2 * 100f, y2 * 100f);

                if (positive)
                    HeightmapTerrainOps.Range(field, x1, y1, x2, y2, heightMeters, rng);
                else
                    HeightmapTerrainOps.Trough(field, x1, y1, x2, y2, heightMeters, rng);
            }

            return trace;
        }

        static void ExecuteMask(ElevationField field, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Mask operation requires: fraction.");

            float fraction = ParseFloat(parts[1]);
            HeightmapTerrainOps.Mask(field, fraction);
        }

        static void ExecuteStrait(ElevationField field, string[] parts, Random rng)
        {
            if (parts.Length < 3)
                throw new ArgumentException("Strait operation requires: width direction.");

            int width = ParseCountRange(parts[1], rng);
            string dirStr = parts[2].ToLowerInvariant();
            int direction = (dirStr == "vertical" || dirStr == "1") ? 1 : 0;
            HeightmapTerrainOps.Strait(field, width, direction, rng);
        }

        static void ExecuteAdd(ElevationField field, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Add operation requires: delta_m [range].");

            float deltaMeters = ParseMeters(parts[1]);
            var (minH, maxH) = parts.Length >= 3
                ? ParseHeightRange(parts[2], field)
                : (-field.MaxSeaDepthMeters, field.MaxElevationMeters);

            HeightmapTerrainOps.Add(field, deltaMeters, minH, maxH);
        }

        static void ExecuteMultiply(ElevationField field, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Multiply operation requires: factor [range].");

            float factor = ParseFloat(parts[1]);
            var (minH, maxH) = parts.Length >= 3
                ? ParseHeightRange(parts[2], field)
                : (-field.MaxSeaDepthMeters, field.MaxElevationMeters);

            HeightmapTerrainOps.Multiply(field, factor, minH, maxH);
        }

        static void ExecuteSmooth(ElevationField field, string[] parts)
        {
            int passes = 1;
            if (parts.Length >= 2)
                passes = (int)Math.Round(ParseFloat(parts[1]), MidpointRounding.AwayFromZero);

            HeightmapTerrainOps.Smooth(field, passes);
        }

        static void ExecuteInvert(ElevationField field, string[] parts, Random rng)
        {
            float probability = 0.5f;
            int axis = 2;

            if (parts.Length >= 2)
                probability = ParseFloat(parts[1]);

            if (parts.Length >= 3)
            {
                string axisStr = parts[2].ToLowerInvariant();
                switch (axisStr)
                {
                    case "x": axis = 0; break;
                    case "y": axis = 1; break;
                    case "both": axis = 2; break;
                    default:
                        if (int.TryParse(axisStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                            axis = parsed;
                        break;
                }
            }

            if (rng.NextDouble() < probability)
                HeightmapTerrainOps.Invert(field, axis);
        }

        static int ParseCountRange(string token, Random rng)
        {
            if (TryParseRange(token, out string minRaw, out string maxRaw))
            {
                int minV = ParseRoundedInt(minRaw, rng);
                int maxV = ParseRoundedInt(maxRaw, rng);
                if (maxV < minV)
                {
                    int t = minV;
                    minV = maxV;
                    maxV = t;
                }

                return rng.Next(minV, maxV + 1);
            }

            return ParseRoundedInt(token, rng);
        }

        static int ParseRoundedInt(string token, Random rng)
        {
            float value = ParseFloat(token);
            int floor = (int)Math.Floor(value);
            float frac = value - floor;
            if (frac > 0f && rng.NextDouble() < frac)
                return floor + 1;
            return floor;
        }

        static float ParseMeterRange(string token, Random rng)
        {
            if (!TryParseRange(token, out string minRaw, out string maxRaw))
                return ParseMeters(token);

            float minV = ParseMeters(minRaw);
            float maxV = ParseMeters(maxRaw);
            if (maxV < minV)
            {
                float t = minV;
                minV = maxV;
                maxV = t;
            }

            return RandInRange(minV, maxV, rng);
        }

        static float ParseMeters(string token)
        {
            string t = token.Trim().ToLowerInvariant();
            if (t.EndsWith("m", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1);

            return ParseFloat(t);
        }

        static (float min, float max) ParsePercentRange(string token)
        {
            if (TryParseRange(token, out string minRaw, out string maxRaw))
                return (ParsePercent(minRaw), ParsePercent(maxRaw));

            float value = ParsePercent(token);
            return (value, value);
        }

        static float ParsePercent(string token)
        {
            string t = token.Trim().TrimEnd('%');
            return ParseFloat(t);
        }

        static (float min, float max) ParseHeightRange(string token, ElevationField field)
        {
            string t = token.Trim().ToLowerInvariant();
            switch (t)
            {
                case "land": return (0f, field.MaxElevationMeters);
                case "water": return (-field.MaxSeaDepthMeters, 0f);
                case "all": return (-field.MaxSeaDepthMeters, field.MaxElevationMeters);
            }

            if (!TryParseRange(t, out string minRaw, out string maxRaw))
                throw new ArgumentException($"Invalid height range token: {token}");

            float minV = ParseMeters(minRaw);
            float maxV = ParseMeters(maxRaw);
            if (maxV < minV)
            {
                float swap = minV;
                minV = maxV;
                maxV = swap;
            }

            return (minV, maxV);
        }

        static bool TryParseRange(string token, out string left, out string right)
        {
            left = null;
            right = null;
            string t = token.Trim();

            for (int i = 1; i < t.Length - 1; i++)
            {
                if (t[i] != '-')
                    continue;

                char prev = t[i - 1];
                char next = t[i + 1];
                bool prevOk = char.IsDigit(prev) || prev == '.' || prev == 'm' || prev == '%' || prev == ')';
                bool nextOk = char.IsDigit(next) || next == '.' || next == '-';

                if (prevOk && nextOk)
                {
                    left = t.Substring(0, i);
                    right = t.Substring(i + 1);
                    return true;
                }
            }

            return false;
        }

        static float ParseFloat(string token)
        {
            return float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static float RandInRange(float min, float max, Random rng)
        {
            if (max <= min)
                return min;
            return min + (float)rng.NextDouble() * (max - min);
        }

        static float ComputeEdgeLandRatio(ElevationField field)
        {
            float edgeMarginX = field.Mesh.Width * 0.12f;
            float edgeMarginY = field.Mesh.Height * 0.12f;
            int edgeCells = 0;
            int edgeLand = 0;

            for (int i = 0; i < field.CellCount; i++)
            {
                Vec2 center = field.Mesh.CellCenters[i];
                bool isEdge = center.X <= edgeMarginX || center.X >= field.Mesh.Width - edgeMarginX
                    || center.Y <= edgeMarginY || center.Y >= field.Mesh.Height - edgeMarginY;
                if (!isEdge)
                    continue;

                edgeCells++;
                if (field.IsLand(i))
                    edgeLand++;
            }

            if (edgeCells == 0)
                return 0f;

            return edgeLand / (float)edgeCells;
        }

        static void ComputeDeltaStats(
            float[] before,
            ElevationField field,
            out float changedRatio,
            out float meanAbsDelta,
            out float maxRaise,
            out float maxLower)
        {
            changedRatio = 0f;
            meanAbsDelta = 0f;
            maxRaise = 0f;
            maxLower = 0f;
            if (before == null || before.Length != field.CellCount || field.CellCount == 0)
                return;

            int changed = 0;
            float sumAbs = 0f;
            for (int i = 0; i < field.CellCount; i++)
            {
                float delta = field[i] - before[i];
                float abs = Math.Abs(delta);
                if (abs > 1e-6f)
                    changed++;

                sumAbs += abs;
                if (delta > maxRaise)
                    maxRaise = delta;
                if (delta < 0f && -delta > maxLower)
                    maxLower = -delta;
            }

            changedRatio = changed / (float)field.CellCount;
            meanAbsDelta = sumAbs / field.CellCount;
        }

        static void ChooseEndpointWithinBounds(
            float x1,
            float y1,
            float xMin,
            float xMax,
            float yMin,
            float yMax,
            float minDist,
            float maxDist,
            float mapW,
            float mapH,
            Random rng,
            out float x2,
            out float y2)
        {
            x2 = x1;
            y2 = y1;
            float bestPenalty = float.MaxValue;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                float candidateX = RandInRange(xMin, xMax, rng);
                float candidateY = RandInRange(yMin, yMax, rng);
                float dist = Distance(candidateX, candidateY, x1, y1, mapW, mapH);
                float penalty = DistanceBandPenalty(dist, minDist, maxDist);
                if (Math.Abs(candidateX - x1) < 0.0001f && Math.Abs(candidateY - y1) < 0.0001f)
                    penalty += minDist;

                if (penalty <= 0f)
                {
                    x2 = candidateX;
                    y2 = candidateY;
                    return;
                }

                if (penalty < bestPenalty)
                {
                    bestPenalty = penalty;
                    x2 = candidateX;
                    y2 = candidateY;
                }
            }

            if (Math.Abs(x2 - x1) < 0.0001f && Math.Abs(y2 - y1) < 0.0001f)
            {
                x2 = Math.Abs(xMax - x1) >= Math.Abs(xMin - x1) ? xMax : xMin;
                y2 = Math.Abs(yMax - y1) >= Math.Abs(yMin - y1) ? yMax : yMin;
            }
        }

        static float DistanceBandPenalty(float distance, float minDist, float maxDist)
        {
            if (distance < minDist)
                return minDist - distance;

            if (distance > maxDist)
                return distance - maxDist;

            return 0f;
        }

        static float Distance(float x2, float y2, float x1, float y1, float mapW, float mapH) =>
            Math.Abs(y2 - y1) * mapH + Math.Abs(x2 - x1) * mapW;

        static void NormalizeRange(float a, float b, out float min, out float max)
        {
            if (b < a)
            {
                min = b;
                max = a;
                return;
            }

            min = a;
            max = b;
        }

        sealed class OpTrace
        {
            public readonly string Operation;
            public int PlacementCount;
            public float? RequestedXMinPercent;
            public float? RequestedXMaxPercent;
            public float? RequestedYMinPercent;
            public float? RequestedYMaxPercent;
            public float? SeedXMinPercent;
            public float? SeedXMaxPercent;
            public float? SeedYMinPercent;
            public float? SeedYMaxPercent;
            public float? EndXMinPercent;
            public float? EndXMaxPercent;
            public float? EndYMinPercent;
            public float? EndYMaxPercent;

            public OpTrace(string operation)
            {
                Operation = operation;
            }

            public void AddSeed(float xPercent, float yPercent)
            {
                ExpandRange(ref SeedXMinPercent, ref SeedXMaxPercent, xPercent);
                ExpandRange(ref SeedYMinPercent, ref SeedYMaxPercent, yPercent);
            }

            public void AddEnd(float xPercent, float yPercent)
            {
                ExpandRange(ref EndXMinPercent, ref EndXMaxPercent, xPercent);
                ExpandRange(ref EndYMinPercent, ref EndYMaxPercent, yPercent);
            }

            static void ExpandRange(ref float? min, ref float? max, float value)
            {
                if (!min.HasValue || value < min.Value)
                    min = value;
                if (!max.HasValue || value > max.Value)
                    max = value;
            }
        }
    }
}
