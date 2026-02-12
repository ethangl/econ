using System;

namespace EconSim.Core.Common
{
    /// <summary>
    /// Shared deterministic color helpers.
    /// </summary>
    public static class ColorMath
    {
        public static float HashToUnitFloat(int value)
        {
            uint h = (uint)value;
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;
            return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        public static Color32 HsvToColor32(float h, float s, float v)
        {
            float r;
            float g;
            float b;
            if (s <= 0)
            {
                r = g = b = v;
            }
            else
            {
                float hh = h * 6f;
                int i = (int)hh;
                float ff = hh - i;
                float p = v * (1f - s);
                float q = v * (1f - (s * ff));
                float t = v * (1f - (s * (1f - ff)));

                switch (i)
                {
                    case 0: r = v; g = t; b = p; break;
                    case 1: r = q; g = v; b = p; break;
                    case 2: r = p; g = v; b = t; break;
                    case 3: r = p; g = q; b = v; break;
                    case 4: r = t; g = p; b = v; break;
                    default: r = v; g = p; b = q; break;
                }
            }

            return new Color32(
                (byte)(r * 255),
                (byte)(g * 255),
                (byte)(b * 255),
                255
            );
        }
    }
}
