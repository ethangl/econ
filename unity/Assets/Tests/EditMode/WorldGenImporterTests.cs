using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Import;
using MapGen.Core;
using PopGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGen")]
    public class WorldGenImporterTests
    {
        [Test]
        public void Convert_WithGenerationContext_StampsDeterministicSeedMetadata()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(20260212);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 4000);

            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(runtime.Info.Seed, Is.EqualTo(context.RootSeed.ToString()));
            Assert.That(runtime.Info.RootSeed, Is.EqualTo(context.RootSeed));
            Assert.That(runtime.Info.MapGenSeed, Is.EqualTo(context.MapGenSeed));
            Assert.That(runtime.Info.PopGenSeed, Is.EqualTo(context.PopGenSeed));
            Assert.That(runtime.Info.EconomySeed, Is.EqualTo(context.EconomySeed));
            Assert.That(runtime.Info.SimulationSeed, Is.EqualTo(context.SimulationSeed));
        }

        [Test]
        public void Convert_WithoutGenerationContext_StillProducesValidRuntimeMap()
        {
            const int mapSeed = 424242;
            MapGenResult map = GenerateMap(mapSeed, cellCount: 3000);

            MapData runtime = WorldGenImporter.Convert(map);

            runtime.AssertElevationInvariants();
            runtime.AssertWorldInvariants();

            Assert.That(runtime.Info.Seed, Is.EqualTo(string.Empty));
            Assert.That(runtime.Info.RootSeed, Is.EqualTo(0));
            Assert.That(runtime.Info.MapGenSeed, Is.EqualTo(0));
            Assert.That(runtime.Info.PopGenSeed, Is.EqualTo(0));
            Assert.That(runtime.Info.EconomySeed, Is.EqualTo(0));
            Assert.That(runtime.Info.SimulationSeed, Is.EqualTo(0));
            Assert.That(runtime.Cells.Count, Is.EqualTo(map.Mesh.CellCount));
            Assert.That(runtime.Info.LandCells, Is.EqualTo(CountLand(runtime.Cells)));
        }

        [Test]
        public void Convert_PopulationLayerMatchesPopGenOutput()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(987654);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 3500);

            PopGenResult expected = PopGenPipeline.Generate(
                map,
                new PopGenConfig(),
                new PopGenSeed(context.PopGenSeed));

            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(runtime.Burgs.Count, Is.EqualTo(expected.Burgs.Length));
            Assert.That(runtime.Counties.Count, Is.EqualTo(expected.Counties.Length));
            Assert.That(runtime.Provinces.Count, Is.EqualTo(expected.Provinces.Length));
            Assert.That(runtime.Realms.Count, Is.EqualTo(expected.Realms.Length));

            int n = Math.Min(runtime.Cells.Count, expected.CellBurgId.Length);
            for (int i = 0; i < n; i++)
                Assert.That(runtime.Cells[i].BurgId, Is.EqualTo(expected.CellBurgId[i]), $"Cell burg mismatch at {i}.");
        }

        [Test]
        public void Convert_BurgCellAssignmentsRoundTripToBurgList()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(121212);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 5000);
            MapData runtime = WorldGenImporter.Convert(map, context);

            var burgById = new Dictionary<int, Burg>(runtime.Burgs.Count);
            foreach (Burg burg in runtime.Burgs)
                burgById[burg.Id] = burg;

            int assignedCount = 0;
            for (int i = 0; i < runtime.Cells.Count; i++)
            {
                int burgId = runtime.Cells[i].BurgId;
                if (burgId <= 0)
                    continue;

                assignedCount++;
                Assert.That(burgById.TryGetValue(burgId, out Burg burg), Is.True, $"Cell {i} references missing burg {burgId}.");
                Assert.That(burg.CellId, Is.EqualTo(i), $"Burg {burgId} cell mismatch.");
            }

            Assert.That(assignedCount, Is.EqualTo(runtime.Burgs.Count), "Every burg should be assigned to exactly one cell.");
        }

        [Test]
        public void Convert_SoilIdsRoundTripFromMapGenBiomes()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(24681012);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 4500);
            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(map.Biomes.Soil, Is.Not.Null);
            Assert.That(map.Biomes.Soil.Length, Is.EqualTo(runtime.Cells.Count));

            for (int i = 0; i < runtime.Cells.Count; i++)
            {
                int expectedSoilId = (int)map.Biomes.Soil[i];
                int actualSoilId = runtime.Cells[i].SoilId;
                Assert.That(actualSoilId, Is.EqualTo(expectedSoilId), $"SoilId mismatch at cell {i}.");
                Assert.That(actualSoilId, Is.InRange(0, 7), $"SoilId out of range at cell {i}.");
            }
        }

        static MapGenResult GenerateMap(int seed, int cellCount)
        {
            var config = new MapGenConfig
            {
                Seed = seed,
                CellCount = cellCount,
                Template = HeightmapTemplateType.Continents,
                RiverThreshold = 180f,
                RiverTraceThreshold = 10f,
                MinRiverVertices = 8
            };

            return MapGenPipeline.Generate(config);
        }

        static int CountLand(List<Cell> cells)
        {
            int count = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].IsLand)
                    count++;
            }

            return count;
        }
    }
}
