using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Adds fractal coastal detail using the mapgen4 approach:
    /// elevation += amplitude * (1 - e^4) * fractal_noise
    ///
    /// The quartic falloff (1 - e^4) concentrates noise near sea level (e ≈ 0.5)
    /// while leaving deep ocean and high mountains smooth. Uses 3D Perlin noise
    /// sampled on the sphere surface so it wraps naturally.
    ///
    /// Noise frequency scales with image width so features stay a consistent
    /// pixel size regardless of resolution. Applied after blur so detail
    /// survives smoothing.
    /// </summary>
    public static class CoastDetail
    {
        const float SeaLevel = 0.5f;
        const int Octaves = 6;
        const float Lacunarity = 2.0f;
        const float Persistence = 0.5f;

        // Base noise features are ~50px wide at the equator.
        // Frequency on unit sphere = width / (2*pi * featurePixels).
        const float FeaturePixels = 50f;

        public static void Apply(Image<L16> image, float amplitude, int seed)
        {
            if (amplitude <= 0f) return;

            var noise = new Noise3D(seed + 777);
            int w = image.Width;
            int h = image.Height;

            float freq = w / (2f * MathF.PI * FeaturePixels);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    float lat = MathF.PI / 2f - (y + 0.5f) / h * MathF.PI;
                    float cosLat = MathF.Cos(lat);
                    float sinLat = MathF.Sin(lat);
                    var row = accessor.GetRowSpan(y);

                    for (int x = 0; x < w; x++)
                    {
                        float elev = row[x].PackedValue / 65535f;

                        float e = elev - SeaLevel;
                        float e2 = e * e;
                        float fade = 1f - e2 * e2 * 16f;
                        if (fade <= 0f) continue;

                        float lon = (x + 0.5f) / w * 2f * MathF.PI - MathF.PI;

                        float px = cosLat * MathF.Cos(lon);
                        float py = sinLat;
                        float pz = cosLat * MathF.Sin(lon);

                        float n = noise.Fractal(
                            px * freq, py * freq, pz * freq,
                            Octaves, Lacunarity, Persistence);

                        float result = elev + n * amplitude * fade;
                        row[x] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                    }
                }
            });
        }
    }
}
