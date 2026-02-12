using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MapGen.Core;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2Tuning")]
    public class MapGenV2TemplateTuningSweepTests
    {
        readonly FocusCase[] _focusCases =
        {
            new FocusCase(2202, HeightmapTemplateType.Continents, 100000),
            new FocusCase(1101, HeightmapTemplateType.LowIsland, 100000),
            new FocusCase(3303, HeightmapTemplateType.HighIsland, 100000),
            new FocusCase(4404, HeightmapTemplateType.Archipelago, 100000),
        };

        [Test]
        [Explicit("Offline 100k tuning sweep. Not for regular/CI runs.")]
        [Category("MapGenV2TuningOffline")]
        public void SweepFocusedTemplates_EmitBestProfiles()
        {
            var summary = new StringBuilder();
            summary.AppendLine("# V2 Focused Template Tuning Sweep");
            summary.AppendLine();
            summary.AppendLine("Objective score = 5*|edgeLand drift| + 3*|land drift| + 2*|coast drift|");
            summary.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("template,seed,terrainScale,maskScale,addScale,landMultiplyScale,score,deltaLand,deltaEdgeLand,deltaCoast,v1Land,v2Land,v1Edge,v2Edge,v1Coast,v2Coast");

            for (int i = 0; i < _focusCases.Length; i++)
            {
                FocusCase focus = _focusCases[i];
                SweepResult best = SweepTemplate(focus, csv);

                summary.AppendLine($"{focus.Template} seed={focus.Seed}");
                summary.AppendLine($"  Best profile: terrain={best.Profile.TerrainMagnitudeScale:0.00}, mask={best.Profile.MaskScale:0.00}, add={best.Profile.AddMagnitudeScale:0.00}, landMultiply={best.Profile.LandMultiplyFactorScale:0.00}");
                summary.AppendLine($"  Drift: land={best.DeltaLand:+0.000;-0.000;0.000}, edgeLand={best.DeltaEdgeLand:+0.000;-0.000;0.000}, coast={best.DeltaCoast:+0.000;-0.000;0.000}, score={best.Score:0.000}");
                summary.AppendLine($"  Default profile drift: land={best.DefaultDeltaLand:+0.000;-0.000;0.000}, edgeLand={best.DefaultDeltaEdgeLand:+0.000;-0.000;0.000}, coast={best.DefaultDeltaCoast:+0.000;-0.000;0.000}, score={best.DefaultScore:0.000}");
                summary.AppendLine();

                Assert.That(best.DefaultScore, Is.LessThanOrEqualTo(best.Score + 0.02f),
                    $"{focus.Template}: built-in tuned profile is not near best-candidate score.");
            }

            string debugDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug"));
            Directory.CreateDirectory(debugDir);
            string txtPath = Path.Combine(debugDir, "mapgen_v2_focus_tuning_sweep_summary_100k.txt");
            string csvPath = Path.Combine(debugDir, "mapgen_v2_focus_tuning_sweep_candidates_100k.csv");
            File.WriteAllText(txtPath, summary.ToString());
            File.WriteAllText(csvPath, csv.ToString());

            TestContext.WriteLine($"Focus sweep summary: {txtPath}");
            TestContext.WriteLine($"Focus sweep candidates: {csvPath}");
            TestContext.WriteLine(summary.ToString());
        }

        SweepResult SweepTemplate(FocusCase focus, StringBuilder csv)
        {
            var config = new MapGenConfig
            {
                Seed = focus.Seed,
                Template = focus.Template,
                CellCount = focus.CellCount,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            MapGenResult baseline = MapGenPipeline.Generate(config);
            var baselineMetrics = Metrics.FromV1(baseline);
            bool hasAdd = TemplateHasAdd(focus.Template);
            bool hasLandMultiply = TemplateHasLandMultiply(focus.Template);

            float[] terrainCandidates = { 0.95f, 1.00f, 1.05f };
            float[] maskCandidates = { 0.90f, 1.00f, 1.20f };
            float[] addCandidates = hasAdd ? new[] { 0.90f, 1.00f, 1.10f } : new[] { 1.00f };
            float[] landMultiplyCandidates = hasLandMultiply ? new[] { 0.90f, 1.00f, 1.10f } : new[] { 1.00f };

            SweepResult best = default;
            bool hasBest = false;

            for (int ti = 0; ti < terrainCandidates.Length; ti++)
            {
                for (int mi = 0; mi < maskCandidates.Length; mi++)
                {
                    for (int ai = 0; ai < addCandidates.Length; ai++)
                    {
                        for (int li = 0; li < landMultiplyCandidates.Length; li++)
                        {
                            var profile = new HeightmapTemplateTuningProfile
                            {
                                TerrainMagnitudeScale = terrainCandidates[ti],
                                MaskScale = maskCandidates[mi],
                                AddMagnitudeScale = addCandidates[ai],
                                LandMultiplyFactorScale = landMultiplyCandidates[li]
                            };

                            MapGenV2Result v2;
                            try
                            {
                                MapGenV2Config v2Config = MapGenComparison.CreateV2Config(config);
                                v2Config.TemplateTuningOverride = profile;
                                v2 = MapGenPipelineV2.Generate(v2Config);
                            }
                            catch (Exception ex)
                            {
                                Assert.Fail(
                                    $"Sweep candidate crashed for {focus.Template} seed={focus.Seed} " +
                                    $"terrain={profile.TerrainMagnitudeScale:0.00} mask={profile.MaskScale:0.00} " +
                                    $"add={profile.AddMagnitudeScale:0.00} landMul={profile.LandMultiplyFactorScale:0.00}: {ex}");
                                return default;
                            }

                            var v2Metrics = Metrics.FromV2(v2);

                            float deltaLand = v2Metrics.LandRatio - baselineMetrics.LandRatio;
                            float deltaEdge = v2Metrics.EdgeLandRatio - baselineMetrics.EdgeLandRatio;
                            float deltaCoast = v2Metrics.CoastRatio - baselineMetrics.CoastRatio;
                            float score = Score(deltaLand, deltaEdge, deltaCoast);

                            csv.AppendLine(
                                $"{focus.Template},{focus.Seed},{profile.TerrainMagnitudeScale:0.00},{profile.MaskScale:0.00},{profile.AddMagnitudeScale:0.00},{profile.LandMultiplyFactorScale:0.00},{score:0.000},{deltaLand:0.000},{deltaEdge:0.000},{deltaCoast:0.000},{baselineMetrics.LandRatio:0.000},{v2Metrics.LandRatio:0.000},{baselineMetrics.EdgeLandRatio:0.000},{v2Metrics.EdgeLandRatio:0.000},{baselineMetrics.CoastRatio:0.000},{v2Metrics.CoastRatio:0.000}");

                            if (!hasBest || score < best.Score)
                            {
                                best = new SweepResult
                                {
                                    Profile = profile.Clone(),
                                    Score = score,
                                    DeltaLand = deltaLand,
                                    DeltaEdgeLand = deltaEdge,
                                    DeltaCoast = deltaCoast
                                };
                                hasBest = true;
                            }
                        }
                    }
                }
            }

            Assert.That(hasBest, Is.True, $"No sweep candidate was evaluated for {focus.Template}");

            MapGenV2Config defaultConfig = MapGenComparison.CreateV2Config(config);
            MapGenV2Result defaultV2 = MapGenPipelineV2.Generate(defaultConfig);
            var defaultMetrics = Metrics.FromV2(defaultV2);
            best.DefaultDeltaLand = defaultMetrics.LandRatio - baselineMetrics.LandRatio;
            best.DefaultDeltaEdgeLand = defaultMetrics.EdgeLandRatio - baselineMetrics.EdgeLandRatio;
            best.DefaultDeltaCoast = defaultMetrics.CoastRatio - baselineMetrics.CoastRatio;
            best.DefaultScore = Score(best.DefaultDeltaLand, best.DefaultDeltaEdgeLand, best.DefaultDeltaCoast);
            return best;
        }

        static bool TemplateHasAdd(HeightmapTemplateType template) =>
            template == HeightmapTemplateType.HighIsland
            || template == HeightmapTemplateType.Archipelago;

        static bool TemplateHasLandMultiply(HeightmapTemplateType template) =>
            template == HeightmapTemplateType.Continents
            || template == HeightmapTemplateType.LowIsland
            || template == HeightmapTemplateType.HighIsland;

        static float Score(float deltaLand, float deltaEdge, float deltaCoast) =>
            5f * Math.Abs(deltaEdge)
            + 3f * Math.Abs(deltaLand)
            + 2f * Math.Abs(deltaCoast);

        readonly struct FocusCase
        {
            public readonly int Seed;
            public readonly HeightmapTemplateType Template;
            public readonly int CellCount;

            public FocusCase(int seed, HeightmapTemplateType template, int cellCount)
            {
                Seed = seed;
                Template = template;
                CellCount = cellCount;
            }
        }

        struct SweepResult
        {
            public HeightmapTemplateTuningProfile Profile;
            public float Score;
            public float DeltaLand;
            public float DeltaEdgeLand;
            public float DeltaCoast;
            public float DefaultScore;
            public float DefaultDeltaLand;
            public float DefaultDeltaEdgeLand;
            public float DefaultDeltaCoast;
        }

        struct Metrics
        {
            public float LandRatio;
            public float EdgeLandRatio;
            public float CoastRatio;

            public static Metrics FromV1(MapGenResult result)
            {
                int n = result.Mesh.CellCount;
                int land = 0;
                int edgeCells = 0;
                int edgeLand = 0;
                float edgeMarginX = result.Mesh.Width * 0.12f;
                float edgeMarginY = result.Mesh.Height * 0.12f;

                for (int i = 0; i < n; i++)
                {
                    bool isLand = !result.Heights.IsWater(i) && !result.Biomes.IsLakeCell[i];
                    if (isLand)
                        land++;

                    Vec2 center = result.Mesh.CellCenters[i];
                    bool isEdge = center.X <= edgeMarginX || center.X >= result.Mesh.Width - edgeMarginX
                        || center.Y <= edgeMarginY || center.Y >= result.Mesh.Height - edgeMarginY;
                    if (!isEdge)
                        continue;

                    edgeCells++;
                    if (isLand)
                        edgeLand++;
                }

                return new Metrics
                {
                    LandRatio = n > 0 ? land / (float)n : 0f,
                    EdgeLandRatio = edgeCells > 0 ? edgeLand / (float)edgeCells : 0f,
                    CoastRatio = ComputeCoastRatio(result.Mesh, c => !result.Heights.IsWater(c) && !result.Biomes.IsLakeCell[c])
                };
            }

            public static Metrics FromV2(MapGenV2Result result)
            {
                int n = result.Mesh.CellCount;
                int land = 0;
                int edgeCells = 0;
                int edgeLand = 0;
                float edgeMarginX = result.Mesh.Width * 0.12f;
                float edgeMarginY = result.Mesh.Height * 0.12f;

                for (int i = 0; i < n; i++)
                {
                    bool isLand = result.Elevation.IsLand(i) && !result.Biomes.IsLakeCell[i];
                    if (isLand)
                        land++;

                    Vec2 center = result.Mesh.CellCenters[i];
                    bool isEdge = center.X <= edgeMarginX || center.X >= result.Mesh.Width - edgeMarginX
                        || center.Y <= edgeMarginY || center.Y >= result.Mesh.Height - edgeMarginY;
                    if (!isEdge)
                        continue;

                    edgeCells++;
                    if (isLand)
                        edgeLand++;
                }

                return new Metrics
                {
                    LandRatio = n > 0 ? land / (float)n : 0f,
                    EdgeLandRatio = edgeCells > 0 ? edgeLand / (float)edgeCells : 0f,
                    CoastRatio = ComputeCoastRatio(result.Mesh, c => result.Elevation.IsLand(c) && !result.Biomes.IsLakeCell[c])
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
        }
    }
}
