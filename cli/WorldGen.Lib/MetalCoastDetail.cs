using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    public static class MetalCoastDetail
    {
        const string HelperResourceName = "WorldGen.Cli.Lib.MetalCoastHelper.swift";
        static readonly object HelperLock = new();
        static string _helperPath = string.Empty;

        public static bool IsSupported => OperatingSystem.IsMacOS();

        public static void Apply(Image<L16> image, float amplitude, int seed)
        {
            if (amplitude <= 0f) return;
            if (!OperatingSystem.IsMacOS())
                throw new PlatformNotSupportedException("Metal coast detail is only supported on macOS.");

            string helperPath = EnsureHelperBuilt();
            string tempDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            string inputPath = Path.Combine(tempDir, "input.raw");
            string outputPath = Path.Combine(tempDir, "output.raw");
            string permPath = Path.Combine(tempDir, "perm.raw");

            try
            {
                ushort[] pixelData = ExtractPixels(image);
                File.WriteAllBytes(inputPath, MemoryMarshal.AsBytes<ushort>(pixelData.AsSpan()).ToArray());

                var perm = new Noise3D(seed + 777).GetPermutationTable();
                File.WriteAllBytes(permPath, MemoryMarshal.AsBytes<int>(perm.AsSpan()).ToArray());

                var psi = new ProcessStartInfo(helperPath)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                };

                psi.ArgumentList.Add("--mode");
                psi.ArgumentList.Add("coast");
                psi.ArgumentList.Add("--input");
                psi.ArgumentList.Add(inputPath);
                psi.ArgumentList.Add("--output");
                psi.ArgumentList.Add(outputPath);
                psi.ArgumentList.Add("--perm");
                psi.ArgumentList.Add(permPath);
                psi.ArgumentList.Add("--width");
                psi.ArgumentList.Add(image.Width.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--height");
                psi.ArgumentList.Add(image.Height.ToString(CultureInfo.InvariantCulture));
                psi.ArgumentList.Add("--amplitude");
                psi.ArgumentList.Add(amplitude.ToString("R", CultureInfo.InvariantCulture));

                using var process = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start Metal coast helper.");
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                    throw new InvalidOperationException($"Metal coast helper failed with exit code {process.ExitCode}: {detail.Trim()}");
                }

                byte[] outputBytes = File.ReadAllBytes(outputPath);
                if (outputBytes.Length != pixelData.Length * sizeof(ushort))
                    throw new InvalidOperationException($"Metal coast helper returned {outputBytes.Length} bytes, expected {pixelData.Length * sizeof(ushort)}.");

                MemoryMarshal.Cast<byte, ushort>(outputBytes.AsSpan()).CopyTo(pixelData);
                WritePixels(image, pixelData);
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

        internal static string EnsureHelperBuilt()
        {
            lock (HelperLock)
            {
                if (!string.IsNullOrEmpty(_helperPath) && File.Exists(_helperPath))
                    return _helperPath;

                string cacheDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal");
                Directory.CreateDirectory(cacheDir);

                string sourcePath = Path.Combine(cacheDir, "MetalCoastHelper.swift");
                string helperPath = Path.Combine(cacheDir, "metal-coast-helper");

                using (Stream resource = typeof(MetalCoastDetail).Assembly.GetManifestResourceStream(HelperResourceName)
                    ?? throw new InvalidOperationException($"Missing embedded helper resource '{HelperResourceName}'."))
                using (var output = File.Create(sourcePath))
                {
                    resource.CopyTo(output);
                }

                bool needsBuild = !File.Exists(helperPath) ||
                    File.GetLastWriteTimeUtc(helperPath) < File.GetLastWriteTimeUtc(sourcePath);
                if (needsBuild)
                {
                    var psi = new ProcessStartInfo("swiftc")
                    {
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                    };
                    psi.ArgumentList.Add("-O");
                    psi.ArgumentList.Add(sourcePath);
                    psi.ArgumentList.Add("-o");
                    psi.ArgumentList.Add(helperPath);

                    using var process = Process.Start(psi)
                        ?? throw new InvalidOperationException("Failed to start swiftc.");
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                        throw new InvalidOperationException($"swiftc failed with exit code {process.ExitCode}: {detail.Trim()}");
                    }
                }

                _helperPath = helperPath;
                return helperPath;
            }
        }

        internal static ushort[] ExtractPixels(Image<L16> image)
        {
            int width = image.Width;
            int height = image.Height;
            ushort[] pixels = new ushort[width * height];

            if (image.DangerousTryGetSinglePixelMemory(out var contiguous))
            {
                MemoryMarshal.Cast<L16, ushort>(contiguous.Span).CopyTo(pixels);
                return pixels;
            }

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    var row = MemoryMarshal.Cast<L16, ushort>(accessor.GetRowSpan(y));
                    row.CopyTo(pixels.AsSpan(y * width, width));
                }
            });

            return pixels;
        }

        internal static void WritePixels(Image<L16> image, ushort[] pixels)
        {
            int width = image.Width;
            int height = image.Height;

            if (image.DangerousTryGetSinglePixelMemory(out var contiguous))
            {
                pixels.AsSpan().CopyTo(MemoryMarshal.Cast<L16, ushort>(contiguous.Span));
                return;
            }

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    pixels.AsSpan(y * width, width).CopyTo(MemoryMarshal.Cast<L16, ushort>(accessor.GetRowSpan(y)));
                }
            });
        }
    }
}
