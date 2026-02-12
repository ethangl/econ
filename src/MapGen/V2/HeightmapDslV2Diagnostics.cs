using System.Collections.Generic;

namespace MapGen.Core
{
    public sealed class HeightmapDslV2Diagnostics
    {
        readonly List<HeightmapDslV2OpMetrics> _operations = new List<HeightmapDslV2OpMetrics>();
        public IReadOnlyList<HeightmapDslV2OpMetrics> Operations => _operations;

        internal void Add(HeightmapDslV2OpMetrics metrics)
        {
            _operations.Add(metrics);
        }
    }

    public sealed class HeightmapDslV2OpMetrics
    {
        public int LineNumber;
        public string Operation;
        public string RawLine;

        public float BeforeLandRatio;
        public float AfterLandRatio;
        public float BeforeEdgeLandRatio;
        public float AfterEdgeLandRatio;

        public float ChangedCellRatio;
        public float MeanAbsDeltaMeters;
        public float MaxRaiseMeters;
        public float MaxLowerMeters;

        public int PlacementCount;
        public float? RequestedXMinPercent;
        public float? RequestedXMaxPercent;
        public float? RequestedYMinPercent;
        public float? RequestedYMaxPercent;
        public float? SeedXMinPercent;
        public float? SeedXMaxPercent;
        public float? SeedYMinPercent;
        public float? SeedYMaxPercent;
        public float? EndXMinPercent;
        public float? EndXMaxPercent;
        public float? EndYMinPercent;
        public float? EndYMaxPercent;
    }
}
