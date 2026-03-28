using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    public readonly record struct MetalStageTimings(double RasterSeconds, double BlurSeconds, double CoastSeconds);

    public static class MetalHeightmapPipeline
    {
        public static bool IsSupported => MetalHost.IsSupported;
        public static string UnavailableReason => MetalHost.UnavailableReason;

        public static Image<L16> Render(
            DenseTerrainData terrain,
            int width,
            int height,
            float blurSigma,
            float coastAmplitude,
            int seed,
            out MetalStageTimings timings)
        {
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Metal heightmap pipeline is only supported on macOS.");

            var lookup = new SphereLookup(terrain.Mesh.CellCenters, terrain.Mesh.Radius);
            string helperPath = MetalHost.EnsureHelperBuilt();
            string tempDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string centerXPath = Path.Combine(tempDir, "center-x.raw");
            string centerYPath = Path.Combine(tempDir, "center-y.raw");
            string centerZPath = Path.Combine(tempDir, "center-z.raw");
            string bucketOffsetsPath = Path.Combine(tempDir, "bucket-offsets.raw");
            string bucketCountsPath = Path.Combine(tempDir, "bucket-counts.raw");
            string bucketCellsPath = Path.Combine(tempDir, "bucket-cells.raw");
            string elevationPath = Path.Combine(tempDir, "elevation.raw");
            string permPath = Path.Combine(tempDir, "perm.raw");
            string outputPath = Path.Combine(tempDir, "output.raw");
            string timingsPath = Path.Combine(tempDir, "timings.txt");

            try
            {
                MetalHost.WriteRaw(centerXPath, lookup.CenterX);
                MetalHost.WriteRaw(centerYPath, lookup.CenterY);
                MetalHost.WriteRaw(centerZPath, lookup.CenterZ);
                MetalHost.WriteRaw(bucketOffsetsPath, lookup.BucketOffsets);
                MetalHost.WriteRaw(bucketCountsPath, lookup.BucketCounts);
                MetalHost.WriteRaw(bucketCellsPath, lookup.BucketCells);
                MetalHost.WriteRaw(elevationPath, terrain.CellElevation);
                MetalHost.WriteRaw(permPath, new Noise3D(seed + 777).GetPermutationTable());

                var psi = MetalHost.CreateHelperProcess(helperPath);
                psi.ArgumentList.Add("--mode");
                psi.ArgumentList.Add("pipeline");
                psi.ArgumentList.Add("--output");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("--timings");
                psi.ArgumentList.Add(timingsPath);
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
                psi.ArgumentList.Add("--perm");
                psi.ArgumentList.Add(permPath);
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
                psi.ArgumentList.Add("--sigma");
                psi.ArgumentList.Add(blurSigma.ToString("R", CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--amplitude");
                psi.ArgumentList.Add(coastAmplitude.ToString("R", CultureInfo.InvariantCulture));

                MetalHost.RunHelper(psi, "Metal heightmap pipeline helper");
                timings = ParseTimings(File.ReadAllText(timingsPath));

                byte[] outputBytes = File.ReadAllBytes(outputPath);
                int pixelCount = width * height;
                if (outputBytes.Length != pixelCount * sizeof(ushort))
                    throw new InvalidOperationException($"Metal heightmap pipeline helper returned {outputBytes.Length} bytes, expected {pixelCount * sizeof(ushort)}.");

                ushort[] pixelData = new ushort[pixelCount];
                MemoryMarshal.Cast<byte, ushort>(outputBytes.AsSpan()).CopyTo(pixelData);

                var image = new Image<L16>(width, height);
                MetalHost.WritePixels(image, pixelData);
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

        static MetalStageTimings ParseTimings(string stdout)
        {
            double raster = 0;
            double blur = 0;
            double coast = 0;

            foreach (string line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!line.StartsWith("TIMING ", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3)
                    continue;

                string secondsText = parts[2].Replace(',', '.');
                if (!double.TryParse(secondsText, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                    continue;

                switch (parts[1])
                {
                    case "raster":
                        raster = seconds;
                        break;
                    case "blur":
                        blur = seconds;
                        break;
                    case "coast":
                        coast = seconds;
                        break;
                }
            }

            return new MetalStageTimings(raster, blur, coast);
        }
    }
}
