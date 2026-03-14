using System;
using System.Collections.Generic;
using MapGen.Core;

namespace EconSim.Core.Economy
{
    public enum GoodType
    {
        // Biome-extracted (14)
        Wheat = 0,
        Barley = 1,
        Fish = 2,
        Meat = 3,
        Salt = 4,
        Timber = 5,
        Stone = 6,
        Iron = 7,
        Wool = 8,
        Leather = 9,
        Grapes = 10,
        Spices = 11,
        Silk = 12,
        Candles = 13,

        // Facility-produced — comfort (5)
        Bread = 14,
        Ale = 15,
        Tools = 16,
        Clothes = 17,
        Furniture = 18,

        // Facility-produced — luxury (5)
        Feast = 20,
        FineClothes = 21,
        Jewelry = 22,
        FineFurniture = 23,
        Wine = 24,

        // Special (2)
        Gold = 19,
        Silver = 25,
    }

    public enum NeedTier
    {
        Staple,
        Basic,
        Comfort,
        Luxury,
    }

    public readonly struct GoodDef
    {
        public readonly GoodType Type;
        public readonly string Name;
        public readonly NeedTier Tier;

        /// <summary>Relative worth for pricing. Wheat=1, Silk=50.</summary>
        public readonly float Value;

        /// <summary>Physical mass/volume for transport cost. High = expensive to move.</summary>
        public readonly float Bulk;

        /// <summary>Per-biome extraction yield (kg/person/day). Null for facility-produced goods.</summary>
        public readonly Dictionary<int, float> BiomeYields;

        public GoodDef(GoodType type, string name, NeedTier tier, float value, float bulk,
            Dictionary<int, float> biomeYields = null)
        {
            Type = type;
            Name = name;
            Tier = tier;
            Value = value;
            Bulk = bulk;
            BiomeYields = biomeYields;
        }
    }

    public static class Goods
    {
        public static readonly GoodDef[] Defs;
        public static readonly int Count;

        // Flat arrays for hot-path access
        public static readonly string[] Names;
        public static readonly float[] Value;
        public static readonly float[] Bulk;
        public static readonly NeedTier[] Tier;
        public static readonly float[,] BiomeYield;

        /// <summary>Good indices per need tier.</summary>
        public static readonly int[] StapleGoods;
        public static readonly int[] BasicGoods;
        public static readonly int[] ComfortGoods;
        public static readonly int[] LuxuryGoods;

        static Goods()
        {
            Defs = new[]
            {
                // ── Biome-extracted (14) ──
                new GoodDef(GoodType.Wheat, "wheat", NeedTier.Staple, 1f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Floodplain, 1.40f }, { (int)BiomeId.Grassland, 1.28f },
                        { (int)BiomeId.Savanna, 1.05f }, { (int)BiomeId.Woodland, 0.98f },
                        { (int)BiomeId.TemperateForest, 0.87f }, { (int)BiomeId.TropicalDryForest, 0.92f },
                        { (int)BiomeId.TropicalRainforest, 0.81f }, { (int)BiomeId.CoastalMarsh, 0.69f },
                        { (int)BiomeId.Wetland, 0.69f }, { (int)BiomeId.BorealForest, 0.58f },
                        { (int)BiomeId.Scrubland, 0.46f }, { (int)BiomeId.MountainShrub, 0.35f },
                        { (int)BiomeId.HotDesert, 0.23f }, { (int)BiomeId.ColdDesert, 0.23f },
                    }),
                new GoodDef(GoodType.Barley, "barley", NeedTier.Staple, 1f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Floodplain, 0.60f }, { (int)BiomeId.Grassland, 0.54f },
                        { (int)BiomeId.Savanna, 0.33f }, { (int)BiomeId.TemperateForest, 0.21f },
                        { (int)BiomeId.Woodland, 0.18f }, { (int)BiomeId.Tundra, 0.15f },
                        { (int)BiomeId.MountainShrub, 0.12f }, { (int)BiomeId.TropicalDryForest, 0.12f },
                        { (int)BiomeId.Wetland, 0.12f }, { (int)BiomeId.ColdDesert, 0.09f },
                    }),
                new GoodDef(GoodType.Fish, "fish", NeedTier.Staple, 1.5f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.CoastalMarsh, 0.60f }, { (int)BiomeId.Floodplain, 0.15f },
                        { (int)BiomeId.Wetland, 0.08f },
                    }),
                new GoodDef(GoodType.Meat, "meat", NeedTier.Staple, 2f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Woodland, 0.30f }, { (int)BiomeId.TemperateForest, 0.25f },
                        { (int)BiomeId.Savanna, 0.20f }, { (int)BiomeId.TropicalDryForest, 0.20f },
                        { (int)BiomeId.Grassland, 0.15f }, { (int)BiomeId.Floodplain, 0.15f },
                        { (int)BiomeId.Scrubland, 0.10f }, { (int)BiomeId.BorealForest, 0.10f },
                        { (int)BiomeId.MountainShrub, 0.08f }, { (int)BiomeId.CoastalMarsh, 0.08f },
                    }),
                new GoodDef(GoodType.Salt, "salt", NeedTier.Basic, 3f, 6f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.SaltFlat, 2.70f }, { (int)BiomeId.CoastalMarsh, 2.10f },
                        { (int)BiomeId.Wetland, 0.45f },
                    }),
                new GoodDef(GoodType.Timber, "timber", NeedTier.Basic, 2f, 10f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.BorealForest, 0.55f }, { (int)BiomeId.TemperateForest, 0.55f },
                        { (int)BiomeId.TropicalRainforest, 0.55f }, { (int)BiomeId.TropicalDryForest, 0.44f },
                        { (int)BiomeId.Woodland, 0.33f }, { (int)BiomeId.MountainShrub, 0.11f },
                        { (int)BiomeId.Wetland, 0.11f }, { (int)BiomeId.Scrubland, 0.11f },
                        { (int)BiomeId.Savanna, 0.11f },
                    }),
                new GoodDef(GoodType.Stone, "stone", NeedTier.Basic, 2f, 12f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.AlpineBarren, 0.40f }, { (int)BiomeId.MountainShrub, 0.30f },
                        { (int)BiomeId.HotDesert, 0.20f }, { (int)BiomeId.ColdDesert, 0.20f },
                        { (int)BiomeId.SaltFlat, 0.10f }, { (int)BiomeId.Scrubland, 0.10f },
                    }),
                new GoodDef(GoodType.Iron, "iron", NeedTier.Basic, 5f, 10f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.AlpineBarren, 1.20f }, { (int)BiomeId.MountainShrub, 0.90f },
                        { (int)BiomeId.HotDesert, 0.75f }, { (int)BiomeId.ColdDesert, 0.75f },
                        { (int)BiomeId.Scrubland, 0.45f },
                    }),
                new GoodDef(GoodType.Wool, "wool", NeedTier.Basic, 4f, 6f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Grassland, 0.35f }, { (int)BiomeId.Savanna, 0.25f },
                        { (int)BiomeId.MountainShrub, 0.15f }, { (int)BiomeId.Scrubland, 0.15f },
                        { (int)BiomeId.Floodplain, 0.10f }, { (int)BiomeId.Woodland, 0.10f },
                        { (int)BiomeId.Tundra, 0.05f }, { (int)BiomeId.ColdDesert, 0.05f },
                        { (int)BiomeId.TemperateForest, 0.05f },
                    }),
                new GoodDef(GoodType.Leather, "leather", NeedTier.Comfort, 8f, 4f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Grassland, 0.12f }, { (int)BiomeId.Savanna, 0.10f },
                        { (int)BiomeId.Woodland, 0.08f }, { (int)BiomeId.Floodplain, 0.08f },
                        { (int)BiomeId.TemperateForest, 0.06f }, { (int)BiomeId.Scrubland, 0.06f },
                    }),
                new GoodDef(GoodType.Grapes, "grapes", NeedTier.Basic, 3f, 6f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Woodland, 0.25f }, { (int)BiomeId.Scrubland, 0.20f },
                        { (int)BiomeId.Grassland, 0.15f }, { (int)BiomeId.TemperateForest, 0.10f },
                    }),
                // Spices and Silk are now raw material inputs for luxury facilities
                new GoodDef(GoodType.Spices, "spices", NeedTier.Basic, 8f, 1f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.TropicalRainforest, 0.06f }, { (int)BiomeId.TropicalDryForest, 0.04f },
                        { (int)BiomeId.Savanna, 0.02f },
                        { (int)BiomeId.Woodland, 0.01f }, { (int)BiomeId.Scrubland, 0.01f },
                    }),
                new GoodDef(GoodType.Silk, "silk", NeedTier.Basic, 10f, 1f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.TropicalRainforest, 0.06f }, { (int)BiomeId.TropicalDryForest, 0.05f },
                        { (int)BiomeId.Savanna, 0.03f }, { (int)BiomeId.Woodland, 0.02f },
                        { (int)BiomeId.TemperateForest, 0.01f },
                    }),
                new GoodDef(GoodType.Candles, "candles", NeedTier.Comfort, 6f, 3f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Grassland, 0.08f }, { (int)BiomeId.Woodland, 0.06f },
                        { (int)BiomeId.BorealForest, 0.04f }, { (int)BiomeId.TemperateForest, 0.04f },
                        { (int)BiomeId.Savanna, 0.05f }, { (int)BiomeId.Floodplain, 0.06f },
                    }),

                // ── Facility-produced — comfort (5) ──
                new GoodDef(GoodType.Bread, "bread", NeedTier.Comfort, 6f, 4f),
                new GoodDef(GoodType.Ale, "ale", NeedTier.Comfort, 5f, 6f),
                new GoodDef(GoodType.Tools, "tools", NeedTier.Comfort, 12f, 6f),
                new GoodDef(GoodType.Clothes, "clothes", NeedTier.Comfort, 10f, 3f),
                new GoodDef(GoodType.Furniture, "furniture", NeedTier.Comfort, 15f, 10f),

                // ── Special ──
                new GoodDef(GoodType.Gold, "gold", NeedTier.Luxury, 0f, 0f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.AlpineBarren, 0.02f }, { (int)BiomeId.MountainShrub, 0.01f },
                    }),

                // ── Facility-produced — luxury (5) ──
                new GoodDef(GoodType.Feast, "feast", NeedTier.Luxury, 25f, 4f),
                new GoodDef(GoodType.FineClothes, "fine clothes", NeedTier.Luxury, 35f, 2f),
                new GoodDef(GoodType.Jewelry, "jewelry", NeedTier.Luxury, 80f, 0.5f),
                new GoodDef(GoodType.FineFurniture, "fine furniture", NeedTier.Luxury, 40f, 8f),
                new GoodDef(GoodType.Wine, "wine", NeedTier.Luxury, 20f, 4f),

                // Silver: wider distribution than gold, minted into coin (Value=0 → not traded/consumed)
                // Yields ~5-10x gold production volume, but 5x less coin per kg
                // MUST be last in array: enum Silver=25 must match array index 25
                new GoodDef(GoodType.Silver, "silver", NeedTier.Basic, 0f, 0f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.MountainShrub, 0.08f }, { (int)BiomeId.AlpineBarren, 0.06f },
                        { (int)BiomeId.Scrubland, 0.03f }, { (int)BiomeId.HotDesert, 0.02f },
                        { (int)BiomeId.ColdDesert, 0.02f }, { (int)BiomeId.TemperateForest, 0.01f },
                    }),
            };

            Count = Defs.Length;

            // Build flat arrays
            Names = new string[Count];
            Value = new float[Count];
            Bulk = new float[Count];
            Tier = new NeedTier[Count];

            var staples = new List<int>();
            var basics = new List<int>();
            var comforts = new List<int>();
            var luxuries = new List<int>();

            for (int i = 0; i < Count; i++)
            {
                var d = Defs[i];
                Names[i] = d.Name;
                Value[i] = d.Value;
                Bulk[i] = d.Bulk;
                Tier[i] = d.Tier;

                switch (d.Tier)
                {
                    case NeedTier.Staple:  staples.Add(i); break;
                    case NeedTier.Basic:   basics.Add(i); break;
                    case NeedTier.Comfort: comforts.Add(i); break;
                    case NeedTier.Luxury:  luxuries.Add(i); break;
                }
            }

            StapleGoods = staples.ToArray();
            BasicGoods = basics.ToArray();
            ComfortGoods = comforts.ToArray();
            LuxuryGoods = luxuries.ToArray();

            // Build biome yield table
            int biomeCount = Enum.GetValues(typeof(BiomeId)).Length;
            BiomeYield = new float[biomeCount, Count];
            for (int i = 0; i < Count; i++)
            {
                var yields = Defs[i].BiomeYields;
                if (yields == null) continue;
                foreach (var kv in yields)
                    BiomeYield[kv.Key, i] = kv.Value;
            }
        }
    }
}
