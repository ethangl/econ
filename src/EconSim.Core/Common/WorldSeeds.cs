namespace EconSim.Core.Common
{
    /// <summary>
    /// Deterministic per-system seeds derived from a single world seed.
    /// </summary>
    public readonly struct WorldSeeds
    {
        public int RootSeed { get; }
        public int MapGenSeed { get; }
        public int PopGenSeed { get; }
        public int EconomySeed { get; }
        public int SimulationSeed { get; }

        WorldSeeds(int rootSeed, int mapGenSeed, int popGenSeed, int economySeed, int simulationSeed)
        {
            RootSeed = rootSeed;
            MapGenSeed = mapGenSeed;
            PopGenSeed = popGenSeed;
            EconomySeed = economySeed;
            SimulationSeed = simulationSeed;
        }

        public static WorldSeeds FromRoot(int rootSeed)
        {
            return new WorldSeeds(
                rootSeed,
                Derive(rootSeed, 0x9E3779B9u),
                Derive(rootSeed, 0xA54FF53Au),
                Derive(rootSeed, 0x63D83595u),
                Derive(rootSeed, 0x7B9D14E1u));
        }

        static int Derive(int rootSeed, uint stream)
        {
            uint x = (uint)rootSeed;
            x ^= stream + 0x9E3779B9u + (x << 6) + (x >> 2);
            x ^= x >> 16;
            x *= 0x7FEB352Du;
            x ^= x >> 15;
            x *= 0x846CA68Bu;
            x ^= x >> 16;

            // Avoid zero so downstream Random(seed) always diverges from default initialization paths.
            if (x == 0)
                x = 0x6D2B79F5u;

            return (int)(x & 0x7FFFFFFF);
        }
    }
}
