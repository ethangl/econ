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
    public class MapGenV2RiverTuningSweepTests
    {
        static readonly float[] FullThresholdCandidates = { 0.35f, 0.50f, 0.75f, 0.90f, 1.00f, 1.10f, 1.25f };
        static readonly float[] FullTraceCandidates = { 0.20f, 0.35f, 0.50f, 0.65f, 0.75f, 0.90f, 1.00f };
        static readonly float[] FullMinVerticesCandidates = { 0.40f, 0.55f, 0.75f, 0.90f, 1.00f, 1.10f };

        static readonly float[] SmokeThresholdCandidates = { 0.75f, 0.95f, 1.15f };
        static readonly float[] SmokeTraceCandidates = { 0.30f, 0.60f, 0.90f };
        static readonly float[] SmokeMinVerticesCandidates = { 0.75f, 1.00f, 1.20f };

        readonly FocusCase[] _focusCases =
        {
            new FocusCase(2202, HeightmapTemplateType.Continents, 5000),
            new FocusCase(1101, HeightmapTemplateType.LowIsland, 5000),
            new FocusCase(3303, HeightmapTemplateType.HighIsland, 5000),
            new FocusCase(4404, HeightmapTemplateType.Archipelago, 5000),
        };

        [Test]
        public void SweepFocusedTemplates_RiverDriftProfiles_Smoke()
        {
            RunSweep(
                SmokeThresholdCandidates,
                SmokeTraceCandidates,
                SmokeMinVerticesCandidates,
                defaultVsBestTolerance: -1f,
                summaryName: "mapgen_v2_focus_river_tuning_sweep_summary.txt",
                csvName: "mapgen_v2_focus_river_tuning_sweep_candidates.csv");
        }

        [Test]
        [Explicit("Long-running offline river sweep for manual retuning sessions.")]
        [Category("MapGenV2TuningOffline")]
        public void SweepFocusedTemplates_RiverDriftProfiles_OfflineFull()
        {
            RunSweep(
                FullThresholdCandidates,
                FullTraceCandidates,
                FullMinVerticesCandidates,
                defaultVsBestTolerance: 0.03f,
                summaryName: "mapgen_v2_focus_river_tuning_sweep_summary_full.txt",
                csvName: "mapgen_v2_focus_river_tuning_sweep_candidates_full.csv");
        }

        void RunSweep(
            float[] thresholdCandidates,
            float[] traceCandidates,
            float[] minVerticesCandidates,
            float defaultVsBestTolerance,
            string summaryName,
            string csvName)
        {
            var summary = new StringBuilder();
            var failures = new List<string>();
            summary.AppendLine("# V2 Focused River Tuning Sweep");
            summary.AppendLine();
            summary.AppendLine("Objective score = 6*|riverCount drift|/max(1,V1 count) + 4*|riverCoverage drift|");
            summary.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("template,seed,riverThresholdScale,riverTraceScale,riverMinVerticesScale,effectiveMinVertices,score,deltaRiverCount,deltaRiverCountNorm,deltaRiverCoverage,v1RiverCount,v2RiverCount,v1RiverCoverage,v2RiverCoverage");

            for (int i = 0; i < _focusCases.Length; i++)
            {
                FocusCase focus = _focusCases[i];
                SweepResult best = SweepTemplate(
                    focus,
                    csv,
                    thresholdCandidates,
                    traceCandidates,
                    minVerticesCandidates);

                summary.AppendLine($"{focus.Template} seed={focus.Seed}");
                summary.AppendLine(
                    $"  Best profile: riverThresholdScale={best.Profile.RiverThresholdScale:0.00}, " +
                    $"riverTraceScale={best.Profile.RiverTraceThresholdScale:0.00}, " +
                    $"riverMinVerticesScale={best.Profile.RiverMinVerticesScale:0.00} " +
                    $"(effectiveMinVertices={best.EffectiveMinVertices})");
                summary.AppendLine($"  Drift: riverCount={best.DeltaRiverCount:+#;-#;0} ({best.DeltaRiverCountNorm:0.000} normalized), riverCoverage={best.DeltaRiverCoverage:+0.000;-0.000;0.000}, score={best.Score:0.000}");
                summary.AppendLine($"  Default profile drift: riverCount={best.DefaultDeltaRiverCount:+#;-#;0} ({best.DefaultDeltaRiverCountNorm:0.000} normalized), riverCoverage={best.DefaultDeltaRiverCoverage:+0.000;-0.000;0.000}, score={best.DefaultScore:0.000}");
                summary.AppendLine();

                if (defaultVsBestTolerance >= 0f &&
                    best.DefaultScore > best.Score + defaultVsBestTolerance)
                {
                    failures.Add(
                        $"{focus.Template}: built-in river profile is not near best-candidate score. " +
                        $"best={best.Score:0.000}, default={best.DefaultScore:0.000}");
                }
            }

            string debugDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug"));
            Directory.CreateDirectory(debugDir);
            string txtPath = Path.Combine(debugDir, summaryName);
            string csvPath = Path.Combine(debugDir, csvName);
            File.WriteAllText(txtPath, summary.ToString());
            File.WriteAllText(csvPath, csv.ToString());

            TestContext.WriteLine($"River sweep summary: {txtPath}");
            TestContext.WriteLine($"River sweep candidates: {csvPath}");
            TestContext.WriteLine(summary.ToString());

            Assert.That(
                failures,
                Is.Empty,
                string.Join(Environment.NewLine, failures));
        }

        SweepResult SweepTemplate(
            FocusCase focus,
            StringBuilder csv,
            float[] thresholdCandidates,
            float[] traceCandidates,
            float[] minVerticesCandidates)
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

            MapGenComparisonCase baseline = MapGenComparison.Compare(config);
            HeightmapTemplateTuningProfile baseProfile = HeightmapTemplatesV2.ResolveTuningProfile(
                focus.Template,
                MapGenComparison.CreateV2Config(config));
            if (baseProfile == null)
                baseProfile = new HeightmapTemplateTuningProfile();

            SweepResult best = default;
            bool hasBest = false;

            for (int ti = 0; ti < thresholdCandidates.Length; ti++)
            {
                for (int ri = 0; ri < traceCandidates.Length; ri++)
                {
                    for (int mi = 0; mi < minVerticesCandidates.Length; mi++)
                    {
                        HeightmapTemplateTuningProfile profile = baseProfile.Clone();
                        profile.RiverThresholdScale = thresholdCandidates[ti];
                        profile.RiverTraceThresholdScale = traceCandidates[ri];
                        profile.RiverMinVerticesScale = minVerticesCandidates[mi];

                        MapGenComparisonCase result;
                        try
                        {
                            result = MapGenComparison.Compare(config, profile);
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail(
                                $"River sweep candidate crashed for {focus.Template} seed={focus.Seed} " +
                                $"thresholdScale={profile.RiverThresholdScale:0.00} traceScale={profile.RiverTraceThresholdScale:0.00} minVerticesScale={profile.RiverMinVerticesScale:0.00}: {ex}");
                            return default;
                        }

                        int deltaCount = result.V2.RiverCount - result.V1.RiverCount;
                        float deltaCountNorm = Math.Abs(deltaCount) / Math.Max(1f, result.V1.RiverCount);
                        float deltaCoverage = result.V2.RiverCoverage - result.V1.RiverCoverage;
                        float score = Score(deltaCountNorm, deltaCoverage);

                        int effectiveMinVertices = Math.Max(
                            1,
                            (int)Math.Round(config.MinRiverVertices * profile.RiverMinVerticesScale, MidpointRounding.AwayFromZero));

                        csv.AppendLine(
                            $"{focus.Template},{focus.Seed},{profile.RiverThresholdScale:0.00},{profile.RiverTraceThresholdScale:0.00},{profile.RiverMinVerticesScale:0.00},{effectiveMinVertices},{score:0.000},{deltaCount},{deltaCountNorm:0.000},{deltaCoverage:0.000},{result.V1.RiverCount},{result.V2.RiverCount},{result.V1.RiverCoverage:0.000},{result.V2.RiverCoverage:0.000}");

                        if (!hasBest || score < best.Score)
                        {
                            best = new SweepResult
                            {
                                Profile = profile.Clone(),
                                EffectiveMinVertices = effectiveMinVertices,
                                Score = score,
                                DeltaRiverCount = deltaCount,
                                DeltaRiverCountNorm = deltaCountNorm,
                                DeltaRiverCoverage = deltaCoverage
                            };
                            hasBest = true;
                        }
                    }
                }
            }

            Assert.That(hasBest, Is.True, $"No river sweep candidate was evaluated for {focus.Template}");
            int defaultDeltaCount = baseline.V2.RiverCount - baseline.V1.RiverCount;
            float defaultDeltaCountNorm = Math.Abs(defaultDeltaCount) / Math.Max(1f, baseline.V1.RiverCount);
            float defaultDeltaCoverage = baseline.V2.RiverCoverage - baseline.V1.RiverCoverage;
            best.DefaultDeltaRiverCount = defaultDeltaCount;
            best.DefaultDeltaRiverCountNorm = defaultDeltaCountNorm;
            best.DefaultDeltaRiverCoverage = defaultDeltaCoverage;
            best.DefaultScore = Score(defaultDeltaCountNorm, defaultDeltaCoverage);
            return best;
        }

        static float Score(float deltaRiverCountNorm, float deltaRiverCoverage) =>
            6f * deltaRiverCountNorm + 4f * Math.Abs(deltaRiverCoverage);

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
            public int EffectiveMinVertices;
            public float Score;
            public int DeltaRiverCount;
            public float DeltaRiverCountNorm;
            public float DeltaRiverCoverage;
            public float DefaultScore;
            public int DefaultDeltaRiverCount;
            public float DefaultDeltaRiverCountNorm;
            public float DefaultDeltaRiverCoverage;
        }
    }
}
