using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Monthly stockpile decay. Perishable goods (food, timber, wool) lose a fraction
    /// each month via precomputed retention factors in Goods.MonthlyRetention.
    /// </summary>
    public class SpoilageSystem : ITickSystem
    {
        public string Name => "Spoilage";
        public int TickInterval => SimulationConfig.Intervals.Monthly;

        int[] _countyIds;
        int[] _provinceIds;
        int[] _realmIds;

        // Goods indices where retention < 1 (i.e. spoilage > 0)
        int[] _perishableGoods;

        public void Initialize(SimulationState state, MapData mapData)
        {
            _countyIds = new int[mapData.Counties.Count];
            for (int i = 0; i < mapData.Counties.Count; i++)
                _countyIds[i] = mapData.Counties[i].Id;

            _provinceIds = new int[mapData.Provinces.Count];
            for (int i = 0; i < mapData.Provinces.Count; i++)
                _provinceIds[i] = mapData.Provinces[i].Id;

            _realmIds = new int[mapData.Realms.Count];
            for (int i = 0; i < mapData.Realms.Count; i++)
                _realmIds[i] = mapData.Realms[i].Id;

            // Precompute which goods actually spoil
            int count = 0;
            for (int g = 0; g < Goods.Count; g++)
                if (Goods.MonthlyRetention[g] < 1f) count++;

            _perishableGoods = new int[count];
            int idx = 0;
            for (int g = 0; g < Goods.Count; g++)
                if (Goods.MonthlyRetention[g] < 1f)
                    _perishableGoods[idx++] = g;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var provinces = econ.Provinces;
            var realms = econ.Realms;
            var retention = Goods.MonthlyRetention;

            for (int pi = 0; pi < _perishableGoods.Length; pi++)
            {
                int g = _perishableGoods[pi];
                float r = retention[g];

                for (int i = 0; i < _countyIds.Length; i++)
                {
                    var ce = counties[_countyIds[i]];
                    if (ce == null) continue;
                    ce.Stock[g] *= r;
                }

                for (int i = 0; i < _provinceIds.Length; i++)
                {
                    var pe = provinces[_provinceIds[i]];
                    if (pe == null) continue;
                    pe.Stockpile[g] *= r;
                }

                for (int i = 0; i < _realmIds.Length; i++)
                {
                    var re = realms[_realmIds[i]];
                    if (re == null) continue;
                    re.Stockpile[g] *= r;
                }
            }
        }
    }
}
