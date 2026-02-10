using System;

namespace MapGen.Core
{
    /// <summary>
    /// Elevation domain describing the valid range and sea-level split.
    /// </summary>
    public readonly struct ElevationDomain
    {
        public float Min { get; }
        public float Max { get; }
        public float SeaLevel { get; }
        public float LandRange => Max - SeaLevel;
        public float HeightRange => Max - Min;

        public ElevationDomain(float min, float max, float seaLevel)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException(nameof(max), "Max must be greater than min.");
            if (seaLevel < min || seaLevel >= max)
                throw new ArgumentOutOfRangeException(nameof(seaLevel), "Sea level must be within [min, max).");

            Min = min;
            Max = max;
            SeaLevel = seaLevel;
        }

        public float Clamp(float height) =>
            height < Min ? Min : (height > Max ? Max : height);

        public float NormalizeHeight(float height)
        {
            float range = HeightRange;
            if (range <= 1e-6f) return 0f;
            return (Clamp(height) - Min) / range;
        }

        public float NormalizeLandHeight(float height)
        {
            float range = LandRange;
            if (range <= 1e-6f) return 0f;
            return (Clamp(height) - SeaLevel) / range;
        }

        public float FromNormalizedLand(float t) =>
            Clamp(SeaLevel + Clamp01(t) * LandRange);

        public float RescaleFrom(float sourceHeight, ElevationDomain sourceDomain)
        {
            float sourceRange = sourceDomain.HeightRange;
            if (sourceRange <= 1e-6f) return Min;
            float normalized = (sourceDomain.Clamp(sourceHeight) - sourceDomain.Min) / sourceRange;
            return Clamp(Min + normalized * HeightRange);
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>
    /// Canonical elevation domains and helper transforms used during migration.
    /// </summary>
    public static class ElevationDomains
    {
        public static readonly ElevationDomain Dsl = new ElevationDomain(0f, 100f, 20f);
        public static readonly ElevationDomain Simulation = new ElevationDomain(0f, 255f, 51f);

        public static float NormalizeHeight(float height, ElevationDomain domain) =>
            domain.NormalizeHeight(height);

        public static float NormalizeLandHeight(float height, ElevationDomain domain) =>
            domain.NormalizeLandHeight(height);

        public static float FromNormalizedLand(float t, ElevationDomain domain) =>
            domain.FromNormalizedLand(t);

        public static float Rescale(float height, ElevationDomain sourceDomain, ElevationDomain targetDomain) =>
            targetDomain.RescaleFrom(height, sourceDomain);

        public static ElevationDomain InferFromSeaLevel(float seaLevel)
        {
            float dslDist = Math.Abs(seaLevel - Dsl.SeaLevel);
            float simDist = Math.Abs(seaLevel - Simulation.SeaLevel);
            return dslDist <= simDist ? Dsl : Simulation;
        }
    }
}
