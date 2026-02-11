namespace MapGen.Core
{
    public sealed class HeightmapTemplateTuningProfile
    {
        // Scales Hill/Pit/Range/Trough magnitudes in meters.
        public float TerrainMagnitudeScale = 1f;

        // Scales Add operation deltas in meters.
        public float AddMagnitudeScale = 1f;

        // Scales mask fraction.
        public float MaskScale = 1f;

        // Scales multiply-land factor around 1.0: new = 1 + (old - 1) * scale.
        public float LandMultiplyFactorScale = 1f;

        public bool IsIdentity() =>
            TerrainMagnitudeScale == 1f
            && AddMagnitudeScale == 1f
            && MaskScale == 1f
            && LandMultiplyFactorScale == 1f;

        public HeightmapTemplateTuningProfile Clone() =>
            new HeightmapTemplateTuningProfile
            {
                TerrainMagnitudeScale = TerrainMagnitudeScale,
                AddMagnitudeScale = AddMagnitudeScale,
                MaskScale = MaskScale,
                LandMultiplyFactorScale = LandMultiplyFactorScale
            };
    }
}
