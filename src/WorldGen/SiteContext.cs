using System.Collections.Generic;

namespace WorldGen.Core
{
    /// <summary>
    /// A nearby continental plate detected from the site via BFS on the coarse mesh.
    /// </summary>
    public struct ContinentalNeighbor
    {
        public int PlateIndex;
        public float DirectionDeg;
        public int DistanceHops;
        public bool IsMajor;
    }

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

        /// <summary>Ocean current temperature anomaly in °C.
        /// Positive = warm current (source water from equator-ward),
        /// negative = cold current (source water from pole-ward). Range ~[-8, +8].</summary>
        public float OceanCurrentAnomaly;

        /// <summary>Moisture bias from continental blocking.
        /// -1 = site is downwind of continent (dry shadow),
        /// +1 = full oceanic fetch (wet). 0 = neutral.</summary>
        public float MoistureBias;

        /// <summary>Prevailing wind at site latitude as tangent-plane unit vector (east component).</summary>
        public float WindDirectionEast;

        /// <summary>Prevailing wind at site latitude as tangent-plane unit vector (north component).</summary>
        public float WindDirectionNorth;

        /// <summary>Nearby continental plates found via BFS, sorted by distance. Capped at 5.</summary>
        public List<ContinentalNeighbor> ContinentalNeighbors;
    }
}
