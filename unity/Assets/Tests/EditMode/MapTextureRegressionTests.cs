using System.Collections.Generic;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("M3Regression")]
    public class MapTextureRegressionTests
    {
        private static readonly string[] GoldenTextureProperties =
        {
            "_HeightmapTex",
            "_RiverMaskTex",
            "_RealmBorderDistTex",
            "_ProvinceBorderDistTex",
            "_CountyBorderDistTex",
            "_MarketBorderDistTex",
            "_CellToMarketTex",
            "_RoadMaskTex"
        };

        private static readonly string[] DeterministicTextureProperties =
        {
            "_PoliticalIdsTex",
            "_GeographyBaseTex",
            "_ModeColorResolve",
            "_HeightmapTex",
            "_RiverMaskTex",
            "_RealmBorderDistTex",
            "_ProvinceBorderDistTex",
            "_CountyBorderDistTex",
            "_MarketBorderDistTex",
            "_CellToMarketTex",
            "_RoadMaskTex"
        };

        [TestCaseSource(typeof(TextureTestHarness), nameof(TextureTestHarness.BaselineCases))]
        public void OverlayTextures_AreDeterministic_ForBaselineCases(int seed, MapGen.Core.HeightmapTemplateType template, int cellCount)
        {
            var baseline = new TextureBaselineCase(seed, template, cellCount);

            Dictionary<string, string> runAHashes;
            Dictionary<string, string> runBHashes;

            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                runAHashes = CollectTextureHashes(fixture.Material);
            }

            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                runBHashes = CollectTextureHashes(fixture.Material);
            }

            bool updateMode = TextureTestHarness.IsBaselineUpdateModeEnabled();
            if (updateMode)
            {
                TextureTestHarness.UpsertExpectedTextureHashes(seed, template, cellCount, runAHashes);
            }

            Dictionary<string, string> expectedHashes = updateMode
                ? runAHashes
                : TextureTestHarness.LoadExpectedTextureHashes(seed, template, cellCount);

            foreach (var expectedKey in expectedHashes.Keys)
            {
                bool knownKey = false;
                for (int i = 0; i < GoldenTextureProperties.Length; i++)
                {
                    if (GoldenTextureProperties[i] == expectedKey)
                    {
                        knownKey = true;
                        break;
                    }
                }

                Assert.That(knownKey, Is.True,
                    $"Unexpected texture key in hash baseline: {expectedKey} (seed={seed}, template={template})");
            }

            foreach (string textureProperty in DeterministicTextureProperties)
            {
                Assert.That(runBHashes.ContainsKey(textureProperty), Is.True, $"Second run missing texture {textureProperty}");
                Assert.That(runAHashes[textureProperty], Is.EqualTo(runBHashes[textureProperty]),
                    $"Texture hash mismatch for {textureProperty} (seed={seed}, template={template})");
            }

            foreach (string textureProperty in GoldenTextureProperties)
            {
                Assert.That(expectedHashes.ContainsKey(textureProperty), Is.True,
                    $"Missing expected baseline hash for {textureProperty} (seed={seed}, template={template})");
                Assert.That(runAHashes[textureProperty], Is.EqualTo(expectedHashes[textureProperty]),
                    $"Golden hash mismatch for {textureProperty} (seed={seed}, template={template}). " +
                    $"Expected={expectedHashes[textureProperty]}, Actual={runAHashes[textureProperty]}");
            }
        }

        private static Dictionary<string, string> CollectTextureHashes(UnityEngine.Material material)
        {
            var hashes = new Dictionary<string, string>(DeterministicTextureProperties.Length);
            foreach (string textureProperty in DeterministicTextureProperties)
            {
                hashes[textureProperty] = TextureTestHarness.HashTextureFromMaterial(material, textureProperty);
            }
            return hashes;
        }
    }
}
