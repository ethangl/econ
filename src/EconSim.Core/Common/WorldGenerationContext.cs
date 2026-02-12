namespace EconSim.Core.Common
{
    /// <summary>
    /// Immutable world-generation contract shared across map generation and simulation initialization.
    /// </summary>
    public readonly struct WorldGenerationContext
    {
        public const string DefaultContractVersion = "worldgen-v1";

        public int RootSeed { get; }
        public WorldSeeds Seeds { get; }
        public string ContractVersion { get; }

        public int MapGenSeed => Seeds.MapGenSeed;
        public int PopGenSeed => Seeds.PopGenSeed;
        public int EconomySeed => Seeds.EconomySeed;
        public int SimulationSeed => Seeds.SimulationSeed;

        WorldGenerationContext(int rootSeed, WorldSeeds seeds, string contractVersion)
        {
            RootSeed = rootSeed;
            Seeds = seeds;
            ContractVersion = string.IsNullOrWhiteSpace(contractVersion)
                ? DefaultContractVersion
                : contractVersion;
        }

        public static WorldGenerationContext FromRootSeed(
            int rootSeed,
            string contractVersion = DefaultContractVersion)
        {
            return new WorldGenerationContext(
                rootSeed,
                WorldSeeds.FromRoot(rootSeed),
                contractVersion);
        }
    }
}
