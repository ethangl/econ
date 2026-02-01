using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Type of labor required to staff a facility.
    /// </summary>
    public enum LaborType
    {
        Unskilled,  // Laborers - farms, mines, basic extraction
        Skilled     // Craftsmen - smiths, millers, carpenters
    }

    /// <summary>
    /// Static definition of a facility type (sawmill, smelter, farm, etc.).
    /// Loaded from data files, immutable at runtime.
    /// </summary>
    [Serializable]
    public class FacilityDef
    {
        public string Id;
        public string Name;

        /// <summary>What this facility produces.</summary>
        public string OutputGoodId;

        /// <summary>Number of workers needed for full efficiency.</summary>
        public int LaborRequired;

        /// <summary>Type of workers needed.</summary>
        public LaborType LaborType;

        /// <summary>Units produced per tick at full staffing.</summary>
        public float BaseThroughput;

        /// <summary>
        /// For extraction facilities: required terrain/biome types.
        /// Empty means no terrain restriction (processing facilities).
        /// </summary>
        public List<string> TerrainRequirements;

        /// <summary>
        /// Whether this facility extracts raw resources (vs processing).
        /// Extraction facilities need matching terrain AND resource presence.
        /// </summary>
        public bool IsExtraction;

        public bool HasTerrainRequirement => TerrainRequirements != null && TerrainRequirements.Count > 0;
    }

    /// <summary>
    /// Registry of all facility type definitions.
    /// </summary>
    public class FacilityRegistry
    {
        private readonly Dictionary<string, FacilityDef> _facilities = new Dictionary<string, FacilityDef>();

        public void Register(FacilityDef facility)
        {
            _facilities[facility.Id] = facility;
        }

        public FacilityDef Get(string id)
        {
            return _facilities.TryGetValue(id, out var facility) ? facility : null;
        }

        public IEnumerable<FacilityDef> All => _facilities.Values;

        public IEnumerable<FacilityDef> ExtractionFacilities
        {
            get
            {
                foreach (var f in _facilities.Values)
                {
                    if (f.IsExtraction)
                        yield return f;
                }
            }
        }

        public IEnumerable<FacilityDef> ProcessingFacilities
        {
            get
            {
                foreach (var f in _facilities.Values)
                {
                    if (!f.IsExtraction)
                        yield return f;
                }
            }
        }
    }
}
