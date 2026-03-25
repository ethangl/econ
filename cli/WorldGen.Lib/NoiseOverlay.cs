using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Applies 1px black-and-white noise using Photoshop-style overlay blend mode.
    /// Overlay: if base &lt; 0.5 → 2*base*blend, else → 1 - 2*(1-base)*(1-blend).
    /// </summary>
    public static class NoiseOverlay
    {
        public static void Apply(Image<L8> image, float opacityPercent, int seed = 0)
        {
            if (opacityPercent <= 0f) return;
            float opacity = Math.Clamp(opacityPercent, 0f, 100f) / 100f;
            var rng = new Random(seed);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        float b = row[x].PackedValue / 255f;
                        float n = rng.Next(2); // 0 or 1
                        float blended = b < 0.5f
                            ? 2f * b * n
                            : 1f - 2f * (1f - b) * (1f - n);
                        float result = b + (blended - b) * opacity;
                        row[x] = new L8((byte)Math.Clamp(result * 255f, 0f, 255f));
                    }
                }
            });
        }

        public static void Apply(Image<L16> image, float opacityPercent, int seed = 0)
        {
            if (opacityPercent <= 0f) return;
            float opacity = Math.Clamp(opacityPercent, 0f, 100f) / 100f;
            var rng = new Random(seed);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < accessor.Width; x++)
                    {
                        float b = row[x].PackedValue / 65535f;
                        float n = rng.Next(2);
                        float blended = b < 0.5f
                            ? 2f * b * n
                            : 1f - 2f * (1f - b) * (1f - n);
                        float result = b + (blended - b) * opacity;
                        row[x] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                    }
                }
            });
        }
    }
}
