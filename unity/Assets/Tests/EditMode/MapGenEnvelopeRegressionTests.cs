using System;
using MapGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGen")]
    public class MapGenEnvelopeRegressionTests
    {
        readonly EnvelopeCase[] _cases =
        {
            new EnvelopeCase(1101, HeightmapTemplateType.LowIsland, 5000),
            new EnvelopeCase(2202, HeightmapTemplateType.Continents, 5000),
            new EnvelopeCase(3303, HeightmapTemplateType.Volcano, 5000),
        };

        [Test]
        public void IncreasingSeaDepthEnvelope_To5000_KeepsTopologyAndDistributionNearBaseline()
        {
            foreach (EnvelopeCase c in _cases)
            {
                Metrics baseline = RunMetrics(c.Template, c.Seed, c.CellCount, maxSeaDepthMeters: 1250f);
                Metrics candidate = RunMetrics(c.Template, c.Seed, c.CellCount, maxSeaDepthMeters: 5000f);

                string label = $"{c.Template} seed={c.Seed} cells={c.CellCount}";
                Assert.That(Math.Abs(candidate.LandRatio - baseline.LandRatio), Is.LessThanOrEqualTo(0.02f),
                    $"{label}: land ratio drift too high.");
                Assert.That(Math.Abs(candidate.CoastRatio - baseline.CoastRatio), Is.LessThanOrEqualTo(0.02f),
                    $"{label}: coast ratio drift too high.");
                Assert.That(Math.Abs(candidate.RiverCount - baseline.RiverCount), Is.LessThanOrEqualTo(20),
                    $"{label}: river count drift too high.");
                Assert.That(Math.Abs(candidate.P50 - baseline.P50), Is.LessThanOrEqualTo(200f),
                    $"{label}: p50 signed elevation drift too high.");
            }
        }

        static Metrics RunMetrics(HeightmapTemplateType template, int seed, int cellCount, float maxSeaDepthMeters)
        {
            var config = new MapGenConfig
            {
                Seed = seed,
                CellCount = cellCount,
                Template = template,
                MaxElevationMeters = 5000f,
                MaxSeaDepthMeters = maxSeaDepthMeters,
                TerrainShapeReferenceSpanMeters = 6250f,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            MapGenResult result = MapGenPipeline.Generate(config);
            float landRatio = result.Elevation.LandRatio();
            float coastRatio = ComputeCoastRatio(result.Mesh, result.Elevation);
            int riverCount = result.Rivers.Rivers.Length;
            float p50 = Percentile(result.Elevation.ElevationMetersSigned, 0.50f);
            return new Metrics(landRatio, coastRatio, riverCount, p50);
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

        readonly struct EnvelopeCase
        {
            public readonly int Seed;
            public readonly HeightmapTemplateType Template;
            public readonly int CellCount;

            public EnvelopeCase(int seed, HeightmapTemplateType template, int cellCount)
            {
                Seed = seed;
                Template = template;
                CellCount = cellCount;
            }
        }

        readonly struct Metrics
        {
            public readonly float LandRatio;
            public readonly float CoastRatio;
            public readonly int RiverCount;
            public readonly float P50;

            public Metrics(float landRatio, float coastRatio, int riverCount, float p50)
            {
                LandRatio = landRatio;
                CoastRatio = coastRatio;
                RiverCount = riverCount;
                P50 = p50;
            }
        }
    }
}
