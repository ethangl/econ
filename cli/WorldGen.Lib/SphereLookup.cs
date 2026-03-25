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
        readonly Vec3[] _centers;
        readonly float _radius;
        readonly List<int>[] _buckets;
        readonly int _latBuckets;
        readonly int _lonBuckets;

        public SphereLookup(Vec3[] cellCenters, float radius, int latBuckets = 180, int lonBuckets = 360)
        {
            _centers = cellCenters;
            _radius = radius;
            _latBuckets = latBuckets;
            _lonBuckets = lonBuckets;
            _buckets = new List<int>[latBuckets * lonBuckets];

            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<int>();

            for (int c = 0; c < cellCenters.Length; c++)
            {
                var (lat, lon) = CartesianToLatLon(cellCenters[c]);
                int bi = LatLonToBucket(lat, lon);
                _buckets[bi].Add(c);
            }
        }

        /// <summary>Find nearest cell index to a point on the sphere.</summary>
        public int Nearest(Vec3 point)
        {
            var (lat, lon) = CartesianToLatLon(point);
            int latIdx = LatIndex(lat);
            int lonIdx = LonIndex(lon);

            // Near poles, longitude buckets are tiny in actual distance.
            // Expand lon search radius by 1/cos(lat) so we always cover enough real area.
            float cosLat = MathF.Cos(lat);
            int lonRadius = cosLat > 0.01f
                ? Math.Min((int)MathF.Ceiling(1f / cosLat), _lonBuckets / 2)
                : _lonBuckets / 2; // at the pole, search all longitudes

            float bestDist = float.MaxValue;
            int bestCell = 0;

            for (int dLat = -1; dLat <= 1; dLat++)
            {
                int li = latIdx + dLat;
                if (li < 0 || li >= _latBuckets) continue;

                for (int dLon = -lonRadius; dLon <= lonRadius; dLon++)
                {
                    int lj = (lonIdx + dLon + _lonBuckets) % _lonBuckets;
                    var bucket = _buckets[li * _lonBuckets + lj];

                    for (int i = 0; i < bucket.Count; i++)
                    {
                        int c = bucket[i];
                        float dist = Vec3.SqrDistance(point, _centers[c]);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestCell = c;
                        }
                    }
                }
            }

            return bestCell;
        }

        /// <summary>Find nearest cell index given lat/lon in radians.</summary>
        public int NearestFromLatLon(float lat, float lon)
        {
            return Nearest(LatLonToCartesian(lat, lon, _radius));
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

        static float Math_Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}
