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
    /// Canonical map generation pipeline.
    /// </summary>
    public static class MapGenPipeline
    {
        public static MapGenResult Generate(MapGenConfig config)
        {
            config ??= new MapGenConfig();
            return MapGenPipelineCore.Generate(config);
        }
    }
}
