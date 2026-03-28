using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Maps grayscale elevation to terrain colors via a configurable ramp.
    /// Sea level is at 0.5 in the 0-1 elevation range.
    /// </summary>
    public static class ColorRamp
    {
        internal readonly record struct GpuStop(float T, float R, float G, float B);

        struct Stop
        {
            public float T;
            public byte R, G, B;

            public Stop(float t, byte r, byte g, byte b)
            {
                T = t; R = r; G = g; B = b;
            }
        }

        static readonly Stop[] Stops = new Stop[]
        {
            new(0.00f,  10,  20,  60),  // deep ocean
            new(0.35f,  20,  50, 120),  // mid ocean
            new(0.48f,  60, 110, 170),  // shallow ocean
            new(0.50f,  70, 120, 175),  // coast water
            new(0.51f, 170, 165, 130),  // beach
            new(0.55f, 100, 140,  60),  // lowland green
            new(0.62f,  60, 120,  40),  // forest green
            new(0.70f, 130, 110,  60),  // highland brown
            new(0.80f, 140, 120,  90),  // mountain
            new(0.90f, 170, 160, 150),  // high mountain gray
            new(1.00f, 240, 240, 245),  // snow peak
        };

        internal static GpuStop[] GetGpuStops()
        {
            var gpuStops = new GpuStop[Stops.Length];
            for (int i = 0; i < Stops.Length; i++)
            {
                var stop = Stops[i];
                gpuStops[i] = new GpuStop(stop.T, stop.R, stop.G, stop.B);
            }

            return gpuStops;
        }

        /// <summary>
        /// Apply color ramp to a 16-bit grayscale image, producing an RGB image.
        /// </summary>
        public static Image<Rgb24> Apply(Image<L16> grayscale)
        {
            int w = grayscale.Width;
            int h = grayscale.Height;
            var color = new Image<Rgb24>(w, h);

            color.ProcessPixelRows(grayscale, (cAcc, gAcc) =>
            {
                for (int y = 0; y < h; y++)
                {
                    var dst = cAcc.GetRowSpan(y);
                    var src = gAcc.GetRowSpan(y);

                    for (int x = 0; x < w; x++)
                    {
                        float t = src[x].PackedValue / 65535f;
                        var (r, g, b) = Sample(t);
                        dst[x] = new Rgb24(r, g, b);
                    }
                }
            });

            return color;
        }

        static (byte r, byte g, byte b) Sample(float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            // Find the two stops we're between
            for (int i = 0; i < Stops.Length - 1; i++)
            {
                if (t <= Stops[i + 1].T)
                {
                    float range = Stops[i + 1].T - Stops[i].T;
                    float f = range > 0f ? (t - Stops[i].T) / range : 0f;

                    byte r = (byte)(Stops[i].R + f * (Stops[i + 1].R - Stops[i].R));
                    byte g = (byte)(Stops[i].G + f * (Stops[i + 1].G - Stops[i].G));
                    byte b = (byte)(Stops[i].B + f * (Stops[i + 1].B - Stops[i].B));
                    return (r, g, b);
                }
            }

            var last = Stops[Stops.Length - 1];
            return (last.R, last.G, last.B);
        }
    }
}
