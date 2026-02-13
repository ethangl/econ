namespace PopGen.Core
{
    public enum ReligionType
    {
        Monotheistic,
        Polytheistic,
        Animistic,
        AncestorWorship,
        Dualistic,
        Philosophical
    }

    public static class ReligionSuffixes
    {
        static readonly string[] Monotheistic = { "ism", "ity", "ion", "aith", "ance", "ence" };
        static readonly string[] Polytheistic = { "eon", "ara", "oth", "ium", "anth", "onar" };
        static readonly string[] Animistic = { "awa", "ani", "uli", "ora", "iru", "oma" };
        static readonly string[] AncestorWorship = { "und", "eld", "orn", "ast", "ith", "ren" };
        static readonly string[] Dualistic = { "oth", "iel", "nar", "ux", "yon", "ael" };
        static readonly string[] Philosophical = { "os", "eia", "sis", "ium", "oia", "eos" };

        public static string[] GetSuffixes(ReligionType type)
        {
            switch (type)
            {
                case ReligionType.Monotheistic: return Monotheistic;
                case ReligionType.Polytheistic: return Polytheistic;
                case ReligionType.Animistic: return Animistic;
                case ReligionType.AncestorWorship: return AncestorWorship;
                case ReligionType.Dualistic: return Dualistic;
                case ReligionType.Philosophical: return Philosophical;
                default: return Monotheistic;
            }
        }
    }
}
