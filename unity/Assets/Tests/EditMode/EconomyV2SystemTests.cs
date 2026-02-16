using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;
using NUnit.Framework;

namespace EconSim.Tests
{
    public class EconomyV2SystemTests
    {
        private bool _prevUseV2;
        private int _prevSeedOverride;

        [SetUp]
        public void SetUp()
        {
            _prevUseV2 = SimulationConfig.UseEconomyV2;
            _prevSeedOverride = SimulationConfig.EconomySeedOverride;
            SimulationConfig.UseEconomyV2 = true;
            SimulationConfig.EconomySeedOverride = 0;
        }

        [TearDown]
        public void TearDown()
        {
            SimulationConfig.UseEconomyV2 = _prevUseV2;
            SimulationConfig.EconomySeedOverride = _prevSeedOverride;
        }

        [Test]
        public void MarketSystem_UsesOneDayLagAndProportionalRationing()
        {
            var economy = BuildMinimalEconomy();
            var market = new Market
            {
                Id = 1,
                Name = "Test Market",
                LocationCellId = -1,
                Type = MarketType.Legitimate,
                Goods = new Dictionary<string, MarketGoodState>
                {
                    ["bread"] = new MarketGoodState
                    {
                        GoodId = "bread",
                        BasePrice = 1f,
                        Price = 1f
                    }
                }
            };

            market.Inventory.Add(new ConsignmentLot
            {
                SellerId = MarketOrderIds.MakeSeedSellerId(market.Id),
                GoodId = "bread",
                Quantity = 50f,
                DayListed = 0
            });

            market.PendingBuyOrders.Add(new BuyOrder
            {
                BuyerId = MarketOrderIds.MakePopulationBuyerId(1),
                GoodId = "bread",
                Quantity = 60f,
                MaxSpend = 60f,
                TransportCost = 0f,
                DayPosted = 0
            });

            market.PendingBuyOrders.Add(new BuyOrder
            {
                BuyerId = MarketOrderIds.MakePopulationBuyerId(2),
                GoodId = "bread",
                Quantity = 40f,
                MaxSpend = 40f,
                TransportCost = 0f,
                DayPosted = 0
            });

            // Posted today: should not clear this tick.
            market.PendingBuyOrders.Add(new BuyOrder
            {
                BuyerId = MarketOrderIds.MakePopulationBuyerId(1),
                GoodId = "bread",
                Quantity = 5f,
                MaxSpend = 5f,
                TransportCost = 0f,
                DayPosted = 2
            });

            economy.Markets[market.Id] = market;
            economy.CountyToMarket[1] = market.Id;
            economy.CountyToMarket[2] = market.Id;

            economy.GetCounty(1).Population.Treasury = 100f;
            economy.GetCounty(2).Population.Treasury = 100f;

            var state = new SimulationState
            {
                Economy = economy,
                CurrentDay = 2
            };

            var system = new MarketSystem();
            system.Tick(state, null);

            Assert.That(economy.GetCounty(1).Population.Treasury, Is.EqualTo(70f).Within(0.001f));
            Assert.That(economy.GetCounty(2).Population.Treasury, Is.EqualTo(80f).Within(0.001f));
            Assert.That(market.Goods["bread"].LastTradeVolume, Is.EqualTo(50f).Within(0.001f));
            Assert.That(market.Goods["bread"].Demand, Is.EqualTo(100f).Within(0.001f));
            Assert.That(market.PendingBuyOrders.Count, Is.EqualTo(1), "Only day-2 order should remain pending.");
        }

        [Test]
        public void PriceSystem_LeavesOffMapFixedAndMovesLegitimatePrice()
        {
            var economy = BuildMinimalEconomy();

            var legit = new Market
            {
                Id = 1,
                Type = MarketType.Legitimate,
                Goods = new Dictionary<string, MarketGoodState>
                {
                    ["bread"] = new MarketGoodState
                    {
                        GoodId = "bread",
                        BasePrice = 10f,
                        Price = 10f,
                        Supply = 1f,
                        Demand = 10f
                    }
                }
            };

            var offMap = new Market
            {
                Id = 2,
                Type = MarketType.OffMap,
                Goods = new Dictionary<string, MarketGoodState>
                {
                    ["bread"] = new MarketGoodState
                    {
                        GoodId = "bread",
                        BasePrice = 20f,
                        Price = 33f,
                        Supply = 1f,
                        Demand = 100f
                    }
                }
            };

            economy.Markets[1] = legit;
            economy.Markets[2] = offMap;

            var state = new SimulationState { Economy = economy, CurrentDay = 2 };
            var system = new PriceSystem();
            system.Tick(state, null);

            Assert.That(legit.Goods["bread"].Price, Is.GreaterThan(10f));
            Assert.That(offMap.Goods["bread"].Price, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void LaborSystem_DistressedFacilityShedsWorkersAndHigherWageFills()
        {
            var economy = BuildMinimalEconomy();
            var county = economy.GetCounty(1);

            county.Population = CountyPopulation.FromTotal(2000);

            var distressedDef = new FacilityDef
            {
                Id = "distressed",
                LaborType = LaborType.Unskilled,
                LaborRequired = 25,
                BaseThroughput = 1,
                IsExtraction = true,
                OutputGoodId = "bread"
            };

            var healthyDef = new FacilityDef
            {
                Id = "healthy",
                LaborType = LaborType.Unskilled,
                LaborRequired = 25,
                BaseThroughput = 1,
                IsExtraction = true,
                OutputGoodId = "bread"
            };

            economy.FacilityDefs.Register(distressedDef);
            economy.FacilityDefs.Register(healthyDef);

            var distressed = new Facility
            {
                Id = 1,
                CountyId = 1,
                TypeId = "distressed",
                IsActive = true,
                AssignedWorkers = 25,
                WageRate = 6f,
                WageDebtDays = 60
            };

            var healthy = new Facility
            {
                Id = 2,
                CountyId = 1,
                TypeId = "healthy",
                IsActive = true,
                AssignedWorkers = 0,
                WageRate = 10f,
                WageDebtDays = 0
            };

            county.FacilityIds.Add(1);
            county.FacilityIds.Add(2);
            economy.Facilities[1] = distressed;
            economy.Facilities[2] = healthy;

            var state = new SimulationState
            {
                Economy = economy,
                SubsistenceWage = 1f,
                CurrentDay = 7
            };

            var system = new LaborSystem();
            system.Tick(state, null);

            Assert.That(distressed.AssignedWorkers, Is.LessThan(25));
            Assert.That(healthy.AssignedWorkers, Is.GreaterThan(0));
            Assert.That(county.Population.EmployedUnskilled, Is.GreaterThan(0));
        }

        private static EconomyState BuildMinimalEconomy()
        {
            var economy = new EconomyState();

            var map = new MapData
            {
                Cells = new List<Cell>
                {
                    new Cell { Id = 1, IsLand = true, CountyId = 1, NeighborIds = new List<int>() },
                    new Cell { Id = 2, IsLand = true, CountyId = 2, NeighborIds = new List<int>() }
                },
                Counties = new List<County>
                {
                    new County { Id = 1, SeatCellId = 1, CellIds = new List<int> { 1 }, TotalPopulation = 1000 },
                    new County { Id = 2, SeatCellId = 2, CellIds = new List<int> { 2 }, TotalPopulation = 1000 }
                },
                Provinces = new List<Province>(),
                Realms = new List<Realm>(),
                Rivers = new List<River>(),
                Burgs = new List<Burg>(),
                Features = new List<Feature>(),
                Biomes = new List<Biome>(),
                Vertices = new List<Vec2>()
            };
            map.BuildLookups();

            economy.InitializeFromMap(map);
            economy.Goods.Register(new GoodDef
            {
                Id = "bread",
                Name = "Bread",
                Category = GoodCategory.Finished,
                NeedCategory = NeedCategory.Basic,
                BaseConsumption = 0.01f,
                BasePrice = 1f
            });

            return economy;
        }
    }
}
