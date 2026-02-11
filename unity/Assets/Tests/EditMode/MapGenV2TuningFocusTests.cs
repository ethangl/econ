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
        readonly FocusCase[] _focusCases =
        {
            new FocusCase(2202, HeightmapTemplateType.Continents, 5000),
            new FocusCase(1101, HeightmapTemplateType.LowIsland, 5000),
            new FocusCase(3303, HeightmapTemplateType.HighIsland, 5000),
            new FocusCase(4404, HeightmapTemplateType.Archipelago, 5000),
        };

        [Test]
        public void FocusTemplates_V2VsV1_DriftStaysWithinTuningBands()
        {
            var configs = new List<MapGenConfig>(_focusCases.Length);
            for (int i = 0; i < _focusCases.Length; i++)
            {
                FocusCase c = _focusCases[i];
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
            string reportPath = Path.Combine(debugDir, "mapgen_v2_tuning_focus_report.txt");
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

                // Focus guardrails (post-tuning): keep V2 tightly aligned with V1 for visual tuning templates.
                if (deltaLand > 0.10f)
                    failures.Add($"{c.Template} seed={c.Seed}: |land ratio drift|={deltaLand:0.000} > 0.100");
                if (deltaEdgeLand > 0.10f)
                    failures.Add($"{c.Template} seed={c.Seed}: |edge land drift|={deltaEdgeLand:0.000} > 0.100");
                if (deltaCoast > 0.08f)
                    failures.Add($"{c.Template} seed={c.Seed}: |coast drift|={deltaCoast:0.000} > 0.080");
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
    }
}
