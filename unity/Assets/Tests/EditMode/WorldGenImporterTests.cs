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

        [Test]
        public void Convert_VegetationRoundTripsAndIsNotUniform()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(1357911);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 4500);
            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(map.Biomes.Vegetation, Is.Not.Null);
            Assert.That(map.Biomes.VegetationDensity, Is.Not.Null);
            Assert.That(map.Biomes.Vegetation.Length, Is.EqualTo(runtime.Cells.Count));
            Assert.That(map.Biomes.VegetationDensity.Length, Is.EqualTo(runtime.Cells.Count));

            var vegetationTypesOnLand = new HashSet<int>();
            float minDensityOnLand = float.MaxValue;
            float maxDensityOnLand = float.MinValue;

            for (int i = 0; i < runtime.Cells.Count; i++)
            {
                int expectedType = (int)map.Biomes.Vegetation[i];
                int actualType = runtime.Cells[i].VegetationTypeId;
                float expectedDensity = map.Biomes.VegetationDensity[i];
                float actualDensity = runtime.Cells[i].VegetationDensity;

                Assert.That(actualType, Is.EqualTo(expectedType), $"VegetationTypeId mismatch at cell {i}.");
                Assert.That(actualType, Is.InRange(0, 6), $"VegetationTypeId out of range at cell {i}.");
                Assert.That(actualDensity, Is.EqualTo(expectedDensity).Within(1e-6f), $"VegetationDensity mismatch at cell {i}.");
                Assert.That(actualDensity, Is.InRange(0f, 1f), $"VegetationDensity out of range at cell {i}.");

                if (!runtime.Cells[i].IsLand)
                    continue;

                vegetationTypesOnLand.Add(actualType);
                if (actualDensity < minDensityOnLand) minDensityOnLand = actualDensity;
                if (actualDensity > maxDensityOnLand) maxDensityOnLand = actualDensity;
            }

            Assert.That(vegetationTypesOnLand.Count, Is.GreaterThan(1), "Vegetation type is unexpectedly uniform across land cells.");
            Assert.That(maxDensityOnLand - minDensityOnLand, Is.GreaterThan(0.05f), "Vegetation density is unexpectedly uniform across land cells.");
        }

        [Test]
        public void Convert_CulturesRoundTripFromPopGenOutput()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(808080);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 3600);

            PopGenResult expected = PopGenPipeline.Generate(
                map,
                new PopGenConfig(),
                new PopGenSeed(context.PopGenSeed));

            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(runtime.Cultures, Is.Not.Null);
            Assert.That(runtime.CultureById, Is.Not.Null);
            Assert.That(runtime.Cultures.Count, Is.EqualTo(expected.Cultures.Length));
            Assert.That(runtime.CultureById.Count, Is.EqualTo(runtime.Cultures.Count));

            foreach (PopCulture expectedCulture in expected.Cultures)
            {
                Assert.That(runtime.CultureById.TryGetValue(expectedCulture.Id, out Culture actualCulture), Is.True,
                    $"Missing culture {expectedCulture.Id}.");
                Assert.That(actualCulture.Name, Is.EqualTo(expectedCulture.Name), $"Culture name mismatch for {expectedCulture.Id}.");
                Assert.That(actualCulture.TypeName, Is.EqualTo(expectedCulture.TypeName ?? "Generic"), $"Culture type mismatch for {expectedCulture.Id}.");
            }
        }

        [Test]
        public void Convert_CellCultureIdsMatchRealmCultureAssignments()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(909090);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 3600);
            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(runtime.RealmById, Is.Not.Null);
            Assert.That(runtime.CultureById, Is.Not.Null);

            for (int i = 0; i < runtime.Cells.Count; i++)
            {
                Cell cell = runtime.Cells[i];
                if (cell.RealmId <= 0)
                {
                    Assert.That(cell.CultureId, Is.EqualTo(0), $"Neutral cell {cell.Id} should not have a culture assignment.");
                    continue;
                }

                Assert.That(runtime.RealmById.TryGetValue(cell.RealmId, out Realm realm), Is.True,
                    $"Cell {cell.Id} references missing realm {cell.RealmId}.");
                Assert.That(realm.CultureId, Is.GreaterThan(0), $"Realm {realm.Id} should have a culture.");
                Assert.That(cell.CultureId, Is.EqualTo(realm.CultureId),
                    $"Cell {cell.Id} culture mismatch: cell={cell.CultureId}, realm={realm.CultureId}.");
                Assert.That(runtime.CultureById.ContainsKey(cell.CultureId), Is.True,
                    $"Cell {cell.Id} references missing culture {cell.CultureId}.");
            }
        }

        [Test]
        public void Convert_ReligionsRoundTripFromPopGenOutput()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(101010);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 3600);

            PopGenResult expected = PopGenPipeline.Generate(
                map,
                new PopGenConfig(),
                new PopGenSeed(context.PopGenSeed));

            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(runtime.Religions, Is.Not.Null);
            Assert.That(runtime.ReligionById, Is.Not.Null);
            Assert.That(runtime.Religions.Count, Is.EqualTo(expected.Religions.Length));
            Assert.That(runtime.ReligionById.Count, Is.EqualTo(runtime.Religions.Count));

            foreach (PopReligion expectedReligion in expected.Religions)
            {
                Assert.That(runtime.ReligionById.TryGetValue(expectedReligion.Id, out Religion actualReligion), Is.True,
                    $"Missing religion {expectedReligion.Id}.");
                Assert.That(actualReligion.Name, Is.EqualTo(expectedReligion.Name),
                    $"Religion name mismatch for {expectedReligion.Id}.");
                Assert.That(actualReligion.TypeName, Is.EqualTo(expectedReligion.TypeName ?? "Unknown"),
                    $"Religion type mismatch for {expectedReligion.Id}.");
            }
        }

        [Test]
        public void Convert_CellReligionIdsMatchCultureReligionAssignments()
        {
            WorldGenerationContext context = WorldGenerationContext.FromRootSeed(111111);
            MapGenResult map = GenerateMap(context.MapGenSeed, cellCount: 3600);
            MapData runtime = WorldGenImporter.Convert(map, context);

            Assert.That(runtime.RealmById, Is.Not.Null);
            Assert.That(runtime.CultureById, Is.Not.Null);
            Assert.That(runtime.ReligionById, Is.Not.Null);

            for (int i = 0; i < runtime.Cells.Count; i++)
            {
                Cell cell = runtime.Cells[i];
                if (cell.RealmId <= 0)
                {
                    Assert.That(cell.ReligionId, Is.EqualTo(0),
                        $"Neutral cell {cell.Id} should not have a religion assignment.");
                    continue;
                }

                Assert.That(runtime.RealmById.TryGetValue(cell.RealmId, out Realm realm), Is.True,
                    $"Cell {cell.Id} references missing realm {cell.RealmId}.");
                Assert.That(realm.CultureId, Is.GreaterThan(0),
                    $"Realm {realm.Id} should have a culture.");
                Assert.That(runtime.CultureById.TryGetValue(realm.CultureId, out Culture culture), Is.True,
                    $"Realm {realm.Id} references missing culture {realm.CultureId}.");
                Assert.That(culture.ReligionId, Is.GreaterThan(0),
                    $"Culture {culture.Id} should have a religion.");
                Assert.That(cell.ReligionId, Is.EqualTo(culture.ReligionId),
                    $"Cell {cell.Id} religion mismatch: cell={cell.ReligionId}, culture={culture.ReligionId}.");
                Assert.That(runtime.ReligionById.ContainsKey(cell.ReligionId), Is.True,
                    $"Cell {cell.Id} references missing religion {cell.ReligionId}.");
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
