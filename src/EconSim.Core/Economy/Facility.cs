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

        /// <summary>
        /// Number of merged facility units represented by this runtime actor.
        /// </summary>
        public int UnitCount = 1;

        /// <summary>Local input inventory (materials waiting to be processed).</summary>
        public Stockpile InputBuffer;

        /// <summary>Local output inventory (finished goods waiting for transport).</summary>
        public Stockpile OutputBuffer;

        /// <summary>Whether this facility is currently operating.</summary>
        public bool IsActive;

        /// <summary>Liquid funds available to this facility.</summary>
        public float Treasury;

        /// <summary>Current wage paid per worker per day.</summary>
        public float WageRate;

        /// <summary>Daily revenue ring buffer (7-day).</summary>
        public float[] DailyRevenue;

        /// <summary>Daily input spend ring buffer (7-day).</summary>
        public float[] DailyInputCost;

        /// <summary>Daily wage spend ring buffer (7-day).</summary>
        public float[] DailyWageBill;

        /// <summary>Consecutive days of negative rolling profit.</summary>
        public int ConsecutiveLossDays;

        /// <summary>Grace period before loss-based shutdown can trigger.</summary>
        public int GraceDaysRemaining;

        /// <summary>Consecutive days wages were underpaid.</summary>
        public int WageDebtDays;
        /// <summary>Absolute simulation day metrics were last initialized for.</summary>
        public int MetricsDay = int.MinValue;

        public Facility()
        {
            InputBuffer = new Stockpile();
            OutputBuffer = new Stockpile();
            IsActive = true;
            DailyRevenue = new float[7];
            DailyInputCost = new float[7];
            DailyWageBill = new float[7];
        }

        /// <summary>
        /// Bind facility stockpiles to the active good registry for dense runtime-ID paths.
        /// </summary>
        public void BindGoods(GoodRegistry goods)
        {
            InputBuffer?.BindGoods(goods);
            OutputBuffer?.BindGoods(goods);
        }

        /// <summary>
        /// Calculate current efficiency based on staffing.
        /// Formula: (workers / required)^α where α < 1 gives diminishing returns.
        /// </summary>
        public float GetEfficiency(FacilityDef def, float alpha = 0.7f)
        {
            int requiredLabor = GetRequiredLabor(def);
            if (requiredLabor <= 0) return 1f;
            if (AssignedWorkers <= 0) return 0f;

            float ratio = (float)AssignedWorkers / requiredLabor;
            if (ratio >= 1f) return 1f;

            return (float)Math.Pow(ratio, alpha);
        }

        /// <summary>
        /// Calculate total labor required at full staffing for this clustered facility.
        /// </summary>
        public int GetRequiredLabor(FacilityDef def)
        {
            if (def == null || def.LaborRequired <= 0)
                return 0;

            long total = (long)def.LaborRequired * Math.Max(1, UnitCount);
            if (total > int.MaxValue)
                return int.MaxValue;
            return (int)total;
        }

        /// <summary>
        /// Calculate nominal throughput at full staffing for this clustered facility.
        /// </summary>
        public float GetNominalThroughput(FacilityDef def)
        {
            if (def == null || def.BaseThroughput <= 0f)
                return 0f;

            return def.BaseThroughput * Math.Max(1, UnitCount);
        }

        /// <summary>
        /// Calculate actual throughput this tick.
        /// </summary>
        public float GetThroughput(FacilityDef def)
        {
            if (!IsActive) return 0f;
            return GetNominalThroughput(def) * GetEfficiency(def);
        }

        public void ClearDayMetrics(int dayIndex)
        {
            int idx = NormalizeDayIndex(dayIndex);
            DailyRevenue[idx] = 0f;
            DailyInputCost[idx] = 0f;
            DailyWageBill[idx] = 0f;
        }

        public void BeginDayMetrics(int currentDay)
        {
            if (MetricsDay == currentDay)
                return;

            int idx = NormalizeDayIndex(currentDay);
            DailyRevenue[idx] = 0f;
            DailyInputCost[idx] = 0f;
            DailyWageBill[idx] = 0f;
            MetricsDay = currentDay;
        }

        public void AddRevenueForDay(int dayIndex, float amount)
        {
            if (amount <= 0f) return;
            DailyRevenue[NormalizeDayIndex(dayIndex)] += amount;
        }

        public void AddInputCostForDay(int dayIndex, float amount)
        {
            if (amount <= 0f) return;
            DailyInputCost[NormalizeDayIndex(dayIndex)] += amount;
        }

        public void AddWageBillForDay(int dayIndex, float amount)
        {
            if (amount <= 0f) return;
            DailyWageBill[NormalizeDayIndex(dayIndex)] += amount;
        }

        public float RollingAvgRevenue => Average(DailyRevenue);
        public float RollingAvgInputCost => Average(DailyInputCost);
        public float RollingAvgWageBill => Average(DailyWageBill);
        public float RollingProfit => RollingAvgRevenue - RollingAvgInputCost - RollingAvgWageBill;

        private static float Average(float[] values)
        {
            if (values == null || values.Length == 0)
                return 0f;

            float sum = 0f;
            for (int i = 0; i < values.Length; i++)
                sum += values[i];
            return sum / values.Length;
        }

        private static int NormalizeDayIndex(int dayIndex)
        {
            const int BufferSize = 7;
            int idx = dayIndex % BufferSize;
            if (idx < 0) idx += BufferSize;
            return idx;
        }
    }
}
