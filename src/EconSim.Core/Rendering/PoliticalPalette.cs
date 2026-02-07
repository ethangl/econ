using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Rendering
{
    /// <summary>
    /// Generates political map colors for realms.
    /// Province and county colors are derived in the shader from realm colors.
    /// </summary>
    public class PoliticalPalette
    {
        // Base HSV values for realms (center of range)
        private const float BaseSaturation = 0.42f;
        private const float BaseValue = 0.70f;

        // Realm variance ranges (±)
        private const float RealmSatVariance = 0.08f;
        private const float RealmValVariance = 0.08f;

        // Clamping bounds
        private const float MinSaturation = 0.28f;
        private const float MaxSaturation = 0.55f;
        private const float MinValue = 0.58f;
        private const float MaxValue = 0.85f;

        // Unowned color (neutral grey)
        private static readonly Color32 UnownedColor = new Color32(128, 128, 128, 255);

        // Generated realm colors (indexed by realm ID)
        private readonly Dictionary<int, Color32> realmColors = new Dictionary<int, Color32>();

        public PoliticalPalette(MapData mapData)
        {
            GenerateRealmColors(mapData);
        }

        /// <summary>
        /// Get color for a realm.
        /// </summary>
        public Color32 GetRealmColor(int realmId)
        {
            if (realmId <= 0 || !realmColors.TryGetValue(realmId, out var color))
                return UnownedColor;
            return color;
        }

        /// <summary>
        /// Generate realm colors using even hue distribution with S/V variance.
        /// </summary>
        private void GenerateRealmColors(MapData mapData)
        {
            // Count valid realms for even distribution
            int validRealmCount = 0;
            foreach (var realm in mapData.Realms)
            {
                if (realm.Id > 0) validRealmCount++;
            }

            int realmIndex = 0;
            foreach (var realm in mapData.Realms)
            {
                if (realm.Id <= 0) continue; // Skip neutral

                // Even hue distribution across the spectrum
                float h = (float)realmIndex / validRealmCount;

                // Hash-based variance for S and V
                float s = BaseSaturation + (HashToFloat(realm.Id + 3000) - 0.5f) * 2f * RealmSatVariance;
                float v = BaseValue + (HashToFloat(realm.Id + 4000) - 0.5f) * 2f * RealmValVariance;

                // Clamp to valid ranges
                s = Math.Max(MinSaturation, Math.Min(MaxSaturation, s));
                v = Math.Max(MinValue, Math.Min(MaxValue, v));

                realmColors[realm.Id] = HsvToColor32(h, s, v);
                realmIndex++;
            }
        }

        /// <summary>
        /// Simple hash function returning 0-1 for deterministic "randomness".
        /// </summary>
        private static float HashToFloat(int value)
        {
            uint h = (uint)value;
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        /// <summary>
        /// Convert HSV (0-1 range) to Color32.
        /// </summary>
        private static Color32 HsvToColor32(float h, float s, float v)
        {
            float r, g, b;

            if (s <= 0)
            {
                r = g = b = v;
            }
            else
            {
                float hh = h * 6f;
                int i = (int)hh;
                float ff = hh - i;
                float p = v * (1f - s);
                float q = v * (1f - (s * ff));
                float t = v * (1f - (s * (1f - ff)));

                switch (i)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
            }

            return new Color32(
                (byte)(r * 255),
                (byte)(g * 255),
                (byte)(b * 255),
                255
            );
        }
    }
}
