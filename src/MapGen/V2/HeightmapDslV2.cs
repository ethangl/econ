using System;
using System.Globalization;

namespace MapGen.Core
{
    /// <summary>
    /// DSL parser for MapGen V2 terrain templates.
    /// Elevation magnitudes are specified in meters (suffix: m).
    /// Coordinates are percentages (0..100) for template authoring convenience.
    /// </summary>
    public static class HeightmapDslV2
    {
        public static void Execute(ElevationFieldV2 field, string script, int seed)
        {
            if (field == null) throw new ArgumentNullException(nameof(field));
            if (script == null) throw new ArgumentNullException(nameof(script));

            var rng = new Random(seed);
            var lines = script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                ExecuteLine(field, line, rng);
                field.ClampAll();
            }
        }

        static void ExecuteLine(ElevationFieldV2 field, string line, Random rng)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            string op = parts[0].ToLowerInvariant();
            switch (op)
            {
                case "hill":
                    ExecuteBlob(field, parts, positive: true, rng);
                    break;
                case "pit":
                    ExecuteBlob(field, parts, positive: false, rng);
                    break;
                case "range":
                    ExecuteLinear(field, parts, positive: true, rng);
                    break;
                case "trough":
                    ExecuteLinear(field, parts, positive: false, rng);
                    break;
                case "mask":
                    ExecuteMask(field, parts);
                    break;
                case "strait":
                    ExecuteStrait(field, parts, rng);
                    break;
                case "add":
                    ExecuteAdd(field, parts);
                    break;
                case "multiply":
                    ExecuteMultiply(field, parts);
                    break;
                case "smooth":
                    ExecuteSmooth(field, parts);
                    break;
                case "invert":
                    ExecuteInvert(field, parts, rng);
                    break;
                default:
                    throw new ArgumentException($"Unknown V2 heightmap operation: {op}");
            }
        }

        static void ExecuteBlob(ElevationFieldV2 field, string[] parts, bool positive, Random rng)
        {
            if (parts.Length < 5)
                throw new ArgumentException("Blob operation requires: count height_m x% y%.");

            int count = ParseCountRange(parts[1], rng);
            float heightMeters = ParseMeterRange(parts[2], rng);
            var xRange = ParsePercentRange(parts[3]);
            var yRange = ParsePercentRange(parts[4]);

            for (int i = 0; i < count; i++)
            {
                float x = RandInRange(xRange.min, xRange.max, rng) / 100f;
                float y = RandInRange(yRange.min, yRange.max, rng) / 100f;

                if (positive)
                    HeightmapOpsV2.Hill(field, x, y, heightMeters, rng);
                else
                    HeightmapOpsV2.Pit(field, x, y, heightMeters, rng);
            }
        }

        static void ExecuteLinear(ElevationFieldV2 field, string[] parts, bool positive, Random rng)
        {
            if (parts.Length < 5)
                throw new ArgumentException("Linear operation requires: count height_m x% y%.");

            int count = ParseCountRange(parts[1], rng);
            float heightMeters = ParseMeterRange(parts[2], rng);
            var xRange = ParsePercentRange(parts[3]);
            var yRange = ParsePercentRange(parts[4]);

            float maxDistFraction = positive ? 3f : 2f;

            for (int i = 0; i < count; i++)
            {
                float x1 = RandInRange(xRange.min, xRange.max, rng) / 100f;
                float y1 = RandInRange(yRange.min, yRange.max, rng) / 100f;

                if (!positive)
                {
                    int limit = 0;
                    while (limit < 50)
                    {
                        int startCell = HeightmapOpsV2.FindNearestCell(field.Mesh, x1 * field.Mesh.Width, y1 * field.Mesh.Height);
                        if (startCell >= 0 && field.IsLand(startCell)) break;
                        x1 = RandInRange(xRange.min, xRange.max, rng) / 100f;
                        y1 = RandInRange(yRange.min, yRange.max, rng) / 100f;
                        limit++;
                    }
                }

                float x2, y2;
                float mapW = field.Mesh.Width;
                int endLimit = 0;
                do
                {
                    x2 = (10f + (float)rng.NextDouble() * 80f) / 100f;
                    y2 = (15f + (float)rng.NextDouble() * 70f) / 100f;
                    float dist = Math.Abs(y2 - y1) * field.Mesh.Height + Math.Abs(x2 - x1) * mapW;
                    endLimit++;
                    if (dist >= mapW / 8f && dist <= mapW / maxDistFraction) break;
                } while (endLimit < 50);

                if (positive)
                    HeightmapOpsV2.Range(field, x1, y1, x2, y2, heightMeters, rng);
                else
                    HeightmapOpsV2.Trough(field, x1, y1, x2, y2, heightMeters, rng);
            }
        }

        static void ExecuteMask(ElevationFieldV2 field, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Mask operation requires: fraction.");

            float fraction = ParseFloat(parts[1]);
            HeightmapOpsV2.Mask(field, fraction);
        }

        static void ExecuteStrait(ElevationFieldV2 field, string[] parts, Random rng)
        {
            if (parts.Length < 3)
                throw new ArgumentException("Strait operation requires: width direction.");

            int width = ParseCountRange(parts[1], rng);
            string dirStr = parts[2].ToLowerInvariant();
            int direction = (dirStr == "vertical" || dirStr == "1") ? 1 : 0;
            HeightmapOpsV2.Strait(field, width, direction, rng);
        }

        static void ExecuteAdd(ElevationFieldV2 field, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Add operation requires: delta_m [range].");

            float deltaMeters = ParseMeters(parts[1]);
            var (minH, maxH) = parts.Length >= 3
                ? ParseHeightRange(parts[2], field)
                : (-field.MaxSeaDepthMeters, field.MaxElevationMeters);

            HeightmapOpsV2.Add(field, deltaMeters, minH, maxH);
        }

        static void ExecuteMultiply(ElevationFieldV2 field, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Multiply operation requires: factor [range].");

            float factor = ParseFloat(parts[1]);
            var (minH, maxH) = parts.Length >= 3
                ? ParseHeightRange(parts[2], field)
                : (-field.MaxSeaDepthMeters, field.MaxElevationMeters);

            HeightmapOpsV2.Multiply(field, factor, minH, maxH);
        }

        static void ExecuteSmooth(ElevationFieldV2 field, string[] parts)
        {
            int passes = 1;
            if (parts.Length >= 2)
                passes = (int)Math.Round(ParseFloat(parts[1]), MidpointRounding.AwayFromZero);

            HeightmapOpsV2.Smooth(field, passes);
        }

        static void ExecuteInvert(ElevationFieldV2 field, string[] parts, Random rng)
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
                HeightmapOpsV2.Invert(field, axis);
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

        static (float min, float max) ParseHeightRange(string token, ElevationFieldV2 field)
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
    }
}
