using System;

namespace MapGen.Core
{
    /// <summary>
    /// Engine-independent Perlin noise (classic 2D).
    /// Uses a permutation table seeded from an integer seed.
    /// </summary>
    public class Noise
    {
        readonly int[] _perm;

        public Noise(int seed)
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
        /// 2D Perlin noise, returns value in approximately [-1, 1].
        /// </summary>
        public float Sample(float x, float y)
        {
            int xi = Floor(x);
            int yi = Floor(y);
            float xf = x - xi;
            float yf = y - yi;
            xi &= 255;
            yi &= 255;

            float u = Fade(xf);
            float v = Fade(yf);

            int aa = _perm[_perm[xi] + yi];
            int ab = _perm[_perm[xi] + yi + 1];
            int ba = _perm[_perm[xi + 1] + yi];
            int bb = _perm[_perm[xi + 1] + yi + 1];

            float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
            float x2 = Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
            return Lerp(x1, x2, v);
        }

        /// <summary>
        /// Normalized 2D Perlin noise, returns value in [0, 1].
        /// </summary>
        public float Sample01(float x, float y)
        {
            return (Sample(x, y) + 1f) * 0.5f;
        }

        static int Floor(float v) => v >= 0 ? (int)v : (int)v - 1;

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static float Lerp(float a, float b, float t) => a + t * (b - a);

        static float Grad(int hash, float x, float y)
        {
            int h = hash & 3;
            switch (h)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                default: return -x - y;
            }
        }
    }
}
