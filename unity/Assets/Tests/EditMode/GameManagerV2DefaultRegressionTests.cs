using EconSim.Core;
using MapGen.Core;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGenV2")]
    public class GameManagerV2DefaultRegressionTests
    {
        [Test]
        public void GenerateMap_DefaultsToV2_AndStaysWithinSmokeBand()
        {
            var gameObject = new GameObject("GameManagerV2DefaultRegressionTests");
            var manager = gameObject.AddComponent<GameManager>();
            manager.GenerationMode = MapGenerationMode.V2Default;

            var config = new MapGenConfig
            {
                Seed = 2202,
                CellCount = 5000,
                Template = HeightmapTemplateType.Continents,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            try
            {
                manager.GenerateMap(config);

                Assert.That(manager.MapGenV2Result, Is.Not.Null, "V2 result should be set by default.");
                Assert.That(manager.MapGenResult, Is.Null, "V1 result should remain null when V2 is default.");
                Assert.That(manager.MapData, Is.Not.Null, "Runtime MapData should be generated.");

                manager.MapData.AssertElevationInvariants();
                manager.MapData.AssertWorldInvariants();

                float v2LandRatio = manager.MapGenV2Result.Elevation.LandRatio();
                float runtimeLandRatio = manager.MapData.Cells.Count > 0
                    ? manager.MapData.Info.LandCells / (float)manager.MapData.Cells.Count
                    : 0f;
                int riverCount = manager.MapGenV2Result.Rivers.Rivers.Length;
                float p50 = Percentile(manager.MapGenV2Result.Elevation.ElevationMetersSigned, 0.50f);

                Assert.That(v2LandRatio, Is.InRange(0.30f, 0.82f), "V2 land ratio drifted outside expected broad band.");
                Assert.That(runtimeLandRatio, Is.InRange(0.30f, 0.82f), "Runtime land ratio drifted outside expected broad band.");
                Assert.That(Mathf.Abs(runtimeLandRatio - v2LandRatio), Is.LessThanOrEqualTo(0.08f),
                    "Runtime conversion drifted too far from V2 land ratio.");
                Assert.That(riverCount, Is.InRange(5, 250), "V2 river count drifted outside expected broad band.");
                Assert.That(p50, Is.InRange(-500f, 1800f), "V2 p50 elevation drifted outside expected broad band.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);

                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light != null && light.type == LightType.Directional && light.gameObject.name == "Sun")
                        Object.DestroyImmediate(light.gameObject);
                }
            }
        }

        [Test]
        public void GenerateMap_ForceV1_UsesLegacyEscapeHatch()
        {
            var gameObject = new GameObject("GameManagerV1EscapeHatchTests");
            var manager = gameObject.AddComponent<GameManager>();
            manager.GenerationMode = MapGenerationMode.ForceV1;

            var config = new MapGenConfig
            {
                Seed = 98765,
                CellCount = 5000,
                Template = HeightmapTemplateType.Continents,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            try
            {
                manager.GenerateMap(config);

                Assert.That(manager.MapGenResult, Is.Not.Null, "V1 result should be populated when ForceV1 is selected.");
                Assert.That(manager.MapGenV2Result, Is.Null, "V2 result should remain null when ForceV1 is selected.");
                Assert.That(manager.MapData, Is.Not.Null, "Runtime MapData should still be generated.");

                manager.MapData.AssertElevationInvariants();
                manager.MapData.AssertWorldInvariants();
                Assert.That(manager.MapData.Info.World.SeaLevelHeight, Is.EqualTo(20f).Within(0.0001f),
                    "V1 runtime anchor changed unexpectedly.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);

                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light != null && light.type == LightType.Directional && light.gameObject.name == "Sun")
                        Object.DestroyImmediate(light.gameObject);
                }
            }
        }

        [Test]
        public void GenerateMap_ForceV2_UsesV2Path()
        {
            var gameObject = new GameObject("GameManagerForceV2Tests");
            var manager = gameObject.AddComponent<GameManager>();
            manager.GenerationMode = MapGenerationMode.ForceV2;

            var config = new MapGenConfig
            {
                Seed = 2202,
                CellCount = 5000,
                Template = HeightmapTemplateType.Continents,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            try
            {
                manager.GenerateMap(config);

                Assert.That(manager.MapGenV2Result, Is.Not.Null, "V2 result should be populated when ForceV2 is selected.");
                Assert.That(manager.MapGenResult, Is.Null, "V1 result should remain null when ForceV2 is selected.");
                Assert.That(manager.MapData, Is.Not.Null, "Runtime MapData should still be generated.");

                manager.MapData.AssertElevationInvariants();
                manager.MapData.AssertWorldInvariants();

                float v2LandRatio = manager.MapGenV2Result.Elevation.LandRatio();
                int riverCount = manager.MapGenV2Result.Rivers.Rivers.Length;
                Assert.That(v2LandRatio, Is.InRange(0.30f, 0.82f), "ForceV2 land ratio drifted outside expected broad band.");
                Assert.That(riverCount, Is.InRange(5, 250), "ForceV2 river count drifted outside expected broad band.");
            }
            finally
            {
                Object.DestroyImmediate(gameObject);

                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light != null && light.type == LightType.Directional && light.gameObject.name == "Sun")
                        Object.DestroyImmediate(light.gameObject);
                }
            }
        }

        static float Percentile(float[] values, float q)
        {
            if (values == null || values.Length == 0)
                return 0f;

            var sorted = (float[])values.Clone();
            System.Array.Sort(sorted);

            if (q <= 0f) return sorted[0];
            if (q >= 1f) return sorted[sorted.Length - 1];

            float index = q * (sorted.Length - 1);
            int lo = Mathf.FloorToInt(index);
            int hi = Mathf.CeilToInt(index);
            if (lo == hi)
                return sorted[lo];

            float t = index - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * t;
        }
    }
}
