using System;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2TemplateMetricsTests
    {
        [Test]
        public void TemplatePort_EmitsMeterAnnotatedScripts()
        {
            var config = new MapGenConfig();

            foreach (HeightmapTemplateType template in Enum.GetValues(typeof(HeightmapTemplateType)))
            {
                string script = HeightmapTemplatesV2.GetTemplate(template, config);
                Assert.That(string.IsNullOrWhiteSpace(script), Is.False, $"Template script missing: {template}");

                string[] lines = script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0)
                        continue;

                    string op = parts[0].ToLowerInvariant();
                    if ((op == "hill" || op == "pit" || op == "range" || op == "trough") && parts.Length >= 3)
                        Assert.That(parts[2].ToLowerInvariant().Contains("m"), Is.True, $"{template} line missing meter magnitude: {line}");

                    if (op == "add" && parts.Length >= 2)
                        Assert.That(parts[1].ToLowerInvariant().Contains("m"), Is.True, $"{template} add line missing meter delta: {line}");

                    if ((op == "add" || op == "multiply") && parts.Length >= 3)
                    {
                        string selector = parts[2].ToLowerInvariant();
                        bool keyword = selector == "land" || selector == "water" || selector == "all";
                        if (!keyword)
                            Assert.That(selector.Contains("m"), Is.True, $"{template} range selector missing meter range: {line}");
                    }
                }
            }
        }

        [Test]
        public void PipelineV2_TemplateMetrics_StayWithinBroadBands()
        {
            foreach (HeightmapTemplateType template in Enum.GetValues(typeof(HeightmapTemplateType)))
            {
                var config = new MapGenConfig
                {
                    Seed = 1337,
                    CellCount = 4000,
                    Template = template,
                    MaxElevationMeters = 5000f,
                    MaxSeaDepthMeters = 1250f
                };

                MapGenResult result = MapGenPipeline.Generate(config);
                Assert.That(result, Is.Not.Null);
                Assert.That(result.Mesh, Is.Not.Null);
                Assert.That(result.Elevation, Is.Not.Null);

                var (landMin, landMax) = HeightmapTemplatesV2.GetLandRatioBand(template);
                float landRatio = result.Elevation.LandRatio();
                Assert.That(landRatio, Is.GreaterThanOrEqualTo(landMin).And.LessThanOrEqualTo(landMax),
                    $"Land ratio out of broad band for {template}: {landRatio:0.000} expected [{landMin:0.000}, {landMax:0.000}]");

                float p10 = Percentile(result.Elevation.ElevationMetersSigned, 0.10f);
                float p50 = Percentile(result.Elevation.ElevationMetersSigned, 0.50f);
                float p90 = Percentile(result.Elevation.ElevationMetersSigned, 0.90f);

                Assert.That(float.IsNaN(p10) || float.IsInfinity(p10), Is.False, $"Invalid p10 for {template}");
                Assert.That(float.IsNaN(p50) || float.IsInfinity(p50), Is.False, $"Invalid p50 for {template}");
                Assert.That(float.IsNaN(p90) || float.IsInfinity(p90), Is.False, $"Invalid p90 for {template}");
                Assert.That(p10, Is.LessThan(p50), $"Expected p10 < p50 for {template}");
                Assert.That(p50, Is.LessThan(p90), $"Expected p50 < p90 for {template}");
                Assert.That(p10, Is.LessThan(0f), $"Expected underwater p10 for {template}");
                Assert.That(p90, Is.GreaterThan(0f), $"Expected above-sea p90 for {template}");

                float coastRatio = ComputeCoastRatio(result.Mesh, result.Elevation);
                Assert.That(coastRatio, Is.GreaterThan(0.01f).And.LessThan(0.70f),
                    $"Coastline complexity outside broad band for {template}: {coastRatio:0.000}");
            }
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

        static float ComputeCoastRatio(CellMesh mesh, ElevationField elevation)
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
                bool land0 = elevation.IsLand(c0);
                bool land1 = elevation.IsLand(c1);
                if (land0 != land1)
                    coastEdges++;
            }

            if (candidateEdges == 0)
                return 0f;

            return (float)coastEdges / candidateEdges;
        }
    }
}
