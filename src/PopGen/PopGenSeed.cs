namespace PopGen.Core
{
    /// <summary>
    /// Deterministic seed for PopGen generation.
    /// </summary>
    public readonly struct PopGenSeed
    {
        public int Value { get; }

        public PopGenSeed(int value)
        {
            Value = value;
        }

        public static PopGenSeed Default => new PopGenSeed(0);
    }
}
