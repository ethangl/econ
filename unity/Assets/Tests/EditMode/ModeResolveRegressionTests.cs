using System.Collections.Generic;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Renderer;
using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("M3Regression")]
    public class ModeResolveRegressionTests
    {
        private static readonly string[] BackingTextureProperties =
        {
            "_CellDataTex",
            "_HeightmapTex",
            "_RiverMaskTex",
            "_RealmBorderDistTex",
            "_ProvinceBorderDistTex",
            "_CountyBorderDistTex",
            "_MarketBorderDistTex",
            "_CellToMarketTex",
            "_RoadMaskTex"
        };

        [TestCase(MapView.MapMode.Political, 1)]
        [TestCase(MapView.MapMode.Province, 2)]
        [TestCase(MapView.MapMode.County, 3)]
        [TestCase(MapView.MapMode.Market, 4)]
        [TestCase(MapView.MapMode.Terrain, 5)]
        [TestCase(MapView.MapMode.Soil, 6)]
        [TestCase(MapView.MapMode.ChannelInspector, 7)]
        public void SetMapMode_UpdatesShaderMapModeProperty(MapView.MapMode mode, int expectedShaderMode)
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                fixture.OverlayManager.SetMapMode(mode);
                int shaderMode = fixture.Material.GetInt("_MapMode");
                Assert.That(shaderMode, Is.EqualTo(expectedShaderMode), $"Unexpected shader mode mapping for {mode}");
            }
        }

        [TestCase(MapOverlayManager.ChannelDebugView.CellDataR)]
        [TestCase(MapOverlayManager.ChannelDebugView.CellDataG)]
        [TestCase(MapOverlayManager.ChannelDebugView.CellDataB)]
        [TestCase(MapOverlayManager.ChannelDebugView.CellDataA)]
        [TestCase(MapOverlayManager.ChannelDebugView.RealmBorderDist)]
        [TestCase(MapOverlayManager.ChannelDebugView.ProvinceBorderDist)]
        [TestCase(MapOverlayManager.ChannelDebugView.CountyBorderDist)]
        [TestCase(MapOverlayManager.ChannelDebugView.MarketBorderDist)]
        [TestCase(MapOverlayManager.ChannelDebugView.RiverMask)]
        [TestCase(MapOverlayManager.ChannelDebugView.Heightmap)]
        [TestCase(MapOverlayManager.ChannelDebugView.RoadMask)]
        public void SetChannelDebugView_UpdatesShaderDebugViewProperty(MapOverlayManager.ChannelDebugView debugView)
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                fixture.OverlayManager.SetChannelDebugView(debugView);
                int shaderDebugView = fixture.Material.GetInt("_DebugView");
                Assert.That(shaderDebugView, Is.EqualTo((int)debugView),
                    $"Unexpected shader debug view mapping for {debugView}");
            }
        }

        [Test]
        public void SetEconomyState_UpdatesMarketTextures_WhenCountyAssignmentsChange()
        {
            // Pre-resolve validation: confirms data-change propagation through current material texture path.
            // Extend this to ModeColorResolve invalidation assertions when M3-S3 resolve texture is introduced.
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                string initialCellToMarket = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                string initialMarketBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");

                var assignmentA = BuildEconomyState(fixture.MapData, CountyAssignmentPattern.Halves);
                fixture.OverlayManager.SetEconomyState(assignmentA);

                string singleMarketCellToMarket = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                string singleMarketBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");

                Assert.That(singleMarketCellToMarket, Is.Not.EqualTo(initialCellToMarket), "Cell-to-market texture did not update after setting economy.");
                Assert.That(singleMarketBorder, Is.Not.EqualTo(initialMarketBorder), "Market border texture did not update after setting economy.");

                // Re-applying identical assignments should be deterministic and stable.
                fixture.OverlayManager.SetEconomyState(assignmentA);
                string singleMarketCellToMarketAgain = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                string singleMarketBorderAgain = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");
                Assert.That(singleMarketCellToMarketAgain, Is.EqualTo(singleMarketCellToMarket),
                    "Cell-to-market texture changed after reapplying identical assignments.");
                Assert.That(singleMarketBorderAgain, Is.EqualTo(singleMarketBorder),
                    "Market border texture changed after reapplying identical assignments.");

                var assignmentB = BuildEconomyState(fixture.MapData, CountyAssignmentPattern.Alternating);
                fixture.OverlayManager.SetEconomyState(assignmentB);

                string splitMarketCellToMarket = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                string splitMarketBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");

                Assert.That(splitMarketCellToMarket, Is.Not.EqualTo(singleMarketCellToMarket), "Cell-to-market texture did not refresh for changed county assignments.");
                Assert.That(splitMarketBorder, Is.Not.EqualTo(singleMarketBorder), "Market border texture did not refresh for changed county assignments.");

                // Returning to previous assignments should return previous outputs.
                fixture.OverlayManager.SetEconomyState(assignmentA);
                string revertedCellToMarket = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                string revertedMarketBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");
                Assert.That(revertedCellToMarket, Is.EqualTo(singleMarketCellToMarket),
                    "Cell-to-market texture did not return to prior baseline after reverting assignments.");
                Assert.That(revertedMarketBorder, Is.EqualTo(singleMarketBorder),
                    "Market border texture did not return to prior baseline after reverting assignments.");
            }
        }

        [Test]
        public void ModeSwitches_DoNotMutateBackingTextures()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                Dictionary<string, string> before = CollectBackingTextureHashes(fixture.Material);

                fixture.OverlayManager.SetMapMode(MapView.MapMode.Political);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Terrain);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Market);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.County);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Political);

                Dictionary<string, string> after = CollectBackingTextureHashes(fixture.Material);
                foreach (string propertyName in BackingTextureProperties)
                {
                    Assert.That(after[propertyName], Is.EqualTo(before[propertyName]),
                        $"Backing texture changed after mode switching: {propertyName}");
                }
            }
        }

        [Test]
        public void EconomyTextureUpdates_PersistAcrossModeSwitchRoundTrips()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                var assignmentA = BuildEconomyState(fixture.MapData, CountyAssignmentPattern.Halves);
                fixture.OverlayManager.SetEconomyState(assignmentA);
                string assignmentABorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");
                string assignmentACellToMarket = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");

                fixture.OverlayManager.SetMapMode(MapView.MapMode.Market);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Terrain);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Political);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Market);

                string assignmentABorderAfterSwitches = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");
                string assignmentACellAfterSwitches = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                Assert.That(assignmentABorderAfterSwitches, Is.EqualTo(assignmentABorder),
                    "Market border texture changed after mode-switch round trip without new economy data.");
                Assert.That(assignmentACellAfterSwitches, Is.EqualTo(assignmentACellToMarket),
                    "Cell-to-market texture changed after mode-switch round trip without new economy data.");

                var assignmentB = BuildEconomyState(fixture.MapData, CountyAssignmentPattern.Alternating);
                fixture.OverlayManager.SetEconomyState(assignmentB);
                string assignmentBBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");
                string assignmentBCellToMarket = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                Assert.That(assignmentBBorder, Is.Not.EqualTo(assignmentABorder), "Market border texture did not change for new economy assignments.");
                Assert.That(assignmentBCellToMarket, Is.Not.EqualTo(assignmentACellToMarket), "Cell-to-market texture did not change for new economy assignments.");

                fixture.OverlayManager.SetMapMode(MapView.MapMode.Political);
                fixture.OverlayManager.SetMapMode(MapView.MapMode.Market);

                string assignmentBBorderAfterSwitches = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_MarketBorderDistTex");
                string assignmentBCellAfterSwitches = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellToMarketTex");
                Assert.That(assignmentBBorderAfterSwitches, Is.EqualTo(assignmentBBorder),
                    "Market border texture reverted/staled after mode switches.");
                Assert.That(assignmentBCellAfterSwitches, Is.EqualTo(assignmentBCellToMarket),
                    "Cell-to-market texture reverted/staled after mode switches.");
            }
        }

        [Test]
        public void SelectionSetters_ClearOtherChannels_AndNormalizeIds()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                fixture.OverlayManager.ClearSelection();
                AssertSelectionState(fixture.Material, -1f, -1f, -1f, -1f);

                fixture.OverlayManager.SetSelectedRealm(42);
                AssertSelectionState(fixture.Material, 42f / 65535f, -1f, -1f, -1f);

                fixture.OverlayManager.SetSelectedProvince(84);
                AssertSelectionState(fixture.Material, -1f, 84f / 65535f, -1f, -1f);

                fixture.OverlayManager.SetSelectedCounty(126);
                AssertSelectionState(fixture.Material, -1f, -1f, 126f / 65535f, -1f);

                fixture.OverlayManager.SetSelectedMarket(168);
                AssertSelectionState(fixture.Material, -1f, -1f, -1f, 168f / 65535f);

                fixture.OverlayManager.SetSelectedCounty(-1);
                AssertSelectionState(fixture.Material, -1f, -1f, -1f, -1f);
            }
        }

        [Test]
        public void HoverSetters_ClearOtherChannels_AndNormalizeIds()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                fixture.OverlayManager.ClearHover();
                AssertHoverState(fixture.Material, -1f, -1f, -1f, -1f);

                fixture.OverlayManager.SetHoveredRealm(11);
                AssertHoverState(fixture.Material, 11f / 65535f, -1f, -1f, -1f);

                fixture.OverlayManager.SetHoveredProvince(22);
                AssertHoverState(fixture.Material, -1f, 22f / 65535f, -1f, -1f);

                fixture.OverlayManager.SetHoveredCounty(33);
                AssertHoverState(fixture.Material, -1f, -1f, 33f / 65535f, -1f);

                fixture.OverlayManager.SetHoveredMarket(44);
                AssertHoverState(fixture.Material, -1f, -1f, -1f, 44f / 65535f);

                fixture.OverlayManager.SetHoveredMarket(-1);
                AssertHoverState(fixture.Material, -1f, -1f, -1f, -1f);
            }
        }

        [Test]
        public void InteractionIntensitySetters_ClampValues()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                fixture.OverlayManager.SetHoverIntensity(-0.5f);
                Assert.That(fixture.Material.GetFloat("_HoverIntensity"), Is.EqualTo(0f).Within(1e-6f));
                fixture.OverlayManager.SetHoverIntensity(1.5f);
                Assert.That(fixture.Material.GetFloat("_HoverIntensity"), Is.EqualTo(1f).Within(1e-6f));

                fixture.OverlayManager.SetSelectionDimming(-2f);
                Assert.That(fixture.Material.GetFloat("_SelectionDimming"), Is.EqualTo(0f).Within(1e-6f));
                fixture.OverlayManager.SetSelectionDimming(2f);
                Assert.That(fixture.Material.GetFloat("_SelectionDimming"), Is.EqualTo(1f).Within(1e-6f));

                fixture.OverlayManager.SetSelectionDesaturation(-1f);
                Assert.That(fixture.Material.GetFloat("_SelectionDesaturation"), Is.EqualTo(0f).Within(1e-6f));
                fixture.OverlayManager.SetSelectionDesaturation(4f);
                Assert.That(fixture.Material.GetFloat("_SelectionDesaturation"), Is.EqualTo(1f).Within(1e-6f));
            }
        }

        [Test]
        public void UpdateCellData_MutatesOnlyCellDataTexture()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                Cell targetCell = fixture.MapData.Cells[0];
                int newRealmId = targetCell.RealmId + 1;
                int newProvinceId = targetCell.ProvinceId + 1;
                int newCountyId = targetCell.CountyId + 1;

                string beforeCellData = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellDataTex");
                string beforeHeight = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_HeightmapTex");
                string beforeRiver = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_RiverMaskTex");
                string beforeRealmBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_RealmBorderDistTex");

                fixture.OverlayManager.UpdateCellData(targetCell.Id, newRealmId, newProvinceId, newCountyId);

                string afterCellData = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellDataTex");
                string afterHeight = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_HeightmapTex");
                string afterRiver = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_RiverMaskTex");
                string afterRealmBorder = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_RealmBorderDistTex");

                Assert.That(afterCellData, Is.Not.EqualTo(beforeCellData), "Cell data texture did not change after UpdateCellData.");
                Assert.That(afterHeight, Is.EqualTo(beforeHeight), "Heightmap changed after UpdateCellData.");
                Assert.That(afterRiver, Is.EqualTo(beforeRiver), "River mask changed after UpdateCellData.");
                Assert.That(afterRealmBorder, Is.EqualTo(beforeRealmBorder), "Realm border texture changed after UpdateCellData.");
            }
        }

        [Test]
        public void UpdateCellData_InvalidCellId_DoesNotChangeTextures()
        {
            var baseline = TextureTestHarness.GetPrimaryBaselineCase();
            using (var fixture = TextureTestHarness.CreateOverlayFixture(baseline))
            {
                string beforeCellData = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellDataTex");
                string beforeHeight = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_HeightmapTex");

                fixture.OverlayManager.UpdateCellData(-12345, newRealmId: 5, newProvinceId: 9, newCountyId: 13);

                string afterCellData = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_CellDataTex");
                string afterHeight = TextureTestHarness.HashTextureFromMaterial(fixture.Material, "_HeightmapTex");

                Assert.That(afterCellData, Is.EqualTo(beforeCellData), "Cell data texture changed for invalid cell update.");
                Assert.That(afterHeight, Is.EqualTo(beforeHeight), "Heightmap changed for invalid cell update.");
            }
        }

        private enum CountyAssignmentPattern
        {
            Halves,
            Alternating
        }

        private static EconomyState BuildEconomyState(MapData mapData, CountyAssignmentPattern assignmentPattern)
        {
            Assert.That(mapData, Is.Not.Null);
            Assert.That(mapData.Cells, Is.Not.Null.And.Not.Empty, "Map has no cells.");
            Assert.That(mapData.Counties, Is.Not.Null.And.Not.Empty, "Map has no counties.");
            Assert.That(mapData.Counties.Count, Is.GreaterThan(1), "Need at least two counties to validate market reassignment behavior.");

            var economy = new EconomyState();
            var landCellIds = new List<int>();
            for (int i = 0; i < mapData.Cells.Count; i++)
            {
                Cell cell = mapData.Cells[i];
                if (cell.IsLand)
                    landCellIds.Add(cell.Id);
            }

            Assert.That(landCellIds.Count, Is.GreaterThan(0), "Map has no land cells.");

            int primaryHubCellId = landCellIds[0];
            int secondaryHubCellId = landCellIds[landCellIds.Count / 2];

            economy.Markets[1] = new Market
            {
                Id = 1,
                Name = "TestMarketA",
                LocationCellId = primaryHubCellId
            };

            economy.Markets[2] = new Market
            {
                Id = 2,
                Name = "TestMarketB",
                LocationCellId = secondaryHubCellId
            };

            int countySplitIndex = mapData.Counties.Count / 2;

            for (int i = 0; i < mapData.Counties.Count; i++)
            {
                int countyId = mapData.Counties[i].Id;
                int marketId = assignmentPattern == CountyAssignmentPattern.Alternating
                    ? (i % 2 == 1 ? 2 : 1)
                    : (i < countySplitIndex ? 1 : 2);
                economy.CountyToMarket[countyId] = marketId;
            }

            return economy;
        }

        private static Dictionary<string, string> CollectBackingTextureHashes(Material material)
        {
            var hashes = new Dictionary<string, string>(BackingTextureProperties.Length);
            for (int i = 0; i < BackingTextureProperties.Length; i++)
            {
                string propertyName = BackingTextureProperties[i];
                hashes[propertyName] = TextureTestHarness.HashTextureFromMaterial(material, propertyName);
            }
            return hashes;
        }

        private static void AssertSelectionState(Material material, float realm, float province, float county, float market)
        {
            Assert.That(material.GetFloat("_SelectedRealmId"), Is.EqualTo(realm).Within(1e-6f));
            Assert.That(material.GetFloat("_SelectedProvinceId"), Is.EqualTo(province).Within(1e-6f));
            Assert.That(material.GetFloat("_SelectedCountyId"), Is.EqualTo(county).Within(1e-6f));
            Assert.That(material.GetFloat("_SelectedMarketId"), Is.EqualTo(market).Within(1e-6f));
        }

        private static void AssertHoverState(Material material, float realm, float province, float county, float market)
        {
            Assert.That(material.GetFloat("_HoveredRealmId"), Is.EqualTo(realm).Within(1e-6f));
            Assert.That(material.GetFloat("_HoveredProvinceId"), Is.EqualTo(province).Within(1e-6f));
            Assert.That(material.GetFloat("_HoveredCountyId"), Is.EqualTo(county).Within(1e-6f));
            Assert.That(material.GetFloat("_HoveredMarketId"), Is.EqualTo(market).Within(1e-6f));
        }
    }
}
