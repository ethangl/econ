using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldGen.Cli.Lib
{
    public static class MetalWrapBlur
    {
        public static bool IsSupported => MetalCoastDetail.IsSupported;

        public static void Apply(Image<L16> image, float sigma)
        {
            if (sigma <= 0f) return;
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Metal blur is only supported on macOS.");

            string helperPath = MetalCoastDetail.EnsureHelperBuilt();
            string tempDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string inputPath = Path.Combine(tempDir, "input.raw");
            string outputPath = Path.Combine(tempDir, "output.raw");

            try
            {
                ushort[] pixelData = MetalCoastDetail.ExtractPixels(image);
                File.WriteAllBytes(inputPath, MemoryMarshal.AsBytes<ushort>(pixelData.AsSpan()).ToArray());

                var psi = new ProcessStartInfo(helperPath)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };

                psi.ArgumentList.Add("--mode");
                psi.ArgumentList.Add("blur");
                psi.ArgumentList.Add("--input");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("--output");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("--width");
                psi.ArgumentList.Add(image.Width.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--height");
                psi.ArgumentList.Add(image.Height.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--sigma");
                psi.ArgumentList.Add(sigma.ToString("R", CultureInfo.InvariantCulture));

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start Metal blur helper.");
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new InvalidOperationException($"Metal blur helper failed with exit code {process.ExitCode}: {detail.Trim()}");
                }

                byte[] outputBytes = File.ReadAllBytes(outputPath);
                if (outputBytes.Length != pixelData.Length * sizeof(ushort))
                    throw new InvalidOperationException($"Metal blur helper returned {outputBytes.Length} bytes, expected {pixelData.Length * sizeof(ushort)}.");

                MemoryMarshal.Cast<byte, ushort>(outputBytes.AsSpan()).CopyTo(pixelData);
                MetalCoastDetail.WritePixels(image, pixelData);
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
