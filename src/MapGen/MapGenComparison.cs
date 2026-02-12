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
        public MapGenComparisonMetrics Baseline;
        public MapGenComparisonMetrics Candidate;

    }

    /// <summary>
    /// Side-by-side baseline/candidate comparison runner and report generator.
    /// </summary>
    public static class MapGenComparison
    {
        public static MapGenConfig CreateConfig(MapGenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return new MapGenConfig
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

            MapGenResult baseline = MapGenPipeline.Generate(config);
            MapGenConfig candidateConfig = CreateConfig(config);
            candidateConfig.TemplateTuningOverride = tuningOverride;
            MapGenResult candidate = MapGenPipeline.Generate(candidateConfig);

            return new MapGenComparisonCase
            {
                Seed = config.Seed,
                Template = config.Template,
                CellCount = config.CellCount,
                Baseline = ComputeMetrics(baseline),
                Candidate = ComputeMetrics(candidate)
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
            sb.AppendLine("# MapGen Baseline vs Candidate Comparison");
            sb.AppendLine();
            sb.AppendLine("Columns: land/water ratio, elevation percentiles (signed meters), river metrics, biome coverage (land+lake cells), political counts.");
            sb.AppendLine();

            for (int i = 0; i < cases.Count; i++)
            {
                MapGenComparisonCase c = cases[i];
                sb.AppendLine($"Case {i + 1}: seed={c.Seed}, template={c.Template}, requestedCells={c.CellCount}");
                sb.AppendLine($"  Baseline land={Fmt(c.Baseline.LandRatio)} water={Fmt(c.Baseline.WaterRatio)} edgeLand={Fmt(c.Baseline.EdgeLandRatio)} coast={Fmt(c.Baseline.CoastRatio)} elev[p10,p50,p90]=[{Fmt(c.Baseline.ElevationP10Meters)}, {Fmt(c.Baseline.ElevationP50Meters)}, {Fmt(c.Baseline.ElevationP90Meters)}] m");
                sb.AppendLine($"  Candidate land={Fmt(c.Candidate.LandRatio)} water={Fmt(c.Candidate.WaterRatio)} edgeLand={Fmt(c.Candidate.EdgeLandRatio)} coast={Fmt(c.Candidate.CoastRatio)} elev[p10,p50,p90]=[{Fmt(c.Candidate.ElevationP10Meters)}, {Fmt(c.Candidate.ElevationP50Meters)}, {Fmt(c.Candidate.ElevationP90Meters)}] m");
                sb.AppendLine($"  Delta land={Fmt(c.Candidate.LandRatio - c.Baseline.LandRatio)} edgeLand={Fmt(c.Candidate.EdgeLandRatio - c.Baseline.EdgeLandRatio)} coast={Fmt(c.Candidate.CoastRatio - c.Baseline.CoastRatio)} riverCount={c.Candidate.RiverCount - c.Baseline.RiverCount} riverCoverage={Fmt(c.Candidate.RiverCoverage - c.Baseline.RiverCoverage)}");
                sb.AppendLine($"  Baseline rivers={c.Baseline.RiverCount}, coverage={Fmt(c.Baseline.RiverCoverage)} realms/provinces/counties={c.Baseline.RealmCount}/{c.Baseline.ProvinceCount}/{c.Baseline.CountyCount}");
                sb.AppendLine($"  Candidate rivers={c.Candidate.RiverCount}, coverage={Fmt(c.Candidate.RiverCoverage)} realms/provinces/counties={c.Candidate.RealmCount}/{c.Candidate.ProvinceCount}/{c.Candidate.CountyCount}");
                sb.AppendLine($"  Biome overlap={Fmt(BiomeOverlap(c.Baseline.BiomeCounts, c.Candidate.BiomeCounts))}");
                sb.AppendLine($"  Biome drift(top)={BuildBiomeDriftSummary(c.Baseline.BiomeCounts, c.Candidate.BiomeCounts, 4)}");
                sb.AppendLine($"  Biome mix(top)={BuildBiomeMixSummary(c.Baseline.BiomeCounts, c.Candidate.BiomeCounts, 6)}");
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

                bool includeBiome = result.Elevation.IsLand(i) || result.Biomes.IsLakeCell[i];
                if (includeBiome)
                {
                    int biome = (int)result.Biomes.Biome[i];
                    if (biome >= 0 && biome < biomeCounts.Length)
                        biomeCounts[biome]++;
                }
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

        static string BuildBiomeDriftSummary(int[] v1, int[] v2, int topCount)
        {
            if (v1 == null || v2 == null || topCount <= 0)
                return "none";

            int len = Math.Min(v1.Length, v2.Length);
            if (len == 0)
                return "none";

            int[] idx = new int[len];
            for (int i = 0; i < len; i++)
                idx[i] = i;

            Array.Sort(idx, (a, b) =>
            {
                int da = Math.Abs(v2[a] - v1[a]);
                int db = Math.Abs(v2[b] - v1[b]);
                return db.CompareTo(da);
            });

            int take = Math.Min(topCount, len);
            var sb = new StringBuilder();
            bool wrote = false;
            for (int i = 0; i < take; i++)
            {
                int biomeIdx = idx[i];
                int delta = v2[biomeIdx] - v1[biomeIdx];
                if (delta == 0)
                    continue;

                if (wrote)
                    sb.Append(", ");

                string name = (biomeIdx >= 0 && biomeIdx <= byte.MaxValue)
                    ? ((BiomeId)(byte)biomeIdx).ToString()
                    : biomeIdx.ToString();
                sb.Append(name);
                sb.Append('=');
                if (delta > 0)
                    sb.Append('+');
                sb.Append(delta);
                wrote = true;
            }

            return wrote ? sb.ToString() : "none";
        }

        static string BuildBiomeMixSummary(int[] v1, int[] v2, int topCount)
        {
            if (v1 == null || v2 == null || topCount <= 0)
                return "none";

            int len = Math.Min(v1.Length, v2.Length);
            if (len == 0)
                return "none";

            int totalV1 = 0;
            int totalV2 = 0;
            for (int i = 0; i < len; i++)
            {
                totalV1 += Math.Max(0, v1[i]);
                totalV2 += Math.Max(0, v2[i]);
            }

            int[] idx = new int[len];
            for (int i = 0; i < len; i++)
                idx[i] = i;

            Array.Sort(idx, (a, b) =>
            {
                int ma = Math.Max(v1[a], v2[a]);
                int mb = Math.Max(v1[b], v2[b]);
                return mb.CompareTo(ma);
            });

            int take = Math.Min(topCount, len);
            var sb = new StringBuilder();
            bool wrote = false;
            for (int i = 0; i < take; i++)
            {
                int biomeIdx = idx[i];
                int c1 = v1[biomeIdx];
                int c2 = v2[biomeIdx];
                if (c1 == 0 && c2 == 0)
                    continue;

                if (wrote)
                    sb.Append("; ");

                string name = (biomeIdx >= 0 && biomeIdx <= byte.MaxValue)
                    ? ((BiomeId)(byte)biomeIdx).ToString()
                    : biomeIdx.ToString();
                float p1 = totalV1 > 0 ? c1 / (float)totalV1 : 0f;
                float p2 = totalV2 > 0 ? c2 / (float)totalV2 : 0f;
                sb.Append(name);
                sb.Append(' ');
                sb.Append(c1);
                sb.Append('(');
                sb.Append(FmtPct(p1));
                sb.Append(")->");
                sb.Append(c2);
                sb.Append('(');
                sb.Append(FmtPct(p2));
                sb.Append(')');
                wrote = true;
            }

            return wrote ? sb.ToString() : "none";
        }

        static string Fmt(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "nan";
            return value.ToString("0.000");
        }

        static string FmtPct(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return "nan";
            return (value * 100f).ToString("0.0") + "%";
        }
    }
}
