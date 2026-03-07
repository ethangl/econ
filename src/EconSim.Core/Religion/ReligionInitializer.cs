using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;

namespace EconSim.Core.Religious
{
    /// <summary>
    /// Initializes ReligionState from MapData. Computes per-county adherence
    /// from cell-level religion assignments, weighted by cell population.
    /// </summary>
    public static class ReligionInitializer
    {
        public static ReligionState Initialize(MapData mapData, int maxCountyId)
        {
            var state = new ReligionState();

            // Build dense faith index from religions that actually exist on the map
            var religionIds = new HashSet<int>();
            foreach (var cell in mapData.Cells)
            {
                if (cell.ReligionId > 0)
                    religionIds.Add(cell.ReligionId);
            }

            // Also include any religions from the Religions list (some may not have cells)
            if (mapData.Religions != null)
            {
                foreach (var r in mapData.Religions)
                    religionIds.Add(r.Id);
            }

            // Sort for deterministic index assignment
            var sortedIds = new List<int>(religionIds);
            sortedIds.Sort();

            state.FaithCount = sortedIds.Count;
            state.ReligionToFaithIndex = new Dictionary<int, int>(sortedIds.Count);
            state.FaithIndexToReligion = new int[sortedIds.Count];
            for (int i = 0; i < sortedIds.Count; i++)
            {
                state.ReligionToFaithIndex[sortedIds[i]] = i;
                state.FaithIndexToReligion[i] = sortedIds[i];
            }

            // Allocate adherence arrays
            state.Adherence = new float[maxCountyId + 1][];
            state.MajorityFaith = new int[maxCountyId + 1];
            state.CountyParishes = new List<int>[maxCountyId + 1];

            for (int c = 0; c <= maxCountyId; c++)
                state.CountyParishes[c] = new List<int>();

            // Compute per-county adherence from cell populations
            foreach (var county in mapData.Counties)
            {
                var adh = new float[state.FaithCount];
                float totalPop = 0f;

                foreach (int cellId in county.CellIds)
                {
                    if (!mapData.CellById.TryGetValue(cellId, out var cell)) continue;
                    if (!cell.IsLand) continue;

                    float cellPop = cell.Population;
                    totalPop += cellPop;

                    if (cell.ReligionId > 0 && state.ReligionToFaithIndex.TryGetValue(cell.ReligionId, out int fi))
                        adh[fi] += cellPop;
                }

                // Normalize to fractions
                if (totalPop > 0f)
                {
                    for (int f = 0; f < state.FaithCount; f++)
                        adh[f] /= totalPop;
                }

                state.Adherence[county.Id] = adh;
            }

            // Compute majority faith per county
            state.UpdateMajorityFaith();

            // Empty hierarchy — populated by ReligionBootstrap
            state.Parishes = new Parish[1]; // slot 0 unused
            state.Dioceses = new Diocese[1];
            state.Archdioceses = new Archdiocese[1];

            SimLog.Log("Religion",
                $"Initialized: {state.FaithCount} faiths, {mapData.Counties.Count} counties with adherence data");

            return state;
        }
    }
}
