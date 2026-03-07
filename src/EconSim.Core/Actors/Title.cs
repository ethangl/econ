namespace EconSim.Core.Actors
{
    public class Title
    {
        public int Id;
        public TitleRank Rank;
        public int TerritoryId;    // CountyId, ProvinceId, or RealmId depending on Rank
        public int HolderActorId;  // De facto holder (0 = vacant)
        public int DeJureActorId;  // De jure holder (0 = same as de facto)
    }
}
