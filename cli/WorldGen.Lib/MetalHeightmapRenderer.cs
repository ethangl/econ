using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    public static class MetalHeightmapRenderer
    {
        public static bool IsSupported => MetalCoastDetail.IsSupported;

        public static Image<L16> Render(DenseTerrainData terrain, int width, int height)
        {
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Metal heightmap rendering is only supported on macOS.");

            var mesh = terrain.Mesh;
            var lookup = new SphereLookup(mesh.CellCenters, mesh.Radius);
            string helperPath = MetalCoastDetail.EnsureHelperBuilt();

            string tempDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string centerXPath = Path.Combine(tempDir, "center-x.raw");
            string centerYPath = Path.Combine(tempDir, "center-y.raw");
            string centerZPath = Path.Combine(tempDir, "center-z.raw");
            string bucketOffsetsPath = Path.Combine(tempDir, "bucket-offsets.raw");
            string bucketCountsPath = Path.Combine(tempDir, "bucket-counts.raw");
            string bucketCellsPath = Path.Combine(tempDir, "bucket-cells.raw");
            string elevationPath = Path.Combine(tempDir, "elevation.raw");
            string outputPath = Path.Combine(tempDir, "output.raw");

            try
            {
                WriteRaw(centerXPath, lookup.CenterX);
                WriteRaw(centerYPath, lookup.CenterY);
                WriteRaw(centerZPath, lookup.CenterZ);
                WriteRaw(bucketOffsetsPath, lookup.BucketOffsets);
                WriteRaw(bucketCountsPath, lookup.BucketCounts);
                WriteRaw(bucketCellsPath, lookup.BucketCells);
                WriteRaw(elevationPath, terrain.CellElevation);

                var psi = new ProcessStartInfo(helperPath)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };

                psi.ArgumentList.Add("--mode");
                psi.ArgumentList.Add("render");
                psi.ArgumentList.Add("--output");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("--center-x");
                psi.ArgumentList.Add(centerXPath);
                psi.ArgumentList.Add("--center-y");
                psi.ArgumentList.Add(centerYPath);
                psi.ArgumentList.Add("--center-z");
                psi.ArgumentList.Add(centerZPath);
                psi.ArgumentList.Add("--bucket-offsets");
                psi.ArgumentList.Add(bucketOffsetsPath);
                psi.ArgumentList.Add("--bucket-counts");
                psi.ArgumentList.Add(bucketCountsPath);
                psi.ArgumentList.Add("--bucket-cells");
                psi.ArgumentList.Add(bucketCellsPath);
                psi.ArgumentList.Add("--elevation");
                psi.ArgumentList.Add(elevationPath);
                psi.ArgumentList.Add("--width");
                psi.ArgumentList.Add(width.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--height");
                psi.ArgumentList.Add(height.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--radius");
                psi.ArgumentList.Add(lookup.Radius.ToString("R", CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--lat-buckets");
                psi.ArgumentList.Add(lookup.LatBucketCount.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--lon-buckets");
                psi.ArgumentList.Add(lookup.LonBucketCount.ToString(CultureInfo.InvariantCulture));

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start Metal render helper.");
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new InvalidOperationException($"Metal render helper failed with exit code {process.ExitCode}: {detail.Trim()}");
                }

                byte[] outputBytes = File.ReadAllBytes(outputPath);
                int pixelCount = width * height;
                if (outputBytes.Length != pixelCount * sizeof(ushort))
                    throw new InvalidOperationException($"Metal render helper returned {outputBytes.Length} bytes, expected {pixelCount * sizeof(ushort)}.");

                ushort[] pixelData = new ushort[pixelCount];
                MemoryMarshal.Cast<byte, ushort>(outputBytes.AsSpan()).CopyTo(pixelData);

                var image = new Image<L16>(width, height);
                MetalCoastDetail.WritePixels(image, pixelData);
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

        static void WriteRaw<T>(string path, T[] values) where T : struct
        {
            File.WriteAllBytes(path, MemoryMarshal.AsBytes<T>(values.AsSpan()).ToArray());
        }
    }
}
