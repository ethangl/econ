namespace PopGen.Core
{
    /// <summary>
    /// A node in the culture forest. Roots represent broad culture families (e.g. "norse"),
    /// leaves represent specific cultures assigned to realms (e.g. "norse.danish").
    /// Only leaf nodes are assigned to realms.
    /// </summary>
    public sealed class CultureNode
    {
        public string Id;               // e.g. "norse", "norse.icelandic"
        public string DisplayName;      // e.g. "Icelandic"
        public string ParentId;         // null for roots
        public int CultureTypeIndex;    // index into CultureTypes.All
        public float EquatorProximity;  // 0.0=polar, 1.0=equatorial (meaningful on roots)
        public float LongitudeBand;     // 0.0-1.0: normalized longitude position (roots only)
        public bool IsLeaf;             // only leaves are assigned to realms
        public SuccessionLaw SuccessionLaw;
        public GenderLaw GenderLaw;
    }
}
