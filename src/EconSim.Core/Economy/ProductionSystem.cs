using System;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Economy
{
    /// <summary>
    /// Extraction + facility processing each tick.
    /// Runs before ConsumptionSystem — both writes Stock[]/Production[] and reads FacilityInputNeed[].
    /// </summary>
    public class ProductionSystem : ITickSystem
    {
        public string Name => "Production";
        public int TickInterval => SimulationConfig.Intervals.Daily;

        int[] _countyIds;
        int[] _countyToMarket;
        readonly float[] _effPopBuf = new float[5]; // reusable buffer for NeedCategory effective pop

        public void Initialize(SimulationState state, MapData mapData)
        {
            // First registered economy system — run full initialization
            if (state.Economy == null)
                EconomyInitializer.Initialize(state, mapData);

            _countyIds = new int[mapData.Counties.Count];
            for (int i = 0; i < mapData.Counties.Count; i++)
                _countyIds[i] = mapData.Counties[i].Id;

            _countyToMarket = state.Economy.CountyToMarket;
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var econ = state.Economy;
            var counties = econ.Counties;
            var countyFacilityIndices = econ.CountyFacilityIndices;
            var countyIds = _countyIds;
            int goodsCount = Goods.Count;
            int todayDow = Calendar.DayOfWeek(state.CurrentDay);
            int dayOfYear = (state.CurrentDay - 1) % Calendar.DaysPerYear;

            // Compute production capacity per good (structural capacity for price discovery)
            var productionCap = econ.ProductionCapacity;
            Array.Clear(productionCap, 0, goodsCount);
            // Precompute seasonal wave from day of year (shared across counties)
            float seasonalWave = (float)Math.Cos(2.0 * Math.PI * (dayOfYear - SimulationConfig.Seasonality.SummerSolsticeDay) / Calendar.DaysPerYear);
            float globalSeverity = SimulationConfig.Seasonality.GlobalSeverity;
            // Long-term climate wave: slow oscillation over decades (baseline shift)
            float climateWave = (float)Math.Cos(2.0 * Math.PI * state.CurrentDay / ((double)SimulationConfig.Seasonality.ClimateWavePeriodYears * Calendar.DaysPerYear));
            float climateAmplitude = SimulationConfig.Seasonality.ClimateWaveAmplitude;

            // Extraction capacity — driven by LowerCommoner (peasant) population
            const int LowerCommoner = (int)Estate.LowerCommoner;
            const int UpperCommoner = (int)Estate.UpperCommoner;
            for (int i = 0; i < countyIds.Length; i++)
            {
                var ce = counties[countyIds[i]];
                if (ce == null) continue;
                float extractionPop = ce.EstatePop[LowerCommoner];
                float wave = ce.Latitude < 0 ? -seasonalWave : seasonalWave;
                float amplitude = Math.Abs(ce.Latitude) / 90f;
                for (int g = 0; g < goodsCount; g++)
                {
                    float cap = extractionPop * ce.Productivity[g];
                    float sens = Goods.SeasonalSensitivity[g];
                    float sm = 1f + sens * amplitude * (globalSeverity * wave * 0.5f + climateAmplitude * climateWave);
                    productionCap[g] += cap * sm;
                }
            }
            // Facility labor capacity
            var allFacilities = econ.Facilities;
            if (allFacilities != null)
            {
                for (int i = 0; i < countyIds.Length; i++)
                {
                    int countyId = countyIds[i];
                    var ce = counties[countyId];
                    if (ce == null) continue;
                    var indices = countyFacilityIndices != null && countyId < countyFacilityIndices.Length
                        ? countyFacilityIndices[countyId] : null;
                    if (indices == null || indices.Count == 0) continue;
                    float pop = ce.Population;
                    for (int fi = 0; fi < indices.Count; fi++)
                    {
                        var def = allFacilities[indices[fi]].Def;
                        if (def.OutputAmount > 0f)
                            productionCap[(int)def.OutputGood] += pop * def.MaxLaborFraction * def.OutputAmount;
                    }
                }
            }

            for (int i = 0; i < countyIds.Length; i++)
            {
                int countyId = countyIds[i];
                var ce = counties[countyId];
                if (ce == null) continue;

                float pop = ce.Population;
                float extractionPop = ce.EstatePop[LowerCommoner];
                float facilityPop = ce.EstatePop[UpperCommoner];
                float[] effPop = _effPopBuf;
                Estates.ComputeEffectivePop(ce.EstatePop, effPop);
                var indices = countyFacilityIndices != null && countyId < countyFacilityIndices.Length
                    ? countyFacilityIndices[countyId]
                    : null;

                // Local market prices for this county's market zone
                int countyMarketId = _countyToMarket != null && countyId < _countyToMarket.Length
                    ? _countyToMarket[countyId] : 0;
                var localPrices = econ.PerMarketPrices != null
                    && countyMarketId > 0 && countyMarketId < econ.PerMarketPrices.Length
                    ? econ.PerMarketPrices[countyMarketId] : econ.MarketPrices;

                // Compute per-county seasonal modifier inputs (shared across goods)
                float countyWave = ce.Latitude < 0 ? -seasonalWave : seasonalWave;
                float countyAmplitude = Math.Abs(ce.Latitude) / 90f;

                bool isRestDay = todayDow == econ.CountySabbathDay[countyId];
                if (isRestDay)
                {
                    // Rest day: no production, zero out for clean downstream reads
                    for (int g = 0; g < goodsCount; g++)
                        ce.Production[g] = 0f;
                    ce.FacilityWorkers = 0f;
                }
                else
                {

                    // Compute facility input demand (two-pass: durables first, then intermediates)
                    Array.Clear(ce.FacilityInputNeed, 0, goodsCount);
                    if (indices != null && indices.Count > 0)
                    {
                        const float DurableBufferMultiplier = 3.0f;

                        // Pass 1: Durable outputs (self-contained demand from TargetStockPerPop)
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var def = econ.Facilities[indices[fi]].Def;
                            if (def.OutputAmount <= 0f) continue;
                            int output = (int)def.OutputGood;
                            if (!Goods.IsDurable[output]) continue;

                            float maxByLabor = pop * def.MaxLaborFraction * def.OutputAmount;

                            float targetStock = effPop[(int)Goods.Defs[output].Need] * Goods.TargetStockPerPop[output];
                            float maintenance = ce.Stock[output] * Goods.Defs[output].SpoilageRate;
                            float gap = Math.Max(0f, targetStock - ce.Stock[output]);
                            float dailyNeed = maintenance + gap * Goods.DurableCatchUpRate[output] * DurableBufferMultiplier;
                            float throughput = Math.Min(maxByLabor, dailyNeed);

                            float scale = throughput / def.OutputAmount;
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                                ce.FacilityInputNeed[(int)def.Inputs[ii].Good] += scale * def.Inputs[ii].Amount;
                        }

                        // Pass 2: Remaining facilities — signal input demand based on downstream need
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var def = econ.Facilities[indices[fi]].Def;
                            if (def.OutputAmount <= 0f) continue;
                            int output = (int)def.OutputGood;
                            if (Goods.IsDurable[output]) continue;

                            float maxByLabor = pop * def.MaxLaborFraction * def.OutputAmount;
                            float throughput = maxByLabor;

                            // Demand planning: cap by downstream need, NOT current stock
                            if (Goods.IsDurableInput[output])
                            {
                                float downstreamDemand = ce.FacilityInputNeed[output];
                                throughput = Math.Min(throughput, downstreamDemand);
                            }

                            float scale = throughput / def.OutputAmount;
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                                ce.FacilityInputNeed[(int)def.Inputs[ii].Good] += scale * def.Inputs[ii].Amount;
                        }
                    }

                    // Extraction — driven by LowerCommoner (peasant) population
                    for (int g = 0; g < goodsCount; g++)
                    {
                        float produced = extractionPop * ce.Productivity[g];

                        // Seasonal + climate extraction modifier
                        float sens = Goods.SeasonalSensitivity[g];
                        float sm = 1f + sens * countyAmplitude * (globalSeverity * countyWave * 0.5f + climateAmplitude * climateWave);
                        produced *= sm;

                        // Stock-ceiling cap for durable-chain raws (demand-driven extraction)
                        if (Goods.IsDurableInput[g])
                        {
                            float localDemand = ce.FacilityInputNeed[g];
                            const float ChainBufferDays = 14f;
                            float targetStock = localDemand * ChainBufferDays;
                            float gap = Math.Max(0f, targetStock - ce.Stock[g]);
                            produced = Math.Min(produced, gap);
                        }
                        // Stock-gap cap for raw durables (e.g. fur — extracted directly)
                        else if (Goods.IsDurable[g])
                        {
                            float targetStock = effPop[(int)Goods.Defs[g].Need] * Goods.TargetStockPerPop[g];
                            float maintenance = ce.Stock[g] * Goods.Defs[g].SpoilageRate;
                            float gap = Math.Max(0f, targetStock - ce.Stock[g]);
                            float dailyNeed = maintenance + gap * Goods.DurableCatchUpRate[g] * 3.0f;
                            produced = Math.Min(produced, dailyNeed);
                        }
                        // Gentle price-based extraction throttle for commodity intermediates
                        // Only kicks in below 50% of base price, using sqrt curve
                        else if (!Goods.HasDirectDemand[g] && Goods.BasePrice[g] > 0f)
                        {
                            float priceRatio = localPrices[g] / Goods.BasePrice[g];
                            if (priceRatio < 0.5f)
                                produced *= (float)Math.Sqrt(priceRatio * 2f);
                        }

                        ce.Stock[g] += produced;
                        ce.Production[g] = produced;
                    }

                    // Facility processing — input/labor constrained, price-throttled for intermediates.
                    // Two-pass: chain intermediates (IsDurableInput) first so they get priority
                    // access to shared inputs (e.g. charcoalBurner gets timber before carpenter).
                    // Facilities draw from a shared UpperCommoner labor pool.
                    if (indices != null && indices.Count > 0)
                    {
                        float totalFacWorkers = 0f;
                        float remainingFacLabor = facilityPop;
                        for (int pass = 0; pass < 2; pass++)
                        {
                        for (int fi = 0; fi < indices.Count; fi++)
                        {
                            var fac = econ.Facilities[indices[fi]];
                            var def = fac.Def;
                            int output = (int)def.OutputGood;
                            bool isChain = Goods.IsDurableInput[output];
                            if ((pass == 0) != isChain) continue;

                            // Material constraint: min across all inputs
                            float maxByInput = float.MaxValue;
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                            {
                                float avail = ce.Stock[(int)def.Inputs[ii].Good] / def.Inputs[ii].Amount * def.OutputAmount;
                                if (avail < maxByInput) maxByInput = avail;
                            }

                            // Per-facility labor constraint
                            float maxByLabor = pop * def.MaxLaborFraction * def.OutputAmount;

                            // Shared UpperCommoner labor pool constraint
                            float maxByPool = remainingFacLabor * def.OutputAmount;

                            float throughput = Math.Min(maxByInput, Math.Min(maxByLabor, maxByPool));
                            if (throughput < 0f) throughput = 0f;

                            // Stock-gap production cap for durables
                            if (Goods.IsDurable[output])
                            {
                                float targetStock = effPop[(int)Goods.Defs[output].Need] * Goods.TargetStockPerPop[output];
                                float currentStock = ce.Stock[output];
                                float maintenance = currentStock * Goods.Defs[output].SpoilageRate;
                                float gap = Math.Max(0f, targetStock - currentStock);
                                const float DurableBufferMultiplier = 3.0f;
                                float dailyNeed = maintenance + gap * Goods.DurableCatchUpRate[output] * DurableBufferMultiplier;
                                throughput = Math.Min(throughput, dailyNeed);
                            }

                            // Stock-ceiling cap for durable-chain intermediates (iron, charcoal)
                            if (Goods.IsDurableInput[output])
                            {
                                float downstreamDemand = ce.FacilityInputNeed[output];
                                const float ChainBufferDays = 7f;
                                float targetStock = downstreamDemand * ChainBufferDays;
                                float gap = Math.Max(0f, targetStock - ce.Stock[output]);
                                throughput = Math.Min(throughput, gap);
                            }

                            // Gentle price-based facility throttle — only below 50% base price, sqrt curve
                            if (Goods.BasePrice[output] > 0f && !Goods.IsDurable[output] && !Goods.IsDurableInput[output])
                            {
                                float priceRatio = localPrices[output] / Goods.BasePrice[output];
                                if (priceRatio < 0.5f)
                                    throughput *= (float)Math.Sqrt(priceRatio * 2f);
                            }

                            // Consume all inputs proportionally
                            for (int ii = 0; ii < def.Inputs.Length; ii++)
                            {
                                float used = def.OutputAmount > 0f
                                    ? throughput / def.OutputAmount * def.Inputs[ii].Amount
                                    : 0f;
                                ce.Stock[(int)def.Inputs[ii].Good] -= used;
                            }
                            ce.Stock[output] += throughput;
                            ce.Production[output] += throughput;

                            fac.Throughput = throughput;
                            fac.Workforce = def.OutputAmount > 0f
                                ? throughput / def.OutputAmount
                                : 0f;
                            totalFacWorkers += fac.Workforce;
                            remainingFacLabor -= fac.Workforce;
                        }
                        }
                        ce.FacilityWorkers = totalFacWorkers;
                    }
                }
            }
        }
    }
}
