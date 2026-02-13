using System;
using System.Collections.Generic;
using System.Text;

namespace PopGen.Core
{
    public readonly struct PopRealmName
    {
        public string Name { get; }
        public string FullName { get; }
        public string GovernmentForm { get; }

        public PopRealmName(string name, string fullName, string governmentForm)
        {
            Name = name;
            FullName = fullName;
            GovernmentForm = governmentForm;
        }
    }

    public readonly struct PopProvinceName
    {
        public string Name { get; }
        public string FullName { get; }

        public PopProvinceName(string name, string fullName)
        {
            Name = name;
            FullName = fullName;
        }
    }

    /// <summary>
    /// Deterministic political name generation for PopGen entities.
    /// </summary>
    public static class PopNameGenerator
    {
        public static PopRealmName GenerateRealmName(int realmId, CultureType culture, PopGenSeed seed, ISet<string> usedNames)
        {
            var rng = new PopRandom(DeriveSeed(seed.Value, 0x01u, realmId, 0));
            string core = BuildCoreName(ref rng, culture, culture.RealmSuffixes);
            string name = EnsureUnique(core, realmId, usedNames);
            string governmentForm = culture.GovernmentForms[rng.NextInt(culture.GovernmentForms.Length)];
            string fullName = $"{governmentForm} of {name}";
            return new PopRealmName(name, fullName, governmentForm);
        }

        public static PopProvinceName GenerateProvinceName(int provinceId, int realmId, CultureType culture, PopGenSeed seed, ISet<string> usedNames)
        {
            var rng = new PopRandom(DeriveSeed(seed.Value, 0x02u, provinceId, realmId));
            string core = BuildCoreName(ref rng, culture, culture.ProvinceSuffixes);
            string name = rng.NextInt(100) < 24
                ? $"{culture.DirectionalPrefixes[rng.NextInt(culture.DirectionalPrefixes.Length)]} {core}"
                : core;

            name = EnsureUnique(name, provinceId, usedNames);
            string fullName = rng.NextInt(100) < 50
                ? $"Province of {name}"
                : $"{name} Province";

            return new PopProvinceName(name, fullName);
        }

        public static string GenerateCountyName(int countyId, int provinceId, int realmId, CultureType culture, PopGenSeed seed, ISet<string> usedNames)
        {
            var rng = new PopRandom(DeriveSeed(seed.Value, 0x03u, countyId, provinceId ^ (realmId << 8)));
            string core = BuildCoreName(ref rng, culture, culture.CountySuffixes);
            string name = rng.NextInt(100) < 34 ? $"{core} County" : core;
            return EnsureUnique(name, countyId, usedNames);
        }

        public static string GenerateCultureName(int cultureId, CultureType culture, PopGenSeed seed)
        {
            var rng = new PopRandom(DeriveSeed(seed.Value, 0x04u, cultureId, 0));
            string core = BuildCoreName(ref rng, culture, culture.RealmSuffixes);
            return core;
        }

        static string BuildCoreName(ref PopRandom rng, CultureType culture, string[] suffixes)
        {
            int roll = rng.NextInt(100);
            int syllableCount = roll < 55 ? 2 : (roll < 90 ? 3 : 4);
            var sb = new StringBuilder(24);

            sb.Append(culture.LeadingOnsets[rng.NextInt(culture.LeadingOnsets.Length)]);
            for (int i = 0; i < syllableCount; i++)
            {
                if (i > 0)
                    sb.Append(culture.MedialOnsets[rng.NextInt(culture.MedialOnsets.Length)]);

                sb.Append(culture.Vowels[rng.NextInt(culture.Vowels.Length)]);
                if (rng.NextInt(100) < 66)
                    sb.Append(culture.Codas[rng.NextInt(culture.Codas.Length)]);
            }

            string root = NormalizeRoot(sb.ToString(), culture);
            string suffix = suffixes[rng.NextInt(suffixes.Length)];
            string merged = MergeRootAndSuffix(root, suffix);
            return ToTitleCase(merged);
        }

        static string EnsureUnique(string candidate, int id, ISet<string> usedNames)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                candidate = "Unnamed";

            if (usedNames == null)
                return candidate;

            if (usedNames.Add(candidate))
                return candidate;

            string roman = $"{candidate} {ToRoman((id % 12) + 2)}";
            if (usedNames.Add(roman))
                return roman;

            int n = 2;
            while (true)
            {
                string numbered = $"{candidate} {n}";
                if (usedNames.Add(numbered))
                    return numbered;

                n++;
            }
        }

        static string MergeRootAndSuffix(string root, string suffix)
        {
            if (string.IsNullOrEmpty(root))
                root = "Alden";
            if (string.IsNullOrEmpty(suffix))
                return root;

            char last = char.ToLowerInvariant(root[root.Length - 1]);
            char first = char.ToLowerInvariant(suffix[0]);
            if (last == first)
                return root + suffix.Substring(1);

            return root + suffix;
        }

        static string NormalizeRoot(string value, CultureType culture)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Alden";

            var sb = new StringBuilder(value.Length);
            char prev = '\0';
            int runLength = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = char.ToLowerInvariant(value[i]);
                if (!char.IsLetter(ch))
                    continue;

                if (ch == prev)
                {
                    runLength++;
                    if (runLength > 2)
                        continue; // allow pairs (gemination/long vowels) but not triples
                }
                else
                {
                    runLength = 1;
                }

                sb.Append(ch);
                prev = ch;
                if (sb.Length >= 14)
                    break;
            }

            while (sb.Length < 4)
                sb.Append(culture.Vowels[sb.Length % culture.Vowels.Length][0]);

            return sb.ToString();
        }

        static string ToTitleCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unnamed";

            string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "Unnamed";

            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].ToLowerInvariant();
                if (p.Length == 0)
                    continue;

                parts[i] = p.Length == 1
                    ? char.ToUpperInvariant(p[0]).ToString()
                    : char.ToUpperInvariant(p[0]) + p.Substring(1);
            }

            return string.Join(" ", parts);
        }

        static string ToRoman(int value)
        {
            if (value <= 0)
                return "I";

            var map = new (int value, string symbol)[]
            {
                (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
                (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
                (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
            };

            var sb = new StringBuilder();
            int remaining = value;
            for (int i = 0; i < map.Length && remaining > 0; i++)
            {
                while (remaining >= map[i].value)
                {
                    sb.Append(map[i].symbol);
                    remaining -= map[i].value;
                }
            }

            return sb.ToString();
        }

        static int DeriveSeed(int rootSeed, uint stream, int id, int parent)
        {
            uint x = (uint)rootSeed;
            x ^= stream * 0x9E3779B9u;
            x ^= (uint)id * 0x85EBCA6Bu;
            x ^= (uint)parent * 0xC2B2AE35u;
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;
            if (x == 0)
                x = 0x6D2B79F5u;

            return (int)(x & 0x7FFFFFFF);
        }

        struct PopRandom
        {
            uint _state;

            public PopRandom(int seed)
            {
                _state = (uint)seed;
                if (_state == 0)
                    _state = 0x6D2B79F5u;
            }

            public int NextInt(int maxExclusive)
            {
                if (maxExclusive <= 1)
                    return 0;

                return (int)(NextUInt() % (uint)maxExclusive);
            }

            uint NextUInt()
            {
                uint x = _state;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                _state = x;
                return x;
            }
        }
    }
}
