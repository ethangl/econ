using System.Collections.Generic;
using UnityEngine;

namespace EconSim.Renderer
{
    /// <summary>
    /// Pure-math utilities for generating noisy (organic-looking) edge polylines.
    /// Extracted from MapOverlayManager so both the rasterized overlay pipeline
    /// and the mesh-based water renderer can share the same noise functions.
    /// </summary>
    public static class NoisyEdgeUtils
    {
        /// <summary>
        /// Build a noisy polyline from straight control-point segments.
        /// Each segment gets fractal midpoint displacement, producing organic meandering.
        /// </summary>
        public static List<Vector2> BuildNoisyPolyline(
            List<Vector2> controlPoints,
            uint seed,
            MapOverlayManager.NoisyEdgeStyle style,
            float baseAmplitudePixels,
            float amplitudeScale = 1f)
        {
            if (controlPoints == null || controlPoints.Count < 2)
                return controlPoints;

            var result = new List<Vector2>(Mathf.Max(controlPoints.Count * 4, controlPoints.Count + 1))
            {
                controlPoints[0]
            };

            for (int i = 0; i < controlPoints.Count - 1; i++)
            {
                uint segmentSeed = BuildSegmentSeed(seed, i);
                AppendNoisySegment(result, controlPoints[i], controlPoints[i + 1], segmentSeed, style, baseAmplitudePixels, amplitudeScale);
            }

            return result;
        }

        /// <summary>
        /// Build a noisy polyline and then smooth it with Catmull-Rom interpolation.
        /// </summary>
        public static List<Vector2> BuildNoisySmoothedPath(
            List<Vector2> controlPoints,
            uint seed,
            MapOverlayManager.NoisyEdgeStyle style,
            float baseAmplitudePixels,
            float amplitudeScale,
            int smoothSamplesPerSegment)
        {
            List<Vector2> noisyPath = BuildNoisyPolyline(controlPoints, seed, style, baseAmplitudePixels, amplitudeScale);
            if (noisyPath == null || noisyPath.Count < 2)
                return noisyPath;

            if (smoothSamplesPerSegment <= 0)
                return noisyPath;

            return SmoothPath(noisyPath, smoothSamplesPerSegment);
        }

        public static void AppendNoisySegment(
            List<Vector2> output,
            Vector2 a,
            Vector2 b,
            uint seed,
            MapOverlayManager.NoisyEdgeStyle style,
            float baseAmplitudePixels,
            float amplitudeScale)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 1e-4f)
            {
                output.Add(b);
                return;
            }

            Vector2 dir = delta / length;
            Vector2 normal = new Vector2(-dir.y, dir.x);
            if (HashSigned(seed, 11, 17, 29) < 0f)
                normal = -normal;

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(length / style.SampleSpacingPx), 2, style.MaxSamples);
            float maxAmplitude = baseAmplitudePixels * Mathf.Max(0f, amplitudeScale);
            float amplitude = Mathf.Min(maxAmplitude, length * 0.2f);

            if (amplitude < 0.35f)
            {
                output.Add(b);
                return;
            }

            var offsets = new float[sampleCount + 1];
            offsets[0] = 0f;
            offsets[sampleCount] = 0f;
            BuildNoisyEdgeOffsets(offsets, 0, sampleCount, amplitude, seed, style.Roughness);

            for (int s = 1; s <= sampleCount; s++)
            {
                float t = s / (float)sampleCount;
                Vector2 point = a + dir * (length * t);
                float offset = s == sampleCount ? 0f : SampleNoisyEdgeOffset(offsets, t);
                output.Add(point + normal * offset);
            }
        }

        public static void BuildNoisyEdgeOffsets(float[] offsets, int start, int end, float amplitude, uint seed, float roughness)
        {
            if (end - start <= 1)
                return;

            int mid = (start + end) >> 1;
            float center = 0.5f * (offsets[start] + offsets[end]);
            float jitter = HashSigned(seed, start, mid, end) * amplitude;
            offsets[mid] = center + jitter;

            float nextAmplitude = amplitude * roughness;
            BuildNoisyEdgeOffsets(offsets, start, mid, nextAmplitude, seed, roughness);
            BuildNoisyEdgeOffsets(offsets, mid, end, nextAmplitude, seed, roughness);
        }

        public static float SampleNoisyEdgeOffset(float[] offsets, float t)
        {
            if (offsets == null || offsets.Length == 0)
                return 0f;

            float clampedT = Mathf.Clamp01(t);
            float pos = clampedT * (offsets.Length - 1);
            int i0 = Mathf.FloorToInt(pos);
            int i1 = Mathf.Min(offsets.Length - 1, i0 + 1);
            float frac = pos - i0;
            return Mathf.Lerp(offsets[i0], offsets[i1], frac);
        }

        public static List<Vector2> SmoothPath(List<Vector2> points, int samplesPerSegment)
        {
            if (points.Count < 2)
                return points;

            var result = new List<Vector2>();

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p0 = points[Mathf.Max(0, i - 1)];
                Vector2 p1 = points[i];
                Vector2 p2 = points[i + 1];
                Vector2 p3 = points[Mathf.Min(points.Count - 1, i + 2)];

                for (int j = 0; j < samplesPerSegment; j++)
                {
                    float t = (float)j / samplesPerSegment;
                    result.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }

            result.Add(points[points.Count - 1]);
            return result;
        }

        public static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        public static uint MixHash(uint state, uint value)
        {
            uint x = state ^ (value + 0x9e3779b9u + (state << 6) + (state >> 2));
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return x;
        }

        public static float HashSigned(uint seed, int a, int b, int c)
        {
            uint h = MixHash(seed, (uint)a);
            h = MixHash(h, (uint)b);
            h = MixHash(h, (uint)c);
            float unit = h / (float)uint.MaxValue;
            return unit * 2f - 1f;
        }

        public static uint BuildUnorderedPairSeed(uint rootSeed, int a, int b)
        {
            uint lo = (uint)Mathf.Min(a, b);
            uint hi = (uint)Mathf.Max(a, b);
            return MixHash(MixHash(rootSeed, lo), hi);
        }

        public static uint BuildSegmentSeed(uint baseSeed, int segmentIndex)
        {
            return MixHash(baseSeed, (uint)segmentIndex);
        }

        public static float GetBaseAmplitude(float effectiveResMultiplier, MapOverlayManager.NoisyEdgeStyle style)
        {
            return Mathf.Min(style.AmplitudeCap, Mathf.Max(1.0f, effectiveResMultiplier * style.AmplitudePerResolution));
        }
    }
}
