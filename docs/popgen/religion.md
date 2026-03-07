# Religion

See also: [Estates & Actors](estates-and-actors.md)

## Overview

Religions are procedurally generated belief systems that shape the clergy
estate, scope social currency flows, drive opinion baselines, and impose
mechanical rules (doctrines) on actors and populations. Like cultures,
religions are organized as a forest — but with inverted sibling dynamics.

## Structure

### Religion Forest

Religions form a **forest** (multiple root religions), with branches
representing schisms, reform movements, and offshoots. Each node in the
tree is a distinct religion with its own identity, doctrines, and
followers.

```
Faith of the Sun
├── Orthodox Solarians (original)
│   └── Contemplative Order (monastic offshoot)
├── Reformed Solarians (schism)
└── Solar Unitarians (radical offshoot)

Ancestor Worship
├── River Spirits tradition
└── Mountain Spirits tradition

The Old Ways (animist root)
└── Forest Covenant (organized offshoot)
```

**Root religions** represent independent religious traditions with no
shared origin. **Branches** descend from a parent through schism or reform.

### Sibling Dynamics

The critical distinction from the culture forest: **sibling religions are
hostile, not affine.** The closer the doctrinal relationship, the more
bitter the disagreement. Heresy is worse than foreignness.

| Tree Distance              | Opinion Baseline                          |
| -------------------------- | ----------------------------------------- |
| Same religion              | Neutral to positive (in-group)            |
| Sibling (shared parent)    | **Strongly negative** (heresy, schism)    |
| Cousin (shared root)       | Negative (wrong branch, but recognizable) |
| Unrelated (different root) | Mildly negative to neutral (foreign)      |

This inversion means:

- Orthodox and Reformed Solarians fight hardest — they claim the same
  truth and accuse each other of corrupting it
- Both regard the Ancestor Worshippers with less intensity — they're
  simply wrong, not dangerously close to right
- Two completely unrelated religions may coexist more easily than two
  branches of the same faith

### Religion Properties

Each religion has:

- **Name** — procedurally generated or from a name pool
- **Parent** — null for root religions, points to parent for branches
- **Doctrines** — mechanical rules (see below)
- **Clergy hierarchy** — structure of the religious institution
- **Holy sites** — territories of religious significance (TBD)

## Doctrines

Doctrines are the **mechanical substance** of a religion — the rules that
affect gameplay. Two religions with identical doctrines but different names
play similarly; two branches of the same root with divergent doctrines
(the cause of their schism) play very differently.

Doctrines will be built up incrementally. The initial framework:

### Worldview

How the religion relates to other faiths. This is the most fundamental
doctrine — it determines baseline opinion modifiers and whether peaceful
coexistence is even possible.

| Worldview       | Effect                                                     |
| --------------- | ---------------------------------------------------------- |
| **Exclusivist** | Only true faith. Strong negative opinion of all others.    |
|                 | Heresy (sibling) is the worst offense. Holy war justified. |
| **Pluralist**   | Truth has many paths. Mild negative opinion of others.     |
|                 | Coexistence possible. Holy war rare/unjustified.           |
| **Syncretist**  | Absorbs elements of other faiths. Neutral to positive      |
|                 | toward related religions. Resists schism. May be seen as   |
|                 | heretical by exclusivist parent.                           |

Worldview affects:

- Base opinion modifiers toward other religions
- Whether holy war is doctrinally available as a casus belli
- How easily populations convert (syncretist populations convert more
  readily; exclusivist populations resist)
- Likelihood of schism (exclusivist religions schism more — rigid
  doctrine creates more grounds for disagreement)

### Clergy Celibacy

Whether clergy actors can marry and produce heirs.

| Setting          | Effect                                                  |
| ---------------- | ------------------------------------------------------- |
| **Celibate**     | Clergy cannot marry. No dynastic entanglement. Clergy   |
|                  | estate is self-contained. Succession is by appointment. |
| **Non-celibate** | Clergy can marry and have children. Produces            |
|                  | cleric-nobles who straddle both estates. Dynastic       |
|                  | competition with nobility. Messier, more political.     |

This single doctrine has outsized impact on the political landscape (see
Cleric-Nobles below).

### Monasticism

Whether monastic orders exist within the religion.

| Setting     | Effect                                                 |
| ----------- | ------------------------------------------------------ |
| **Present** | Monasteries as institutions. Path for second sons.     |
|             | Source of clergy actors. Monastic lands and wealth.    |
|             | Contemplative offshoots more likely.                   |
| **Absent**  | No monastic tradition. Clergy are parish/temple-based. |
|             | Fewer clergy actors. Less institutional church wealth. |

### Holy War

Whether the religion doctrinally justifies war for religious reasons.

| Setting       | Effect                                                  |
| ------------- | ------------------------------------------------------- |
| **Justified** | Religious difference is a valid casus belli. Clergy can |
|               | call for holy wars. Piety earned through conquest of    |
|               | infidel/heretic territory.                              |
| **Defensive** | War justified only to defend the faith or reclaim holy  |
|               | sites. Cannot initiate religious conquest.              |
| **Forbidden** | Religion opposes war. Clergy lose piety for supporting  |
|               | warfare. Pacifist tradition.                            |

### Usury

Stance on lending at interest.

| Setting        | Effect                                                   |
| -------------- | -------------------------------------------------------- |
| **Prohibited** | Lending at interest is sinful. Limits commoner financial |
|                | activity. Clergy actors cannot engage in moneylending.   |
|                | Creates niche for other religions' merchants.            |
| **Permitted**  | No restriction. Full economic activity for all estates.  |

### Succession Preference

Whether the religion favors or imposes specific succession models on
church-held territory.

| Setting           | Effect                                             |
| ----------------- | -------------------------------------------------- |
| **Theocratic**    | Church lands pass by appointment, not inheritance. |
|                   | Senior clergy choose the next holder. No dynastic  |
|                   | claims on church territory.                        |
| **Dynastic**      | Church lands can be inherited like secular titles. |
|                   | Only meaningful for non-celibate religions.        |
| **No preference** | Defers to cultural succession law.                 |

### Future Doctrines (TBD)

- **Dietary laws** — affects consumption patterns and trade
- **Pilgrimage** — periodic travel to holy sites, economic/opinion effects
- **Charity/alms** — mandatory wealth redistribution
- **Iconoclasm** — stance on religious art and architecture
- **Proselytism** — active conversion vs. passive faith
- **Ancestor veneration** — interaction with family/lineage mechanics

## Clergy Structure

The clergy estate's internal organization is **shaped by doctrine**. Not
all religions produce the same kind of clergy.

### Hierarchy

Clergy peerage maps to the political hierarchy (see Estates & Actors):

| Level    | Title       | Role                                        |
| -------- | ----------- | ------------------------------------------- |
| County   | Prior/Abbot | Local religious authority, parish/monastery |
| Province | Bishop      | Regional authority, diocese                 |
| Realm    | Archbishop  | National/major authority                    |

Religions without strong hierarchy (e.g., animist traditions) may have
flatter structures — a local shaman with no formal superior. The peerage
labels are cultural (see Estates & Actors) but the mechanical ranks are
universal.

### Clergy Actor Production

Who becomes a clergy actor depends on doctrine:

- **Monastic religions** — monasteries are a pipeline for clergy actors.
  Second sons, pious nobles, devout commoners enter orders and may rise
  through the hierarchy.
- **Non-monastic religions** — clergy come from devout upper-class pools
  directly. Fewer clergy actors overall.
- **Celibate religions** — clergy actors have no heirs. Succession is
  always by appointment. Creates a clean separation from secular dynasties.
- **Non-celibate religions** — clergy actors have families. Their children
  may enter either the clergy or secular life. Creates dynastic
  entanglement with nobility.

### Cleric-Nobles

Non-celibate religions produce actors who straddle the clergy and nobility
estates. A bishop who inherits a county. A warrior-monk dynasty. An abbot
whose children compete with the local lord's heirs.

A cleric-noble:

- Earns prestige _and_ piety
- Holds feudal contracts _and_ church appointments
- Has children who might end up in either estate
- May face conflicting obligations (church hierarchy vs. feudal lord)

Celibate religions prevent this blurring entirely — the clergy is a
parallel hierarchy with no dynastic crossover.

## Schisms

### What Causes Schisms

Schisms are **doctrinal splits** that create a new sibling religion. They
can be triggered by:

- **Doctrinal disputes** — a clergy actor or faction pushes for a change
  in doctrine (e.g., abolish celibacy, adopt pluralism). If the
  establishment rejects the change, the reformers split.
- **Political fracture** — when a realm splits, the new realm's clergy
  may declare independence from the old religious hierarchy, creating a
  de facto schism.
- **Reform movements** — a charismatic clergy actor gathers followers
  around a new interpretation. If the movement grows large enough, it
  becomes a distinct religion.
- **Geographic drift** — isolated populations (islands, distant provinces)
  gradually develop distinct practices that eventually formalize.

### Schism Mechanics

When a schism occurs:

- A new religion node is added as a child of the parent
- The new religion inherits most doctrines from the parent but **diverges
  on the disputed doctrine(s)** — the divergence is the reason for the
  schism
- Populations and clergy actors choose sides (or are assigned based on
  geography, loyalty, opinion)
- **Immediate mutual hostility** — the new sibling opinion baseline is
  strongly negative
- Existing contracts between actors on opposite sides of the schism come
  under strain (opinion hits, potential breach)

### Schism Likelihood

Some doctrines and conditions make schisms more likely:

- **Exclusivist** worldview — rigid doctrine creates more grounds for
  disagreement
- **Large geographic spread** — harder to maintain doctrinal unity across
  distant territories
- **Powerful clergy actors with low opinion of hierarchy** — discontented
  bishops are natural schism leaders
- **Political pressure** — a king wanting to break free of a foreign
  archbishop's authority may sponsor a schism

**Syncretist** and **pluralist** religions schism less — their tolerance
for doctrinal variation absorbs disagreements that would fracture an
exclusivist faith.

## Conversion

### Population Conversion

Populations can convert from one religion to another over time. Conversion
is driven by:

- **Ruler's religion** — populations slowly drift toward their controller's
  faith (especially if the controller actively promotes it)
- **Clergy presence** — active clergy in the territory accelerate
  conversion
- **Doctrine** — syncretist populations convert more readily; exclusivist
  populations resist
- **Opinion** — populations with low opinion of the incoming religion
  resist conversion; populations with low opinion of their current clergy
  may be open to alternatives

### Actor Conversion

Individual actors can convert for:

- **Political advantage** — adopting the local religion to gain legitimacy
- **Marriage** — converting to a spouse's religion
- **Genuine belief** — triggered by events, clergy influence, or low faith
  in current religion
- **Coercion** — forced conversion by a conqueror (generates strong
  negative opinion)

Actor conversion realigns their social currency pools — piety earned with
the old religion is lost (or severely discounted) with the new one.

## Procedural Generation

### World Generation

At map generation, religions are created and distributed:

1. **Root religions** are generated based on the number of landmasses and
   cultural diversity — each major cultural group tends to have a distinct
   religious tradition
2. **Doctrines** are assigned procedurally with some coherence rules (e.g.,
   exclusivist religions are more likely to have holy war justified)
3. **Distribution** follows cultural boundaries initially but with
   deliberate mismatches — a single culture might be split between two
   religions, or two cultures might share one
4. **Initial schisms** may be generated to create pre-existing religious
   diversity within a tradition

### Runtime Evolution

During simulation, religions evolve through:

- Schisms (new branches)
- Conversion (territory changes religion)
- Doctrinal reform (rare — an existing religion changes a doctrine,
  potentially triggering a schism if contested)
- Extinction (a religion with no remaining followers is dead, but its
  node remains in the tree for historical reference)

## Interaction with Other Systems

### Social Currencies

- **Piety** flows within a religious group — actors of different religions
  cannot easily exchange piety
- Clergy earn **faith** from same-religion commoner pools
- Cross-religion piety is near-impossible for exclusivist faiths,
  discounted for pluralist, and partially available for syncretist

### Opinions

- Sibling religions impose baseline **negative** opinion (heresy)
- Unrelated religions start closer to **neutral** (foreign)
- Worldview doctrine modulates intensity (exclusivist = stronger negatives)
- Pool opinions of clergy are affected by doctrine match (populations of
  religion A under a bishop of religion B have low opinion)

### Economy

- **Usury doctrine** affects commoner economic activity and market
  development
- **Monasticism** creates institutional wealth (monastic lands, production)
- **Tithes** — monthly collection from county surplus, flowing through
  the religious hierarchy (see Tithe System below)
- **Worship goods** — candles (from honey via candlemakers) satisfy the
  Worship comfort category for population satisfaction
- **Pilgrimage** (TBD) creates trade routes and economic activity
- **Charity/alms** (TBD) creates wealth redistribution

### Contracts

- **Tithe contracts** — populations owe a fraction of production to the
  church (currently automatic via adherence weighting)
- **Church appointments** — bishops appoint local clergy, sometimes
  contested by secular rulers (investiture disputes)
- **Religious obligations** — actors may owe service or gold to their
  religious hierarchy

## Tithe System (Implemented)

Monthly tick system that models religious taxation and redistribution.

### Collection

Each parish computes the surplus production value (production minus
consumption, valued at market prices) across its counties over the past
month. The tithe is **7% of surplus value × the county's adherence** to
the parish's faith, deducted from the county treasury.

A county split between two faiths pays proportionally to each — a county
that is 60% Faith A and 40% Faith B pays 60% of its tithe to Faith A's
parish and 40% to Faith B's.

### Hierarchy Flow

Collected tithes flow upward through the hierarchy:

| Level        | Keeps | Passes Up |
| ------------ | ----- | --------- |
| Parish       | 60%   | 40% → Diocese |
| Diocese      | 70%   | 30% → Archdiocese |
| Archdiocese  | 100%  | — (top of hierarchy) |

### Church Wages

Parishes spend **50% of their treasury** back into the local economy as
wages, distributed to their counties proportional to adherence. This
recirculates crowns and stimulates counties with strong religious
presence.

### Worship Economy

Population worship satisfaction is driven by **candles**, a finished
comfort good in the Worship category:

- **Candles** — 0.02 kg/day per capita, base price 0.15 Cr
- **CandleMaker** facility — converts 2 honey → 4 candles (5% max labor)
- Honey competes between mead production and candle production

### Tracking

- `CountyEconomy.TithePaid` — monthly tithe per county (reset each month)
- `Parish.Treasury`, `Diocese.Treasury`, `Archdiocese.Treasury` — accumulated crowns
- EconDebugBridge exports tithe data; analyzer prints per-faith breakdown

## Temple Construction (TODO)

Faith hierarchies accumulate treasury from tithes but currently have no
spending mechanism beyond parish wages. A future system will allow
dioceses and archdioceses to commission **temple construction** in their
territory, spending accumulated treasury to:

- Build temples/cathedrals as physical structures in counties
- Boost worship satisfaction beyond what candles alone provide
- Increase local adherence (impressive buildings attract converts)
- Provide prestige to the commissioning clergy actor

Design details TBD — the key constraint is that temple spending gives
the accumulated church treasury a meaningful purpose and creates a
feedback loop: tithes → treasury → temples → more adherence → more
tithes.

## Open Questions

- How many root religions at world generation? Proportional to cultural
  diversity? Fixed range?
- How are doctrines assigned — fully random, or correlated with geography
  and culture?
- What is the clergy hierarchy for religions without strong organization
  (animist, shamanistic)?
- How fast does conversion happen? Decades? Generations?
- Can doctrines change without causing a schism? Under what conditions?
- What are holy sites and how do they work mechanically?
- How does the religion tree interact with procedural name generation?
- What is the relationship between religion and burial/death customs
  (affects actor lifecycle)?
