namespace EconSim.Renderer
{
    /// <summary>
    /// Static utility for coherent domain warping of grid coordinates.
    /// Value noise implementation matches MapOverlay.shader hash2d/valueNoise (lines 214-236).
    /// Two decorrelated noise evaluations per point (X and Y offset via seed offset).
    /// </summary>
    public static class DomainWarp
    {
        // ~25 grid-pixel period, roughly one wobble per cell
        public const float Frequency = 0.04f;

        // ~1.3 data pixels displacement at 6x resolution, well under half cell radius
        public const float Amplitude = 8.0f;

        // Seed offset to decorrelate X and Y warp channels
        private const float SeedOffset = 137.5f;

        /// <summary>
        /// Warp a grid-pixel coordinate. Returns warped (x, y).
        /// </summary>
        public static (float wx, float wy) Warp(float x, float y)
        {
            float nx = x * Frequency;
            float ny = y * Frequency;

            float offsetX = (ValueNoise(nx, ny) - 0.5f) * 2f * Amplitude;
            float offsetY = (ValueNoise(nx + SeedOffset, ny + SeedOffset) - 0.5f) * 2f * Amplitude;

            return (x + offsetX, y + offsetY);
        }

        /// <summary>
        /// Warp a data-space coordinate. Scales to grid space, warps, scales back.
        /// </summary>
        public static (float wx, float wy) WarpDataPoint(float dataX, float dataY, float gridScale)
        {
            float gx = dataX * gridScale;
            float gy = dataY * gridScale;
            var (wx, wy) = Warp(gx, gy);
            return (wx / gridScale, wy / gridScale);
        }

        // Matches shader hash2d: frac(float3(p.xyx) * 0.1031) -> dot+frac
        private static float Hash2D(float px, float py)
        {
            // p3 = frac(float3(p.x, p.y, p.x) * 0.1031)
            float p3x = Frac(px * 0.1031f);
            float p3y = Frac(py * 0.1031f);
            float p3z = Frac(px * 0.1031f);

            // p3 += dot(p3, p3.yzx + 33.33)
            float d = p3x * (p3y + 33.33f) + p3y * (p3z + 33.33f) + p3z * (p3x + 33.33f);
            p3x += d;
            p3y += d;
            p3z += d;

            // return frac((p3.x + p3.y) * p3.z)
            return Frac((p3x + p3y) * p3z);
        }

        // Matches shader valueNoise with cubic (smoothstep) interpolation
        private static float ValueNoise(float px, float py)
        {
            float ix = Floor(px);
            float iy = Floor(py);
            float fx = px - ix;
            float fy = py - iy;

            // Cubic interpolation: f * f * (3 - 2 * f)
            float ux = fx * fx * (3f - 2f * fx);
            float uy = fy * fy * (3f - 2f * fy);

            float a = Hash2D(ix, iy);
            float b = Hash2D(ix + 1f, iy);
            float c = Hash2D(ix, iy + 1f);
            float d = Hash2D(ix + 1f, iy + 1f);

            // Bilinear interpolation with cubic weights
            float ab = a + (b - a) * ux;
            float cd = c + (d - c) * ux;
            return ab + (cd - ab) * uy;
        }

        private static float Frac(float x)
        {
            return x - Floor(x);
        }

        private static float Floor(float x)
        {
            int i = (int)x;
            return x < i ? i - 1f : i;
        }
    }
}
