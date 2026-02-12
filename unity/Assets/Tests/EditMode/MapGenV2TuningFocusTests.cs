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
    [Category("MapGenV2")]
    public class MapGenV2TuningFocusTests
    {
        static readonly FocusCase[] SmokeFocusCases =
        {
            new FocusCase(2202, HeightmapTemplateType.Continents, 5000),
            new FocusCase(1101, HeightmapTemplateType.LowIsland, 5000),
            new FocusCase(3303, HeightmapTemplateType.HighIsland, 5000),
            new FocusCase(4404, HeightmapTemplateType.Archipelago, 5000),
        };

        static readonly FocusCase[] TargetScaleFocusCases =
        {
            new FocusCase(2202, HeightmapTemplateType.Continents, 100000),
            new FocusCase(1101, HeightmapTemplateType.LowIsland, 100000),
            new FocusCase(3303, HeightmapTemplateType.HighIsland, 100000),
            new FocusCase(4404, HeightmapTemplateType.Archipelago, 100000),
        };

        [Test]
        public void FocusTemplates_V2VsV1_DriftReport_Smoke5k()
        {
            RunFocusDrift(
                SmokeFocusCases,
                reportFileName: "mapgen_v2_tuning_focus_report_smoke_5k.txt",
                enforceGuardrails: false);
        }

        [Test]
        [Explicit("Representative target-scale focus report for retuning (100k cells).")]
        [Category("MapGenV2TuningOffline")]
        public void FocusTemplates_V2VsV1_DriftReport_Target100k()
        {
            RunFocusDrift(
                TargetScaleFocusCases,
                reportFileName: "mapgen_v2_tuning_focus_report_target_100k.txt",
                enforceGuardrails: false);
        }

        [Test]
        [Explicit("Enable after retuning to enforce target-scale drift guardrails.")]
        [Category("MapGenV2TuningOffline")]
        public void FocusTemplates_V2VsV1_DriftStaysWithinTuningBands_Target100k()
        {
            RunFocusDrift(
                TargetScaleFocusCases,
                reportFileName: "mapgen_v2_tuning_focus_report_target_100k_guardrails.txt",
                enforceGuardrails: true);
        }

        static void RunFocusDrift(FocusCase[] focusCases, string reportFileName, bool enforceGuardrails)
        {
            var configs = new List<MapGenConfig>(focusCases.Length);
            for (int i = 0; i < focusCases.Length; i++)
            {
                FocusCase c = focusCases[i];
                configs.Add(new MapGenConfig
                {
                    Seed = c.Seed,
                    Template = c.Template,
                    CellCount = c.CellCount,
                    RiverThreshold = 180f,
                    RiverTraceThreshold = 10f,
                    MinRiverVertices = 8
                });
            }

            List<MapGenComparisonCase> results = MapGenComparison.CompareMatrix(configs);
            string report = MapGenComparison.BuildReport(results);
            string debugDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug"));
            Directory.CreateDirectory(debugDir);
            string reportPath = Path.Combine(debugDir, reportFileName);
            File.WriteAllText(reportPath, report);
            TestContext.WriteLine($"MapGen V2 tuning report written: {reportPath}");
            TestContext.WriteLine(report);

            var failures = new List<string>();
            for (int i = 0; i < results.Count; i++)
            {
                MapGenComparisonCase c = results[i];
                float deltaLand = Math.Abs(c.V2.LandRatio - c.V1.LandRatio);
                float deltaEdgeLand = Math.Abs(c.V2.EdgeLandRatio - c.V1.EdgeLandRatio);
                float deltaCoast = Math.Abs(c.V2.CoastRatio - c.V1.CoastRatio);
                int deltaRealmCount = Math.Abs(c.V2.RealmCount - c.V1.RealmCount);
                int deltaProvinceCount = Math.Abs(c.V2.ProvinceCount - c.V1.ProvinceCount);
                int deltaCountyCount = Math.Abs(c.V2.CountyCount - c.V1.CountyCount);
                float biomeOverlap = ComputeBiomeOverlap(c.V1.BiomeCounts, c.V2.BiomeCounts);
                int maxAbsBiomeDrift = ComputeMaxAbsBiomeDrift(c.V1.BiomeCounts, c.V2.BiomeCounts);
                int floodplainDrift = Math.Abs(BiomeDelta(c.V1.BiomeCounts, c.V2.BiomeCounts, BiomeId.Floodplain));
                int wetlandDrift = Math.Abs(BiomeDelta(c.V1.BiomeCounts, c.V2.BiomeCounts, BiomeId.Wetland));
                int temperateForestDrift = Math.Abs(BiomeDelta(c.V1.BiomeCounts, c.V2.BiomeCounts, BiomeId.TemperateForest));
                int mountainShrubDrift = Math.Abs(BiomeDelta(c.V1.BiomeCounts, c.V2.BiomeCounts, BiomeId.MountainShrub));
                int coastalMarshDrift = Math.Abs(BiomeDelta(c.V1.BiomeCounts, c.V2.BiomeCounts, BiomeId.CoastalMarsh));

                // Focus guardrails (post-retuning): keep V2 tightly aligned with V1 for visual tuning templates.
                if (deltaLand > 0.10f)
                    failures.Add($"{c.Template} seed={c.Seed}: |land ratio drift|={deltaLand:0.000} > 0.100");
                if (deltaEdgeLand > 0.10f)
                    failures.Add($"{c.Template} seed={c.Seed}: |edge land drift|={deltaEdgeLand:0.000} > 0.100");
                if (deltaCoast > 0.08f)
                    failures.Add($"{c.Template} seed={c.Seed}: |coast drift|={deltaCoast:0.000} > 0.080");
                if (deltaRealmCount > 2)
                    failures.Add($"{c.Template} seed={c.Seed}: |realm count drift|={deltaRealmCount} > 2");
                if (deltaProvinceCount > 2)
                    failures.Add($"{c.Template} seed={c.Seed}: |province count drift|={deltaProvinceCount} > 2");
                if (deltaCountyCount > 8)
                    failures.Add($"{c.Template} seed={c.Seed}: |county count drift|={deltaCountyCount} > 8");

                // Hard biome guardrails by template.
                switch (c.Template)
                {
                    case HeightmapTemplateType.Continents:
                        if (biomeOverlap < 0.58f)
                            failures.Add($"{c.Template} seed={c.Seed}: biome overlap={biomeOverlap:0.000} < 0.580");
                        if (floodplainDrift > 240)
                            failures.Add($"{c.Template} seed={c.Seed}: |Floodplain drift|={floodplainDrift} > 240");
                        if (mountainShrubDrift > 180)
                            failures.Add($"{c.Template} seed={c.Seed}: |MountainShrub drift|={mountainShrubDrift} > 180");
                        break;
                    case HeightmapTemplateType.LowIsland:
                        if (biomeOverlap < 0.54f)
                            failures.Add($"{c.Template} seed={c.Seed}: biome overlap={biomeOverlap:0.000} < 0.540");
                        if (coastalMarshDrift > 260)
                            failures.Add($"{c.Template} seed={c.Seed}: |CoastalMarsh drift|={coastalMarshDrift} > 260");
                        break;
                    case HeightmapTemplateType.HighIsland:
                        if (biomeOverlap < 0.46f)
                            failures.Add($"{c.Template} seed={c.Seed}: biome overlap={biomeOverlap:0.000} < 0.460");
                        if (floodplainDrift > 220)
                            failures.Add($"{c.Template} seed={c.Seed}: |Floodplain drift|={floodplainDrift} > 220");
                        if (wetlandDrift > 340)
                            failures.Add($"{c.Template} seed={c.Seed}: |Wetland drift|={wetlandDrift} > 340");
                        if (temperateForestDrift > 180)
                            failures.Add($"{c.Template} seed={c.Seed}: |TemperateForest drift|={temperateForestDrift} > 180");
                        break;
                    case HeightmapTemplateType.Archipelago:
                        if (biomeOverlap < 0.55f)
                            failures.Add($"{c.Template} seed={c.Seed}: biome overlap={biomeOverlap:0.000} < 0.550");
                        if (coastalMarshDrift > 220)
                            failures.Add($"{c.Template} seed={c.Seed}: |CoastalMarsh drift|={coastalMarshDrift} > 220");
                        break;
                }

                if (maxAbsBiomeDrift > 520)
                    failures.Add($"{c.Template} seed={c.Seed}: |max biome drift|={maxAbsBiomeDrift} > 520");
            }

            if (!enforceGuardrails)
            {
                if (failures.Count > 0)
                {
                    TestContext.WriteLine(
                        $"Guardrail violations recorded (report-only mode):{Environment.NewLine}- {string.Join(Environment.NewLine + "- ", failures)}");
                }

                return;
            }

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Focused V2 tuning drift exceeded guardrails:");
                for (int i = 0; i < failures.Count; i++)
                    sb.AppendLine($"- {failures[i]}");
                sb.AppendLine();
                sb.Append(report);
                Assert.Fail(sb.ToString());
            }
        }

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

        static int ComputeMaxAbsBiomeDrift(int[] v1, int[] v2)
        {
            if (v1 == null || v2 == null || v1.Length == 0 || v2.Length == 0)
                return 0;

            int len = Math.Min(v1.Length, v2.Length);
            int maxAbs = 0;
            for (int i = 0; i < len; i++)
            {
                int abs = Math.Abs(v2[i] - v1[i]);
                if (abs > maxAbs)
                    maxAbs = abs;
            }

            return maxAbs;
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
    }
}
