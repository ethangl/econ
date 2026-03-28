using System;
using System.Globalization;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldGen.Cli.Lib
{
    public static class MetalColorRamp
    {
        public static Image<Rgb24> Apply(Image<L16> grayscale)
        {
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Metal color ramp is only supported on macOS.");

            string helperPath = MetalHost.EnsureHelperBuilt();
            string tempDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string inputPath = Path.Combine(tempDir, "input.raw");
            string stopsPath = Path.Combine(tempDir, "stops.raw");
            string outputPath = Path.Combine(tempDir, "output.raw");

            try
            {
                MetalHost.WriteRaw(inputPath, MetalHost.ExtractPixels(grayscale));
                MetalHost.WriteRaw(stopsPath, ColorRamp.GetGpuStops());

                var psi = MetalHost.CreateHelperProcess(helperPath);
                psi.ArgumentList.Add("--mode");
                psi.ArgumentList.Add("color");
                psi.ArgumentList.Add("--input");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("--stops");
                psi.ArgumentList.Add(stopsPath);
                psi.ArgumentList.Add("--output");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("--width");
                psi.ArgumentList.Add(grayscale.Width.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--height");
                psi.ArgumentList.Add(grayscale.Height.ToString(CultureInfo.InvariantCulture));

                MetalHost.RunHelper(psi, "Metal color ramp helper");

                byte[] outputBytes = File.ReadAllBytes(outputPath);
                int expectedBytes = grayscale.Width * grayscale.Height * 3;
                if (outputBytes.Length != expectedBytes)
                    throw new InvalidOperationException($"Metal color ramp helper returned {outputBytes.Length} bytes, expected {expectedBytes}.");

                var image = new Image<Rgb24>(grayscale.Width, grayscale.Height);
                MetalHost.WritePixels(image, outputBytes);
                return image;
            }
            finally
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best-effort temp cleanup.
                }
            }
        }
    }
}
