using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Atmospheric debug visualization for coarse wind and precipitation fields.
    /// </summary>
    public static class AtmosphericDebugRenderer
    {
        public static void DrawWindOverlay(Image<Rgb24> image, TectonicData tectonics, SphereMesh mesh)
        {
            if (tectonics.CellWind == null || tectonics.CellWindSpeed == null)
                return;

            int w = image.Width;
            int h = image.Height;
            int dotRadius = Math.Max(1, w / 600);
            int arrowLen = Math.Max(4, w / 160);

            for (int c = 0; c < mesh.CellCount; c++)
            {
                var dotColor = WindColor(tectonics.CellWindSpeed[c]);
                DrawDot(image, mesh.CellCenters[c], dotColor, dotRadius, w, h);
                DrawWindArrow(image, mesh.CellCenters[c], tectonics.CellWind[c], new Rgb24(255, 255, 255), arrowLen);
            }
        }

        public static void DrawPrecipitationOverlay(Image<Rgb24> image, TectonicData tectonics, SphereMesh mesh)
        {
            if (tectonics.CellPrecipitation == null)
                return;

            int w = image.Width;
            int h = image.Height;
            int dotRadius = Math.Max(1, w / 550);

            for (int c = 0; c < mesh.CellCount; c++)
            {
                float precip = tectonics.CellPrecipitation[c];
                if (precip <= 0f)
                    continue;
                DrawDot(image, mesh.CellCenters[c], PrecipitationColor(precip), dotRadius, w, h);
            }
        }

        public static void SaveStandaloneMaps(
            string previewPath,
            int width,
            int height,
            TectonicData tectonics,
            SphereMesh mesh,
            PngEncoder encoder)
        {
            if (tectonics.CellWindSpeed != null)
            {
                using var windImage = RenderScalarField(mesh, tectonics.CellWindSpeed, width, height, WindColor);
                if (tectonics.CellWind != null)
                {
                    int arrowLen = Math.Max(6, width / 120);
                    for (int c = 0; c < mesh.CellCount; c++)
                        DrawWindArrow(windImage, mesh.CellCenters[c], tectonics.CellWind[c], new Rgb24(255, 255, 255), arrowLen);
                }
                windImage.Save(BuildSiblingPath(previewPath, ".wind.png"), encoder);
            }

            if (tectonics.CellPrecipitation != null)
            {
                using var precipImage = RenderScalarField(mesh, tectonics.CellPrecipitation, width, height, PrecipitationColor);
                precipImage.Save(BuildSiblingPath(previewPath, ".precip.png"), encoder);
            }
        }

        static Image<Rgb24> RenderScalarField(
            SphereMesh mesh,
            float[] values,
            int width,
            int height,
            Func<float, Rgb24> colorize)
        {
            var lookup = new SphereLookup(mesh.CellCenters, mesh.Radius);
            var image = new Image<Rgb24>(width, height);

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < height; y++)
                {
                    float lat = MathF.PI / 2f - (y + 0.5f) / height * MathF.PI;
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < width; x++)
                    {
                        float lon = (x + 0.5f) / width * 2f * MathF.PI - MathF.PI;
                        int cell = lookup.NearestFromLatLon(lat, lon);
                        row[x] = colorize(values[cell]);
                    }
                }
            });

            return image;
        }

        static void DrawWindArrow(Image<Rgb24> image, Vec3 position, Vec3 wind, Rgb24 color, int arrowLength)
        {
            if (wind.SqrMagnitude < 1e-8f)
                return;

            var (px, py) = Project(position, image.Width, image.Height);
            BuildLocalBasis(position.Normalized, out Vec3 east, out Vec3 north);
            Vec3 tangent = wind.Normalized;
            float eastComp = Vec3.Dot(tangent, east);
            float northComp = Vec3.Dot(tangent, north);
            int x1 = px + (int)MathF.Round(eastComp * arrowLength);
            int y1 = py - (int)MathF.Round(northComp * arrowLength);
            DrawLine(image, px, py, x1, y1, color);
        }

        static void DrawDot(Image<Rgb24> image, Vec3 position, Rgb24 color, int radius, int w, int h)
        {
            var (px, py) = Project(position, w, h);
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > radius * radius)
                        continue;
                    int ix = (px + dx + w) % w;
                    int iy = Math.Clamp(py + dy, 0, h - 1);
                    image[ix, iy] = color;
                }
            }
        }

        static void DrawLine(Image<Rgb24> image, int x0, int y0, int x1, int y1, Rgb24 color)
        {
            int steps = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            if (steps <= 0)
            {
                if (x0 >= 0 && x0 < image.Width && y0 >= 0 && y0 < image.Height)
                    image[x0, y0] = color;
                return;
            }

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                int x = (int)MathF.Round(x0 + (x1 - x0) * t);
                int y = (int)MathF.Round(y0 + (y1 - y0) * t);
                x = ((x % image.Width) + image.Width) % image.Width;
                if (y >= 0 && y < image.Height)
                    image[x, y] = color;
            }
        }

        static (int x, int y) Project(Vec3 position, int width, int height)
        {
            Vec3 p = position.Normalized;
            float lat = MathF.Asin(Math.Clamp(p.Y, -1f, 1f));
            float lon = MathF.Atan2(p.Z, p.X);
            int px = (int)((lon / MathF.PI + 1f) * 0.5f * width);
            int py = (int)((0.5f - lat / MathF.PI) * height);
            px = ((px % width) + width) % width;
            py = Math.Clamp(py, 0, height - 1);
            return (px, py);
        }

        static void BuildLocalBasis(Vec3 normal, out Vec3 east, out Vec3 north)
        {
            east = new Vec3(-normal.Z, 0f, normal.X);
            if (east.SqrMagnitude < 1e-8f)
                east = new Vec3(1f, 0f, 0f);
            east = east.Normalized;
            north = Vec3.Cross(east, normal).Normalized;
        }

        static string BuildSiblingPath(string previewPath, string suffix)
        {
            string directory = Path.GetDirectoryName(previewPath) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(previewPath);
            return Path.Combine(directory, $"{stem}{suffix}");
        }

        static Rgb24 WindColor(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            return LerpColor(new Rgb24(24, 78, 181), new Rgb24(231, 76, 60), value);
        }

        static Rgb24 PrecipitationColor(float value)
        {
            value = Math.Clamp(value, 0f, 1f);
            if (value < 0.33f)
                return LerpColor(new Rgb24(120, 72, 36), new Rgb24(214, 189, 88), value / 0.33f);
            if (value < 0.66f)
                return LerpColor(new Rgb24(214, 189, 88), new Rgb24(82, 165, 89), (value - 0.33f) / 0.33f);
            return LerpColor(new Rgb24(82, 165, 89), new Rgb24(52, 125, 209), (value - 0.66f) / 0.34f);
        }

        static Rgb24 LerpColor(Rgb24 a, Rgb24 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            byte r = (byte)MathF.Round(a.R + (b.R - a.R) * t);
            byte g = (byte)MathF.Round(a.G + (b.G - a.G) * t);
            byte bl = (byte)MathF.Round(a.B + (b.B - a.B) * t);
            return new Rgb24(r, g, bl);
        }
    }
}
