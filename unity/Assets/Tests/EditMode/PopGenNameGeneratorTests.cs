using System;
using System.Collections.Generic;
using MapGen.Core;
using PopGen.Core;
using NUnit.Framework;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("MapGen")]
    public class PopGenNameGeneratorTests
    {
        [Test]
        public void GeneratePoliticalNames_AreDeterministicForSameSeed()
        {
            MapGenResult map = GenerateMap(seed: 8675309, cellCount: 3000);
            var config = new PopGenConfig();
            var seed = new PopGenSeed(424242);

            PopGenResult first = PopGenPipeline.Generate(map, config, seed);
            PopGenResult second = PopGenPipeline.Generate(map, config, seed);

            AssertNameArraysEqual(first.Realms, second.Realms, r => r.Name);
            AssertNameArraysEqual(first.Provinces, second.Provinces, p => p.Name);
            AssertNameArraysEqual(first.Counties, second.Counties, c => c.Name);
        }

        [Test]
        public void GeneratePoliticalNames_ChangeWhenPopSeedChanges()
        {
            MapGenResult map = GenerateMap(seed: 1357911, cellCount: 2800);
            var config = new PopGenConfig();

            PopGenResult lowSeed = PopGenPipeline.Generate(map, config, new PopGenSeed(10101));
            PopGenResult highSeed = PopGenPipeline.Generate(map, config, new PopGenSeed(90909));

            bool differs = HasNameDifference(lowSeed.Realms, highSeed.Realms, r => r.Name)
                || HasNameDifference(lowSeed.Provinces, highSeed.Provinces, p => p.Name)
                || HasNameDifference(lowSeed.Counties, highSeed.Counties, c => c.Name);

            Assert.That(differs, Is.True, "Expected at least one political name to differ when PopGenSeed changes.");
        }

        [Test]
        public void GeneratePoliticalNames_AreUniqueAndNotLegacyPlaceholders()
        {
            MapGenResult map = GenerateMap(seed: 24681357, cellCount: 3200);
            PopGenResult result = PopGenPipeline.Generate(map, new PopGenConfig(), new PopGenSeed(777777));

            AssertNamesAreUniqueAndGenerated(result.Realms, "Kingdom", r => r.Name, r => r.Id);
            AssertNamesAreUniqueAndGenerated(result.Provinces, "Province", p => p.Name, p => p.Id);
            AssertNamesAreUniqueAndGenerated(result.Counties, "County", c => c.Name, c => c.Id);
        }

        [Test]
        public void Cultures_AreGeneratedAndNonEmpty()
        {
            MapGenResult map = GenerateMap(seed: 55555, cellCount: 3000);
            PopGenResult result = PopGenPipeline.Generate(map, new PopGenConfig(), new PopGenSeed(111111));

            Assert.That(result.Cultures, Is.Not.Null);
            Assert.That(result.Cultures.Length, Is.GreaterThanOrEqualTo(1),
                "Expected at least one culture.");

            int expectedCount = Math.Max(1, result.Realms.Length / 2);
            Assert.That(result.Cultures.Length, Is.EqualTo(expectedCount),
                $"Expected culture count = max(1, realmCount/2) = {expectedCount}");

            foreach (var culture in result.Cultures)
            {
                Assert.That(culture.Id, Is.GreaterThan(0), "Culture Id must be positive.");
                Assert.That(string.IsNullOrWhiteSpace(culture.Name), Is.False,
                    $"Culture {culture.Id} has empty name.");
            }
        }

        [Test]
        public void Realms_HaveValidCultureId()
        {
            MapGenResult map = GenerateMap(seed: 77777, cellCount: 3000);
            PopGenResult result = PopGenPipeline.Generate(map, new PopGenConfig(), new PopGenSeed(222222));

            var cultureIds = new HashSet<int>();
            foreach (var c in result.Cultures)
                cultureIds.Add(c.Id);

            foreach (var realm in result.Realms)
            {
                Assert.That(realm.CultureId, Is.GreaterThan(0),
                    $"Realm {realm.Id} ({realm.Name}) has no culture assigned.");
                Assert.That(cultureIds.Contains(realm.CultureId), Is.True,
                    $"Realm {realm.Id} references invalid CultureId {realm.CultureId}.");
            }
        }

        [Test]
        public void Cultures_AreDeterministicForSameSeed()
        {
            MapGenResult map = GenerateMap(seed: 99999, cellCount: 3000);
            var config = new PopGenConfig();
            var seed = new PopGenSeed(333333);

            PopGenResult first = PopGenPipeline.Generate(map, config, seed);
            PopGenResult second = PopGenPipeline.Generate(map, config, seed);

            AssertNameArraysEqual(first.Cultures, second.Cultures, c => c.Name);
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

        static void AssertNameArraysEqual<T>(T[] expected, T[] actual, Func<T, string> selector)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Entity count mismatch.");
            for (int i = 0; i < expected.Length; i++)
                Assert.That(selector(actual[i]), Is.EqualTo(selector(expected[i])), $"Name mismatch at index {i}.");
        }

        static bool HasNameDifference<T>(T[] first, T[] second, Func<T, string> selector)
        {
            int n = Math.Min(first.Length, second.Length);
            for (int i = 0; i < n; i++)
            {
                if (!string.Equals(selector(first[i]), selector(second[i]), StringComparison.Ordinal))
                    return true;
            }

            return first.Length != second.Length;
        }

        static void AssertNamesAreUniqueAndGenerated<T>(
            T[] source,
            string legacyPrefix,
            Func<T, string> selector,
            Func<T, int> idSelector)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < source.Length; i++)
            {
                string name = selector(source[i]);
                int id = idSelector(source[i]);

                Assert.That(string.IsNullOrWhiteSpace(name), Is.False, $"Missing name for id {id}.");
                Assert.That(set.Add(name), Is.True, $"Duplicate name '{name}' generated for id {id}.");
                Assert.That(IsLegacyPlaceholder(name, legacyPrefix), Is.False, $"Legacy placeholder name detected: '{name}'.");
            }
        }

        static bool IsLegacyPlaceholder(string value, string prefix)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string patternPrefix = prefix + " ";
            if (!value.StartsWith(patternPrefix, StringComparison.Ordinal))
                return false;

            if (value.Length <= patternPrefix.Length)
                return false;

            for (int i = patternPrefix.Length; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                    return false;
            }

            return true;
        }
    }
}
