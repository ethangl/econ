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

        // Scales RiverThreshold used for river mouth detection / extraction.
        public float RiverThresholdScale = 1f;

        // Scales RiverTraceThreshold used for upstream tracing and biome river influence.
        public float RiverTraceThresholdScale = 1f;

        // Scales MinRiverVertices and rounds to nearest integer (min 1).
        public float RiverMinVerticesScale = 1f;

        // Scales target realm seed count.
        public float RealmTargetScale = 1f;

        // Scales target province seed count per realm.
        public float ProvinceTargetScale = 1f;

        // Scales target county seed count per province.
        public float CountyTargetScale = 1f;

        // Scales coastline salt signal used for pseudo-soil classification.
        public float BiomeCoastSaltScale = 1f;

        // Scales saline threshold used for pseudo-soil classification.
        public float BiomeSalineThresholdScale = 1f;

        // Scales slope used for biome soil-classification thresholds.
        public float BiomeSlopeScale = 1f;

        // Scales alluvial flux threshold used for pseudo-soil classification.
        public float BiomeAlluvialFluxThresholdScale = 1f;

        // Scales alluvial max-slope threshold used for pseudo-soil classification.
        public float BiomeAlluvialMaxSlopeScale = 1f;

        // Scales alluvial->wetland flux threshold used for biome split.
        public float BiomeWetlandFluxThresholdScale = 1f;

        // Scales alluvial->wetland max-slope threshold used for biome split.
        public float BiomeWetlandMaxSlopeScale = 1f;

        // Scales the temperature upper bound used to classify podzol soils.
        public float BiomePodzolTempMaxScale = 1f;

        // Scales the precipitation threshold used to classify podzol soils.
        public float BiomePodzolPrecipThresholdScale = 1f;

        // Scales the precipitation threshold used for woodland vs grassland split.
        public float BiomeWoodlandPrecipThresholdScale = 1f;

        public bool IsIdentity() =>
            TerrainMagnitudeScale == 1f
            && AddMagnitudeScale == 1f
            && MaskScale == 1f
            && LandMultiplyFactorScale == 1f
            && RiverThresholdScale == 1f
            && RiverTraceThresholdScale == 1f
            && RiverMinVerticesScale == 1f
            && RealmTargetScale == 1f
            && ProvinceTargetScale == 1f
            && CountyTargetScale == 1f
            && BiomeCoastSaltScale == 1f
            && BiomeSalineThresholdScale == 1f
            && BiomeSlopeScale == 1f
            && BiomeAlluvialFluxThresholdScale == 1f
            && BiomeAlluvialMaxSlopeScale == 1f
            && BiomeWetlandFluxThresholdScale == 1f
            && BiomeWetlandMaxSlopeScale == 1f
            && BiomePodzolTempMaxScale == 1f
            && BiomePodzolPrecipThresholdScale == 1f
            && BiomeWoodlandPrecipThresholdScale == 1f;

        public HeightmapTemplateTuningProfile Clone() =>
            new HeightmapTemplateTuningProfile
            {
                TerrainMagnitudeScale = TerrainMagnitudeScale,
                AddMagnitudeScale = AddMagnitudeScale,
                MaskScale = MaskScale,
                LandMultiplyFactorScale = LandMultiplyFactorScale,
                RiverThresholdScale = RiverThresholdScale,
                RiverTraceThresholdScale = RiverTraceThresholdScale,
                RiverMinVerticesScale = RiverMinVerticesScale,
                RealmTargetScale = RealmTargetScale,
                ProvinceTargetScale = ProvinceTargetScale,
                CountyTargetScale = CountyTargetScale,
                BiomeCoastSaltScale = BiomeCoastSaltScale,
                BiomeSalineThresholdScale = BiomeSalineThresholdScale,
                BiomeSlopeScale = BiomeSlopeScale,
                BiomeAlluvialFluxThresholdScale = BiomeAlluvialFluxThresholdScale,
                BiomeAlluvialMaxSlopeScale = BiomeAlluvialMaxSlopeScale,
                BiomeWetlandFluxThresholdScale = BiomeWetlandFluxThresholdScale,
                BiomeWetlandMaxSlopeScale = BiomeWetlandMaxSlopeScale,
                BiomePodzolTempMaxScale = BiomePodzolTempMaxScale,
                BiomePodzolPrecipThresholdScale = BiomePodzolPrecipThresholdScale,
                BiomeWoodlandPrecipThresholdScale = BiomeWoodlandPrecipThresholdScale
            };
    }
}
