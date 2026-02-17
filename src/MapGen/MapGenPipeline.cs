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
        Peninsula,
        Shattered,
        OldWorld
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
