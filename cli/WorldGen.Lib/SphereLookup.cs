using System;
using System.Collections.Generic;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Spatial hash for fast nearest-cell lookup on a sphere.
    /// Buckets cells by lat/lon, then searches nearby buckets for each query.
    /// </summary>
    public class SphereLookup
    {
        readonly float _radius;
        readonly float[] _centerX;
        readonly float[] _centerY;
        readonly float[] _centerZ;
        readonly int[] _bucketOffsets;
        readonly int[] _bucketCounts;
        readonly int[] _bucketCells;
        readonly int _latBuckets;
        readonly int _lonBuckets;

        internal float Radius => _radius;
        internal float[] CenterX => _centerX;
        internal float[] CenterY => _centerY;
        internal float[] CenterZ => _centerZ;
        internal int[] BucketOffsets => _bucketOffsets;
        internal int[] BucketCounts => _bucketCounts;
        internal int[] BucketCells => _bucketCells;
        internal int LatBucketCount => _latBuckets;
        internal int LonBucketCount => _lonBuckets;

        public SphereLookup(Vec3[] cellCenters, float radius, int latBuckets = 180, int lonBuckets = 360)
        {
            _radius = radius;
            _latBuckets = latBuckets;
            _lonBuckets = lonBuckets;
            _centerX = new float[cellCenters.Length];
            _centerY = new float[cellCenters.Length];
            _centerZ = new float[cellCenters.Length];

            var buckets = new List<int>[latBuckets * lonBuckets];
            for (int i = 0; i < buckets.Length; i++)
                buckets[i] = new List<int>();

            for (int c = 0; c < cellCenters.Length; c++)
            {
                _centerX[c] = cellCenters[c].X;
                _centerY[c] = cellCenters[c].Y;
                _centerZ[c] = cellCenters[c].Z;

                var (lat, lon) = CartesianToLatLon(cellCenters[c]);
                int bi = LatLonToBucket(lat, lon);
                buckets[bi].Add(c);
            }

            _bucketOffsets = new int[buckets.Length];
            _bucketCounts = new int[buckets.Length];

            int totalCells = 0;
            for (int i = 0; i < buckets.Length; i++)
            {
                _bucketOffsets[i] = totalCells;
                _bucketCounts[i] = buckets[i].Count;
                totalCells += buckets[i].Count;
            }

            _bucketCells = new int[totalCells];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i].CopyTo(_bucketCells, _bucketOffsets[i]);
            }
        }

        /// <summary>Find nearest cell index to a point on the sphere.</summary>
        public int Nearest(Vec3 point)
        {
            var (lat, lon) = CartesianToLatLon(point);
            return Nearest(point.X, point.Y, point.Z, LatIndex(lat), LonIndex(lon), GetLongitudeSearchRadius(lat));
        }

        /// <summary>Find nearest cell index given lat/lon in radians.</summary>
        public int NearestFromLatLon(float lat, float lon)
        {
            float cosLat = MathF.Cos(lat);
            return Nearest(
                _radius * cosLat * MathF.Cos(lon),
                _radius * MathF.Sin(lat),
                _radius * cosLat * MathF.Sin(lon),
                LatIndex(lat),
                LonIndex(lon),
                GetLongitudeSearchRadius(lat));
        }

        public int GetLatitudeBucket(float lat) => LatIndex(lat);

        public int GetLongitudeBucket(float lon) => LonIndex(lon);

        public int GetLongitudeSearchRadius(float lat)
        {
            // Near poles, longitude buckets are tiny in actual distance.
            // Expand lon search radius by 1/cos(lat) so we always cover enough real area.
            float cosLat = MathF.Cos(lat);
            return cosLat > 0.01f
                ? Math.Min((int)MathF.Ceiling(1f / cosLat), _lonBuckets / 2)
                : _lonBuckets / 2;
        }

        public int Nearest(float px, float py, float pz, int latIdx, int lonIdx, int lonRadius)
        {
            float bestDist = float.MaxValue;
            int bestCell = -1;

            SearchWindow(px, py, pz, latIdx, lonIdx, lonRadius, ref bestDist, ref bestCell);
            if (bestCell >= 0)
                return bestCell;

            // Sparse lookups can leave the initial 3-row search empty. Expand until we
            // hit populated buckets so callers never silently fall back to cell 0.
            for (int latRadius = 2; latRadius < _latBuckets && bestCell < 0; latRadius++)
            {
                SearchWindow(px, py, pz, latIdx, lonIdx, _lonBuckets / 2, ref bestDist, ref bestCell, latRadius);
            }

            return bestCell >= 0 ? bestCell : 0;
        }

        void SearchWindow(
            float px,
            float py,
            float pz,
            int latIdx,
            int lonIdx,
            int lonRadius,
            ref float bestDist,
            ref int bestCell,
            int latRadius = 1)
        {
            lonRadius = Math.Clamp(lonRadius, 0, _lonBuckets / 2);

            for (int dLat = -latRadius; dLat <= latRadius; dLat++)
            {
                int li = latIdx + dLat;
                if (li < 0 || li >= _latBuckets) continue;

                int rowOffset = li * _lonBuckets;
                for (int dLon = -lonRadius; dLon <= lonRadius; dLon++)
                {
                    int lj = (lonIdx + dLon + _lonBuckets) % _lonBuckets;
                    int bucketIndex = rowOffset + lj;
                    int start = _bucketOffsets[bucketIndex];
                    int end = start + _bucketCounts[bucketIndex];

                    for (int i = start; i < end; i++)
                    {
                        int c = _bucketCells[i];
                        float dx = px - _centerX[c];
                        float dy = py - _centerY[c];
                        float dz = pz - _centerZ[c];
                        float dist = dx * dx + dy * dy + dz * dz;
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCell = c;
                        }
                    }
                }
            }
        }

        int LatIndex(float lat)
        {
            // lat: -PI/2..PI/2 → 0..latBuckets-1
            float t = (lat + MathF.PI / 2f) / MathF.PI;
            return Math.Clamp((int)(t * _latBuckets), 0, _latBuckets - 1);
        }

        int LonIndex(float lon)
        {
            // lon: -PI..PI → 0..lonBuckets-1
            float t = (lon + MathF.PI) / (2f * MathF.PI);
            return Math.Clamp((int)(t * _lonBuckets), 0, _lonBuckets - 1);
        }

        int LatLonToBucket(float lat, float lon) => LatIndex(lat) * _lonBuckets + LonIndex(lon);

        public static (float lat, float lon) CartesianToLatLon(Vec3 p)
        {
            float r = p.Magnitude;
            if (r < 1e-9f) return (0f, 0f);
            float lat = MathF.Asin(Math.Clamp(p.Y / r, -1f, 1f));
            float lon = MathF.Atan2(p.Z, p.X);
            return (lat, lon);
        }

        public static Vec3 LatLonToCartesian(float lat, float lon, float radius)
        {
            float cosLat = MathF.Cos(lat);
            return new Vec3(
                radius * cosLat * MathF.Cos(lon),
                radius * MathF.Sin(lat),
                radius * cosLat * MathF.Sin(lon)
            );
        }
    }
}
