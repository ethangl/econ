using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Engine-independent 3D Perlin noise.
    /// Uses a permutation table seeded from an integer seed.
    /// Follows the same pattern as MapGen.Core.Noise (2D).
    /// </summary>
    public class Noise3D
    {
        readonly int[] _perm;

        public Noise3D(int seed)
        {
            _perm = new int[512];
            var rng = new Random(seed);
            int[] p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            // Fisher-Yates shuffle
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = p[i]; p[i] = p[j]; p[j] = tmp;
            }
            for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
        }

        /// <summary>
        /// 3D Perlin noise, returns value in approximately [-1, 1].
        /// </summary>
        public float Sample(float x, float y, float z)
        {
            int xi = Floor(x);
            int yi = Floor(y);
            int zi = Floor(z);
            float xf = x - xi;
            float yf = y - yi;
            float zf = z - zi;
            xi &= 255;
            yi &= 255;
            zi &= 255;

            float u = Fade(xf);
            float v = Fade(yf);
            float w = Fade(zf);

            int a  = _perm[xi] + yi;
            int aa = _perm[a] + zi;
            int ab = _perm[a + 1] + zi;
            int b  = _perm[xi + 1] + yi;
            int ba = _perm[b] + zi;
            int bb = _perm[b + 1] + zi;

            float x1 = Lerp(Grad(_perm[aa], xf, yf, zf), Grad(_perm[ba], xf - 1f, yf, zf), u);
            float x2 = Lerp(Grad(_perm[ab], xf, yf - 1f, zf), Grad(_perm[bb], xf - 1f, yf - 1f, zf), u);
            float y1 = Lerp(x1, x2, v);

            float x3 = Lerp(Grad(_perm[aa + 1], xf, yf, zf - 1f), Grad(_perm[ba + 1], xf - 1f, yf, zf - 1f), u);
            float x4 = Lerp(Grad(_perm[ab + 1], xf, yf - 1f, zf - 1f), Grad(_perm[bb + 1], xf - 1f, yf - 1f, zf - 1f), u);
            float y2 = Lerp(x3, x4, v);

            return Lerp(y1, y2, w);
        }

        /// <summary>
        /// Fractal (multi-octave) 3D Perlin noise, returns value in approximately [-1, 1].
        /// </summary>
        public float Fractal(float x, float y, float z, int octaves, float lacunarity, float persistence)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = 1f;
            float maxAmp = 0f;

            for (int i = 0; i < octaves; i++)
            {
                sum += Sample(x * freq, y * freq, z * freq) * amp;
                maxAmp += amp;
                freq *= lacunarity;
                amp *= persistence;
            }

            return sum / maxAmp;
        }

        static int Floor(float v) => v >= 0 ? (int)v : (int)v - 1;

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Lerp(float a, float b, float t) => a + t * (b - a);

        // Ken Perlin's optimized gradient for 3D (12 edge vectors via bit ops)
        static float Grad(int hash, float x, float y, float z)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
