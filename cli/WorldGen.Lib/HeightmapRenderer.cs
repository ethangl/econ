using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Renders a sphere's cell elevation data to a 2D equirectangular heightmap image.
    /// </summary>
    public static class HeightmapRenderer
    {
        /// <summary>
        /// Render an equirectangular heightmap from dense terrain data.
        /// </summary>
        public static Image<L16> Render(DenseTerrainData terrain, int width, int height)
        {
            var mesh = terrain.Mesh;
            var elevation = terrain.CellElevation;
            var lookup = new SphereLookup(mesh.CellCenters, mesh.Radius);
            var image = new Image<L16>(width, height);

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
                        float elev = elevation[cell];
                        ushort val = (ushort)(Math.Clamp(elev, 0f, 1f) * 65535f);
                        row[x] = new L16(val);
                    }
                }
            });

            return image;
        }

        /// <summary>
        /// Render an 8-bit grayscale heightmap.
        /// </summary>
        public static Image<L8> Render8(DenseTerrainData terrain, int width, int height)
        {
            var mesh = terrain.Mesh;
            var elevation = terrain.CellElevation;
            var lookup = new SphereLookup(mesh.CellCenters, mesh.Radius);
            var image = new Image<L8>(width, height);

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
                        float elev = elevation[cell];
                        byte val = (byte)(Math.Clamp(elev, 0f, 1f) * 255f);
                        row[x] = new L8(val);
                    }
                }
            });

            return image;
        }
    }
}
