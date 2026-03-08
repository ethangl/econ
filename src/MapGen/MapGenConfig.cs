using System;

namespace MapGen.Core
{
    /// <summary>
    /// Optional tectonic context from globe site selection.
    /// When non-null, biases DSL feature placement and magnitude.
    /// </summary>
    public class TectonicHints
    {
        /// <summary>Convergence magnitude of nearest boundary (0-1 typical, can exceed 1).</summary>
        public float ConvergenceMagnitude;

        /// <summary>Direction toward nearest coast as normalized bias in map space.
        /// x: 0=left edge, 1=right edge. y: 0=bottom, 1=top.
        /// (0.5, 0.5) = center = no directional bias.</summary>
        public float CoastDirectionX;
        public float CoastDirectionY;

        /// <summary>BFS hops to plate boundary (0=on boundary, higher=more distant).</summary>
        public int BoundaryDistanceHops;

        /// <summary>Ocean current temperature anomaly in °C. Range ~[-8, +8].</summary>
        public float OceanCurrentAnomalyC;

        /// <summary>Moisture bias. -1 dry (downwind of continent) to +1 wet (oceanic fetch).</summary>
        public float MoistureBias;

        /// <summary>Prevailing wind direction in flat-map space (X=east, Y=north). Unit vector.</summary>
        public float WindDirectionX;
        public float WindDirectionY;
    }

    /// <summary>
    /// Configuration for world-unit map generation.
    /// Elevation values are signed meters relative to sea level.
    /// </summary>
    public class MapGenConfig
    {
        public int Seed = 12345;
        public int CellCount = 10000;
        public float AspectRatio = 16f / 9f;
        public float CellSizeKm = 2.5f;
        public HeightmapTemplateType Template = HeightmapTemplateType.LowIsland;
        /// <summary>
        /// Latitude of the map center in degrees. South/north edges are derived
        /// from this value and the map's height in km.
        /// </summary>
        public float Latitude = 50f;
        /// <summary>
        /// Longitude of the map center in degrees (-180 to 180).
        /// Used for culture selection bias. Defaults to 0 (no bias).
        /// </summary>
        public float Longitude = 0f;

        // Elevation envelope in signed meters (sea level = 0).
        public float MaxElevationMeters = 8000f;
        public float MaxSeaDepthMeters = 8000f;
        // Terrain-shaping domain ceilings. The generator clamps into this domain first,
        // then values are remapped to the world envelope when the envelope is larger.
        public float TerrainShapeDomainMaxElevationMeters = 5000f;
        public float TerrainShapeDomainMaxSeaDepthMeters = 5000f;
        // Reference span used by terrain-shaping DSL ops (Hill/Pit/Range/Trough/Strait).
        // Keep fixed to preserve morphology when only the elevation envelope changes.
        public float TerrainShapeReferenceSpanMeters = 6250f;
        // Initial water fill depth for terrain shaping before DSL ops (sea level = 0).
        // This is clamped to MaxSeaDepthMeters at runtime.
        public float TerrainShapeInitialSeaDepthMeters = 1250f;
        // Fallback post-generation remap curve for underwater depths when expanding world envelope.
        // Template DSL directives (DepthRemap/DepthCurve) override this value.
        // 1.0 = linear, <1 pushes more cells toward abyss, >1 keeps more continental shelf.
        public float TerrainDepthRemapExponent = 1f;

        // Climate defaults.
        public float EquatorTempC = 29f;
        public float NorthPoleTempC = -15f;
        public float SouthPoleTempC = -25f;
        public float LapseRateCPerKm = 6.5f;
        public float MaxAnnualPrecipitationMm = 3000f;
        public WindBand[] WindBands = new WindBand[]
        {
            new WindBand(-90, -60, 315),
            new WindBand(-60, -30, 135),
            new WindBand(-30,   0, 315),
            new WindBand(  0,  30, 225),
            new WindBand( 30,  60,  45),
            new WindBand( 60,  90, 225),
        };

        // River extraction thresholds in normalized flux space.
        public float RiverThreshold = 96f;
        public float RiverTraceThreshold = 40f;
        public int MinRiverVertices = 12;
        const float RiverTuningReferenceCellCount = 5000f;

        // Realm seeding floors: a landmass must pass both to get its own realm seed.
        public int MinRealmCells = 64;
        public float MinRealmPopulationFraction = 0.02f;

        // Optional per-template tuning override used by analysis/sweeps.
        public HeightmapTemplateTuningProfile TemplateTuningOverride;

        // Optional tectonic context from globe site selection.
        // Null when generating without a globe (no impact on non-globe flows).
        public TectonicHints Tectonics;

        public float EffectiveRiverThreshold
        {
            get
            {
                HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(Template, this);
                float scale = profile != null ? profile.RiverThresholdScale : 1f;
                if (scale <= 0f) scale = 1f;
                return RiverThreshold * scale * RiverResolutionScale();
            }
        }

        public float EffectiveRiverTraceThreshold
        {
            get
            {
                HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(Template, this);
                float scale = profile != null ? profile.RiverTraceThresholdScale : 1f;
                if (scale <= 0f) scale = 1f;
                return RiverTraceThreshold * scale * RiverResolutionScale();
            }
        }

        public int EffectiveMinRiverVertices
        {
            get
            {
                HeightmapTemplateTuningProfile profile = HeightmapTemplateCompiler.ResolveTuningProfile(Template, this);
                float scale = profile != null ? profile.RiverMinVerticesScale : 1f;
                if (scale <= 0f) scale = 1f;
                int effective = (int)Math.Round(MinRiverVertices * scale, MidpointRounding.AwayFromZero);
                return Math.Max(1, effective);
            }
        }

        float RiverResolutionScale()
        {
            if (CellCount <= 0)
                return 1f;

            float ratio = CellCount / RiverTuningReferenceCellCount;
            if (ratio <= 1f)
                return 1f;

            return (float)Math.Sqrt(ratio);
        }

        // Sub-seed constants (golden-ratio hashing for decorrelation).
        const uint MeshXor = 0x9E3779B9;
        const uint ElevationXor = 0xA54FF53A;
        const uint ClimateXor = 0x63D83595;
        const uint RiverXor = 0x7B9D14E1;
        const uint GeologyXor = 0xD1B54A32;

        public int MeshSeed => (int)((uint)Seed ^ MeshXor);
        public int ElevationSeed => (int)((uint)Seed ^ ElevationXor);
        public int ClimateSeed => (int)((uint)Seed ^ ClimateXor);
        public int RiverSeed => (int)((uint)Seed ^ RiverXor);
        public int GeologySeed => (int)((uint)Seed ^ GeologyXor);

        public void Validate()
        {
            if (CellCount <= 0) throw new ArgumentOutOfRangeException(nameof(CellCount), "CellCount must be positive.");
            if (AspectRatio <= 0f) throw new ArgumentOutOfRangeException(nameof(AspectRatio), "AspectRatio must be positive.");
            if (CellSizeKm <= 0f) throw new ArgumentOutOfRangeException(nameof(CellSizeKm), "CellSizeKm must be positive.");
            if (float.IsNaN(Latitude) || float.IsInfinity(Latitude))
                throw new ArgumentOutOfRangeException(nameof(Latitude), "Latitude must be finite.");
            if (Latitude < -90f || Latitude > 90f)
                throw new ArgumentOutOfRangeException(nameof(Latitude), "Latitude must be within [-90, 90].");
            if (MaxElevationMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxElevationMeters), "MaxElevationMeters must be positive.");
            if (MaxSeaDepthMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxSeaDepthMeters), "MaxSeaDepthMeters must be positive.");
            if (float.IsNaN(TerrainShapeDomainMaxElevationMeters) || float.IsInfinity(TerrainShapeDomainMaxElevationMeters) || TerrainShapeDomainMaxElevationMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(TerrainShapeDomainMaxElevationMeters), "TerrainShapeDomainMaxElevationMeters must be positive and finite.");
            if (float.IsNaN(TerrainShapeDomainMaxSeaDepthMeters) || float.IsInfinity(TerrainShapeDomainMaxSeaDepthMeters) || TerrainShapeDomainMaxSeaDepthMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(TerrainShapeDomainMaxSeaDepthMeters), "TerrainShapeDomainMaxSeaDepthMeters must be positive and finite.");
            if (float.IsNaN(TerrainShapeReferenceSpanMeters) || float.IsInfinity(TerrainShapeReferenceSpanMeters) || TerrainShapeReferenceSpanMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(TerrainShapeReferenceSpanMeters), "TerrainShapeReferenceSpanMeters must be positive and finite.");
            if (float.IsNaN(TerrainShapeInitialSeaDepthMeters) || float.IsInfinity(TerrainShapeInitialSeaDepthMeters) || TerrainShapeInitialSeaDepthMeters <= 0f)
                throw new ArgumentOutOfRangeException(nameof(TerrainShapeInitialSeaDepthMeters), "TerrainShapeInitialSeaDepthMeters must be positive and finite.");
            if (float.IsNaN(TerrainDepthRemapExponent) || float.IsInfinity(TerrainDepthRemapExponent) || TerrainDepthRemapExponent <= 0f)
                throw new ArgumentOutOfRangeException(nameof(TerrainDepthRemapExponent), "TerrainDepthRemapExponent must be positive and finite.");
            if (LapseRateCPerKm <= 0f) throw new ArgumentOutOfRangeException(nameof(LapseRateCPerKm), "LapseRateCPerKm must be positive.");
            if (MaxAnnualPrecipitationMm <= 0f) throw new ArgumentOutOfRangeException(nameof(MaxAnnualPrecipitationMm), "MaxAnnualPrecipitationMm must be positive.");
            if (RiverThreshold <= 0f) throw new ArgumentOutOfRangeException(nameof(RiverThreshold), "RiverThreshold must be positive.");
            if (RiverTraceThreshold <= 0f) throw new ArgumentOutOfRangeException(nameof(RiverTraceThreshold), "RiverTraceThreshold must be positive.");
            if (MinRiverVertices <= 0) throw new ArgumentOutOfRangeException(nameof(MinRiverVertices), "MinRiverVertices must be positive.");
            if (MinRealmCells <= 0) throw new ArgumentOutOfRangeException(nameof(MinRealmCells), "MinRealmCells must be positive.");
            if (MinRealmPopulationFraction < 0f || MinRealmPopulationFraction > 1f)
                throw new ArgumentOutOfRangeException(nameof(MinRealmPopulationFraction), "MinRealmPopulationFraction must be in [0, 1].");
            if (WindBands == null || WindBands.Length == 0) throw new ArgumentException("WindBands must be configured.", nameof(WindBands));

            double halfSpan = ResolveHalfLatitudeSpanEstimate();
            if (double.IsNaN(halfSpan) || double.IsInfinity(halfSpan) || halfSpan <= 0d)
                throw new ArgumentOutOfRangeException(nameof(Latitude), "Derived latitude span must be positive and finite.");
            double latitudeSouth = Latitude - halfSpan;
            double latitudeNorth = Latitude + halfSpan;
            if (latitudeSouth < -90d)
                throw new ArgumentOutOfRangeException(nameof(Latitude), "Derived latitude south edge is below -90. Increase Latitude or reduce map size.");
            if (latitudeNorth > 90d)
                throw new ArgumentOutOfRangeException(nameof(Latitude), "Derived latitude north edge exceeds +90. Decrease Latitude or reduce map size.");
        }

        double ResolveHalfLatitudeSpanEstimate()
        {
            double cellSizeKm = CellSizeKm;
            double aspectRatio = AspectRatio;
            double mapAreaKm2 = CellCount * cellSizeKm * cellSizeKm;
            double mapWidthKm = Math.Sqrt(mapAreaKm2 * aspectRatio);
            double mapHeightKm = mapWidthKm / aspectRatio;
            return mapHeightKm / 111d / 2d;
        }
    }
}
