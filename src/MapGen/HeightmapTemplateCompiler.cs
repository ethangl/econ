using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MapGen.Core
{
    /// <summary>
    /// Template adapter that ports legacy DSL templates into meter-annotated scripts.
    /// </summary>
    public static class HeightmapTemplateCompiler
    {
        static readonly IReadOnlyDictionary<HeightmapTemplateType, HeightmapTemplateTuningProfile> TunedProfiles
            = new Dictionary<HeightmapTemplateType, HeightmapTemplateTuningProfile>
            {
                // Set after focused baseline-vs-candidate drift sweeps.
                [HeightmapTemplateType.Continents] = new HeightmapTemplateTuningProfile
                {
                    TerrainMagnitudeScale = 0.95f,
                    AddMagnitudeScale = 1f,
                    MaskScale = 0.75f,
                    LandMultiplyFactorScale = 1.35f,
                    RiverThresholdScale = 1.25f,
                    RiverTraceThresholdScale = 1.30f,
                    RiverMinVerticesScale = 1.30f,
                    RealmTargetScale = 0.22f,
                    ProvinceTargetScale = 0.60f,
                    CountyTargetScale = 2.30f,
                    BiomeCoastSaltScale = 1f,
                    BiomeSalineThresholdScale = 1f,
                    BiomeSlopeScale = 0.85f,
                    BiomeAlluvialFluxThresholdScale = 0.80f,
                    BiomeAlluvialMaxSlopeScale = 1.25f,
                    BiomeWetlandFluxThresholdScale = 1.40f,
                    BiomeWetlandMaxSlopeScale = 0.80f,
                    BiomePodzolTempMaxScale = 1.10f,
                    BiomePodzolPrecipThresholdScale = 0.92f,
                    BiomeWoodlandPrecipThresholdScale = 1.10f
                },
                [HeightmapTemplateType.LowIsland] = new HeightmapTemplateTuningProfile
                {
                    TerrainMagnitudeScale = 0.85f,
                    AddMagnitudeScale = 1f,
                    MaskScale = 0.75f,
                    LandMultiplyFactorScale = 0.90f,
                    RiverThresholdScale = 1.10f,
                    RiverTraceThresholdScale = 1.00f,
                    RiverMinVerticesScale = 1.30f,
                    RealmTargetScale = 0.24f,
                    ProvinceTargetScale = 0.65f,
                    CountyTargetScale = 2.40f,
                    BiomeCoastSaltScale = 0.65f,
                    BiomeSalineThresholdScale = 1.20f,
                    BiomeSlopeScale = 1f,
                    BiomeAlluvialFluxThresholdScale = 1.10f,
                    BiomeAlluvialMaxSlopeScale = 1.00f,
                    BiomeWetlandFluxThresholdScale = 1.80f,
                    BiomeWetlandMaxSlopeScale = 0.80f,
                    BiomePodzolTempMaxScale = 1.15f,
                    BiomePodzolPrecipThresholdScale = 0.95f,
                    BiomeWoodlandPrecipThresholdScale = 1.10f
                },
                [HeightmapTemplateType.HighIsland] = new HeightmapTemplateTuningProfile
                {
                    TerrainMagnitudeScale = 0.98f,
                    AddMagnitudeScale = 1.00f,
                    MaskScale = 0.85f,
                    LandMultiplyFactorScale = 1.00f,
                    RiverThresholdScale = 1.25f,
                    RiverTraceThresholdScale = 1.00f,
                    RiverMinVerticesScale = 1.35f,
                    RealmTargetScale = 0.20f,
                    ProvinceTargetScale = 0.50f,
                    CountyTargetScale = 2.20f,
                    BiomeCoastSaltScale = 1f,
                    BiomeSalineThresholdScale = 1f,
                    BiomeSlopeScale = 0.85f,
                    BiomeAlluvialFluxThresholdScale = 0.95f,
                    BiomeAlluvialMaxSlopeScale = 1.00f,
                    BiomeWetlandFluxThresholdScale = 1.80f,
                    BiomeWetlandMaxSlopeScale = 0.70f
                },
                [HeightmapTemplateType.Archipelago] = new HeightmapTemplateTuningProfile
                {
                    TerrainMagnitudeScale = 0.65f,
                    AddMagnitudeScale = 1.40f,
                    MaskScale = 0.90f,
                    LandMultiplyFactorScale = 1f,
                    RiverThresholdScale = 1.15f,
                    RiverTraceThresholdScale = 0.85f,
                    RiverMinVerticesScale = 0.90f,
                    RealmTargetScale = 0.70f,
                    ProvinceTargetScale = 1.10f,
                    CountyTargetScale = 3.10f,
                    BiomeCoastSaltScale = 1.10f,
                    BiomeSalineThresholdScale = 1f,
                    BiomeSlopeScale = 0.35f,
                    BiomeAlluvialFluxThresholdScale = 1.10f,
                    BiomeAlluvialMaxSlopeScale = 0.80f,
                    BiomeWetlandFluxThresholdScale = 1.30f,
                    BiomeWetlandMaxSlopeScale = 0.70f
                },
            };

        public static string GetTemplate(HeightmapTemplateType template, MapGenConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            string v1 = HeightmapTemplates.GetTemplate(template.ToString());
            if (string.IsNullOrWhiteSpace(v1))
                return null;

            var output = new StringBuilder();
            string[] lines = v1.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    output.AppendLine(line);
                    continue;
                }

                string converted = ConvertLine(line, config);
                output.AppendLine(converted);
            }

            string script = output.ToString();
            HeightmapTemplateTuningProfile profile = ResolveTuningProfile(template, config);
            if (profile == null || profile.IsIdentity())
                return script;

            return ApplyTuningProfile(script, profile);
        }

        public static (float min, float max) GetLandRatioBand(HeightmapTemplateType template)
        {
            switch (template)
            {
                case HeightmapTemplateType.LowIsland:
                case HeightmapTemplateType.Atoll:
                    return (0.10f, 0.60f);
                case HeightmapTemplateType.HighIsland:
                case HeightmapTemplateType.Volcano:
                    return (0.20f, 0.72f);
                case HeightmapTemplateType.Archipelago:
                    return (0.08f, 0.68f);
                case HeightmapTemplateType.Shattered:
                case HeightmapTemplateType.Fractious:
                    return (0.15f, 0.68f);
                case HeightmapTemplateType.Continents:
                case HeightmapTemplateType.Mediterranean:
                    return (0.25f, 0.82f);
                case HeightmapTemplateType.Peninsula:
                case HeightmapTemplateType.Isthmus:
                    return (0.20f, 0.78f);
                case HeightmapTemplateType.Pangea:
                case HeightmapTemplateType.OldWorld:
                    return (0.35f, 0.92f);
                case HeightmapTemplateType.Taklamakan:
                    return (0.20f, 0.86f);
                default:
                    return (0.15f, 0.85f);
            }
        }

        static string ConvertLine(string line, MapGenConfig config)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return line;

            string op = parts[0].ToLowerInvariant();
            switch (op)
            {
                case "hill":
                case "pit":
                case "range":
                case "trough":
                    if (parts.Length >= 3)
                        parts[2] = ConvertLegacyDeltaRangeToMeters(parts[2], config);
                    return Join(parts);

                case "add":
                    if (parts.Length >= 2)
                        parts[1] = ConvertLegacyDeltaRangeToMeters(parts[1], config);
                    if (parts.Length >= 3)
                        parts[2] = ConvertRangeSelector(parts[2], config);
                    return Join(parts);

                case "multiply":
                    if (parts.Length >= 3)
                        parts[2] = ConvertRangeSelector(parts[2], config);
                    return Join(parts);

                default:
                    return line;
            }
        }

        public static HeightmapTemplateTuningProfile ResolveTuningProfile(HeightmapTemplateType template, MapGenConfig config)
        {
            if (config != null && config.TemplateTuningOverride != null)
                return config.TemplateTuningOverride;

            if (TunedProfiles.TryGetValue(template, out HeightmapTemplateTuningProfile profile))
                return profile;

            return null;
        }

        static string ApplyTuningProfile(string script, HeightmapTemplateTuningProfile profile)
        {
            var output = new StringBuilder();
            string[] lines = script.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    output.AppendLine(line);
                    continue;
                }

                output.AppendLine(ApplyTuningToLine(line, profile));
            }

            return output.ToString();
        }

        static string ApplyTuningToLine(string line, HeightmapTemplateTuningProfile profile)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return line;

            string op = parts[0].ToLowerInvariant();
            switch (op)
            {
                case "hill":
                case "pit":
                case "range":
                case "trough":
                    if (parts.Length >= 3)
                        parts[2] = ScaleMeterToken(parts[2], profile.TerrainMagnitudeScale);
                    return Join(parts);

                case "add":
                    if (parts.Length >= 2)
                        parts[1] = ScaleMeterToken(parts[1], profile.AddMagnitudeScale);
                    return Join(parts);

                case "mask":
                    if (parts.Length >= 2)
                        parts[1] = ScaleFloat(parts[1], profile.MaskScale);
                    return Join(parts);

                case "multiply":
                    if (parts.Length >= 3
                        && string.Equals(parts[2], "land", StringComparison.OrdinalIgnoreCase)
                        && parts.Length >= 2)
                    {
                        float factor = ParseFloat(parts[1]);
                        float scaled = 1f + (factor - 1f) * profile.LandMultiplyFactorScale;
                        parts[1] = FormatFloat(scaled);
                    }

                    return Join(parts);

                default:
                    return line;
            }
        }

        static string ScaleMeterToken(string token, float scale)
        {
            if (Math.Abs(scale - 1f) < 0.0001f)
                return token;

            if (!TryParseRange(token, out string minRaw, out string maxRaw))
            {
                float value = ParseMetersToken(token);
                return FormatMeters(value * scale);
            }

            float minV = ParseMetersToken(minRaw) * scale;
            float maxV = ParseMetersToken(maxRaw) * scale;
            if (maxV < minV)
            {
                float t = minV;
                minV = maxV;
                maxV = t;
            }

            return FormatMeters(minV) + "-" + FormatMeters(maxV);
        }

        static string ScaleFloat(string token, float scale)
        {
            if (Math.Abs(scale - 1f) < 0.0001f)
                return token;

            float value = ParseFloat(token);
            return FormatFloat(value * scale);
        }

        static string ConvertRangeSelector(string token, MapGenConfig config)
        {
            string t = token.Trim().ToLowerInvariant();
            if (t == "land" || t == "water" || t == "all")
                return t;

            if (!TryParseRange(t, out string minRaw, out string maxRaw))
                return token;

            float minLegacy = ParseFloat(minRaw);
            float maxLegacy = ParseFloat(maxRaw);

            float minMeters = LegacyAbsoluteToSignedMeters(minLegacy, config);
            float maxMeters = LegacyAbsoluteToSignedMeters(maxLegacy, config);
            if (maxMeters < minMeters)
            {
                float swap = minMeters;
                minMeters = maxMeters;
                maxMeters = swap;
            }

            return FormatMeters(minMeters) + "-" + FormatMeters(maxMeters);
        }

        static string ConvertLegacyDeltaRangeToMeters(string token, MapGenConfig config)
        {
            if (!TryParseRange(token, out string minRaw, out string maxRaw))
            {
                float value = ParseFloat(token);
                return FormatMeters(LegacyDeltaToMeters(value, config));
            }

            float min = ParseFloat(minRaw);
            float max = ParseFloat(maxRaw);
            float minMeters = LegacyDeltaToMeters(min, config);
            float maxMeters = LegacyDeltaToMeters(max, config);
            if (maxMeters < minMeters)
            {
                float swap = minMeters;
                minMeters = maxMeters;
                maxMeters = swap;
            }

            return FormatMeters(minMeters) + "-" + FormatMeters(maxMeters);
        }

        static float LegacyDeltaToMeters(float legacyDelta, MapGenConfig config)
        {
            float unit = (config.MaxElevationMeters + config.MaxSeaDepthMeters) / 100f;
            return legacyDelta * unit;
        }

        static float LegacyAbsoluteToSignedMeters(float legacyAbsolute, MapGenConfig config)
        {
            if (legacyAbsolute >= 20f)
            {
                float t = (legacyAbsolute - 20f) / 80f;
                return t * config.MaxElevationMeters;
            }

            float belowSea = (20f - legacyAbsolute) / 20f;
            return -belowSea * config.MaxSeaDepthMeters;
        }

        static string FormatMeters(float value)
        {
            float rounded = (float)Math.Round(value, 1, MidpointRounding.AwayFromZero);
            string token = rounded.ToString("0.0", CultureInfo.InvariantCulture);
            if (token.EndsWith(".0", StringComparison.Ordinal))
                token = token.Substring(0, token.Length - 2);
            return token + "m";
        }

        static bool TryParseRange(string token, out string left, out string right)
        {
            left = null;
            right = null;
            string t = token.Trim();

            for (int i = 1; i < t.Length - 1; i++)
            {
                if (t[i] != '-')
                    continue;

                char prev = t[i - 1];
                char next = t[i + 1];
                bool prevOk = char.IsDigit(prev) || prev == '.' || prev == ')' || prev == 'm' || prev == '%';
                bool nextOk = char.IsDigit(next) || next == '.' || next == '-';
                if (!prevOk || !nextOk)
                    continue;

                left = t.Substring(0, i);
                right = t.Substring(i + 1);
                return true;
            }

            return false;
        }

        static float ParseFloat(string token) => float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);

        static float ParseMetersToken(string token)
        {
            string t = token.Trim().ToLowerInvariant();
            if (t.EndsWith("m", StringComparison.Ordinal))
                t = t.Substring(0, t.Length - 1);
            return ParseFloat(t);
        }

        static string FormatFloat(float value)
        {
            float rounded = (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Join(IReadOnlyList<string> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(parts[i]);
            }

            return sb.ToString();
        }
    }
}
