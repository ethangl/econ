# Layer 2: Actors & Peerage — Implementation Plan

## Goal

Introduce named individuals (actors) who hold titles linked to territories.
At the end of this layer, every county, province, and realm has a named noble
ruler visible in the UI. Actors exist but don't yet act — behavior comes in
later layers.

## Data Model

### TitleRank enum

```csharp
// src/EconSim.Core/Actors/TitleRank.cs
namespace EconSim.Core.Actors
{
    public enum TitleRank
    {
        County = 0,   // Count / Prior / Alderman
        Province = 1, // Duke / Bishop / Guildmaster
        Realm = 2,    // King / Archbishop / (rare)
    }
}
```

### Title

```csharp
// src/EconSim.Core/Actors/Title.cs
namespace EconSim.Core.Actors
{
    public class Title
    {
        public int Id;
        public TitleRank Rank;
        public int TerritoryId;    // CountyId, ProvinceId, or RealmId depending on Rank
        public int HolderActorId;  // De facto holder (0 = vacant)
        public int DeJureActorId;  // De jure holder (0 = same as holder)
    }
}
```

One title per territory. At bootstrap, de jure = de facto for all titles.
Title IDs are globally unique across all ranks. Allocation scheme:

- County titles: 1 .. countyCount
- Province titles: countyCount+1 .. countyCount+provinceCount
- Realm titles: countyCount+provinceCount+1 .. countyCount+provinceCount+realmCount

This gives O(1) lookup from territory to title without a dictionary.

### Actor

```csharp
// src/EconSim.Core/Actors/Actor.cs
namespace EconSim.Core.Actors
{
    public class Actor
    {
        public int Id;             // 1-based
        public string Name;        // Full display name, e.g. "Erik Halvorsen"
        public string GivenName;   // First name only
        public int BirthDay;       // Simulation day (for age calculation)
        public bool IsFemale;
        public int CultureId;
        public int ReligionId;
        public int TitleId;        // Primary title held (0 = unlanded)
        public int LiegeActorId;   // Direct superior (0 = sovereign / none)
        public int CountyId;       // Home county (where the actor "lives")
    }
}
```

Minimal. No traits, no ambitions, no opinion vectors — those are later layers.
`LiegeActorId` captures the feudal chain without the full contract system.

### ActorState

```csharp
// src/EconSim.Core/Actors/ActorState.cs
namespace EconSim.Core.Actors
{
    public class ActorState
    {
        public Actor[] Actors;                        // Indexed by actor ID (slot 0 unused)
        public Title[] Titles;                        // Indexed by title ID (slot 0 unused)
        public Dictionary<int, int> CountyTitleId;    // CountyId -> TitleId
        public Dictionary<int, int> ProvinceTitleId;  // ProvinceId -> TitleId
        public Dictionary<int, int> RealmTitleId;     // RealmId -> TitleId
    }
}
```

Owned by `SimulationRunner` alongside `EconomyState`. Passed to systems that
need actor lookups. The reverse lookups (territory → title) are dictionaries
for clarity, but could be computed from the ID allocation scheme.

## Person Name Generation

Extend `PopNameGenerator` with person name methods. Person names use the
same phonetic system as place names but with different structure:

```
GenerateGivenName(actorId, cultureId, isFemale, seed) -> string
GenerateFamilyName(actorId, cultureId, seed) -> string
```

**Given names:** shorter (1-2 syllables), no suffix. Use the culture's
existing phonetic tables (onsets, vowels, codas) but cap length.

**Family names:** patronymic-style using culture suffixes. Each CultureType
gets a `PersonSuffixes` array (e.g. Norse: "-sen", "-sson", "-dottir";
Germanic: "-berg", "-stein"; Celtic: "mac ", "O'"; Slavic: "-ov", "-ich").

Female variants where culturally appropriate (e.g. Norse "-dottir" vs
"-sson"). The `isFemale` + culture determines suffix selection.

**Full name:** `"{GivenName} {FamilyName}"` — simple for now.

### New arrays on CultureType

```csharp
public string[] MalePersonSuffixes;
public string[] FemalePersonSuffixes;
```

Fallback: if not defined, use existing `CountySuffixes` (place-like family
names are historically common).

## Bootstrap: Initial Actor Spawning

Runs once during world generation, after PopGen produces realms/provinces/
counties. This is a new step in `WorldGenImporter` (or a new
`ActorBootstrap` class called from `GameManager`).

### Algorithm

1. **Create titles** — one per county, province, realm.

2. **Spawn realm actors (kings):**
   - For each realm: create an actor from the realm's culture/religion.
   - Assign the realm title. LiegeActorId = 0 (sovereign).
   - CountyId = capital county of the realm.
   - Age: random 25-55 (seeded).

3. **Spawn province actors (dukes):**
   - For each province: create an actor from the province's realm culture.
   - Assign the province title.
   - LiegeActorId = the realm actor who holds this province's realm.
   - CountyId = capital county of the province.

4. **Spawn county actors (counts):**
   - For each county: create an actor.
   - Assign the county title.
   - LiegeActorId = the province actor who holds this county's province.
   - CountyId = this county.

5. **Collapse where appropriate:**
   - A realm's capital county title is held by the king (no separate count).
   - A province's capital county title is held by the duke.
   - This avoids spawning actors for territories that are directly held by
     their superior. The actor holds multiple titles implicitly — their
     `TitleId` is their highest-rank title, and the lower titles just point
     `HolderActorId` at the same actor.

### Expected actor count

For a map with ~10 realms, ~100 provinces, ~1000 counties:
- ~10 realm actors
- ~90 province actors (100 minus ~10 collapsed into realm holders)
- ~900 county actors (1000 minus ~100 collapsed into province holders)
- Total: ~1000 actors, all upper nobility

All actors are Estate.UpperNobility at bootstrap. Clergy and commoner
title-holders are exceptional and come later.

## Integration Points

### SimulationRunner

Add `ActorState` field alongside `EconomyState`. Initialized during
`GameManager.InitializeWithMapData` after economy init.

### SelectionPanel (UI)

When a county/province/realm is selected, show the controlling actor's name
and title. E.g. "Count Erik Halvorsen" for a county, "King Sigurd Norssen"
for a realm. This is the primary visible output of Layer 2.

### MapData

No changes to `County`, `Province`, `Realm` structs. The link is through
`ActorState.CountyTitleId[countyId]` → title → actor, not through fields
on the territory.

## File Plan

New files:
```
src/EconSim.Core/Actors/TitleRank.cs
src/EconSim.Core/Actors/Title.cs
src/EconSim.Core/Actors/Actor.cs
src/EconSim.Core/Actors/ActorState.cs
src/EconSim.Core/Actors/ActorBootstrap.cs   -- spawning logic
```

Modified files:
```
src/PopGen/PopNameGenerator.cs              -- add person name methods
src/PopGen/CultureType.cs                   -- add PersonSuffixes arrays
src/EconSim.Core/ISimulation.cs             -- expose ActorState (if needed)
src/EconSim.Core/SimulationRunner.cs        -- hold ActorState
unity/Assets/Scripts/GameManager.cs          -- wire bootstrap
unity/Assets/Scripts/UI/SelectionPanel.cs    -- display actor names
```

## Implementation Order

1. **Data model** — TitleRank, Title, Actor, ActorState (pure data, no deps)
2. **Person name gen** — extend PopNameGenerator + CultureType
3. **ActorBootstrap** — spawning logic, title creation, liege chain
4. **Wire into GameManager** — call bootstrap after map gen, store ActorState
5. **SelectionPanel** — show actor name/title on territory selection

Steps 1-2 are independent and can be done in parallel.
Step 3 depends on both.
Steps 4-5 depend on 3.

## What's NOT in scope

- Actor decision-making, ambitions, or behavior
- Opinions or social currencies (Layer 3)
- Contracts or feudal relationship terms (Layer 4)
- Marriage, offspring, death, succession (Layer 6)
- Clergy or commoner title-holders
- Unlanded actors (courtiers, kin)
- Actor aging or lifecycle during simulation
- Any runtime mutations to the actor/title graph
