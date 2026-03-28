using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Unsharp mask with horizontal wrapping for seamless equirectangular tiling.
    /// USM = original + amount * (original - blurred)
    /// </summary>
    public static class WrapUnsharpMask
    {
        public static void Apply(Image<L16> image, float amount, float radius)
        {
            if (amount <= 0f || radius <= 0f) return;

            using var blurred = image.Clone();
            WrapBlur.Apply(blurred, radius);

            image.ProcessPixelRows(blurred, (srcAcc, blurAcc) =>
            {
                for (int y = 0; y < srcAcc.Height; y++)
                {
                    var src = srcAcc.GetRowSpan(y);
                    var blur = blurAcc.GetRowSpan(y);

                    for (int x = 0; x < srcAcc.Width; x++)
                    {
                        float orig = src[x].PackedValue / 65535f;
                        float lo = blur[x].PackedValue / 65535f;
                        float result = orig + amount * (orig - lo);
                        src[x] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                    }
                }
            });
        }
    }
}
