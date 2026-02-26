# Culture

See also: [Estates & Actors](estates-and-actors.md) | [Religion](religion.md)

## Overview

Cultures are the primary identity system for populations and actors. They
determine naming conventions, traditions (including succession law), opinion
baselines, and social currency scoping. Cultures are organized as a forest
with sibling affinity — related cultures recognize shared heritage.

## Current Implementation

The culture system is already partially implemented across PopGen and
EconSim.Core. This document describes both what exists and what needs to
be built for the estates/actors system.

### What Exists

**Culture forest** (`src/PopGen/CultureForest.cs`):

- 5 root families: Norse, Celtic, Germanic, Uralic, Balto-Slavic
- ~15 leaf cultures (depth 2)
- Only leaf nodes are assigned to realms
- Each root has an `EquatorProximity` (0=polar, 1=equatorial) for
  latitude-based selection
- Tree queries: `TreeDistance(a, b)` via LCA, `Affinity(a, b)` = 1/(1+dist)
- Cross-tree distance = `int.MaxValue`, cross-tree affinity = 0

```
Norse (0.15)
├── Icelandic
├── Danish
└── Finnish

Celtic (0.30)
├── Welsh
├── Gaelic
└── Brythonic

Germanic (0.40)
├── Lowland
├── Highland
└── Coastal

Uralic (0.20)
├── Western Uralic
└── Eastern Uralic

Balto-Slavic (0.50)
├── Northern Slavic
├── Southern Slavic
└── Baltic
```

**Culture types** (`src/PopGen/CultureType.cs`):

13 phonetic templates (Finnish, Icelandic, Danish, Welsh, Gaelic, Brythonic,
LowGerman, HighGerman, Frisian, WestSlavic, SouthSlavic, Baltic, Ugric),
each defining:

- Phonetic fragment tables (onsets, vowels, codas) for name generation
- Geographic suffixes (realm, province, county) for place names
- Government forms (culture-specific labels for political entities)
- Directional prefixes (North/South/etc. in the culture's language)

**Culture assignment pipeline:**

1. `CultureForest.SelectForLatitudeRange()` picks leaf cultures based on
   map latitude — polar maps get Norse/Uralic, temperate maps get
   Germanic/Balto-Slavic
2. `PopGenPipeline.BuildCultures()` creates `PopCulture` instances
   (count = max(1, realmCount / 2))
3. Realms are assigned cultures; cells inherit from their realm
4. Religions are generated separately and assigned to cultures by
   geographic proximity

**Data model** (`src/EconSim.Core/Data/MapData.cs`):

- `Culture` class: Id, Name, TypeName, NodeId, ReligionId
- `Cell.CultureId` — per-cell culture assignment
- `Realm.CultureId` — each realm has a primary culture
- `Burg.CultureId` — burgs inherit from realm
- `MapData.Cultures[]` / `MapData.CultureById` — registry

**Culture spread** (`PoliticalGenerationOps.AssignByTransportFrontier()`):

Multi-source Dijkstra from culture seeds, weighted by movement cost and
distance. Terrain naturally shapes culture boundaries — mountains and
deserts become cultural divides. Known limitation: no max water crossing
distance, so cultures can spread across wide ocean gaps on archipelago maps.

### What Needs to Change

The existing system handles identity, naming, and geographic distribution.
What's missing is the **behavioral** dimension — traditions that affect
gameplay mechanics.

## Sibling Dynamics

Unlike religions (where siblings are hostile), sibling cultures have
**baseline affinity**. The closer the relationship in the tree, the more
mutual recognition:

| Tree Distance              | Opinion Baseline                      |
| -------------------------- | ------------------------------------- |
| Same culture               | Neutral to positive (in-group)        |
| Sibling (shared parent)    | **Mildly positive** (shared heritage) |
| Cousin (shared root)       | Neutral (recognizably related)        |
| Unrelated (different root) | Mildly negative (foreign)             |

This is the inverse of religion's sibling dynamics. Two Norse cultures
(Icelandic and Danish) recognize kinship even if they're politically
separate. A Norse and a Celtic culture are foreign but not hostile. The
affinity function already implements this: `1/(1+distance)`.

## Traditions

Cultures carry **traditions** — inherited behavioral rules that shape how
actors of that culture behave by default. Traditions are the cultural
equivalent of religious doctrines: they have mechanical effects and are
expensive to change.

### Succession Law

The most consequential tradition. Determines how territory and titles pass
when an actor dies. This is a **cultural** property, not a per-actor
choice. Changing it requires enormous social currency expenditure.

| Succession Type   | Description              | Notes                      |
| ----------------- | ------------------------ | -------------------------- |
| **Primogeniture** | Eldest child inherits    | Concentrates power         |
| **Partition**     | Split among children     | Fragments over generations |
| **Elective**      | Vassals choose successor | Rewards political skill    |
| **Seniority**     | Eldest family member     | Stability but old rulers   |

Sibling cultures share succession law from their common ancestor but may
diverge over time — a cultural split might _be_ a succession law divergence.

### Additional Traditions (TBD)

To be fleshed out as needed. Categories under consideration:

- **Gender roles** — who can inherit titles, hold office, lead armies.
  Some cultures may be agnatic (male-only), cognatic (either gender), or
  enatic (female-preference).
- **Warfare conventions** — raiding culture vs. formal warfare, treatment
  of prisoners, siege behavior. Affects how wars are fought and what
  peace terms are acceptable.
- **Economic customs** — communal vs. private land tenure, attitudes
  toward trade and merchants, guild culture. Affects how the commoner
  estate operates and what contracts are normal.
- **Kinship structure** — nuclear vs. extended family, clan systems,
  how marriage alliances work, inheritance of non-title property.
- **Hospitality norms** — affects diplomacy, opinion formation from
  interactions, treatment of foreign actors.

### Changing Traditions

Changing a tradition is a civilization-level shift. An actor who wants
to change their culture's succession law from partition to primogeniture
must:

- Spend enormous amounts of legitimacy (commoners expect the old way)
- Spend prestige (other nobles see it as self-serving)
- Spend piety (clergy may have doctrinal opinions on succession)
- Risk the change causing a **cultural split** — traditionalists may
  refuse, creating a sibling culture that retains the old tradition

This mirrors how religious doctrine changes can cause schisms.

## Naming System

The existing `CultureType` system already handles procedural name
generation. Each culture type defines phonetic tables that produce
culturally appropriate names for:

- **Places** — realm names (Norðheim), provinces (Skálfjörður),
  counties (Holmbær)
- **Government forms** — culture-specific labels (Konungsríki,
  Königreich, Teyrnas)
- **Directional prefixes** — for derived names (Norður-, Nord-, Gogledd-)

### Peerage Labels

The parallel peerage system (see Estates & Actors) uses universal
mechanical ranks but culture-specific labels:

| Level    | Noble (Germanic) | Noble (Celtic) | Noble (Norse) |
| -------- | ---------------- | -------------- | ------------- |
| County   | Baron            | Tiarna         | Jarl          |
| Province | Duke             | Rí             | Hertogi       |
| Realm    | King             | Ard Rí         | Konungur      |

These labels come from the `GovernmentForms` arrays in `CultureType`.
The clergy and commoner peerage would need similar culture-specific
label sets (TBD).

### Actor Names

Actor name generation is not yet implemented. It would use the same
phonetic fragment tables (`CultureType`) to produce culturally appropriate
personal names. Considerations:

- Naming patterns (patronymic, family name, place-based)
- Name pools vs. fully procedural generation
- Dynastic naming traditions (naming children after grandparents, etc.)

## Cultural Spread and Change

### At World Generation

The existing pipeline handles initial distribution:

1. Latitude-appropriate root families are selected
2. Leaf cultures are distributed among realms
3. Cell-level culture is assigned via transport-frontier Dijkstra
4. Culture boundaries follow terrain (mountains, deserts, water)

### Runtime Evolution (TBD)

During simulation, cultures could evolve through:

- **Assimilation** — populations under foreign rule slowly adopt the
  ruler's culture (modulated by opinion, proximity, time)
- **Drift** — geographically isolated populations diverge, potentially
  splitting into a new sibling culture
- **Tradition change** — expensive, actor-driven changes to specific
  traditions (see above)
- **Cultural influence** — trade and proximity can shift cultural
  opinions and soften borders without full assimilation

### Multi-Culture Territories

Currently each cell has a single `CultureId`. For the estates system,
counties and territories may contain mixed populations:

- A conquered county might have a noble of culture A ruling commoners
  of culture B
- This creates a legitimacy deficit (see Estates & Actors — currency
  scoping)
- Over time, assimilation or cultural tolerance reduces the gap

How mixed-culture populations are represented is an open question —
majority culture per county, or fractional culture pools?

## Interaction with Other Systems

### Social Currencies

Culture is one of two axes (with religion) that scope currency pools:

- A noble's legitimacy is with commoners of the same culture
- Cross-culture currency flows are penalized (harder to earn legitimacy
  from foreign populations)
- Cultural affinity (sibling cultures) may reduce this penalty

### Opinions

- Sibling cultures have baseline positive opinion (shared heritage)
- Distant/unrelated cultures have baseline mild negative opinion
- The existing `Affinity()` function provides the raw score
- Culture opinions are the slowest-moving layer (shaped by generations
  of interaction, not individual events)

### Contracts

Cultural expectations shape contract norms:

- What tax rates are considered acceptable
- What levy obligations are normal
- Whether certain contract types exist (guild charters may be a
  Germanic tradition, clan obligations a Celtic one)
- Violating cultural norms in contracts costs extra legitimacy

### Religion

Culture and religion are **independent axes** — two realms may share a
culture but differ in religion, or share a religion but differ in culture.
Currently, religions are assigned to cultures by geographic proximity at
world generation, creating correlation but not identity.

The interaction creates four combinations:

|                       | Same Religion      | Different Religion |
| --------------------- | ------------------ | ------------------ |
| **Same Culture**      | Easiest governance | Religious tension  |
| **Different Culture** | Cultural tension   | Hardest governance |

## Key Files

| File                                          | Purpose                                              |
| --------------------------------------------- | ---------------------------------------------------- |
| `src/PopGen/CultureForest.cs`                 | Forest structure, tree queries, latitude selection   |
| `src/PopGen/CultureNode.cs`                   | Node data (Id, parent, equator proximity, leaf flag) |
| `src/PopGen/CultureType.cs`                   | 13 phonetic templates for name generation            |
| `src/PopGen/PopCulture.cs`                    | Runtime culture instance                             |
| `src/MapGen/PoliticalGenerationOps.cs`        | `AssignByTransportFrontier` — Dijkstra spread        |
| `src/EconSim.Core/Data/MapData.cs`            | Culture class, Cell/Realm/Burg CultureId fields      |
| `src/EconSim.Core/Import/WorldGenImporter.cs` | Culture conversion and cell assignment               |

## Open Questions

- How are traditions assigned at world generation? Per-root defaults with
  leaf variation?
- How is mixed-culture population represented? Majority per county, or
  fractional culture pools?
- How fast does cultural assimilation happen under foreign rule?
- What actor name generation approach? Phonetic tables, name pools, or
  hybrid?
- Should the forest deepen beyond 2 levels during runtime (cultures
  splitting into sub-cultures)?
- How do peerage labels for clergy and commoner estates map to culture
  types? Extend `CultureType` or separate system?
- Should cultural drift create new leaf nodes automatically, or is it
  always actor-driven?
