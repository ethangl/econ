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
    /// Canonical MapGen configuration (V2 world-unit pipeline).
    /// </summary>
    public class MapGenConfig : MapGenV2Config
    {
    }

    /// <summary>
    /// Canonical MapGen result (V2 world-unit pipeline).
    /// </summary>
    public class MapGenResult : MapGenV2Result
    {
        // Legacy alias kept for call sites that still read `result.Heights`.
        public ElevationField Heights => Elevation;
    }

    /// <summary>
    /// Canonical map generation pipeline.
    /// </summary>
    public static class MapGenPipeline
    {
        public static MapGenResult Generate(MapGenConfig config)
        {
            config ??= new MapGenConfig();

            var v2 = MapGenPipelineV2.Generate(config);
            return new MapGenResult
            {
                Mesh = v2.Mesh,
                Elevation = v2.Elevation,
                Climate = v2.Climate,
                Rivers = v2.Rivers,
                Biomes = v2.Biomes,
                Political = v2.Political,
                World = v2.World
            };
        }
    }
}
