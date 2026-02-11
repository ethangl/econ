using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MapGen.Core
{
    /// <summary>
    /// V2 template adapter that ports V1 DSL templates into meter-annotated V2 scripts.
    /// </summary>
    public static class HeightmapTemplatesV2
    {
        public static string GetTemplate(HeightmapTemplateType template, MapGenV2Config config)
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

            return output.ToString();
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

        static string ConvertLine(string line, MapGenV2Config config)
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

        static string ConvertRangeSelector(string token, MapGenV2Config config)
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

        static string ConvertLegacyDeltaRangeToMeters(string token, MapGenV2Config config)
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

        static float LegacyDeltaToMeters(float legacyDelta, MapGenV2Config config)
        {
            float unit = config.MaxElevationMeters / 100f;
            return legacyDelta * unit;
        }

        static float LegacyAbsoluteToSignedMeters(float legacyAbsolute, MapGenV2Config config)
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
                bool prevOk = char.IsDigit(prev) || prev == '.' || prev == ')';
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
