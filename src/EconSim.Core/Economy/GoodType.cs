using System;
using System.Collections.Generic;

namespace EconSim.Core.Economy
{
    public enum GoodType
    {
        Food = 0,
        Timber = 1,
        IronOre = 2,
        GoldOre = 3,
        SilverOre = 4,
        Salt = 5,
        Wool = 6,
        Stone = 7,
        Ale = 8,
        Clay = 9,
        Pottery = 10,
    }

    public static class Goods
    {
        /// <summary>Single source of truth for all per-good data.</summary>
        public static readonly GoodDef[] Defs;

        /// <summary>Number of goods. Identical to Defs.Length.</summary>
        public static readonly int Count;

        // ── Flat arrays extracted from Defs for hot-path performance ──

        /// <summary>Daily consumption per person (kg/day), indexed by GoodType.</summary>
        public static readonly float[] ConsumptionPerPop;

        /// <summary>Serialization names, indexed by GoodType.</summary>
        public static readonly string[] Names;

        /// <summary>Anchor price in Crowns per kg, indexed by GoodType. Gold/Silver = 0 (not traded).</summary>
        public static readonly float[] BasePrice;

        /// <summary>Minimum price (10% of base) — price floor.</summary>
        public static readonly float[] MinPrice;

        /// <summary>Maximum price (10x base) — price cap.</summary>
        public static readonly float[] MaxPrice;

        /// <summary>County administrative consumption per capita per day (building upkeep).</summary>
        public static readonly float[] CountyAdminPerPop;

        /// <summary>Provincial administrative consumption per capita per day (infrastructure).</summary>
        public static readonly float[] ProvinceAdminPerPop;

        /// <summary>Realm administrative consumption per capita per day (military upkeep).</summary>
        public static readonly float[] RealmAdminPerPop;

        /// <summary>Monthly retention factor pow(1 - spoilageRate, 30), indexed by GoodType.</summary>
        public static readonly float[] MonthlyRetention;

        /// <summary>Goods that can be traded on the inter-realm market.</summary>
        public static readonly int[] TradeableGoods;

        /// <summary>Buy priority order — staples first, stone last (infrastructure can wait).</summary>
        public static readonly int[] BuyPriority =
        {
            (int)GoodType.Food,
            (int)GoodType.Ale,
            (int)GoodType.IronOre,
            (int)GoodType.Salt,
            (int)GoodType.Wool,
            (int)GoodType.Pottery,
            (int)GoodType.Timber,
            (int)GoodType.Stone,
            (int)GoodType.Clay,
        };

        // ── Minting constants (process parameters, not per-good data) ──

        /// <summary>Fraction of pure metal per kg of ore (smelting yield).</summary>
        public const float GoldSmeltingYield = 0.01f;   // 1% — rich medieval deposits
        public const float SilverSmeltingYield = 0.05f;  // 5% — silver ores are richer

        /// <summary>Crowns minted per kg of pure metal.</summary>
        public const float CrownsPerKgGold = 1000f;
        public const float CrownsPerKgSilver = 100f;

        /// <summary>Precious metals — 100% tax rate (regal right), minted into currency.</summary>
        public static bool IsPreciousMetal(int goodIndex)
        {
            return Defs[goodIndex].IsPreciousMetal;
        }

        static Goods()
        {
            //                            Type                Name        Category            Need             Cons    CAdmin  PAdmin  RAdmin  Base   Min    Max    Trade  Prec
            Defs = new[]
            {
                //                                                                                                                                              spoilage
                new GoodDef(GoodType.Food,      "food",      GoodCategory.Raw, NeedCategory.Basic,   1.0f,   0.0f,   0.0f,   0.02f,  1.0f,  0.1f,  10.0f, true,  false, 0.03f),
                new GoodDef(GoodType.Timber,     "timber",    GoodCategory.Raw, NeedCategory.Comfort, 0.2f,   0.02f,  0.01f,  0.01f,  0.5f,  0.05f, 5.0f,  true,  false, 0.001f),
                new GoodDef(GoodType.IronOre,    "ironOre",   GoodCategory.Raw, NeedCategory.Comfort, 0.005f, 0.0f,   0.001f, 0.003f, 5.0f,  0.5f,  50.0f, true,  false),
                new GoodDef(GoodType.GoldOre,    "goldOre",   GoodCategory.Raw, NeedCategory.None,    0.0f,   0.0f,   0.0f,   0.0f,   0.0f,  0.0f,  0.0f,  false, true),
                new GoodDef(GoodType.SilverOre,  "silverOre", GoodCategory.Raw, NeedCategory.None,    0.0f,   0.0f,   0.0f,   0.0f,   0.0f,  0.0f,  0.0f,  false, true),
                new GoodDef(GoodType.Salt,       "salt",      GoodCategory.Raw, NeedCategory.Basic,   0.05f,  0.0f,   0.0f,   0.0f,   3.0f,  0.3f,  30.0f, true,  false),
                new GoodDef(GoodType.Wool,       "wool",      GoodCategory.Raw, NeedCategory.Comfort, 0.1f,   0.0f,   0.0f,   0.005f, 2.0f,  0.2f,  20.0f, true,  false, 0.001f),
                new GoodDef(GoodType.Stone,      "stone",     GoodCategory.Raw, NeedCategory.None,    0.0f,   0.005f, 0.008f, 0.012f, 0.3f,  0.03f, 3.0f,  true,  false),
                new GoodDef(GoodType.Ale,        "ale",       GoodCategory.Raw, NeedCategory.Basic,   0.5f,   0.0f,   0.0f,   0.0f,   0.8f,  0.08f, 8.0f,  true,  false, 0.05f),
                new GoodDef(GoodType.Clay,       "clay",      GoodCategory.Raw,     NeedCategory.None,    0.0f,   0.0f,   0.0f,   0.0f,   0.2f,  0.02f, 2.0f,  true,  false),
                new GoodDef(GoodType.Pottery,    "pottery",   GoodCategory.Refined, NeedCategory.Comfort, 0.01f,  0.002f, 0.001f, 0.001f, 2.0f,  0.2f,  20.0f, true,  false),
            };

            Count = Defs.Length;

            // Extract flat arrays for hot-path access
            ConsumptionPerPop   = new float[Count];
            Names               = new string[Count];
            BasePrice           = new float[Count];
            MinPrice            = new float[Count];
            MaxPrice            = new float[Count];
            CountyAdminPerPop   = new float[Count];
            ProvinceAdminPerPop = new float[Count];
            RealmAdminPerPop    = new float[Count];
            MonthlyRetention    = new float[Count];

            var tradeable = new List<int>();

            for (int i = 0; i < Count; i++)
            {
                var d = Defs[i];
                ConsumptionPerPop[i]   = d.ConsumptionPerPop;
                Names[i]               = d.Name;
                BasePrice[i]           = d.BasePrice;
                MinPrice[i]            = d.MinPrice;
                MaxPrice[i]            = d.MaxPrice;
                CountyAdminPerPop[i]   = d.CountyAdminPerPop;
                ProvinceAdminPerPop[i] = d.ProvinceAdminPerPop;
                RealmAdminPerPop[i]    = d.RealmAdminPerPop;
                MonthlyRetention[i]    = (float)Math.Pow(1.0 - d.SpoilageRate, 30);

                if (d.IsTradeable)
                    tradeable.Add(i);
            }

            TradeableGoods = tradeable.ToArray();
        }
    }
}
