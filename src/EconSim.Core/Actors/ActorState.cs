namespace EconSim.Core.Actors
{
    public class ActorState
    {
        /// <summary>All actors, indexed by actor ID. Slot 0 unused.</summary>
        public Actor[] Actors;

        /// <summary>All titles, indexed by title ID. Slot 0 unused.</summary>
        public Title[] Titles;

        /// <summary>Total number of valid actors (excluding slot 0).</summary>
        public int ActorCount;

        /// <summary>Total number of valid titles (excluding slot 0).</summary>
        public int TitleCount;

        // Title ID ranges (set during bootstrap):
        // County titles:  1 .. CountyTitleCount
        // Province titles: CountyTitleCount+1 .. CountyTitleCount+ProvinceTitleCount
        // Realm titles:    CountyTitleCount+ProvinceTitleCount+1 .. SecularTitleCount

        public int CountyTitleCount;
        public int ProvinceTitleCount;
        public int RealmTitleCount;

        // Religious title ranges (set by ReligionBootstrap):
        // Parish titles:      SecularTitleCount+1 .. SecularTitleCount+ParishTitleCount
        // Diocese titles:     SecularTitleCount+ParishTitleCount+1 .. +DioceseTitleCount
        // Archdiocese titles: ... +1 .. TitleCount

        public int ParishTitleCount;
        public int DioceseTitleCount;
        public int ArchdioceseTitleCount;

        /// <summary>Number of secular (noble) titles.</summary>
        public int SecularTitleCount => CountyTitleCount + ProvinceTitleCount + RealmTitleCount;

        /// <summary>Get the title ID for a county. O(1).</summary>
        public int GetCountyTitleId(int countyId) => countyId; // county titles start at 1, matching countyId

        /// <summary>Get the title ID for a province. O(1).</summary>
        public int GetProvinceTitleId(int provinceId) => CountyTitleCount + provinceId;

        /// <summary>Get the title ID for a realm. O(1).</summary>
        public int GetRealmTitleId(int realmId) => CountyTitleCount + ProvinceTitleCount + realmId;

        /// <summary>Get the title ID for a parish (1-based parishId). O(1).</summary>
        public int GetParishTitleId(int parishId) => SecularTitleCount + parishId;

        /// <summary>Get the title ID for a diocese (1-based dioceseId). O(1).</summary>
        public int GetDioceseTitleId(int dioceseId) => SecularTitleCount + ParishTitleCount + dioceseId;

        /// <summary>Get the title ID for an archdiocese (1-based archdioceseId). O(1).</summary>
        public int GetArchdioceseTitleId(int archdioceseId) => SecularTitleCount + ParishTitleCount + DioceseTitleCount + archdioceseId;

        /// <summary>Get the actor who holds a given title, or null if vacant.</summary>
        public Actor GetHolder(int titleId)
        {
            if (titleId <= 0 || titleId >= Titles.Length) return null;
            int actorId = Titles[titleId].HolderActorId;
            if (actorId <= 0 || actorId >= Actors.Length) return null;
            return Actors[actorId];
        }

        /// <summary>Get the actor controlling a county.</summary>
        public Actor GetCountyHolder(int countyId) => GetHolder(GetCountyTitleId(countyId));

        /// <summary>Get the actor controlling a province.</summary>
        public Actor GetProvinceHolder(int provinceId) => GetHolder(GetProvinceTitleId(provinceId));

        /// <summary>Get the actor controlling a realm.</summary>
        public Actor GetRealmHolder(int realmId) => GetHolder(GetRealmTitleId(realmId));
    }
}
