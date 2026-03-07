using System;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Religious
{
    /// <summary>
    /// Monthly system that collects tithes from counties and distributes them
    /// through the religious hierarchy (parish → diocese → archdiocese).
    ///
    /// Tithe amount = surplus production value × TitheRate × county adherence to parish faith.
    /// Parish keeps 60%, passes 40% to diocese. Diocese keeps 70%, passes 30% to archdiocese.
    /// Church wages flow back to parish seat counties, stimulating local economies.
    /// </summary>
    public class TitheSystem : ITickSystem
    {
        public string Name => "Tithe";
        public int TickInterval => SimulationConfig.Intervals.Monthly;

        /// <summary>Fraction of monthly surplus value collected as tithe.</summary>
        const float TitheRate = 0.07f;

        /// <summary>Fraction of parish tithe passed up to diocese.</summary>
        const float ParishToDioceseRate = 0.40f;

        /// <summary>Fraction of diocese tithe passed up to archdiocese.</summary>
        const float DioceseToArchdioceseRate = 0.30f;

        /// <summary>Fraction of parish treasury spent on wages (returned to seat county).</summary>
        const float ChurchWageRate = 0.50f;

        int[] _countyIds;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _countyIds = new int[mapData.Counties.Count];
            for (int i = 0; i < mapData.Counties.Count; i++)
                _countyIds[i] = mapData.Counties[i].Id;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var religion = state.Religion;
            if (religion == null || religion.Parishes == null) return;

            var econ = state.Economy;
            if (econ == null) return;

            var counties = econ.Counties;
            var prices = econ.MarketPrices;

            // Reset monthly tithe accumulators
            for (int i = 0; i < _countyIds.Length; i++)
            {
                var ce = counties[_countyIds[i]];
                if (ce != null) ce.TithePaid = 0f;
            }

            // ── Phase 1: Collect tithes from counties into parishes ──
            for (int p = 1; p < religion.Parishes.Length; p++)
            {
                var parish = religion.Parishes[p];
                if (parish == null) continue;

                float parishTithe = 0f;
                for (int c = 0; c < parish.CountyIds.Count; c++)
                {
                    int countyId = parish.CountyIds[c];
                    if (countyId < 0 || countyId >= counties.Length) continue;
                    var ce = counties[countyId];
                    if (ce == null) continue;

                    // Compute monthly surplus value (production - consumption, accumulated over 30 days)
                    float surplusValue = 0f;
                    for (int g = 0; g < Goods.Count; g++)
                    {
                        float surplus = ce.Production[g] - ce.Consumption[g];
                        if (surplus > 0f)
                            surplusValue += surplus * prices[g];
                    }
                    // Scale to monthly (Production/Consumption are daily snapshots)
                    surplusValue *= SimulationConfig.Intervals.Monthly;

                    // Adherence-weighted tithe
                    var adh = religion.Adherence[countyId];
                    float adherence = adh != null && parish.FaithIndex < adh.Length
                        ? adh[parish.FaithIndex] : 0f;

                    float tithe = Math.Min(surplusValue * TitheRate * adherence, ce.Treasury);
                    if (tithe > 0f)
                    {
                        ce.Treasury -= tithe;
                        ce.TithePaid += tithe;
                        parishTithe += tithe;
                    }
                }

                parish.Treasury += parishTithe;
            }

            // ── Phase 2: Parishes pass share to dioceses ──
            for (int d = 1; d < religion.Dioceses.Length; d++)
            {
                var diocese = religion.Dioceses[d];
                if (diocese == null) continue;

                float dioceseTithe = 0f;
                for (int p = 0; p < diocese.ParishIds.Count; p++)
                {
                    int parishId = diocese.ParishIds[p];
                    if (parishId < 1 || parishId >= religion.Parishes.Length) continue;
                    var parish = religion.Parishes[parishId];
                    if (parish == null) continue;

                    float share = parish.Treasury * ParishToDioceseRate;
                    if (share > 0f)
                    {
                        parish.Treasury -= share;
                        dioceseTithe += share;
                    }
                }

                diocese.Treasury += dioceseTithe;
            }

            // ── Phase 3: Dioceses pass share to archdioceses ──
            for (int a = 1; a < religion.Archdioceses.Length; a++)
            {
                var arch = religion.Archdioceses[a];
                if (arch == null) continue;

                float archTithe = 0f;
                for (int d = 0; d < arch.DioceseIds.Count; d++)
                {
                    int dioceseId = arch.DioceseIds[d];
                    if (dioceseId < 1 || dioceseId >= religion.Dioceses.Length) continue;
                    var diocese = religion.Dioceses[dioceseId];
                    if (diocese == null) continue;

                    float share = diocese.Treasury * DioceseToArchdioceseRate;
                    if (share > 0f)
                    {
                        diocese.Treasury -= share;
                        archTithe += share;
                    }
                }

                arch.Treasury += archTithe;
            }

            // ── Phase 4: Church wages — parishes spend treasury on local economy ──
            for (int p = 1; p < religion.Parishes.Length; p++)
            {
                var parish = religion.Parishes[p];
                if (parish == null || parish.Treasury <= 0f) continue;

                float wages = parish.Treasury * ChurchWageRate;
                parish.Treasury -= wages;

                // Distribute wages to parish counties proportional to adherence
                float totalAdherence = 0f;
                for (int c = 0; c < parish.CountyIds.Count; c++)
                {
                    int countyId = parish.CountyIds[c];
                    if (countyId < 0 || countyId >= counties.Length) continue;
                    var adh = religion.Adherence[countyId];
                    if (adh != null && parish.FaithIndex < adh.Length)
                        totalAdherence += adh[parish.FaithIndex];
                }

                if (totalAdherence <= 0f) continue;

                for (int c = 0; c < parish.CountyIds.Count; c++)
                {
                    int countyId = parish.CountyIds[c];
                    if (countyId < 0 || countyId >= counties.Length) continue;
                    var ce = counties[countyId];
                    if (ce == null) continue;

                    var adh = religion.Adherence[countyId];
                    float adherence = adh != null && parish.FaithIndex < adh.Length
                        ? adh[parish.FaithIndex] : 0f;
                    ce.Treasury += wages * (adherence / totalAdherence);
                }
            }
        }
    }
}
