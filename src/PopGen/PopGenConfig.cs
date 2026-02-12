namespace PopGen.Core
{
    /// <summary>
    /// Configuration for population and political shaping generation.
    /// </summary>
    public class PopGenConfig
    {
        /// <summary>
        /// If true, realm/province color variance incorporates PopGen seed.
        /// Default false to preserve current visual compatibility.
        /// </summary>
        public bool UseSeedForPoliticalColorVariation = false;
    }
}
