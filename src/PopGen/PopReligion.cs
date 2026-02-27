namespace PopGen.Core
{
    public sealed class PopReligion
    {
        public int Id;
        public string Name;
        public ReligionType Type;
        public string TypeName;
        public int SabbathDay; // 0=Monday .. 6=Sunday
        public int ParentId;   // 0 for root religions
        public Worldview Worldview;
        public Celibacy Celibacy;
        public HolyWar HolyWar;
    }
}
