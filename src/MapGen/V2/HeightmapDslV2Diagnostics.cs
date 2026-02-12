using System.Collections.Generic;

namespace MapGen.Core
{
    public class HeightmapDslDiagnostics
    {
        readonly List<HeightmapDslOpMetrics> _operations = new List<HeightmapDslOpMetrics>();
        public IReadOnlyList<HeightmapDslOpMetrics> Operations => _operations;

        internal void Add(HeightmapDslOpMetrics metrics)
        {
            _operations.Add(metrics);
        }
    }

    public class HeightmapDslOpMetrics
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

    [System.Obsolete("Use HeightmapDslDiagnostics.")]
    public sealed class HeightmapDslV2Diagnostics : HeightmapDslDiagnostics
    {
    }

    [System.Obsolete("Use HeightmapDslOpMetrics.")]
    public sealed class HeightmapDslV2OpMetrics : HeightmapDslOpMetrics
    {
    }
}
