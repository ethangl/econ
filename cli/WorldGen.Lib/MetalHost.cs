using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace WorldGen.Cli.Lib
{
    internal static class MetalHost
    {
        const string HelperResourceName = "WorldGen.Cli.Lib.MetalCoastHelper.swift";
        static readonly object HelperLock = new();
        static string _helperPath = string.Empty;

        public static bool IsSupported => OperatingSystem.IsMacOS();

        public static string EnsureHelperBuilt()
        {
            lock (HelperLock)
            {
                if (!string.IsNullOrEmpty(_helperPath) && File.Exists(_helperPath))
                    return _helperPath;

                string cacheDir = Path.Combine(Path.GetTempPath(), "econsim-worldgen-metal");
                Directory.CreateDirectory(cacheDir);

                string sourcePath = Path.Combine(cacheDir, "MetalCoastHelper.swift");
                string helperPath = Path.Combine(cacheDir, "metal-coast-helper");

                using (Stream resource = typeof(MetalHost).Assembly.GetManifestResourceStream(HelperResourceName)
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

        public static ushort[] ExtractPixels(Image<L16> image)
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

        public static void WritePixels(Image<L16> image, ushort[] pixels)
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

        public static void WritePixels(Image<Rgb24> image, byte[] pixels)
        {
            int width = image.Width;
            int height = image.Height;

            if (image.DangerousTryGetSinglePixelMemory(out var contiguous))
            {
                pixels.AsSpan().CopyTo(MemoryMarshal.Cast<Rgb24, byte>(contiguous.Span));
                return;
            }

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    pixels.AsSpan(y * width * 3, width * 3).CopyTo(MemoryMarshal.Cast<Rgb24, byte>(accessor.GetRowSpan(y)));
                }
            });
        }

        public static void WriteRaw<T>(string path, T[] values) where T : struct
        {
            File.WriteAllBytes(path, MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
        }

        public static (string StdOut, string StdErr) RunHelper(ProcessStartInfo psi, string failureLabel)
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start {failureLabel}.");
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                throw new InvalidOperationException($"{failureLabel} failed with exit code {process.ExitCode}: {detail.Trim()}");
            }

            return (stdout, stderr);
        }

        public static ProcessStartInfo CreateHelperProcess(string helperPath)
        {
            return new ProcessStartInfo(helperPath)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
        }
    }
}
