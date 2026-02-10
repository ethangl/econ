using System;

namespace MapGen.Core
{
    /// <summary>
    /// Available heightmap templates.
    /// </summary>
    public enum HeightmapTemplateType
    {
        Volcano,
        LowIsland,
        Archipelago,
        Continents,
        Pangea,
        HighIsland,
        Atoll,
        Peninsula,
        Mediterranean,
        Isthmus,
        Shattered,
        Taklamakan,
        OldWorld,
        Fractious
    }

    /// <summary>
    /// Configuration for headless map generation.
    /// </summary>
    public class MapGenConfig
    {
        public int Seed = 12345;
        public int CellCount = 10000;
        public float AspectRatio = 16f / 9f;
        public HeightmapTemplateType Template = HeightmapTemplateType.LowIsland;
        public float LatitudeSouth = 30f;
        public float RiverThreshold = 240f;
        public float RiverTraceThreshold = 12f;
        public int MinRiverVertices = 12;

        // Sub-seed constants (golden ratio hashing for decorrelation)
        const uint HeightmapXor = 0x9E3779B9;
        const uint RockXor = 0x517CC1B7;
        const uint IronXor = 0x6C62272E;
        const uint GoldXor = 0x2545F491;
        const uint LeadXor = 0x369DEA0F;

        public int HeightmapSeed => (int)((uint)Seed ^ HeightmapXor);
        public int RockSeed => (int)((uint)Seed ^ RockXor);
        public int IronSeed => (int)((uint)Seed ^ IronXor);
        public int GoldSeed => (int)((uint)Seed ^ GoldXor);
        public int LeadSeed => (int)((uint)Seed ^ LeadXor);
    }

    /// <summary>
    /// Complete output from a map generation run.
    /// </summary>
    public class MapGenResult
    {
        public CellMesh Mesh;
        public HeightGrid Heights;
        public ClimateData Climate;
        public WorldConfig WorldConfig;
        public RiverData Rivers;
        public BiomeData Biomes;
        public PoliticalData Political;
    }

    /// <summary>
    /// Headless map generation pipeline. Runs the full generation pipeline
    /// without any Unity dependencies.
    /// </summary>
    public static class MapGenPipeline
    {
        const float CellSizeKm = 2.5f;

        public static MapGenResult Generate(MapGenConfig config)
        {
            // 1. Cell mesh: JitteredGrid -> VoronoiBuilder.Build -> ComputeAreas
            float cellArea = CellSizeKm * CellSizeKm;
            float mapArea = config.CellCount * cellArea;
            float mapWidth = (float)Math.Sqrt(mapArea * config.AspectRatio);
            float mapHeight = mapWidth / config.AspectRatio;

            var (gridPoints, spacing) = PointGenerator.JitteredGrid(mapWidth, mapHeight, config.CellCount, config.Seed);
            var boundaryPoints = PointGenerator.BoundaryPoints(mapWidth, mapHeight, spacing);
            var mesh = VoronoiBuilder.Build(mapWidth, mapHeight, gridPoints, boundaryPoints);
            mesh.ComputeAreas();

            // 2. Heightmap: HeightmapDSL.Execute with template
            var heights = new HeightGrid(mesh, ElevationDomains.Dsl);
            string script = HeightmapTemplates.GetTemplate(config.Template.ToString());
            HeightmapDSL.Execute(heights, script, config.HeightmapSeed);

            // 3. Explicit domain boundary: DSL shaping -> simulation/runtime domain.
            heights.RescaleTo(ElevationDomains.Simulation);

            // 4. Climate: WorldConfig + TemperatureOps + PrecipitationOps
            var worldConfig = new WorldConfig { LatitudeSouth = config.LatitudeSouth };
            worldConfig.AutoLatitudeSpan(mesh);
            var climate = new ClimateData(mesh);
            TemperatureOps.Compute(climate, heights, worldConfig);
            PrecipitationOps.Compute(climate, heights, worldConfig);

            // 5. Rivers: FlowOps.Compute
            var rivers = new RiverData(mesh);
            FlowOps.Compute(rivers, heights, climate, config.RiverThreshold, config.RiverTraceThreshold, config.MinRiverVertices);

            // 6. Biomes: full BiomeOps pipeline + Suitability + Population
            var biomes = new BiomeData(mesh);
            BiomeOps.ComputeLakeCells(biomes, heights, rivers);
            BiomeOps.ComputeSlope(biomes, heights);
            BiomeOps.ComputeSaltProximity(biomes, heights);
            BiomeOps.ComputeLakeProximity(biomes, heights);
            BiomeOps.ComputeCellFlux(biomes, rivers);
            BiomeOps.ComputeRockType(biomes, config.RockSeed);
            BiomeOps.ComputeLoess(biomes, heights, climate, worldConfig);
            BiomeOps.ClassifySoil(biomes, heights, climate);
            BiomeOps.ComputeFertility(biomes, heights, climate);
            BiomeOps.AssignBiomes(biomes, heights, climate);
            BiomeOps.ComputeHabitability(biomes, heights, rivers);
            BiomeOps.ComputeVegetation(biomes, heights, climate);
            BiomeOps.ComputeFauna(biomes, heights, climate, rivers);
            BiomeOps.ComputeSubsistence(biomes, heights, climate);
            BiomeOps.ComputeMovementCost(biomes, heights);
            BiomeOps.ComputeGeologicalResources(biomes, heights, config.IronSeed, config.GoldSeed, config.LeadSeed);
            BiomeOps.ComputeSaltResource(biomes, heights);
            BiomeOps.ComputeStoneResource(biomes, heights);
            SuitabilityOps.ComputeSuitability(biomes, heights, climate, rivers);
            PopulationOps.ComputePopulation(biomes, heights);
            GeographyOps.ComputeCoastDistance(biomes, heights);
            GeographyOps.ComputeWaterFeatures(biomes, heights);

            // 7. Political: 6-stage PoliticalOps pipeline
            var political = new PoliticalData(mesh);
            PoliticalOps.DetectLandmasses(political, heights, biomes);
            PoliticalOps.PlaceCapitals(political, biomes, heights);
            PoliticalOps.GrowRealms(political, biomes, heights);
            PoliticalOps.NormalizeRealms(political);
            PoliticalOps.SubdivideProvinces(political, biomes, heights);
            PoliticalOps.GroupCounties(political, biomes, heights);

            return new MapGenResult
            {
                Mesh = mesh,
                Heights = heights,
                Climate = climate,
                WorldConfig = worldConfig,
                Rivers = rivers,
                Biomes = biomes,
                Political = political
            };
        }
    }
}
