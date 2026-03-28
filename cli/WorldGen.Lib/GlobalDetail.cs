using System;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Adds a subtle full-map micro-relief layer in image space.
    /// The noise wraps horizontally, survives blur, and helps break up
    /// visible Voronoi cell boundaries with small hills and valleys.
    /// </summary>
    public static class GlobalDetail
    {
        const float FeaturePixels = 28f;

        public static void Apply(Image<L16> image, float amplitude, int seed)
        {
            if (amplitude <= 0f)
                return;

            var noise = new Noise3D(seed + 1337);
            int w = image.Width;
            int h = image.Height;
            float wrapRadius = w / (2f * MathF.PI * FeaturePixels);
            float verticalScale = 1f / FeaturePixels;
            var cosLon = new float[w];
            var sinLon = new float[w];

            for (int x = 0; x < w; x++)
            {
                float theta = (x + 0.5f) / w * 2f * MathF.PI;
                cosLon[x] = MathF.Cos(theta);
                sinLon[x] = MathF.Sin(theta);
            }

            if (image.DangerousTryGetSinglePixelMemory(out var pixels))
            {
                Parallel.For(0, h, y =>
                {
                    var row = pixels.Span.Slice(y * w, w);
                    float py = (y + 0.5f) * verticalScale;

                    for (int x = 0; x < w; x++)
                    {
                        float elev = row[x].PackedValue / 65535f;
                        float px = cosLon[x] * wrapRadius;
                        float pz = sinLon[x] * wrapRadius;
                        float n = noise.Fractal4Lacunarity2Persistence05(px, py, pz);
                        float result = elev + n * amplitude;
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
                        float py = (y + 0.5f) * verticalScale;
                        var row = accessor.GetRowSpan(y);

                        for (int x = 0; x < w; x++)
                        {
                            float elev = row[x].PackedValue / 65535f;
                            float px = cosLon[x] * wrapRadius;
                            float pz = sinLon[x] * wrapRadius;
                            float n = noise.Fractal4Lacunarity2Persistence05(px, py, pz);
                            float result = elev + n * amplitude;
                            row[x] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                        }
                    }
                });
            }
        }
    }
}
