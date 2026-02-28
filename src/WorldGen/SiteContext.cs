namespace WorldGen.Core
{
    /// <summary>
    /// Terrain archetype derived from tectonic context.
    /// Caller maps to MapGen's HeightmapTemplateType.
    /// </summary>
    public enum SiteType
    {
        /// <summary>Strong convergent boundary within 2 hops</summary>
        Volcanic,
        /// <summary>Moderate convergent boundary (2-4 hops)</summary>
        HighIsland,
        /// <summary>Divergent boundary or no nearby boundary (stable ocean)</summary>
        LowIsland,
        /// <summary>Multiple plate boundaries or triple junction nearby</summary>
        Archipelago
    }

    /// <summary>
    /// Describes a selected site on the globe for flat map generation.
    /// Bridges WorldGen (globe/tectonics) to MapGen (flat heightmap template).
    /// </summary>
    public class SiteContext
    {
        /// <summary>Coarse mesh cell index at the site</summary>
        public int CellIndex;

        /// <summary>Latitude in degrees (-90 to 90)</summary>
        public float Latitude;

        /// <summary>Longitude in degrees (-180 to 180)</summary>
        public float Longitude;

        /// <summary>BFS hops to nearest land (0 = coastal ocean cell)</summary>
        public int CoastDistanceHops;

        /// <summary>Unit vector pointing toward nearest coast</summary>
        public Vec3 CoastDirection;

        /// <summary>Type of closest plate boundary</summary>
        public BoundaryType NearestBoundary;

        /// <summary>Convergence magnitude of nearest boundary</summary>
        public float BoundaryConvergence;

        /// <summary>BFS hops to nearest plate boundary</summary>
        public int BoundaryDistanceHops;

        /// <summary>Derived template recommendation</summary>
        public SiteType SiteType;
    }
}
