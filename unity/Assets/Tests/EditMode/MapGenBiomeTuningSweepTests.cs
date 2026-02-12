using System;
using System.IO;
using System.Text;
using MapGen.Core;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenTuning")]
    public class MapGenBiomeTuningSweepTests
    {
        [Test]
        public void SweepHighIslandBiomeProfile_EmitBestCandidate()
        {
            var config = new MapGenConfig
            {
                Seed = 3303,
                Template = HeightmapTemplateType.HighIsland,
                CellCount = 5000,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            MapGenComparisonCase baseline = MapGenComparison.Compare(config);
            int[] baselineBiome = baseline.Baseline.BiomeCounts;
            HeightmapTemplateTuningProfile defaultProfile = HeightmapTemplateCompiler.ResolveTuningProfile(
                config.Template,
                MapGenComparison.CreateConfig(config));
            if (defaultProfile == null)
                defaultProfile = new HeightmapTemplateTuningProfile();

            float[] slopeCandidates = { 0.30f, 0.35f, 0.45f };
            float[] alluvialFluxCandidates = { 0.35f, 0.45f, 0.55f };
            float[] alluvialMaxSlopeCandidates = { 2.20f, 2.50f };
            float[] wetlandFluxCandidates = { 0.60f, 0.80f, 1.00f };
            float[] wetlandMaxSlopeCandidates = { 1.60f, 1.80f };

            var csv = new StringBuilder();
            csv.AppendLine(
                "slopeScale,alluvialFluxScale,alluvialMaxSlopeScale,wetlandFluxScale,wetlandMaxSlopeScale,score,overlap,floodplainDelta,wetlandDelta,temperateForestDelta,coastalMarshDelta");

            SweepResult best = default;
            bool hasBest = false;

            for (int si = 0; si < slopeCandidates.Length; si++)
            {
                for (int fi = 0; fi < alluvialFluxCandidates.Length; fi++)
                {
                    for (int ai = 0; ai < alluvialMaxSlopeCandidates.Length; ai++)
                    {
                        for (int wi = 0; wi < wetlandFluxCandidates.Length; wi++)
                        {
                            for (int mi = 0; mi < wetlandMaxSlopeCandidates.Length; mi++)
                            {
                                HeightmapTemplateTuningProfile profile = defaultProfile.Clone();
                                profile.BiomeSlopeScale = slopeCandidates[si];
                                profile.BiomeAlluvialFluxThresholdScale = alluvialFluxCandidates[fi];
                                profile.BiomeAlluvialMaxSlopeScale = alluvialMaxSlopeCandidates[ai];
                                profile.BiomeWetlandFluxThresholdScale = wetlandFluxCandidates[wi];
                                profile.BiomeWetlandMaxSlopeScale = wetlandMaxSlopeCandidates[mi];

                                int[] candidateBiome = GenerateBiomeCounts(config, profile);
                                SweepResult candidate = ScoreCandidate(baselineBiome, candidateBiome, profile);

                                csv.AppendLine(
                                    $"{profile.BiomeSlopeScale:0.00},{profile.BiomeAlluvialFluxThresholdScale:0.00},{profile.BiomeAlluvialMaxSlopeScale:0.00},{profile.BiomeWetlandFluxThresholdScale:0.00},{profile.BiomeWetlandMaxSlopeScale:0.00},{candidate.Score:0.000},{candidate.Overlap:0.000},{candidate.FloodplainDelta:+#;-#;0},{candidate.WetlandDelta:+#;-#;0},{candidate.TemperateForestDelta:+#;-#;0},{candidate.CoastalMarshDelta:+#;-#;0}");

                                if (!hasBest || candidate.Score < best.Score)
                                {
                                    best = candidate;
                                    hasBest = true;
                                }
                            }
                        }
                    }
                }
            }

            Assert.That(hasBest, Is.True, "No biome sweep candidates were evaluated.");

            SweepResult current = ScoreCandidate(baselineBiome, GenerateBiomeCounts(config, defaultProfile), defaultProfile);

            var summary = new StringBuilder();
            summary.AppendLine("# MapGen HighIsland Biome Tuning Sweep");
            summary.AppendLine();
            summary.AppendLine("Objective score = 5*(1-overlap) + 2*|floodplain norm drift| + 2*|wetland norm drift| + 1.5*temperate-forest deficit + 1.0*|coastal-marsh norm drift|");
            summary.AppendLine();
            summary.AppendLine(
                $"Current profile: slope={current.Profile.BiomeSlopeScale:0.00}, alluvialFlux={current.Profile.BiomeAlluvialFluxThresholdScale:0.00}, alluvialMaxSlope={current.Profile.BiomeAlluvialMaxSlopeScale:0.00}, wetlandFlux={current.Profile.BiomeWetlandFluxThresholdScale:0.00}, wetlandMaxSlope={current.Profile.BiomeWetlandMaxSlopeScale:0.00}");
            summary.AppendLine(
                $"  overlap={current.Overlap:0.000}, floodplain={current.FloodplainDelta:+#;-#;0}, wetland={current.WetlandDelta:+#;-#;0}, temperateForest={current.TemperateForestDelta:+#;-#;0}, coastalMarsh={current.CoastalMarshDelta:+#;-#;0}, score={current.Score:0.000}");
            summary.AppendLine();
            summary.AppendLine(
                $"Best profile: slope={best.Profile.BiomeSlopeScale:0.00}, alluvialFlux={best.Profile.BiomeAlluvialFluxThresholdScale:0.00}, alluvialMaxSlope={best.Profile.BiomeAlluvialMaxSlopeScale:0.00}, wetlandFlux={best.Profile.BiomeWetlandFluxThresholdScale:0.00}, wetlandMaxSlope={best.Profile.BiomeWetlandMaxSlopeScale:0.00}");
            summary.AppendLine(
                $"  overlap={best.Overlap:0.000}, floodplain={best.FloodplainDelta:+#;-#;0}, wetland={best.WetlandDelta:+#;-#;0}, temperateForest={best.TemperateForestDelta:+#;-#;0}, coastalMarsh={best.CoastalMarshDelta:+#;-#;0}, score={best.Score:0.000}");

            string debugDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug"));
            Directory.CreateDirectory(debugDir);
            string txtPath = Path.Combine(debugDir, "mapgen_highisland_biome_tuning_sweep_summary.txt");
            string csvPath = Path.Combine(debugDir, "mapgen_highisland_biome_tuning_sweep_candidates.csv");
            File.WriteAllText(txtPath, summary.ToString());
            File.WriteAllText(csvPath, csv.ToString());

            TestContext.WriteLine($"HighIsland biome sweep summary: {txtPath}");
            TestContext.WriteLine($"HighIsland biome sweep candidates: {csvPath}");
            TestContext.WriteLine(summary.ToString());
        }

        static SweepResult ScoreCandidate(int[] baselineBiome, int[] candidateBiome, HeightmapTemplateTuningProfile profile)
        {
            float overlap = ComputeBiomeOverlap(baselineBiome, candidateBiome);
            int floodplainDelta = BiomeDelta(baselineBiome, candidateBiome, BiomeId.Floodplain);
            int wetlandDelta = BiomeDelta(baselineBiome, candidateBiome, BiomeId.Wetland);
            int temperateForestDelta = BiomeDelta(baselineBiome, candidateBiome, BiomeId.TemperateForest);
            int coastalMarshDelta = BiomeDelta(baselineBiome, candidateBiome, BiomeId.CoastalMarsh);

            int baselineFloodplain = Math.Max(1, CountForBiome(baselineBiome, BiomeId.Floodplain));
            int baselineWetland = Math.Max(1, CountForBiome(baselineBiome, BiomeId.Wetland));
            int baselineCoastalMarsh = Math.Max(1, CountForBiome(baselineBiome, BiomeId.CoastalMarsh));
            int baselineTemperateForest = Math.Max(1, CountForBiome(baselineBiome, BiomeId.TemperateForest));

            float floodplainNorm = Math.Abs(floodplainDelta) / (float)baselineFloodplain;
            float wetlandNorm = Math.Abs(wetlandDelta) / (float)baselineWetland;
            float coastalMarshNorm = Math.Abs(coastalMarshDelta) / (float)baselineCoastalMarsh;
            float temperateForestDeficit = Math.Max(0, -temperateForestDelta) / (float)baselineTemperateForest;

            float score =
                5f * (1f - overlap)
                + 2f * floodplainNorm
                + 2f * wetlandNorm
                + 1.5f * temperateForestDeficit
                + 1.0f * coastalMarshNorm;

            return new SweepResult
            {
                Profile = profile.Clone(),
                Score = score,
                Overlap = overlap,
                FloodplainDelta = floodplainDelta,
                WetlandDelta = wetlandDelta,
                TemperateForestDelta = temperateForestDelta,
                CoastalMarshDelta = coastalMarshDelta
            };
        }

        static int[] GenerateBiomeCounts(MapGenConfig config, HeightmapTemplateTuningProfile profile)
        {
            MapGenConfig v2Config = MapGenComparison.CreateConfig(config);
            v2Config.TemplateTuningOverride = profile;
            MapGenResult result = MapGenPipeline.Generate(v2Config);

            int biomeCount = Enum.GetValues(typeof(BiomeId)).Length;
            var counts = new int[biomeCount];
            for (int i = 0; i < result.Mesh.CellCount; i++)
            {
                bool includeBiome = result.Elevation.IsLand(i) || result.Biomes.IsLakeCell[i];
                if (!includeBiome)
                    continue;

                int biome = (int)result.Biomes.Biome[i];
                if (biome >= 0 && biome < counts.Length)
                    counts[biome]++;
            }

            return counts;
        }

        static float ComputeBiomeOverlap(int[] v1, int[] v2)
        {
            if (v1 == null || v2 == null || v1.Length == 0 || v2.Length == 0)
                return 0f;

            int len = Math.Min(v1.Length, v2.Length);
            float numer = 0f;
            float denom = 0f;
            for (int i = 0; i < len; i++)
            {
                numer += Mathf.Min(v1[i], v2[i]);
                denom += Mathf.Max(v1[i], v2[i]);
            }

            if (denom <= 1e-6f)
                return 0f;
            return numer / denom;
        }

        static int BiomeDelta(int[] v1, int[] v2, BiomeId biome)
        {
            if (v1 == null || v2 == null)
                return 0;
            int idx = (int)biome;
            if (idx < 0 || idx >= v1.Length || idx >= v2.Length)
                return 0;
            return v2[idx] - v1[idx];
        }

        static int CountForBiome(int[] counts, BiomeId biome)
        {
            if (counts == null)
                return 0;
            int idx = (int)biome;
            if (idx < 0 || idx >= counts.Length)
                return 0;
            return counts[idx];
        }

        struct SweepResult
        {
            public HeightmapTemplateTuningProfile Profile;
            public float Score;
            public float Overlap;
            public int FloodplainDelta;
            public int WetlandDelta;
            public int TemperateForestDelta;
            public int CoastalMarshDelta;
        }
    }
}
