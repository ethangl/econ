using UnityEngine;
using MapGen.Core;

namespace EconSim.Editor
{
    /// <summary>
    /// Color palettes for MapGen debug visualization.
    /// Used by CellInspectorOverlay and available for future debug texture generation.
    /// </summary>
    public static class MapGenDebugColors
    {
        public static readonly Color[] Soil = new Color[]
        {
            new Color(0.60f, 0.65f, 0.72f), // Permafrost - blue-gray
            new Color(0.90f, 0.88f, 0.85f), // Saline - white/pale
            new Color(0.55f, 0.55f, 0.53f), // Lithosol - rocky gray
            new Color(0.30f, 0.22f, 0.14f), // Alluvial - dark brown
            new Color(0.82f, 0.75f, 0.50f), // Aridisol - sandy yellow
            new Color(0.78f, 0.38f, 0.18f), // Laterite - red-orange
            new Color(0.50f, 0.44f, 0.38f), // Podzol - gray-brown
            new Color(0.15f, 0.12f, 0.10f), // Chernozem - near-black
        };

        public static readonly Color[] Vegetation = new Color[]
        {
            new Color(0.70f, 0.65f, 0.55f), // None - bare ground
            new Color(0.55f, 0.60f, 0.50f), // LichenMoss - muted gray-green
            new Color(0.70f, 0.75f, 0.30f), // Grass - gold-green
            new Color(0.50f, 0.55f, 0.30f), // Shrub - dusty olive
            new Color(0.30f, 0.60f, 0.20f), // DeciduousForest - green
            new Color(0.15f, 0.35f, 0.30f), // ConiferousForest - dark blue-green
            new Color(0.10f, 0.40f, 0.15f), // BroadleafForest - deep green
        };

        public static readonly Color[] Biome = new Color[]
        {
            new Color(0.85f, 0.92f, 0.98f), // Glacier
            new Color(0.70f, 0.75f, 0.65f), // Tundra
            new Color(0.95f, 0.93f, 0.85f), // SaltFlat
            new Color(0.45f, 0.60f, 0.45f), // CoastalMarsh
            new Color(0.60f, 0.58f, 0.55f), // AlpineBarren
            new Color(0.55f, 0.58f, 0.40f), // MountainShrub
            new Color(0.40f, 0.65f, 0.25f), // Floodplain
            new Color(0.30f, 0.50f, 0.40f), // Wetland
            new Color(0.92f, 0.82f, 0.45f), // HotDesert
            new Color(0.75f, 0.72f, 0.60f), // ColdDesert
            new Color(0.70f, 0.65f, 0.40f), // Scrubland
            new Color(0.10f, 0.45f, 0.15f), // TropicalRainforest
            new Color(0.25f, 0.50f, 0.20f), // TropicalDryForest
            new Color(0.75f, 0.70f, 0.30f), // Savanna
            new Color(0.20f, 0.35f, 0.30f), // BorealForest
            new Color(0.25f, 0.55f, 0.20f), // TemperateForest
            new Color(0.65f, 0.72f, 0.35f), // Grassland
            new Color(0.35f, 0.55f, 0.25f), // Woodland
            new Color(0.25f, 0.45f, 0.65f), // Lake
        };

        public static readonly Color[] Rock = new Color[]
        {
            new Color(0.55f, 0.55f, 0.55f), // Granite
            new Color(0.76f, 0.70f, 0.50f), // Sedimentary
            new Color(0.85f, 0.85f, 0.75f), // Limestone
            new Color(0.40f, 0.25f, 0.25f), // Volcanic
        };

        public static readonly Color Water = new Color(0.2f, 0.2f, 0.25f);

        /// <summary>Red(low) -> white(mid) -> blue(high) heatmap.</summary>
        public static Color Heatmap(float value, float min, float max)
        {
            float range = max - min;
            if (range < 1e-6f) range = 1f;
            float t = Mathf.Clamp01((value - min) / range);

            if (t < 0.5f)
                return Color.Lerp(new Color(0.9f, 0.1f, 0.1f), Color.white, t * 2f);
            else
                return Color.Lerp(Color.white, new Color(0.1f, 0.2f, 0.9f), (t - 0.5f) * 2f);
        }

        /// <summary>White(low) -> red(high) intensity gradient.</summary>
        public static Color Intensity(float value, float min, float max)
        {
            float range = max - min;
            if (range < 1e-6f) range = 1f;
            float t = Mathf.Clamp01((value - min) / range);
            return Color.Lerp(Color.white, new Color(0.8f, 0.05f, 0.05f), t);
        }
    }
}
