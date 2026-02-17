namespace MapGen.Core
{
    /// <summary>
    /// Predefined canonical terrain templates expressed in meter-based DSL tokens.
    /// </summary>
    public static class HeightmapTemplates
    {
        /// <summary>
        /// Low Island: Small landmass, minimal elevation.
        /// Good for: Small island nations, tropical settings.
        /// </summary>
        public const string Volcano = @"
Hill 1 5625m-6250m 44-56 40-60
Multiply 0.8 1875m-5000m
Range 1.5 1875m-3437.5m 45-55 40-60
Smooth 3
Hill 1.5 2187.5m-2812.5m 25-30 20-75
Hill 1 2187.5m-3437.5m 75-80 25-75
Hill 0.5 1250m-1562.5m 10-15 20-25
Mask 3
";

        public const string LowIsland = @"
Hill 1 5625m-6187.5m 60-80 45-55
Hill 1-2 1250m-1875m 10-30 10-90
Smooth 2
Hill 6-7 1562.5m-2187.5m 20-70 30-70
Range 1 2500m-3125m 45-55 45-55
Trough 2-3 1250m-1875m 15-85 20-30
Trough 2-3 1250m-1875m 15-85 70-80
Hill 1.5 625m-937.5m 5-15 20-80
Hill 1 625m-937.5m 85-95 70-80
Pit 5-7 937.5m-1562.5m 15-85 20-80
Multiply 0.4 0m-5000m
Mask 4
";

        /// <summary>
        /// Archipelago: Scattered islands, lots of coastline.
        /// Good for: Naval campaigns, exploration settings.
        /// </summary>
        public const string Archipelago = @"
Add 687.5m all
Range 2-3 2500m-3750m 20-80 20-80
Hill 5 937.5m-1250m 10-90 30-70
Hill 2 625m-937.5m 10-30 20-80
Hill 2 625m-937.5m 60-90 20-80
Smooth 3
Trough 10 1250m-1875m 5-95 5-95
Strait 2 vertical
Strait 2 horizontal
";

        /// <summary>
        /// Continents: Large landmasses with inland seas.
        /// Good for: Epic campaigns, diverse terrain.
        /// </summary>
        public const string Continents = @"
Hill 1 5000m-5312.5m 60-80 40-60
Hill 1 5000m-5312.5m 20-30 40-60
Hill 6-7 937.5m-1875m 25-75 15-85
Multiply 0.6 land
Hill 8-10 312.5m-625m 15-85 20-80
Range 1-2 1875m-3750m 5-15 25-75
Range 1-2 1875m-3750m 80-95 25-75
Range 0-3 1875m-3750m 80-90 20-80
Strait 2 vertical
Strait 1 vertical
Smooth 3
Trough 3-4 937.5m-1250m 15-85 20-80
Trough 3-4 312.5m-625m 45-55 45-55
Pit 3-4 625m-1250m 15-85 20-80
Mask 4
";

        /// <summary>
        /// Pangaea: Single supercontinent.
        /// Good for: Early world settings, land-focused campaigns.
        /// </summary>
        public const string Pangea = @"
Hill 1-2 1562.5m-2500m 15-50 0-10
Hill 1-2 312.5m-2500m 50-85 0-10
Hill 1-2 1562.5m-2500m 50-85 90-100
Hill 1-2 312.5m-2500m 15-50 90-100
Hill 8-12 1250m-2500m 20-80 48-52
Smooth 2
Multiply 0.7 land
Trough 3-4 1562.5m-2187.5m 5-95 10-20
Trough 3-4 1562.5m-2187.5m 5-95 80-90
Range 5-6 1875m-2500m 10-90 35-65
";

        public const string HighIsland = @"
Hill 1 5625m-6250m 65-75 47-53
Add 437.5m all
Hill 5-6 1250m-1875m 25-55 45-55
Range 1 2500m-3125m 45-55 45-55
Multiply 0.8 land
Mask 3
Smooth 2
Trough 2-3 1250m-1875m 20-30 20-30
Trough 2-3 1250m-1875m 60-80 70-80
Hill 1 625m-937.5m 60-60 50-50
Hill 1.5 812.5m-1000m 15-20 20-75
Range 1.5 1875m-2500m 15-85 30-40
Range 1.5 1875m-2500m 15-85 60-70
Pit 3-5 625m-1875m 15-85 20-80
";

        /// <summary>
        /// Peninsula: Land extending into water.
        /// Good for: Coastal kingdoms, maritime settings.
        /// </summary>
        public const string Peninsula = @"
Range 2-3 1250m-2187.5m 40-50 0-15
Add 312.5m all
Hill 1 5625m-6250m 10-90 0-5
Add 812.5m all
Hill 3-4 187.5m-312.5m 5-95 80-100
Hill 1-2 187.5m-312.5m 5-95 40-60
Trough 5-6 625m-1562.5m 5-95 5-95
Smooth 3
Invert 0.4 both
";

        public const string Shattered = @"
DepthRemap 0.75
Hill 8 2187.5m-2500m 15-85 30-70
Trough 10-20 2500m-3125m 5-95 5-95
Range 5-7 1875m-2500m 10-90 20-80
Pit 12-20 1875m-2500m 15-85 20-80
";

        public const string OldWorld = @"
Range 3 4375m 15-85 20-80
Hill 2-3 3125m-4375m 15-45 20-80
Hill 2-3 3125m-4375m 65-85 20-80
Hill 4-6 1250m-1562.5m 15-85 20-80
Multiply 0.5 land
Smooth 2
Range 3-4 1250m-3125m 15-35 20-45
Range 2-4 1250m-3125m 65-85 45-80
Strait 3-7 vertical
Trough 6-8 1250m-3125m 15-85 45-65
Pit 5-6 1250m-1875m 10-90 10-90
";

        /// <summary>
        /// Get template by name (case-insensitive).
        /// Returns null if not found.
        /// </summary>
        public static string GetTemplate(string name)
        {
            switch (name.ToLowerInvariant().Replace(" ", "").Replace("_", ""))
            {
                case "volcano": return Volcano;
                case "lowisland": return LowIsland;
                case "archipelago": return Archipelago;
                case "continents": return Continents;
                case "pangea": return Pangea;
                case "highisland": return HighIsland;
                case "peninsula": return Peninsula;
                case "shattered": return Shattered;
                case "oldworld": return OldWorld;
                default: return null;
            }
        }

        public static string GetTemplate(HeightmapTemplateType template)
        {
            return GetTemplate(template.ToString());
        }

        /// <summary>
        /// List of all available template names.
        /// </summary>
        public static readonly string[] AllTemplates = new[]
        {
            "Volcano",
            "LowIsland",
            "Archipelago",
            "Continents",
            "Pangea",
            "HighIsland",
            "Peninsula",
            "Shattered",
            "OldWorld"
        };
    }
}
