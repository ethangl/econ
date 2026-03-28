using System;
using System.Threading.Tasks;
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
        static readonly float[] ElevationLut = BuildElevationLut();
        static readonly float[] FadeLut = BuildFadeLut();

        public static void Apply(Image<L16> image, float amplitude, int seed)
        {
            if (amplitude <= 0f) return;

            var noise = new Noise3D(seed + 777);
            int w = image.Width;
            int h = image.Height;

            float freq = w / (2f * MathF.PI * FeaturePixels);
            var cosLon = new float[w];
            var sinLon = new float[w];
            var cosLat = new float[h];
            var sinLat = new float[h];

            for (int x = 0; x < w; x++)
            {
                float lon = (x + 0.5f) / w * 2f * MathF.PI - MathF.PI;
                cosLon[x] = MathF.Cos(lon);
                sinLon[x] = MathF.Sin(lon);
            }

            for (int y = 0; y < h; y++)
            {
                float lat = MathF.PI / 2f - (y + 0.5f) / h * MathF.PI;
                cosLat[y] = MathF.Cos(lat);
                sinLat[y] = MathF.Sin(lat);
            }

            if (image.DangerousTryGetSinglePixelMemory(out var pixels))
            {
                Parallel.For(0, h, y =>
                {
                    var row = pixels.Span.Slice(y * w, w);
                    float rowCosLat = cosLat[y];
                    float rowSinLat = sinLat[y];

                    for (int x = 0; x < w; x++)
                    {
                        ushort packed = row[x].PackedValue;
                        float elev = ElevationLut[packed];
                        float fade = FadeLut[packed];
                        if (fade <= 0f) continue;

                        float px = rowCosLat * cosLon[x];
                        float py = rowSinLat;
                        float pz = rowCosLat * sinLon[x];
                        float n = noise.Fractal6Lacunarity2Persistence05(px * freq, py * freq, pz * freq);

                        float result = elev + n * amplitude * fade;
                        row[x] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                    }
                });
            }
            else
            {
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < h; y++)
                    {
                        float rowCosLat = cosLat[y];
                        float rowSinLat = sinLat[y];
                        var row = accessor.GetRowSpan(y);

                        for (int x = 0; x < w; x++)
                        {
                            ushort packed = row[x].PackedValue;
                            float elev = ElevationLut[packed];
                            float fade = FadeLut[packed];
                            if (fade <= 0f) continue;

                            float px = rowCosLat * cosLon[x];
                            float py = rowSinLat;
                            float pz = rowCosLat * sinLon[x];
                            float n = noise.Fractal6Lacunarity2Persistence05(px * freq, py * freq, pz * freq);

                            float result = elev + n * amplitude * fade;
                            row[x] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                        }
                    }
                });
            }
        }

        static float[] BuildElevationLut()
        {
            var lut = new float[ushort.MaxValue + 1];
            for (int i = 0; i < lut.Length; i++)
                lut[i] = (ushort)i / 65535f;
            return lut;
        }

        static float[] BuildFadeLut()
        {
            var lut = new float[ushort.MaxValue + 1];
            for (int i = 0; i < lut.Length; i++)
            {
                float elev = ElevationLut[i];
                float e = elev - SeaLevel;
                float e2 = e * e;
                lut[i] = 1f - e2 * e2 * 16f;
            }

            return lut;
        }
    }
}
