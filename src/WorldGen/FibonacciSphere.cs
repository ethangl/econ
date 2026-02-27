using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Generates nearly-uniform point distributions on a unit sphere
    /// using the Fibonacci (golden spiral) method.
    /// </summary>
    public static class FibonacciSphere
    {
        const float GoldenRatio = 1.6180339887498949f;

        /// <summary>
        /// Generate N points on the unit sphere using the golden spiral method.
        /// Points are well-separated and approximately uniform.
        /// </summary>
        /// <param name="count">Number of points to generate</param>
        /// <param name="jitter">Jitter amount (0 = none, 1 = max). Displaces points randomly
        /// by up to jitter * average spacing.</param>
        /// <param name="seed">Random seed for jitter</param>
        public static Vec3[] Generate(int count, float jitter = 0f, int seed = 0)
        {
            if (count < 4)
                throw new ArgumentException("Need at least 4 points for a tetrahedron", nameof(count));

            var points = new Vec3[count];
            var rng = jitter > 0f ? new Random(seed) : null;

            // Average angular spacing between points
            float avgSpacing = (float)Math.Sqrt(4.0 * Math.PI / count);

            for (int i = 0; i < count; i++)
            {
                // Theta: polar angle from north pole
                // Map i uniformly in cos(theta) space
                float cosTheta = 1f - 2f * (i + 0.5f) / count;
                float theta = (float)Math.Acos(Math.Max(-1.0, Math.Min(1.0, cosTheta)));

                // Phi: azimuthal angle, incremented by golden angle
                float phi = 2f * (float)Math.PI * i / GoldenRatio;

                if (rng != null && jitter > 0f)
                {
                    // Jitter in theta and phi
                    float dTheta = (float)(rng.NextDouble() - 0.5) * 2f * jitter * avgSpacing;
                    float dPhi = (float)(rng.NextDouble() - 0.5) * 2f * jitter * avgSpacing;
                    theta = Math.Max(0.001f, Math.Min((float)Math.PI - 0.001f, theta + dTheta));
                    phi += dPhi;
                }

                float sinTheta = (float)Math.Sin(theta);
                points[i] = new Vec3(
                    sinTheta * (float)Math.Cos(phi),
                    sinTheta * (float)Math.Sin(phi),
                    cosTheta
                );

                // Re-normalize after jitter (theta changed but cosTheta didn't update)
                if (rng != null && jitter > 0f)
                {
                    float st = (float)Math.Sin(theta);
                    float ct = (float)Math.Cos(theta);
                    points[i] = new Vec3(
                        st * (float)Math.Cos(phi),
                        st * (float)Math.Sin(phi),
                        ct
                    );
                    points[i] = points[i].Normalized;
                }
            }

            return points;
        }
    }
}
