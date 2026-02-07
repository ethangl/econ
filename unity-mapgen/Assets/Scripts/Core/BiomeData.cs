namespace MapGen.Core
{
    public enum SoilType : byte
    {
        Permafrost,
        Saline,
        Lithosol,
        Alluvial,
        Aridisol,
        Laterite,
        Podzol,
        Chernozem
    }

    public enum RockType : byte
    {
        Granite,
        Sedimentary,
        Limestone,
        Volcanic
    }

    public enum VegetationType : byte
    {
        None,
        LichenMoss,
        Grass,
        Shrub,
        DeciduousForest,
        ConiferousForest,
        BroadleafForest
    }

    public enum BiomeId : byte
    {
        Glacier,
        Tundra,
        SaltFlat,
        CoastalMarsh,
        AlpineBarren,
        MountainShrub,
        Floodplain,
        Wetland,
        HotDesert,
        ColdDesert,
        Scrubland,
        TropicalRainforest,
        TropicalDryForest,
        Savanna,
        BorealForest,
        TemperateForest,
        Grassland,
        Woodland,
        Lake
    }

    /// <summary>
    /// Container for all per-cell biome pipeline outputs.
    /// Stages: derived inputs -> soil -> biome -> vegetation -> fauna -> movement/resources.
    /// Water cells (height <= sea level) are excluded from the pipeline.
    /// </summary>
    public class BiomeData
    {
        public CellMesh Mesh { get; }
        public int CellCount => Mesh.CellCount;

        // Lake detection (set before other derived inputs)
        public bool[] IsLakeCell;       // true for cells where majority of vertices are lake vertices

        // Derived inputs
        public float[] Slope;           // 0-1, normalized max height gradient to neighbors
        public float[] SaltEffect;      // 0-1, BFS decay from ocean cells
        public float[] LakeEffect;      // 0-1, BFS decay from freshwater lake cells
        public float[] CellFlux;        // averaged vertex flux onto cells
        public float[] Loess;           // 0-1, wind-deposited silt

        // Stage 1: Soil
        public RockType[] Rock;         // Perlin noise -> 4 categories
        public SoilType[] Soil;         // priority cascade -> 8 types
        public float[] Fertility;       // 0-1, base * rock * loess * drainage

        // Stage 2: Biome
        public BiomeId[] Biome;         // soil + temp + precip -> 18 biomes
        public float[] Habitability;    // 0-100, biome base + river bonus

        // Stage 3: Vegetation
        public VegetationType[] Vegetation;      // biome -> dominant type
        public float[] VegetationDensity;        // 0-1, precip * elevation * salinity

        // Stage 4: Fauna
        public float[] FishAbundance;       // 0-1
        public float[] GameAbundance;       // 0-1
        public float[] WaterfowlAbundance;  // 0-1
        public float[] FurAbundance;        // 0-1
        public float[] Subsistence;         // 0-1, natural carrying capacity

        // Final outputs
        public float[] MovementCost;        // per-cell base cost
        public float[] IronAbundance;       // 0-1, geological Perlin noise
        public float[] GoldAbundance;       // 0-1, geological Perlin noise
        public float[] LeadAbundance;       // 0-1, geological Perlin noise
        public float[] SaltAbundance;       // 0-1, biome-derived (saline soil + salt effect)
        public float[] StoneAbundance;      // 0-1, rock type + slope

        // Suitability scoring
        public float[] Suitability;         // composite settlement score
        public float[] SuitabilityGeo;      // geographic bonus subtotal (debug)

        // Population
        public float[] Population;          // suitability * area-normalized

        public BiomeData(CellMesh mesh)
        {
            Mesh = mesh;
            int n = mesh.CellCount;

            IsLakeCell = new bool[n];

            Slope = new float[n];
            SaltEffect = new float[n];
            LakeEffect = new float[n];
            CellFlux = new float[n];
            Loess = new float[n];

            Rock = new RockType[n];
            Soil = new SoilType[n];
            Fertility = new float[n];

            Biome = new BiomeId[n];
            Habitability = new float[n];

            Vegetation = new VegetationType[n];
            VegetationDensity = new float[n];

            FishAbundance = new float[n];
            GameAbundance = new float[n];
            WaterfowlAbundance = new float[n];
            FurAbundance = new float[n];
            Subsistence = new float[n];

            MovementCost = new float[n];
            IronAbundance = new float[n];
            GoldAbundance = new float[n];
            LeadAbundance = new float[n];
            SaltAbundance = new float[n];
            StoneAbundance = new float[n];

            Suitability = new float[n];
            SuitabilityGeo = new float[n];

            Population = new float[n];
        }

        /// <summary>Count cells per soil type (land cells only).</summary>
        public int[] SoilCounts(HeightGrid heights)
        {
            int[] counts = new int[8];
            for (int i = 0; i < CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                counts[(int)Soil[i]]++;
            }
            return counts;
        }

        /// <summary>Count cells per biome (land cells only).</summary>
        public int[] BiomeCounts(HeightGrid heights)
        {
            int biomeCount = System.Enum.GetValues(typeof(BiomeId)).Length;
            int[] counts = new int[biomeCount];
            for (int i = 0; i < CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                counts[(int)Biome[i]]++;
            }
            return counts;
        }
    }
}
