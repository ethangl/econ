using System;
using System.Collections.Generic;
using System.Text;

namespace MapGen.Core
{
    public struct MapGenComparisonMetrics
    {
        public float LandRatio;
        public float WaterRatio;
        public float EdgeLandRatio;
        public float CoastRatio;
        public float ElevationP10Meters;
        public float ElevationP50Meters;
        public float ElevationP90Meters;

        public int RiverCount;
        public float RiverCoverage;

        public int RealmCount;
        public int ProvinceCount;
        public int CountyCount;

        public int[] BiomeCounts;
    }

    public sealed class MapGenComparisonCase
    {
        public int Seed;
        public HeightmapTemplateType Template;
        public int CellCount;
        public MapGenComparisonMetrics V1;
        public MapGenComparisonMetrics V2;
    }

    /// <summary>
    /// Side-by-side V1/V2 comparison runner and report generator.
    /// </summary>
    public static class MapGenComparison
    {
        public static MapGenV2Config CreateV2Config(MapGenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new MapGenV2Config
            {
                Seed = config.Seed,
                CellCount = config.CellCount,
                AspectRatio = config.AspectRatio,
                CellSizeKm = config.CellSizeKm,
                Template = config.Template,
                LatitudeSouth = config.LatitudeSouth,
                RiverThreshold = config.RiverThreshold,
                RiverTraceThreshold = config.RiverTraceThreshold,
                MinRiverVertices = config.MinRiverVertices
            };
        }

        public static MapGenComparisonCase Compare(MapGenConfig config)
        {
            return Compare(config, null);
        }

        public static MapGenComparisonCase Compare(MapGenConfig config, HeightmapTemplateTuningProfile tuningOverride)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            MapGenResult v1 = MapGenPipeline.Generate(config);
            MapGenV2Config v2Config = CreateV2Config(config);
            v2Config.TemplateTuningOverride = tuningOverride;
            MapGenV2Result v2 = MapGenPipelineV2.Generate(v2Config);

            return new MapGenComparisonCase
            {
                Seed = config.Seed,
                Template = config.Template,
                CellCount = config.CellCount,
                V1 = ComputeMetrics(v1),
                V2 = ComputeMetrics(v2)
            };
        }

        public static List<MapGenComparisonCase> CompareMatrix(IEnumerable<MapGenConfig> configs)
        {
            if (configs == null) throw new ArgumentNullException(nameof(configs));

            var results = new List<MapGenComparisonCase>();
            foreach (MapGenConfig config in configs)
                results.Add(Compare(config));

            return results;
        }

        public static string BuildReport(IReadOnlyList<MapGenComparisonCase> cases)
        {
            if (cases == null) throw new ArgumentNullException(nameof(cases));

            var sb = new StringBuilder();
            sb.AppendLine("# MapGen V1 vs V2 Comparison");
            sb.AppendLine();
            sb.AppendLine("Columns: land/water ratio, elevation percentiles (signed meters), river metrics, biome coverage, political counts.");
            sb.AppendLine();

            for (int i = 0; i < cases.Count; i++)
            {
                MapGenComparisonCase c = cases[i];
                sb.AppendLine($"Case {i + 1}: seed={c.Seed}, template={c.Template}, requestedCells={c.CellCount}");
                sb.AppendLine($"  V1 land={Fmt(c.V1.LandRatio)} water={Fmt(c.V1.WaterRatio)} edgeLand={Fmt(c.V1.EdgeLandRatio)} coast={Fmt(c.V1.CoastRatio)} elev[p10,p50,p90]=[{Fmt(c.V1.ElevationP10Meters)}, {Fmt(c.V1.ElevationP50Meters)}, {Fmt(c.V1.ElevationP90Meters)}] m");
                sb.AppendLine($"  V2 land={Fmt(c.V2.LandRatio)} water={Fmt(c.V2.WaterRatio)} edgeLand={Fmt(c.V2.EdgeLandRatio)} coast={Fmt(c.V2.CoastRatio)} elev[p10,p50,p90]=[{Fmt(c.V2.ElevationP10Meters)}, {Fmt(c.V2.ElevationP50Meters)}, {Fmt(c.V2.ElevationP90Meters)}] m");
                sb.AppendLine($"  Delta land={Fmt(c.V2.LandRatio - c.V1.LandRatio)} edgeLand={Fmt(c.V2.EdgeLandRatio - c.V1.EdgeLandRatio)} coast={Fmt(c.V2.CoastRatio - c.V1.CoastRatio)} riverCount={c.V2.RiverCount - c.V1.RiverCount} riverCoverage={Fmt(c.V2.RiverCoverage - c.V1.RiverCoverage)}");
                sb.AppendLine($"  V1 rivers={c.V1.RiverCount}, coverage={Fmt(c.V1.RiverCoverage)} realms/provinces/counties={c.V1.RealmCount}/{c.V1.ProvinceCount}/{c.V1.CountyCount}");
                sb.AppendLine($"  V2 rivers={c.V2.RiverCount}, coverage={Fmt(c.V2.RiverCoverage)} realms/provinces/counties={c.V2.RealmCount}/{c.V2.ProvinceCount}/{c.V2.CountyCount}");
                sb.AppendLine($"  Biome overlap={Fmt(BiomeOverlap(c.V1.BiomeCounts, c.V2.BiomeCounts))}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        static MapGenComparisonMetrics ComputeMetrics(MapGenResult result)
        {
            int n = result.Mesh.CellCount;
            int land = 0;
            int edgeCells = 0;
            int edgeLand = 0;
            var signed = new float[n];
            int biomeCount = Enum.GetValues(typeof(BiomeId)).Length;
            var biomeCounts = new int[biomeCount];
            float edgeMarginX = result.Mesh.Width * 0.12f;
            float edgeMarginY = result.Mesh.Height * 0.12f;

            for (int i = 0; i < n; i++)
            {
                bool isLand = !result.Heights.IsWater(i) && !result.Biomes.IsLakeCell[i];
                if (isLand) land++;

                Vec2 center = result.Mesh.CellCenters[i];
                bool isEdge = center.X <= edgeMarginX || center.X >= result.Mesh.Width - edgeMarginX
                    || center.Y <= edgeMarginY || center.Y >= result.Mesh.Height - edgeMarginY;
                if (isEdge)
                {
                    edgeCells++;
                    if (isLand) edgeLand++;
                }

                signed[i] = LegacyAbsoluteToSignedMeters(result.Heights.Heights[i], result.World);

                int biome = (int)result.Biomes.Biome[i];
                if (biome >= 0 && biome < biomeCounts.Length)
                    biomeCounts[biome]++;
            }

            int riverVertices = 0;
            for (int i = 0; i < result.Rivers.Rivers.Length; i++)
                riverVertices += result.Rivers.Rivers[i].Vertices.Length;

            float landRatio = n > 0 ? land / (float)n : 0f;
            float edgeLandRatio = edgeCells > 0 ? edgeLand / (float)edgeCells : 0f;
            float coastRatio = ComputeCoastRatio(result.Mesh, c => !result.Heights.IsWater(c) && !result.Biomes.IsLakeCell[c]);

            return new MapGenComparisonMetrics
            {
                LandRatio = landRatio,
                WaterRatio = 1f - landRatio,
                EdgeLandRatio = edgeLandRatio,
                CoastRatio = coastRatio,
                ElevationP10Meters = Percentile(signed, 0.10f),
                ElevationP50Meters = Percentile(signed, 0.50f),
                ElevationP90Meters = Percentile(signed, 0.90f),
                RiverCount = result.Rivers.Rivers.Length,
                RiverCoverage = result.Mesh.VertexCount > 0 ? riverVertices / (float)result.Mesh.VertexCount : 0f,
                RealmCount = result.Political.RealmCount,
                ProvinceCount = result.Political.ProvinceCount,
                CountyCount = result.Political.CountyCount,
                BiomeCounts = biomeCounts
            };
        }

        static MapGenComparisonMetrics ComputeMetrics(MapGenV2Result result)
        {
            int n = result.Mesh.CellCount;
            int land = 0;
            int edgeCells = 0;
            int edgeLand = 0;
            var signed = new float[n];
            int biomeCount = Enum.GetValues(typeof(BiomeId)).Length;
            var biomeCounts = new int[biomeCount];
            float edgeMarginX = result.Mesh.Width * 0.12f;
            float edgeMarginY = result.Mesh.Height * 0.12f;

            for (int i = 0; i < n; i++)
            {
                bool isLand = result.Elevation.IsLand(i) && !result.Biomes.IsLakeCell[i];
                if (isLand) land++;

                Vec2 center = result.Mesh.CellCenters[i];
                bool isEdge = center.X <= edgeMarginX || center.X >= result.Mesh.Width - edgeMarginX
                    || center.Y <= edgeMarginY || center.Y >= result.Mesh.Height - edgeMarginY;
                if (isEdge)
                {
                    edgeCells++;
                    if (isLand) edgeLand++;
                }

                signed[i] = result.Elevation.ElevationMetersSigned[i];

                int biome = (int)result.Biomes.Biome[i];
                if (biome >= 0 && biome < biomeCounts.Length)
                    biomeCounts[biome]++;
            }

            int riverVertices = 0;
            for (int i = 0; i < result.Rivers.Rivers.Length; i++)
                riverVertices += result.Rivers.Rivers[i].Vertices.Length;

            float landRatio = n > 0 ? land / (float)n : 0f;
            float edgeLandRatio = edgeCells > 0 ? edgeLand / (float)edgeCells : 0f;
            float coastRatio = ComputeCoastRatio(result.Mesh, c => result.Elevation.IsLand(c) && !result.Biomes.IsLakeCell[c]);

            return new MapGenComparisonMetrics
            {
                LandRatio = landRatio,
                WaterRatio = 1f - landRatio,
                EdgeLandRatio = edgeLandRatio,
                CoastRatio = coastRatio,
                ElevationP10Meters = Percentile(signed, 0.10f),
                ElevationP50Meters = Percentile(signed, 0.50f),
                ElevationP90Meters = Percentile(signed, 0.90f),
                RiverCount = result.Rivers.Rivers.Length,
                RiverCoverage = result.Mesh.VertexCount > 0 ? riverVertices / (float)result.Mesh.VertexCount : 0f,
                RealmCount = result.Political.RealmCount,
                ProvinceCount = result.Political.ProvinceCount,
                CountyCount = result.Political.CountyCount,
                BiomeCounts = biomeCounts
            };
        }

        static float LegacyAbsoluteToSignedMeters(float absoluteHeight, WorldMetadata world)
        {
            float sea = world.SeaLevelHeight;
            if (absoluteHeight >= sea)
            {
                float landRange = Math.Max(1f, 100f - sea);
                return ((absoluteHeight - sea) / landRange) * world.MaxElevationMeters;
            }

            float waterRange = Math.Max(1f, sea - 0f);
            return -((sea - absoluteHeight) / waterRange) * world.MaxSeaDepthMeters;
        }

        static float ComputeCoastRatio(CellMesh mesh, Func<int, bool> isLand)
        {
            int candidateEdges = 0;
            int coastEdges = 0;
            for (int i = 0; i < mesh.EdgeCount; i++)
            {
                var edge = mesh.EdgeCells[i];
                int c0 = edge.C0;
                int c1 = edge.C1;
                if (c0 < 0 || c1 < 0)
                    continue;

                candidateEdges++;
                if (isLand(c0) != isLand(c1))
                    coastEdges++;
            }

            if (candidateEdges == 0)
                return 0f;

            return coastEdges / (float)candidateEdges;
        }

        static float Percentile(float[] values, float q)
        {
            if (values == null || values.Length == 0)
                return 0f;

            var sorted = (float[])values.Clone();
            Array.Sort(sorted);

            if (q <= 0f) return sorted[0];
            if (q >= 1f) return sorted[sorted.Length - 1];

            float index = q * (sorted.Length - 1);
            int lo = (int)Math.Floor(index);
            int hi = (int)Math.Ceiling(index);
            if (lo == hi)
                return sorted[lo];

            float t = index - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }

        static float BiomeOverlap(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0)
                return 0f;

            int len = Math.Min(a.Length, b.Length);
            float numer = 0f;
            float denom = 0f;
            for (int i = 0; i < len; i++)
            {
                numer += Math.Min(a[i], b[i]);
                denom += Math.Max(a[i], b[i]);
            }

            if (denom <= 1e-6f)
                return 0f;
            return numer / denom;
        }

        static string Fmt(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "nan";
            return value.ToString("0.000");
        }
    }
}
