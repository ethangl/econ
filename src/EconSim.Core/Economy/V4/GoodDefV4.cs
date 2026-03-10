using System;
using System.Collections.Generic;
using MapGen.Core;

namespace EconSim.Core.Economy.V4
{
    public enum GoodTypeV4
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

    public enum NeedTierV4
    {
        Staple,
        Basic,
        Comfort,
        Luxury,
    }

    public readonly struct GoodDefV4
    {
        public readonly GoodTypeV4 Type;
        public readonly string Name;
        public readonly NeedTierV4 Tier;

        /// <summary>Relative worth for pricing. Wheat=1, Silk=50.</summary>
        public readonly float Value;

        /// <summary>Physical mass/volume for transport cost. High = expensive to move.</summary>
        public readonly float Bulk;

        /// <summary>Per-biome extraction yield (kg/person/day). Null for facility-produced goods.</summary>
        public readonly Dictionary<int, float> BiomeYields;

        public GoodDefV4(GoodTypeV4 type, string name, NeedTierV4 tier, float value, float bulk,
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

    public static class GoodsV4
    {
        public static readonly GoodDefV4[] Defs;
        public static readonly int Count;

        // Flat arrays for hot-path access
        public static readonly string[] Names;
        public static readonly float[] Value;
        public static readonly float[] Bulk;
        public static readonly NeedTierV4[] Tier;
        public static readonly float[,] BiomeYield;

        /// <summary>Good indices per need tier.</summary>
        public static readonly int[] StapleGoods;
        public static readonly int[] BasicGoods;
        public static readonly int[] ComfortGoods;
        public static readonly int[] LuxuryGoods;

        static GoodsV4()
        {
            Defs = new[]
            {
                // ── Biome-extracted (14) ──
                new GoodDefV4(GoodTypeV4.Wheat, "wheat", NeedTierV4.Staple, 1f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Floodplain, 1.40f }, { (int)BiomeId.Grassland, 1.28f },
                        { (int)BiomeId.Savanna, 1.05f }, { (int)BiomeId.Woodland, 0.98f },
                        { (int)BiomeId.TemperateForest, 0.87f }, { (int)BiomeId.TropicalDryForest, 0.92f },
                        { (int)BiomeId.TropicalRainforest, 0.81f }, { (int)BiomeId.CoastalMarsh, 0.69f },
                        { (int)BiomeId.Wetland, 0.69f }, { (int)BiomeId.BorealForest, 0.58f },
                        { (int)BiomeId.Scrubland, 0.46f }, { (int)BiomeId.MountainShrub, 0.35f },
                        { (int)BiomeId.HotDesert, 0.23f }, { (int)BiomeId.ColdDesert, 0.23f },
                    }),
                new GoodDefV4(GoodTypeV4.Barley, "barley", NeedTierV4.Staple, 1f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Floodplain, 0.60f }, { (int)BiomeId.Grassland, 0.54f },
                        { (int)BiomeId.Savanna, 0.33f }, { (int)BiomeId.TemperateForest, 0.21f },
                        { (int)BiomeId.Woodland, 0.18f }, { (int)BiomeId.Tundra, 0.15f },
                        { (int)BiomeId.MountainShrub, 0.12f }, { (int)BiomeId.TropicalDryForest, 0.12f },
                        { (int)BiomeId.Wetland, 0.12f }, { (int)BiomeId.ColdDesert, 0.09f },
                    }),
                new GoodDefV4(GoodTypeV4.Fish, "fish", NeedTierV4.Staple, 1.5f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.CoastalMarsh, 0.60f }, { (int)BiomeId.Floodplain, 0.15f },
                        { (int)BiomeId.Wetland, 0.08f },
                    }),
                new GoodDefV4(GoodTypeV4.Meat, "meat", NeedTierV4.Staple, 2f, 8f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Woodland, 0.30f }, { (int)BiomeId.TemperateForest, 0.25f },
                        { (int)BiomeId.Savanna, 0.20f }, { (int)BiomeId.TropicalDryForest, 0.20f },
                        { (int)BiomeId.Grassland, 0.15f }, { (int)BiomeId.Floodplain, 0.15f },
                        { (int)BiomeId.Scrubland, 0.10f }, { (int)BiomeId.BorealForest, 0.10f },
                        { (int)BiomeId.MountainShrub, 0.08f }, { (int)BiomeId.CoastalMarsh, 0.08f },
                    }),
                new GoodDefV4(GoodTypeV4.Salt, "salt", NeedTierV4.Basic, 3f, 6f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.SaltFlat, 2.70f }, { (int)BiomeId.CoastalMarsh, 2.10f },
                        { (int)BiomeId.Wetland, 0.45f },
                    }),
                new GoodDefV4(GoodTypeV4.Timber, "timber", NeedTierV4.Basic, 2f, 10f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.BorealForest, 0.55f }, { (int)BiomeId.TemperateForest, 0.55f },
                        { (int)BiomeId.TropicalRainforest, 0.55f }, { (int)BiomeId.TropicalDryForest, 0.44f },
                        { (int)BiomeId.Woodland, 0.33f }, { (int)BiomeId.MountainShrub, 0.11f },
                        { (int)BiomeId.Wetland, 0.11f }, { (int)BiomeId.Scrubland, 0.11f },
                        { (int)BiomeId.Savanna, 0.11f },
                    }),
                new GoodDefV4(GoodTypeV4.Stone, "stone", NeedTierV4.Basic, 2f, 12f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.AlpineBarren, 0.40f }, { (int)BiomeId.MountainShrub, 0.30f },
                        { (int)BiomeId.HotDesert, 0.20f }, { (int)BiomeId.ColdDesert, 0.20f },
                        { (int)BiomeId.SaltFlat, 0.10f }, { (int)BiomeId.Scrubland, 0.10f },
                    }),
                new GoodDefV4(GoodTypeV4.Iron, "iron", NeedTierV4.Basic, 5f, 10f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.AlpineBarren, 1.20f }, { (int)BiomeId.MountainShrub, 0.90f },
                        { (int)BiomeId.HotDesert, 0.75f }, { (int)BiomeId.ColdDesert, 0.75f },
                        { (int)BiomeId.Scrubland, 0.45f },
                    }),
                new GoodDefV4(GoodTypeV4.Wool, "wool", NeedTierV4.Basic, 4f, 6f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Grassland, 0.35f }, { (int)BiomeId.Savanna, 0.25f },
                        { (int)BiomeId.MountainShrub, 0.15f }, { (int)BiomeId.Scrubland, 0.15f },
                        { (int)BiomeId.Floodplain, 0.10f }, { (int)BiomeId.Woodland, 0.10f },
                        { (int)BiomeId.Tundra, 0.05f }, { (int)BiomeId.ColdDesert, 0.05f },
                        { (int)BiomeId.TemperateForest, 0.05f },
                    }),
                new GoodDefV4(GoodTypeV4.Leather, "leather", NeedTierV4.Comfort, 8f, 4f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Grassland, 0.12f }, { (int)BiomeId.Savanna, 0.10f },
                        { (int)BiomeId.Woodland, 0.08f }, { (int)BiomeId.Floodplain, 0.08f },
                        { (int)BiomeId.TemperateForest, 0.06f }, { (int)BiomeId.Scrubland, 0.06f },
                    }),
                new GoodDefV4(GoodTypeV4.Grapes, "grapes", NeedTierV4.Basic, 3f, 6f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Woodland, 0.25f }, { (int)BiomeId.Scrubland, 0.20f },
                        { (int)BiomeId.Grassland, 0.15f }, { (int)BiomeId.TemperateForest, 0.10f },
                    }),
                // Spices and Silk are now raw material inputs for luxury facilities
                new GoodDefV4(GoodTypeV4.Spices, "spices", NeedTierV4.Basic, 8f, 1f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.TropicalRainforest, 0.06f }, { (int)BiomeId.TropicalDryForest, 0.04f },
                        { (int)BiomeId.Savanna, 0.02f },
                        { (int)BiomeId.Woodland, 0.01f }, { (int)BiomeId.Scrubland, 0.01f },
                    }),
                new GoodDefV4(GoodTypeV4.Silk, "silk", NeedTierV4.Basic, 10f, 1f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.TropicalRainforest, 0.06f }, { (int)BiomeId.TropicalDryForest, 0.05f },
                        { (int)BiomeId.Savanna, 0.03f }, { (int)BiomeId.Woodland, 0.02f },
                        { (int)BiomeId.TemperateForest, 0.01f },
                    }),
                new GoodDefV4(GoodTypeV4.Candles, "candles", NeedTierV4.Comfort, 6f, 3f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.Grassland, 0.08f }, { (int)BiomeId.Woodland, 0.06f },
                        { (int)BiomeId.BorealForest, 0.04f }, { (int)BiomeId.TemperateForest, 0.04f },
                        { (int)BiomeId.Savanna, 0.05f }, { (int)BiomeId.Floodplain, 0.06f },
                    }),

                // ── Facility-produced — comfort (5) ──
                new GoodDefV4(GoodTypeV4.Bread, "bread", NeedTierV4.Comfort, 6f, 4f),
                new GoodDefV4(GoodTypeV4.Ale, "ale", NeedTierV4.Comfort, 5f, 6f),
                new GoodDefV4(GoodTypeV4.Tools, "tools", NeedTierV4.Comfort, 12f, 6f),
                new GoodDefV4(GoodTypeV4.Clothes, "clothes", NeedTierV4.Comfort, 10f, 3f),
                new GoodDefV4(GoodTypeV4.Furniture, "furniture", NeedTierV4.Comfort, 15f, 10f),

                // ── Special ──
                new GoodDefV4(GoodTypeV4.Gold, "gold", NeedTierV4.Luxury, 0f, 0f,
                    new Dictionary<int, float> {
                        { (int)BiomeId.AlpineBarren, 0.02f }, { (int)BiomeId.MountainShrub, 0.01f },
                    }),

                // ── Facility-produced — luxury (5) ──
                new GoodDefV4(GoodTypeV4.Feast, "feast", NeedTierV4.Luxury, 25f, 4f),
                new GoodDefV4(GoodTypeV4.FineClothes, "fine clothes", NeedTierV4.Luxury, 35f, 2f),
                new GoodDefV4(GoodTypeV4.Jewelry, "jewelry", NeedTierV4.Luxury, 80f, 0.5f),
                new GoodDefV4(GoodTypeV4.FineFurniture, "fine furniture", NeedTierV4.Luxury, 40f, 8f),
                new GoodDefV4(GoodTypeV4.Wine, "wine", NeedTierV4.Luxury, 20f, 4f),

                // Silver: wider distribution than gold, minted into coin (Value=0 → not traded/consumed)
                // Yields ~5-10x gold production volume, but 5x less coin per kg
                // MUST be last in array: enum Silver=25 must match array index 25
                new GoodDefV4(GoodTypeV4.Silver, "silver", NeedTierV4.Basic, 0f, 0f,
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
            Tier = new NeedTierV4[Count];

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
                    case NeedTierV4.Staple:  staples.Add(i); break;
                    case NeedTierV4.Basic:   basics.Add(i); break;
                    case NeedTierV4.Comfort: comforts.Add(i); break;
                    case NeedTierV4.Luxury:  luxuries.Add(i); break;
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
