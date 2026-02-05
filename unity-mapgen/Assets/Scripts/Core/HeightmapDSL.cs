using System;
using System.Collections.Generic;
using System.Globalization;

namespace MapGen.Core
{
    /// <summary>
    /// DSL parser for heightmap generation scripts.
    /// Format matches Azgaar's heightmap templates.
    ///
    /// Operations:
    ///   Hill count height x% y%       - Add hills (positive blobs)
    ///   Pit count height x% y%        - Add pits (negative blobs)
    ///   Range count height x% y%      - Add mountain ranges
    ///   Trough count height x% y%     - Add valleys
    ///   Strait width direction [pos]  - Water passage (0=horiz, 1=vert)
    ///   Mask fraction                 - Edge falloff for islands
    ///   Add value [minH-maxH]         - Add constant to heights
    ///   Multiply factor [minH-maxH]   - Scale heights
    ///   Smooth passes                 - Average with neighbors
    ///   Invert probability axis       - Mirror heightmap
    ///
    /// Ranges use "min-max" syntax: "20-30" means random 20-30.
    /// Percentages (x%, y%) are 0-100 map coordinates.
    /// </summary>
    public static class HeightmapDSL
    {
        /// <summary>
        /// Execute a DSL script on a height grid.
        /// </summary>
        /// <param name="grid">Height grid to modify</param>
        /// <param name="script">DSL script (one operation per line)</param>
        /// <param name="seed">Random seed</param>
        public static void Execute(HeightGrid grid, string script, int seed)
        {
            var rng = new Random(seed);
            var lines = script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue; // Skip empty lines and comments

                ExecuteLine(grid, trimmed, rng);

                // Truncate heights to integers after each step.
                // Azgaar uses Uint8Array for heights — all values are integers.
                // Without this, fractional heights accumulate and inflate land ratio
                // (e.g. 20.7 is land in float but water (20) in Azgaar).
                for (int i = 0; i < grid.Heights.Length; i++)
                    grid.Heights[i] = (int)grid.Heights[i];
            }

            grid.ClampAll();
        }

        /// <summary>
        /// Execute a single DSL line.
        /// </summary>
        private static void ExecuteLine(HeightGrid grid, string line, Random rng)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            string op = parts[0].ToLowerInvariant();

            switch (op)
            {
                case "hill":
                    ExecuteBlob(grid, parts, positive: true, rng);
                    break;
                case "pit":
                    ExecuteBlob(grid, parts, positive: false, rng);
                    break;
                case "range":
                    ExecuteLinear(grid, parts, positive: true, rng);
                    break;
                case "trough":
                    ExecuteLinear(grid, parts, positive: false, rng);
                    break;
                case "mask":
                    ExecuteMask(grid, parts);
                    break;
                case "strait":
                    ExecuteStrait(grid, parts, rng);
                    break;
                case "add":
                    ExecuteAdd(grid, parts);
                    break;
                case "multiply":
                    ExecuteMultiply(grid, parts);
                    break;
                case "smooth":
                    ExecuteSmooth(grid, parts);
                    break;
                case "invert":
                    ExecuteInvert(grid, parts, rng);
                    break;
                default:
                    throw new ArgumentException($"Unknown heightmap operation: {op}");
            }
        }

        /// <summary>
        /// Execute Hill or Pit operation.
        /// Format: Hill count height x% y%
        /// Example: Hill 1 90-99 60-80 45-55
        /// </summary>
        private static void ExecuteBlob(HeightGrid grid, string[] parts, bool positive, Random rng)
        {
            if (parts.Length < 5)
                throw new ArgumentException($"Blob requires 4 parameters: count height x% y%");

            int count = ParseRange(parts[1], rng);
            float height = ParseRange(parts[2], rng);
            var xRange = ParseRangeFloat(parts[3]);
            var yRange = ParseRangeFloat(parts[4]);

            for (int i = 0; i < count; i++)
            {
                float x = RandInRange(xRange.min, xRange.max, rng) / 100f;
                float y = RandInRange(yRange.min, yRange.max, rng) / 100f;

                if (positive)
                    HeightmapOps.Hill(grid, x, y, height, rng);
                else
                    HeightmapOps.Pit(grid, x, y, height, rng);
            }
        }

        /// <summary>
        /// Execute Range or Trough operation.
        /// Format: Range count height x% y%
        /// Example: Range 1 40-50 45-55 45-55
        ///
        /// x% and y% define a box; the line runs from one random point to another.
        /// </summary>
        private static void ExecuteLinear(HeightGrid grid, string[] parts, bool positive, Random rng)
        {
            if (parts.Length < 5)
                throw new ArgumentException($"Linear requires 4 parameters: count height x% y%");

            int count = ParseRange(parts[1], rng);
            float height = ParseRange(parts[2], rng);
            var xRange = ParseRangeFloat(parts[3]);
            var yRange = ParseRangeFloat(parts[4]);

            // Azgaar uses different max distance: Range=width/3, Trough=width/2
            float maxDistFraction = positive ? 3f : 2f;

            for (int i = 0; i < count; i++)
            {
                float x1 = RandInRange(xRange.min, xRange.max, rng) / 100f;
                float y1 = RandInRange(yRange.min, yRange.max, rng) / 100f;

                // Trough: retry start point until on land (Azgaar behavior)
                if (!positive)
                {
                    int limit = 0;
                    while (limit < 50)
                    {
                        int startCell = HeightmapOps.FindNearestCell(grid.Mesh,
                            x1 * grid.Mesh.Width, y1 * grid.Mesh.Height);
                        if (startCell >= 0 && grid.Heights[startCell] >= HeightGrid.SeaLevel) break;
                        x1 = RandInRange(xRange.min, xRange.max, rng) / 100f;
                        y1 = RandInRange(yRange.min, yRange.max, rng) / 100f;
                        limit++;
                    }
                }

                // Azgaar retries end point until distance is in [width/8, width/maxDistFraction]
                float x2, y2;
                float mapW = grid.Mesh.Width;
                int endLimit = 0;
                do
                {
                    x2 = (10f + (float)rng.NextDouble() * 80f) / 100f;
                    y2 = (15f + (float)rng.NextDouble() * 70f) / 100f;
                    float dist = Math.Abs(y2 - y1) * grid.Mesh.Height + Math.Abs(x2 - x1) * mapW;
                    endLimit++;
                    if (dist >= mapW / 8f && dist <= mapW / maxDistFraction) break;
                } while (endLimit < 50);

                if (positive)
                    HeightmapOps.Range(grid, x1, y1, x2, y2, height, rng);
                else
                    HeightmapOps.Trough(grid, x1, y1, x2, y2, height, rng);
            }
        }

        /// <summary>
        /// Execute Mask operation.
        /// Format: Mask fraction
        /// Example: Mask 4
        /// </summary>
        private static void ExecuteMask(HeightGrid grid, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Mask requires 1 parameter: fraction");

            float fraction = ParseFloat(parts[1]);
            HeightmapOps.Mask(grid, fraction);
        }

        /// <summary>
        /// Execute Strait operation.
        /// Format: Strait width direction [0 0]
        /// Width = number of BFS expansion rings from path.
        /// Direction = "vertical" or "horizontal".
        /// Matches Azgaar: BFS path edge-to-edge, h **= 0.8 per ring.
        /// </summary>
        private static void ExecuteStrait(HeightGrid grid, string[] parts, Random rng)
        {
            if (parts.Length < 3)
                throw new ArgumentException("Strait requires 2 parameters: width direction");

            int width = ParseRange(parts[1], rng);
            string dirStr = parts[2].ToLowerInvariant();
            int direction = dirStr == "vertical" || dirStr == "1" ? 1 : 0;

            HeightmapOps.Strait(grid, width, direction, rng);
        }

        /// <summary>
        /// Execute Add operation.
        /// Format: Add value range [0 0]
        /// Range can be "land" (20-100), "all" (0-100), or "min-max".
        /// Matches Azgaar's addStep: modify(range, +value, 1)
        /// </summary>
        private static void ExecuteAdd(HeightGrid grid, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Add requires 1-2 parameters: value [range]");

            float value = ParseFloat(parts[1]);
            var (minH, maxH) = parts.Length >= 3
                ? ParseHeightRange(parts[2])
                : (HeightGrid.MinHeight, HeightGrid.MaxHeight);

            HeightmapOps.Add(grid, value, minH, maxH);
        }

        /// <summary>
        /// Execute Multiply operation.
        /// Format: Multiply factor range [0 0]
        /// Range can be "land" (20-100), "all" (0-100), or "min-max".
        /// Matches Azgaar's addStep: modify(range, 0, +factor)
        /// </summary>
        private static void ExecuteMultiply(HeightGrid grid, string[] parts)
        {
            if (parts.Length < 2)
                throw new ArgumentException("Multiply requires 1-2 parameters: factor [range]");

            float factor = ParseFloat(parts[1]);
            var (minH, maxH) = parts.Length >= 3
                ? ParseHeightRange(parts[2])
                : (HeightGrid.MinHeight, HeightGrid.MaxHeight);

            HeightmapOps.Multiply(grid, factor, minH, maxH);
        }

        /// <summary>
        /// Execute Smooth operation.
        /// Format: Smooth passes
        /// Example: Smooth 2
        /// </summary>
        private static void ExecuteSmooth(HeightGrid grid, string[] parts)
        {
            int passes = 1;
            if (parts.Length >= 2)
                passes = int.Parse(parts[1]);

            HeightmapOps.Smooth(grid, passes);
        }

        /// <summary>
        /// Execute Invert operation.
        /// Format: Invert probability axes
        /// Axes: "x" (horizontal), "y" (vertical), "both" (both), or 0/1/2.
        /// Matches Azgaar: invertX = axes !== "y", invertY = axes !== "x".
        /// </summary>
        private static void ExecuteInvert(HeightGrid grid, string[] parts, Random rng)
        {
            float probability = 0.5f;
            int axis = 2; // default: both

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
                        if (int.TryParse(axisStr, out int axisNum))
                            axis = axisNum;
                        break;
                }
            }

            if (rng.NextDouble() < probability)
            {
                HeightmapOps.Invert(grid, axis);
            }
        }

        /// <summary>
        /// Parse a number or range, matching Azgaar's getNumberInRange.
        /// Fractional values use probabilistic rounding: "1.5" → 1 (50%) or 2 (50%).
        /// Ranges: "3-7" → random integer in [3,7].
        /// </summary>
        private static int ParseRange(string s, Random rng)
        {
            // Try simple float parse first (handles "1.5", "0.5", "7", etc.)
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float fval))
            {
                int ipart = (int)fval;
                float frac = fval - ipart;
                // Probabilistic rounding of fractional part (Azgaar's Pint behavior)
                return ipart + (rng.NextDouble() < frac ? 1 : 0);
            }

            // Handle ranges like "3-7" or "20-30"
            int dashIdx = s.IndexOf('-', s[0] == '-' ? 1 : 0);
            if (dashIdx > 0)
            {
                float minF = float.Parse(s.Substring(0, dashIdx), CultureInfo.InvariantCulture);
                float maxF = float.Parse(s.Substring(dashIdx + 1), CultureInfo.InvariantCulture);
                int min = (int)minF;
                int max = (int)maxF;
                return rng.Next(min, max + 1);
            }

            return int.Parse(s);
        }

        /// <summary>
        /// Parse a height range: "land" (20-100), "all" (0-100), or "min-max".
        /// Matches Azgaar's modify() range parameter.
        /// </summary>
        private static (float min, float max) ParseHeightRange(string s)
        {
            switch (s.ToLowerInvariant())
            {
                case "land": return (HeightGrid.SeaLevel, HeightGrid.MaxHeight);
                case "all": return (HeightGrid.MinHeight, HeightGrid.MaxHeight);
                default: return ParseRangeFloat(s);
            }
        }

        /// <summary>
        /// Parse a float range (e.g., "45-55") and return both values.
        /// </summary>
        private static (float min, float max) ParseRangeFloat(string s)
        {
            if (s.Contains("-"))
            {
                var parts = s.Split('-');
                float min = ParseFloat(parts[0]);
                float max = ParseFloat(parts[1]);
                return (min, max);
            }
            float val = ParseFloat(s);
            return (val, val);
        }

        /// <summary>
        /// Parse a float value.
        /// </summary>
        private static float ParseFloat(string s)
        {
            return float.Parse(s, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Get random float in range.
        /// </summary>
        private static float RandInRange(float min, float max, Random rng)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}
