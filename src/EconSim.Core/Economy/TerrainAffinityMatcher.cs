using System;
using System.Collections.Generic;
using System.Text;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Shared matcher for terrain-affinity strings against biome names.
    /// Handles case differences and common naming aliases between economy data and MapGen biomes.
    /// </summary>
    internal static class TerrainAffinityMatcher
    {
        private static readonly Dictionary<string, string> AliasToCanonical =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "taiga", "boreal forest" },
                { "tropical seasonal forest", "tropical dry forest" },
                { "temperate deciduous forest", "temperate forest" },
                { "temperate rainforest", "temperate forest" },
                { "highland", "mountain" },
                { "alpine barren", "mountain" },
                { "mountain shrub", "mountain" },
                { "steppe", "grassland" }
            };

        public static bool MatchesBiome(string terrainAffinity, string biomeName)
        {
            if (string.IsNullOrWhiteSpace(terrainAffinity) || string.IsNullOrWhiteSpace(biomeName))
                return false;

            string canonicalTerrain = Canonicalize(terrainAffinity);
            string canonicalBiome = Canonicalize(biomeName);

            if (canonicalTerrain.Length == 0 || canonicalBiome.Length == 0)
                return false;

            if (canonicalTerrain == canonicalBiome)
                return true;

            if (canonicalBiome.Contains(canonicalTerrain, StringComparison.Ordinal) ||
                canonicalTerrain.Contains(canonicalBiome, StringComparison.Ordinal))
            {
                return true;
            }

            return HasTokenSubsetMatch(canonicalTerrain, canonicalBiome);
        }

        private static bool HasTokenSubsetMatch(string left, string right)
        {
            var leftTokens = Tokenize(left);
            var rightTokens = Tokenize(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0)
                return false;

            HashSet<string> smaller = leftTokens.Count <= rightTokens.Count ? leftTokens : rightTokens;
            HashSet<string> larger = leftTokens.Count <= rightTokens.Count ? rightTokens : leftTokens;
            foreach (var token in smaller)
            {
                if (!larger.Contains(token))
                    return false;
            }

            return true;
        }

        private static HashSet<string> Tokenize(string value)
        {
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            string[] parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length < 3)
                    continue;
                tokens.Add(part);
            }
            return tokens;
        }

        private static string Canonicalize(string value)
        {
            string normalized = NormalizeText(value);
            if (AliasToCanonical.TryGetValue(normalized, out string alias))
                return alias;
            return normalized;
        }

        private static string NormalizeText(string value)
        {
            var sb = new StringBuilder(value.Length);
            bool lastWasSpace = true;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (char.IsLetterOrDigit(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                    lastWasSpace = false;
                    continue;
                }

                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }

            return sb.ToString().Trim();
        }
    }
}
