using System;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace WorldGen.Cli.Lib
{
    public static class HeightmapOutput
    {
        public static void SaveRawL16(Image<L16> image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

            ushort[] pixels = new ushort[image.Width * image.Height];
            if (image.DangerousTryGetSinglePixelMemory(out var contiguous))
            {
                MemoryMarshal.Cast<L16, ushort>(contiguous.Span).CopyTo(pixels);
            }
            else
            {
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < image.Height; y++)
                    {
                        var row = MemoryMarshal.Cast<L16, ushort>(accessor.GetRowSpan(y));
                        row.CopyTo(pixels.AsSpan(y * image.Width, image.Width));
                    }
                });
            }

            File.WriteAllBytes(path, MemoryMarshal.AsBytes(pixels.AsSpan()).ToArray());
        }

        public static Image<TPixel> CreatePreview<TPixel>(Image<TPixel> image, int maxWidth)
            where TPixel : unmanaged, IPixel<TPixel>
        {
            if (maxWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxWidth), "Preview width must be greater than zero.");

            if (image.Width <= maxWidth)
                return image.Clone();

            int previewHeight = Math.Max(1, (int)Math.Round(image.Height * (maxWidth / (double)image.Width)));
            return image.Clone(ctx => ctx.Resize(maxWidth, previewHeight));
        }
    }
}
