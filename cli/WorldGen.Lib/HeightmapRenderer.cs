using System;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
            var projection = SphericalProjection.Create(width, height, lookup);

            if (image.DangerousTryGetSinglePixelMemory(out var pixels))
            {
                Parallel.For(0, height, y =>
                {
                    var row = pixels.Span.Slice(y * width, width);
                    float cosLat = projection.CosLat[y];
                    float rowRadius = mesh.Radius * cosLat;
                    float py = mesh.Radius * projection.SinLat[y];
                    int latIdx = projection.LatBucket[y];
                    int lonRadius = projection.LonSearchRadius[y];

                    for (int x = 0; x < width; x++)
                    {
                        int cell = lookup.Nearest(
                            rowRadius * projection.CosLon[x],
                            py,
                            rowRadius * projection.SinLon[x],
                            latIdx,
                            projection.LonBucket[x],
                            lonRadius);
                        float elev = elevation[cell];
                        ushort val = (ushort)(Math.Clamp(elev, 0f, 1f) * 65535f);
                        row[x] = new L16(val);
                    }
                });
            }
            else
            {
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        float cosLat = projection.CosLat[y];
                        float rowRadius = mesh.Radius * cosLat;
                        float py = mesh.Radius * projection.SinLat[y];
                        int latIdx = projection.LatBucket[y];
                        int lonRadius = projection.LonSearchRadius[y];
                        var row = accessor.GetRowSpan(y);

                        for (int x = 0; x < width; x++)
                        {
                            int cell = lookup.Nearest(
                                rowRadius * projection.CosLon[x],
                                py,
                                rowRadius * projection.SinLon[x],
                                latIdx,
                                projection.LonBucket[x],
                                lonRadius);
                            float elev = elevation[cell];
                            ushort val = (ushort)(Math.Clamp(elev, 0f, 1f) * 65535f);
                            row[x] = new L16(val);
                        }
                    }
                });
            }

            return image;
        }

        sealed class SphericalProjection
        {
            public float[] CosLon { get; }
            public float[] SinLon { get; }
            public int[] LonBucket { get; }
            public float[] CosLat { get; }
            public float[] SinLat { get; }
            public int[] LatBucket { get; }
            public int[] LonSearchRadius { get; }

            SphericalProjection(
                float[] cosLon,
                float[] sinLon,
                int[] lonBucket,
                float[] cosLat,
                float[] sinLat,
                int[] latBucket,
                int[] lonSearchRadius)
            {
                CosLon = cosLon;
                SinLon = sinLon;
                LonBucket = lonBucket;
                CosLat = cosLat;
                SinLat = sinLat;
                LatBucket = latBucket;
                LonSearchRadius = lonSearchRadius;
            }

            public static SphericalProjection Create(int width, int height, SphereLookup lookup)
            {
                var lon = new float[width];
                var sinLon = new float[width];
                var lonBucket = new int[width];
                var cosLat = new float[height];
                var sinLat = new float[height];
                var latBucket = new int[height];
                var lonSearchRadius = new int[height];

                for (int x = 0; x < width; x++)
                {
                    float longitude = (x + 0.5f) / width * 2f * MathF.PI - MathF.PI;
                    lon[x] = MathF.Cos(longitude);
                    sinLon[x] = MathF.Sin(longitude);
                    lonBucket[x] = lookup.GetLongitudeBucket(longitude);
                }

                for (int y = 0; y < height; y++)
                {
                    float latitude = MathF.PI / 2f - (y + 0.5f) / height * MathF.PI;
                    cosLat[y] = MathF.Cos(latitude);
                    sinLat[y] = MathF.Sin(latitude);
                    latBucket[y] = lookup.GetLatitudeBucket(latitude);
                    lonSearchRadius[y] = lookup.GetLongitudeSearchRadius(latitude);
                }

                return new SphericalProjection(lon, sinLon, lonBucket, cosLat, sinLat, latBucket, lonSearchRadius);
            }
        }
    }
}
