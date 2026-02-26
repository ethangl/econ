namespace PopGen.Core
{
    /// <summary>
    /// A generated culture instance referencing a CultureType.
    /// Multiple realms can share the same culture.
    /// </summary>
    public sealed class PopCulture
    {
        public int Id;
        public string Name;
        public int TypeIndex;
        public string TypeName;
        public string NodeId;    // CultureForest node ID, e.g. "norse.danish"
        public int ReligionId;
        public SuccessionLaw SuccessionLaw;
        public GenderLaw GenderLaw;
    }
}
