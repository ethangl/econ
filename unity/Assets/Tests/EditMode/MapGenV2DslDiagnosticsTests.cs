using System;
using System.IO;
using System.Text;
using MapGen.Core;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class MapGenV2DslDiagnosticsTests
    {
        [Test]
        public void PlacementOps_StayInsideRequestedDslBounds()
        {
            ElevationFieldV2 field = CreateField(seed: 501, cellCount: 2500, aspectRatio: 16f / 9f);
            for (int i = 0; i < field.CellCount; i++)
                field[i] = field.MaxElevationMeters * 0.95f;

            string script = @"
hill 6 500m 20-22 70-72
pit 6 300m 30-32 40-42
range 6 700m 25-35 45-55
trough 6 400m 60-65 35-45
";

            var diagnostics = new HeightmapDslV2Diagnostics();
            HeightmapDslV2.Execute(field, script, seed: 1234, diagnostics);

            HeightmapDslV2OpMetrics hill = FindOp(diagnostics, "hill");
            HeightmapDslV2OpMetrics pit = FindOp(diagnostics, "pit");
            HeightmapDslV2OpMetrics range = FindOp(diagnostics, "range");
            HeightmapDslV2OpMetrics trough = FindOp(diagnostics, "trough");

            AssertPlacementWithinRequested(hill, "hill");
            AssertPlacementWithinRequested(pit, "pit");
            AssertPlacementWithinRequested(range, "range");
            AssertPlacementWithinRequested(trough, "trough");

            AssertEndpointWithinRequested(range, "range");
            AssertEndpointWithinRequested(trough, "trough");
        }

        [Test]
        public void Diagnostics_EmitPerOperationImpactMetrics()
        {
            var config = new MapGenV2Config
            {
                Seed = 2202,
                CellCount = 4000,
                Template = HeightmapTemplateType.Continents
            };

            ElevationFieldV2 field = CreateField(config.MeshSeed, config.CellCount, config.AspectRatio, config.CellSizeKm, config.MaxSeaDepthMeters, config.MaxElevationMeters);
            string script = HeightmapTemplatesV2.GetTemplate(config.Template, config);
            var diagnostics = new HeightmapDslV2Diagnostics();
            HeightmapDslV2.Execute(field, script, config.ElevationSeed, diagnostics);

            Assert.That(diagnostics.Operations.Count, Is.GreaterThan(0), "Expected at least one recorded DSL operation.");

            bool sawChangingOp = false;
            bool sawHillOp = false;
            var report = new StringBuilder();
            report.AppendLine("# DSL V2 Operation Diagnostics");

            for (int i = 0; i < diagnostics.Operations.Count; i++)
            {
                HeightmapDslV2OpMetrics m = diagnostics.Operations[i];
                AssertFinite(m.BeforeLandRatio, $"{m.Operation} before-land");
                AssertFinite(m.AfterLandRatio, $"{m.Operation} after-land");
                AssertFinite(m.BeforeEdgeLandRatio, $"{m.Operation} before-edge");
                AssertFinite(m.AfterEdgeLandRatio, $"{m.Operation} after-edge");
                AssertFinite(m.ChangedCellRatio, $"{m.Operation} changed ratio");
                AssertFinite(m.MeanAbsDeltaMeters, $"{m.Operation} mean abs delta");
                AssertFinite(m.MaxRaiseMeters, $"{m.Operation} max raise");
                AssertFinite(m.MaxLowerMeters, $"{m.Operation} max lower");

                if (m.ChangedCellRatio > 0f)
                    sawChangingOp = true;

                if (string.Equals(m.Operation, "hill", StringComparison.Ordinal))
                    sawHillOp = true;

                report.AppendLine(
                    $"{m.LineNumber:00} {m.Operation,-8} placements={m.PlacementCount,3} " +
                    $"land={m.BeforeLandRatio:0.000}->{m.AfterLandRatio:0.000} " +
                    $"edge={m.BeforeEdgeLandRatio:0.000}->{m.AfterEdgeLandRatio:0.000} " +
                    $"chg={m.ChangedCellRatio:0.000} meanAbs={m.MeanAbsDeltaMeters:0.0}m " +
                    $"raise={m.MaxRaiseMeters:0.0}m lower={m.MaxLowerMeters:0.0}m");
            }

            Assert.That(sawChangingOp, Is.True, "Expected at least one operation to modify elevation.");
            Assert.That(sawHillOp, Is.True, "Expected at least one hill operation in template diagnostics.");

            string debugDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "debug"));
            Directory.CreateDirectory(debugDir);
            string reportPath = Path.Combine(debugDir, "mapgen_v2_dsl_op_diagnostics.txt");
            File.WriteAllText(reportPath, report.ToString());
            TestContext.WriteLine($"DSL diagnostics report written: {reportPath}");
        }

        static void AssertPlacementWithinRequested(HeightmapDslV2OpMetrics m, string label)
        {
            Assert.That(m.PlacementCount, Is.GreaterThan(0), $"{label}: expected at least one placement.");
            Assert.That(m.RequestedXMinPercent.HasValue && m.RequestedXMaxPercent.HasValue, Is.True, $"{label}: missing requested x-range.");
            Assert.That(m.RequestedYMinPercent.HasValue && m.RequestedYMaxPercent.HasValue, Is.True, $"{label}: missing requested y-range.");
            Assert.That(m.SeedXMinPercent.HasValue && m.SeedXMaxPercent.HasValue, Is.True, $"{label}: missing accepted seed x-range.");
            Assert.That(m.SeedYMinPercent.HasValue && m.SeedYMaxPercent.HasValue, Is.True, $"{label}: missing accepted seed y-range.");

            const float eps = 0.001f;
            Assert.That(m.SeedXMinPercent.Value, Is.GreaterThanOrEqualTo(m.RequestedXMinPercent.Value - eps), $"{label}: seed x-min escaped requested range.");
            Assert.That(m.SeedXMaxPercent.Value, Is.LessThanOrEqualTo(m.RequestedXMaxPercent.Value + eps), $"{label}: seed x-max escaped requested range.");
            Assert.That(m.SeedYMinPercent.Value, Is.GreaterThanOrEqualTo(m.RequestedYMinPercent.Value - eps), $"{label}: seed y-min escaped requested range.");
            Assert.That(m.SeedYMaxPercent.Value, Is.LessThanOrEqualTo(m.RequestedYMaxPercent.Value + eps), $"{label}: seed y-max escaped requested range.");
        }

        static void AssertEndpointWithinRequested(HeightmapDslV2OpMetrics m, string label)
        {
            Assert.That(m.EndXMinPercent.HasValue && m.EndXMaxPercent.HasValue, Is.True, $"{label}: missing accepted endpoint x-range.");
            Assert.That(m.EndYMinPercent.HasValue && m.EndYMaxPercent.HasValue, Is.True, $"{label}: missing accepted endpoint y-range.");

            const float eps = 0.001f;
            Assert.That(m.EndXMinPercent.Value, Is.GreaterThanOrEqualTo(m.RequestedXMinPercent.Value - eps), $"{label}: endpoint x-min escaped requested range.");
            Assert.That(m.EndXMaxPercent.Value, Is.LessThanOrEqualTo(m.RequestedXMaxPercent.Value + eps), $"{label}: endpoint x-max escaped requested range.");
            Assert.That(m.EndYMinPercent.Value, Is.GreaterThanOrEqualTo(m.RequestedYMinPercent.Value - eps), $"{label}: endpoint y-min escaped requested range.");
            Assert.That(m.EndYMaxPercent.Value, Is.LessThanOrEqualTo(m.RequestedYMaxPercent.Value + eps), $"{label}: endpoint y-max escaped requested range.");
        }

        static HeightmapDslV2OpMetrics FindOp(HeightmapDslV2Diagnostics diagnostics, string operation)
        {
            for (int i = 0; i < diagnostics.Operations.Count; i++)
            {
                HeightmapDslV2OpMetrics op = diagnostics.Operations[i];
                if (string.Equals(op.Operation, operation, StringComparison.Ordinal))
                    return op;
            }

            Assert.Fail($"Expected operation metrics for '{operation}' but none were recorded.");
            return null;
        }

        static void AssertFinite(float value, string label)
        {
            Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False, $"Invalid metric for {label}.");
        }

        static ElevationFieldV2 CreateField(
            int seed,
            int cellCount,
            float aspectRatio,
            float cellSizeKm = 2.5f,
            float maxSeaDepthMeters = 1250f,
            float maxElevationMeters = 5000f)
        {
            float cellAreaKm2 = cellSizeKm * cellSizeKm;
            float mapAreaKm2 = cellCount * cellAreaKm2;
            float mapWidthKm = (float)Math.Sqrt(mapAreaKm2 * aspectRatio);
            float mapHeightKm = mapWidthKm / aspectRatio;

            var (gridPoints, spacing) = PointGenerator.JitteredGrid(mapWidthKm, mapHeightKm, cellCount, seed);
            Vec2[] boundaryPoints = PointGenerator.BoundaryPoints(mapWidthKm, mapHeightKm, spacing);
            CellMesh mesh = VoronoiBuilder.Build(mapWidthKm, mapHeightKm, gridPoints, boundaryPoints);
            mesh.ComputeAreas();
            return new ElevationFieldV2(mesh, maxSeaDepthMeters, maxElevationMeters);
        }
    }
}
