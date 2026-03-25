using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Gaussian blur that wraps horizontally (left↔right) for seamless equirectangular tiling.
    /// Top/bottom edges clamp (poles don't wrap).
    /// </summary>
    public static class WrapBlur
    {
        public static void Apply(Image<L8> image, float sigma)
        {
            if (sigma <= 0f) return;

            int pad = (int)System.MathF.Ceiling(sigma * 3f);
            int w = image.Width;
            int h = image.Height;

            // Create padded image: width + 2*pad, same height
            using var padded = new Image<L8>(w + 2 * pad, h);

            padded.ProcessPixelRows(image, (pAcc, srcAcc) =>
            {
                for (int y = 0; y < h; y++)
                {
                    var dst = pAcc.GetRowSpan(y);
                    var src = srcAcc.GetRowSpan(y);

                    // Left pad: wrap from right edge
                    for (int i = 0; i < pad; i++)
                        dst[i] = src[w - pad + i];

                    // Center: copy original
                    for (int x = 0; x < w; x++)
                        dst[pad + x] = src[x];

                    // Right pad: wrap from left edge
                    for (int i = 0; i < pad; i++)
                        dst[pad + w + i] = src[i];
                }
            });

            padded.Mutate(ctx => ctx.GaussianBlur(sigma));

            // Copy center back
            image.ProcessPixelRows(padded, (dstAcc, pAcc) =>
            {
                for (int y = 0; y < h; y++)
                {
                    var dst = dstAcc.GetRowSpan(y);
                    var src = pAcc.GetRowSpan(y);

                    for (int x = 0; x < w; x++)
                        dst[x] = src[pad + x];
                }
            });
        }

        public static void Apply(Image<L16> image, float sigma)
        {
            if (sigma <= 0f) return;

            int pad = (int)System.MathF.Ceiling(sigma * 3f);
            int w = image.Width;
            int h = image.Height;

            using var padded = new Image<L16>(w + 2 * pad, h);

            padded.ProcessPixelRows(image, (pAcc, srcAcc) =>
            {
                for (int y = 0; y < h; y++)
                {
                    var dst = pAcc.GetRowSpan(y);
                    var src = srcAcc.GetRowSpan(y);

                    for (int i = 0; i < pad; i++)
                        dst[i] = src[w - pad + i];

                    for (int x = 0; x < w; x++)
                        dst[pad + x] = src[x];

                    for (int i = 0; i < pad; i++)
                        dst[pad + w + i] = src[i];
                }
            });

            padded.Mutate(ctx => ctx.GaussianBlur(sigma));

            image.ProcessPixelRows(padded, (dstAcc, pAcc) =>
            {
                for (int y = 0; y < h; y++)
                {
                    var dst = dstAcc.GetRowSpan(y);
                    var src = pAcc.GetRowSpan(y);

                    for (int x = 0; x < w; x++)
                        dst[x] = src[pad + x];
                }
            });
        }
    }
}
