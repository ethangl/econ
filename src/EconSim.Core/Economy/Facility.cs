using System;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// A facility instance in a county. Runtime state, mutable.
    /// </summary>
    [Serializable]
    public class Facility
    {
        /// <summary>Unique ID for this facility instance.</summary>
        public int Id;

        /// <summary>Reference to the facility type definition.</summary>
        public string TypeId;

        /// <summary>Cell where this facility is physically located.</summary>
        public int CellId;

        /// <summary>County that owns this facility (for economic flows).</summary>
        public int CountyId;

        /// <summary>Current number of workers assigned.</summary>
        public int AssignedWorkers;

        /// <summary>Local input inventory (materials waiting to be processed).</summary>
        public Stockpile InputBuffer;

        /// <summary>Local output inventory (finished goods waiting for transport).</summary>
        public Stockpile OutputBuffer;

        /// <summary>Whether this facility is currently operating.</summary>
        public bool IsActive;

        public Facility()
        {
            InputBuffer = new Stockpile();
            OutputBuffer = new Stockpile();
            IsActive = true;
        }

        /// <summary>
        /// Calculate current efficiency based on staffing.
        /// Formula: (workers / required)^α where α < 1 gives diminishing returns.
        /// </summary>
        public float GetEfficiency(FacilityDef def, float alpha = 0.7f)
        {
            if (def.LaborRequired <= 0) return 1f;
            if (AssignedWorkers <= 0) return 0f;

            float ratio = (float)AssignedWorkers / def.LaborRequired;
            if (ratio >= 1f) return 1f;

            return (float)Math.Pow(ratio, alpha);
        }

        /// <summary>
        /// Calculate actual throughput this tick.
        /// </summary>
        public float GetThroughput(FacilityDef def)
        {
            if (!IsActive) return 0f;
            return def.BaseThroughput * GetEfficiency(def);
        }
    }
}
