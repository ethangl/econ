using System;

namespace EconSim.Core.Data
{
    /// <summary>
    /// World-scale normalization helpers shared by transport and market systems.
    /// </summary>
    public static class WorldScale
    {
        private const float CalibrationCellSizeKm = 2.5f;
        public const float LegacyDistanceNormalizationKm = 30f;
        public const float DistanceNormalizationPerCellSize = LegacyDistanceNormalizationKm / CalibrationCellSizeKm;

        public const float LegacyReferenceCellCount = 10000f;
        public const float LegacyReferenceAspectRatio = 16f / 9f;
        public static readonly float LegacyReferenceMapSpanCost = ComputeLegacyReferenceMapSpanCost();

        public static float ResolveCellSizeKm(MapInfo info)
        {
            if (info == null)
                throw new InvalidOperationException("ResolveCellSizeKm requires non-null MapInfo.");
            if (info.World == null)
                throw new InvalidOperationException("ResolveCellSizeKm requires MapInfo.World metadata.");
            if (float.IsNaN(info.World.CellSizeKm) || float.IsInfinity(info.World.CellSizeKm) || info.World.CellSizeKm <= 0f)
                throw new InvalidOperationException($"World.CellSizeKm must be > 0, got {info.World.CellSizeKm}.");
            return info.World.CellSizeKm;
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

            if (info == null)
                throw new InvalidOperationException("TryResolveMapDimensionsKm requires non-null MapInfo.");
            if (info.World == null)
                throw new InvalidOperationException("TryResolveMapDimensionsKm requires MapInfo.World metadata.");
            if (float.IsNaN(info.World.MapWidthKm) || float.IsInfinity(info.World.MapWidthKm) || info.World.MapWidthKm <= 0f ||
                float.IsNaN(info.World.MapHeightKm) || float.IsInfinity(info.World.MapHeightKm) || info.World.MapHeightKm <= 0f)
                throw new InvalidOperationException(
                    $"World map dimensions must be > 0, got ({info.World.MapWidthKm}, {info.World.MapHeightKm}).");

            mapWidthKm = info.World.MapWidthKm;
            mapHeightKm = info.World.MapHeightKm;
            return true;
        }

        public static float ResolveMapSpanCost(MapInfo info)
        {
            TryResolveMapDimensionsKm(info, out float mapWidthKm, out float mapHeightKm);
            return ComputeMapSpanCost(mapWidthKm, mapHeightKm, ResolveDistanceNormalizationKm(info));
        }

        public static float ComputeMapSpanCost(float mapWidthKm, float mapHeightKm, float distanceNormalizationKm)
        {
            if (mapWidthKm <= 0f || mapHeightKm <= 0f)
                throw new InvalidOperationException($"Map dimensions must be > 0, got ({mapWidthKm}, {mapHeightKm}).");
            if (float.IsNaN(distanceNormalizationKm) || float.IsInfinity(distanceNormalizationKm))
                throw new InvalidOperationException($"Distance normalization must be finite, got {distanceNormalizationKm}.");

            float diagonalKm = (float)Math.Sqrt((mapWidthKm * mapWidthKm) + (mapHeightKm * mapHeightKm));
            return diagonalKm / Math.Max(1f, distanceNormalizationKm);
        }

        private static float ComputeLegacyReferenceMapSpanCost()
        {
            float mapAreaKm2 = LegacyReferenceCellCount * CalibrationCellSizeKm * CalibrationCellSizeKm;
            float mapWidthKm = (float)Math.Sqrt(mapAreaKm2 * LegacyReferenceAspectRatio);
            float mapHeightKm = mapWidthKm / LegacyReferenceAspectRatio;
            return ComputeMapSpanCost(mapWidthKm, mapHeightKm, LegacyDistanceNormalizationKm);
        }
    }
}
