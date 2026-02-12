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
        private const float BaseSaturation = 0.33f;
        private const float BaseValue = 0.77f;

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
                float s = BaseSaturation + (ColorMath.HashToUnitFloat(realm.Id + 3000) - 0.5f) * 2f * RealmSatVariance;
                float v = BaseValue + (ColorMath.HashToUnitFloat(realm.Id + 4000) - 0.5f) * 2f * RealmValVariance;

                // Clamp to valid ranges
                s = Math.Max(MinSaturation, Math.Min(MaxSaturation, s));
                v = Math.Max(MinValue, Math.Min(MaxValue, v));

                realmColors[realm.Id] = ColorMath.HsvToColor32(h, s, v);
                realmIndex++;
            }
        }
    }
}
