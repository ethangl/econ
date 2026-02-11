using System;

namespace EconSim.Core.Data
{
    /// <summary>
    /// World-scale normalization helpers shared by transport and market systems.
    /// </summary>
    public static class WorldScale
    {
        public const float LegacyCellSizeKm = 2.5f;
        public const float LegacyDistanceNormalizationKm = 30f;
        public const float DistanceNormalizationPerCellSize = LegacyDistanceNormalizationKm / LegacyCellSizeKm;

        public const float LegacyReferenceCellCount = 10000f;
        public const float LegacyReferenceAspectRatio = 16f / 9f;
        public static readonly float LegacyReferenceMapSpanCost = ComputeLegacyReferenceMapSpanCost();

        public static float ResolveCellSizeKm(MapInfo info)
        {
            if (info?.World != null && info.World.CellSizeKm > 0f)
                return info.World.CellSizeKm;

            return LegacyCellSizeKm;
        }

        public static float ResolveDistanceNormalizationKm(MapInfo info)
        {
            float scale = ResolveCellSizeKm(info) * DistanceNormalizationPerCellSize;
            return Math.Max(1f, scale);
        }

        public static bool TryResolveMapDimensionsKm(MapInfo info, out float mapWidthKm, out float mapHeightKm)
        {
            mapWidthKm = 0f;
            mapHeightKm = 0f;

            if (info?.World == null)
                return false;
            if (info.World.MapWidthKm <= 0f || info.World.MapHeightKm <= 0f)
                return false;

            mapWidthKm = info.World.MapWidthKm;
            mapHeightKm = info.World.MapHeightKm;
            return true;
        }

        public static float ResolveMapSpanCost(MapInfo info)
        {
            if (!TryResolveMapDimensionsKm(info, out float mapWidthKm, out float mapHeightKm))
                return 0f;

            return ComputeMapSpanCost(mapWidthKm, mapHeightKm, ResolveDistanceNormalizationKm(info));
        }

        public static float ComputeMapSpanCost(float mapWidthKm, float mapHeightKm, float distanceNormalizationKm)
        {
            if (mapWidthKm <= 0f || mapHeightKm <= 0f)
                return 0f;

            float diagonalKm = (float)Math.Sqrt((mapWidthKm * mapWidthKm) + (mapHeightKm * mapHeightKm));
            return diagonalKm / Math.Max(1f, distanceNormalizationKm);
        }

        private static float ComputeLegacyReferenceMapSpanCost()
        {
            float mapAreaKm2 = LegacyReferenceCellCount * LegacyCellSizeKm * LegacyCellSizeKm;
            float mapWidthKm = (float)Math.Sqrt(mapAreaKm2 * LegacyReferenceAspectRatio);
            float mapHeightKm = mapWidthKm / LegacyReferenceAspectRatio;
            return ComputeMapSpanCost(mapWidthKm, mapHeightKm, LegacyDistanceNormalizationKm);
        }
    }
}
