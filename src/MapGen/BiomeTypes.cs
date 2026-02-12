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

    public enum WaterFeatureType : byte
    {
        Ocean,
        Lake
    }

    public struct WaterFeature
    {
        public int Id;
        public WaterFeatureType Type;
        public bool TouchesBorder;
        public int CellCount;
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
}
