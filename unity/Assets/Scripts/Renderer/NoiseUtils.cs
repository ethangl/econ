namespace EconSim.Renderer
{
    /// <summary>
    /// Shared noise and hash utilities for deterministic procedural generation.
    /// </summary>
    public static class NoiseUtils
    {
        /// <summary>
        /// Hash single int to float in range [-1, 1]. Deterministic.
        /// </summary>
        public static float HashToFloat(int seed)
        {
            uint h = (uint)(seed * 374761393);
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF * 2f - 1f;
        }

        /// <summary>
        /// Combine multiple ints into single hash seed.
        /// </summary>
        public static int HashCombine(int a, int b, int c)
        {
            return a * 374761393 + b * 668265263 + c * 1013904223;
        }
    }
}
