using System.Collections.Generic;

namespace EconSim.Core.Religious
{
    /// <summary>
    /// Runtime religious state. Tracks per-county adherence and organizational hierarchy.
    /// Adherence is a weighted overlay — multiple faiths can coexist in the same county.
    /// </summary>
    public class ReligionState
    {
        /// <summary>Number of distinct faiths (religions) in this world.</summary>
        public int FaithCount;

        /// <summary>
        /// Per-county adherence fractions. [countyId][faithIndex] where faithIndex = religionId - 1.
        /// Values are population fractions in [0,1], summing to &lt;= 1.0 per county (remainder = unaffiliated).
        /// </summary>
        public float[][] Adherence;

        /// <summary>Majority faith per county (religion ID, 0 = none/unaffiliated). Cached from Adherence.</summary>
        public int[] MajorityFaith;

        /// <summary>Religion ID → dense faith index (0-based). Only populated religions get indices.</summary>
        public Dictionary<int, int> ReligionToFaithIndex;

        /// <summary>Dense faith index → religion ID.</summary>
        public int[] FaithIndexToReligion;

        /// <summary>Parishes, indexed by parish ID (slot 0 unused).</summary>
        public Parish[] Parishes;

        /// <summary>Dioceses, indexed by diocese ID (slot 0 unused).</summary>
        public Diocese[] Dioceses;

        /// <summary>Archdioceses, indexed by archdiocese ID (slot 0 unused).</summary>
        public Archdiocese[] Archdioceses;

        /// <summary>County ID → list of parish IDs covering this county (may be empty or multi-faith).</summary>
        public List<int>[] CountyParishes;

        /// <summary>Set by ReligionSpreadSystem when any county's adherence crosses a visual threshold.</summary>
        public bool OverlayDirty;

        /// <summary>
        /// Recompute MajorityFaith from Adherence for all counties.
        /// </summary>
        public void UpdateMajorityFaith()
        {
            for (int c = 0; c < Adherence.Length; c++)
            {
                var adh = Adherence[c];
                if (adh == null)
                {
                    MajorityFaith[c] = 0;
                    continue;
                }

                int bestIdx = -1;
                float bestVal = 0f;
                for (int f = 0; f < FaithCount; f++)
                {
                    if (adh[f] > bestVal)
                    {
                        bestVal = adh[f];
                        bestIdx = f;
                    }
                }

                MajorityFaith[c] = bestIdx >= 0 && bestVal > 0f
                    ? FaithIndexToReligion[bestIdx]
                    : 0;
            }
        }
    }

    public class Parish
    {
        public int Id;
        public int FaithIndex;          // Dense faith index (not religion ID)
        public int SeatCellId;          // Cell where the parish church is
        public List<int> CountyIds;     // Counties served by this parish
        public int PriestActorId;       // 0 = vacant
        public float Treasury;          // Crowns collected from tithes
    }

    public class Diocese
    {
        public int Id;
        public int FaithIndex;
        public int CathedralCellId;     // Cell where the cathedral is
        public List<int> ParishIds;     // Parishes in this diocese
        public int BishopActorId;       // 0 = vacant
        public float Treasury;          // Crowns collected from parish tithes
    }

    public class Archdiocese
    {
        public int Id;
        public int FaithIndex;
        public int SeatCellId;          // Archbishopric seat
        public List<int> DioceseIds;    // Dioceses in this archdiocese
        public int ArchbishopActorId;   // 0 = vacant
        public float Treasury;          // Crowns collected from diocese tithes
    }
}
