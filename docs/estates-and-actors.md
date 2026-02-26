# Estates & Actors

## Overview

The estates system introduces social stratification and political agency on top
of the existing economic simulation. Population is divided into three estates,
each subdivided into upper and lower classes. Upper classes produce **actors** —
named individuals with ambitions who can control territory and take political
actions gated by **social currencies**.

## Estates

Three classic estates, each with an upper and lower class:

| Estate        | Upper Class                           | Lower Class                  |
| ------------- | ------------------------------------- | ---------------------------- |
| **Nobility**  | Magnates, counts, dukes               | Knights, minor lords         |
| **Clergy**    | Bishops, abbots                       | Parish priests, monks        |
| **Commoners** | Burghers, merchants, master craftsmen | Peasants, unskilled laborers |

**Lower classes** are anonymous population pools (cohorts, extending the
existing population model).

**Upper classes** are also population pools, but they are the spawning ground
for actors.

### Mapping to Existing Economy

The current population model has `unskilled` and `craftsman` skill levels.
These roughly correspond to lower and upper commoners. Nobility and clergy are
new population categories that don't yet exist in the simulation.

## Actors

Actors are **named individuals** drawn from the upper classes of any estate.
They have:

- A name and identity
- Ambitions / goals
- Social currency balances
- Contractual relationships with other actors
- Optionally, territorial control

### Actor Origins

Any upper-class pool can produce actors:

- **Upper nobility** — feudal lords, the most common territorial controllers
- **Upper clergy** — bishops and abbots with political ambitions
- **Upper commoners** — wealthy burghers, guild masters, merchant princes

### Territorial Control

Actors can control territory at any level of the political hierarchy:

| Controller Estate | County                      | Province                 | Realm             |
| ----------------- | --------------------------- | ------------------------ | ----------------- |
| Nobility          | Feudal lord (the norm)      | Duke / count             | King              |
| Clergy            | Prince-bishopric, monastery | Archbishop               | Theocracy         |
| Commoners         | Free city, merchant town    | Merchant republic (rare) | Nearly unheard of |

Any estate _can_ control any level, but the power requirements scale sharply.
A commoner accumulating enough social capital to control a province would be
exceptional. This rarity is emergent from the mechanics, not hard-coded.

### Peerage

Titles are held by **actors** but have a **de jure territorial claim** —
a formal link between a title and a piece of land. The de jure holder of
County X is Baron Y, even if someone else controls it de facto.

**Changing de jure assignments** (redrawing who "rightfully" holds what)
is a major political act that costs the issuing ruler significant social
currency.

#### Parallel Peerage Hierarchies

Each estate has its own rank system, mapped 1:1 to the political hierarchy
for simplicity:

| Level    | Noble | Clergy     | Commoner        |
| -------- | ----- | ---------- | --------------- |
| County   | Baron | Prior      | Alderman        |
| Province | Duke  | Bishop     | Guildmaster (?) |
| Realm    | King  | Archbishop | — (very rare)   |

Culture determines **labels** (a king in one culture is a khan in another)
but not mechanics. The rank system is universal; the naming is cosmetic.

#### De Jure vs. De Facto

The distinction between de jure (rightful) and de facto (actual) control
is a natural source of:

- **Ambitions** — "reclaim my de jure title" is a common goal for
  dispossessed actors
- **Legitimacy problems** — a de facto controller without the de jure
  claim suffers a legitimacy deficit with local populations
- **Casus belli** — de jure claims are the most basic justification for
  war (see warfare doc)
- **Diplomatic tension** — other actors may or may not recognize de facto
  control, depending on opinions and interests

### Contracts

Contracts are the **universal mechanism** for formal relationships between
actors (and between actors and pools). Every obligation, right, and
privilege is expressed as a contract with specific, enumerated terms.
Contracts can be **negotiated, renegotiated, or broken**.

#### Contract Types

| Type                   | Between                | Example Terms                                                                      |
| ---------------------- | ---------------------- | ---------------------------------------------------------------------------------- |
| **Feudal**             | Lord ↔ vassal          | Levy of N men, tax of X%, military service Y months/year, right to local justice   |
| **Charter**            | Noble → commoner actor | Right to hold a market, form a guild, self-govern; in exchange for fees or loyalty |
| **Tithe**              | Parish ↔ bishop        | X% of production to the church; spiritual services in return                       |
| **Trade**              | Merchant ↔ merchant    | Exclusive trading rights, price agreements, shared risk                            |
| **Church appointment** | Bishop ↔ clergy actor  | Office granted in exchange for obedience, revenue sharing                          |
| **Marriage pact**      | Family ↔ family        | Alliance terms, dowry, inheritance claims                                          |
| **Protection**         | Lord → commoner pool   | Military defense in exchange for legitimacy and labor                              |

#### Feudal Relationships as Contract Bundles

A feudal relationship is not a single "is vassal of" flag — it is a
**bundle of contracts** with individually negotiated terms:

- Levy of N men when called
- Tax of X% of county production
- Military service for Y months per year
- Right to administer justice locally
- Obligation to attend court
- Right to pass title to heirs

Each term can be renegotiated independently. A lord might accept lower
taxes in exchange for larger levies. A vassal might trade military service
obligations for gold payments.

#### Breaking Contracts

Breaking a contract has **social currency costs**:

- The breaker loses legitimacy, prestige, or piety (depending on the
  contract type and who witnesses the breach)
- The wronged party gains a **grievance** — an opinion hit and potentially
  a new ambition ("take revenge," "reclaim what is owed")
- Other actors observing the breach may lower their opinion of the breaker
  (reputation for untrustworthiness)

Conversely, consistently honoring contracts builds trust — opinion
improvements and easier future negotiations.

#### Contract Scope

The realm/province/county hierarchy generally maps to a suzerain-vassal
chain, but exceptions exist (free cities answering directly to the crown,
monasteries answering to church hierarchy rather than local lords). Contracts
make these exceptions expressible — the political hierarchy is a default
pattern, not a hard constraint.

## Social Currencies

Each estate both produces and consumes social currencies, creating mutual
dependency. No estate is self-sufficient — political action requires building
social capital across estate boundaries.

### Currency Definitions

| Currency       | Produced By       | Meaning                                              |
| -------------- | ----------------- | ---------------------------------------------------- |
| **Prestige**   | Nobles            | Peer respect, military reputation, dynastic standing |
| **Legitimacy** | Commoners         | Popular support, consent of the governed             |
| **Piety**      | Clergy            | Religious sanction, moral authority                  |
| **Agency**     | Nobles            | Permission to act — charters, rights, freedoms       |
| **Solidarity** | Commoners         | Guild bonds, mutual aid, collective organization     |
| **Faith**      | Commoners         | Popular devotion, religious participation            |
| **Gold**       | (Economic system) | Patronage, endowments, funding                       |
| **Manpower**   | Commoners         | People to staff institutions, fight wars             |

### Currency Needs by Estate

| Estate        | Needs from Nobles | Needs from Commoners | Needs from Clergy |
| ------------- | ----------------- | -------------------- | ----------------- |
| **Nobles**    | Prestige          | Legitimacy           | Piety             |
| **Commoners** | Agency            | Solidarity           | Piety             |
| **Clergy**    | Gold              | Faith + Manpower     | Piety             |

### How Currencies Flow

Currencies are **earned** through actions that benefit the source group, and
**spent** to take major political actions.

**Earning examples:**

- A noble earns **legitimacy** by protecting commoners, keeping taxes low,
  providing justice
- A noble earns **piety** by endowing churches, going on pilgrimage, defending
  the faith
- A noble earns **prestige** by winning battles, hosting tournaments, making
  advantageous marriages
- A commoner actor earns **agency** by receiving charters, guild rights, or
  trade privileges from nobles
- A commoner actor earns **solidarity** through guild bonds, mutual aid,
  collective action
- Clergy earn **gold** through noble patronage and endowments
- Clergy earn **faith** and **manpower** from commoner devotion and recruitment

**Spending examples:**

- Waging war costs **prestige** + **piety** (peer support + religious
  justification)
- Seizing territory costs **legitimacy** + **prestige**
- Forming a guild or commune costs **solidarity** + **agency**
- Excommunicating a rival costs **piety**
- Raising a levy costs **legitimacy** + **manpower**

### Scope: Culture & Religion

Social currency pools are not global per-actor. They are connected through
**culture** and **religion**:

- A noble's legitimacy is with commoners who share their cultural/religious
  context
- A heterodox ruler has a piety deficit with orthodox clergy
- Conquest of foreign-culture territory creates immediate social capital
  deficits across all currencies

This means culture and religion are first-class systems that determine how
social currencies pool and flow. A noble ruling over a population of a
different culture or religion must bridge that gap — through assimilation,
tolerance, or force.

## Opinions

Actors hold opinions as a **signed float from -1.0 to 1.0**, where 0 is
neutral, positive is favorable, and negative is hostile. Opinions exist at
multiple levels:

### Opinion Layers

| Layer                 | Target              | Changes                                       | Example                                                 |
| --------------------- | ------------------- | --------------------------------------------- | ------------------------------------------------------- |
| **Actor-to-actor**    | Specific individual | Fast — driven by personal history             | "Duke Aldric betrayed our alliance" (-0.8)              |
| **Actor-to-estate**   | An entire estate    | Moderate — shaped by institutional experience | "Commoners are getting too powerful" (-0.3)             |
| **Actor-to-culture**  | A cultural group    | Slow — shaped by proximity, trade, conflict   | "The Northlanders are reliable trading partners" (+0.4) |
| **Actor-to-religion** | A religious group   | Slow — shaped by doctrine, events, wars       | "The Reformed are heretics" (-0.7)                      |

### Interaction Between Layers

Opinions stack. An actor's effective disposition toward another actor is a
blend of all applicable layers — personal opinion, opinion of their estate,
opinion of their culture, opinion of their religion. You might personally
like a specific foreign merchant (high actor-to-actor) while distrusting
merchants generally (low estate opinion) and disliking their culture (low
culture opinion).

### Effect on Social Currencies

Opinions modulate currency flows. It is easier to earn legitimacy from
commoners who have a favorable opinion of you, and harder (or impossible)
to earn piety from clergy of a religion that considers you heretical.
Negative opinions could impose a **multiplier or floor** on currency
earning rates.

### Pool Opinions

Estate pools (the non-actor populations) also hold opinions, in a simpler
form. A county's lower commoner pool has an opinion of their lord, of the
prevailing religion, of the local culture. These are **aggregate** values
for the pool, not per-individual.

Pool opinions act as levers on:

- **Passive currency accrual** — a lord passively earns legitimacy from
  content commoners; discontented commoners withhold it or generate
  negative legitimacy
- **Stability** — low pool opinion increases unrest, revolt risk, and
  resistance to taxation or levy
- **Migration** — populations move away from territories where pool opinion
  of the controller is low (connects to the existing population mobility
  future feature)

### Opinion Drivers

- **Actor-to-actor:** honoring/breaking contracts, betrayal, aid in war,
  marriage, gifts, insults, killing kin
- **Actor-to-estate:** taxation policy, granting/revoking charters, land
  seizure, protection
- **Actor-to-culture:** border conflicts, trade volume, intermarriage,
  shared enemies
- **Actor-to-religion:** doctrinal alignment, persecution, holy wars,
  tolerance, conversion

## Ambitions

Ambitions are the primary driver of actor behavior. They serve as the
actor's objective function — all decisions (raise taxes? forge alliance?
build a church?) are evaluated against whether they advance the current
ambitions.

### Lifecycle

1. **Acquired** — on coming of age, or triggered by events during life
2. **Pursued** — actor spends currencies and takes actions toward the goal
3. **Achieved** or **abandoned** — success, changed circumstances, or
   superseded by a more pressing ambition
4. **Replaced** — a new ambition takes hold (or an existing one gains
   priority)

Actors can hold **multiple ambitions simultaneously**. At any given moment,
an actor prioritizes among their active ambitions based on urgency,
opportunity, and feasibility.

### Decomposition into Sub-Goals

Ambitions decompose into a **dependency graph** of sub-goals. The actor's
moment-to-moment behavior is driven by whichever leaf-level sub-goal they
are currently pursuing.

Example — "Conquer Province X":

```
Conquer Province X
├── Stockpile prestige (win a tournament, settle a dispute)
├── Secure piety from the bishop
│   └── Endow a monastery (costs gold)
├── Raise levy
│   └── Accumulate legitimacy (lower taxes, protect borders)
└── Win the war
    ├── Levy is raised (blocked until above completes)
    └── March and engage
```

Sub-goals can **block on each other** — you can't raise a levy without
legitimacy. The plan is a dependency graph, not a linear sequence, and it
can be disrupted at any point by events that invalidate a sub-goal or open
a shortcut.

### Inter-Actor Coupling

Ambition trees explain **why actors interact**. An actor doesn't accumulate
piety in a vacuum — they negotiate with a clergy actor who has their own
ambition tree. The clergy actor might demand gold or a land grant in return.
Two ambition trees interlock through contracts and currency exchange.

### Ambition Types by Estate

Ambitions cluster by estate, but cross-estate ambitions are possible and
interesting:

| Estate    | Typical Ambitions                                                          |
| --------- | -------------------------------------------------------------------------- |
| Nobility  | Conquer territory, defend the realm, found a dynasty, accumulate wealth    |
| Clergy    | Spread the faith, build institutions, rise in hierarchy, reform corruption |
| Commoners | Accumulate wealth, secure a charter, establish a guild, gain noble title   |

Cross-estate examples: a noble who wants to reform the church, a merchant
who wants to be knighted, a bishop who wants temporal power.

### Non-Ambitions

A great many actors — probably the majority — will have **passive goals**:
lead a comfortable life, maintain the family estate, serve the community,
provide for one's children. These are not ambitious in the political sense
but they still drive behavior (invest in the local economy, avoid conflict,
pay obligations, resist threats to stability).

Passive actors are important for two reasons:

1. **Realism** — most people, even powerful ones, aren't schemers. A world
   where every actor is aggressively pursuing expansion would be in constant
   upheaval.
2. **Stability** — passive actors provide the stable backdrop against which
   ambitious actors operate. They react to events rather than initiating
   them, and they can be pushed into active ambition by circumstance (a
   comfortable lord becomes vengeful when his lands are seized).

### Events and Ambition Change

Events reshape ambitions dynamically:

- A noble pursuing "expand trade" shifts to "take revenge" after their
  father is killed
- A bishop pursuing "build a cathedral" pivots to "become archbishop" when
  the seat opens up
- A merchant who achieves great wealth develops the ambition to "secure a
  noble title"
- A failed military campaign might cause an actor to abandon "conquer
  territory" and adopt "consolidate holdings"

This keeps actors dynamic rather than locked into their initial ambitions.

## Actor Lifecycle

### Coming of Age

An actor spawns from an upper estate pool with:

- Initial ambitions (influenced by estate, family, circumstances — could be
  passive "lead a comfortable life" or active "reclaim father's lands")
- Opinions **inherited from parents** but with variance — children are
  heavily influenced by their parents' views but sometimes rebel, seeing
  things very differently. A pious father may produce a cynical son; a
  warmongering lord's heir may seek peace.
- Social currency balances starting near zero (must build their own capital)
- Inherited claims (if applicable — children of territorial controllers)

### Marriage

Marriage is a **political tool** as much as a personal event. It creates:

- **Alliances** — formal or informal ties between families/actors
- **Merged claims** — children inherit claims from both parents
- **Cultural bridges** — marrying into a foreign culture improves
  cross-culture opinions over time
- **Social climbing** — a burgher marrying into minor nobility, a minor
  noble marrying into a ducal family

Marriage availability is estate-specific. Clergy may be celibate depending
on religious doctrine (a cultural/religious rule, not a hard-coded one).

### Offspring

Children exist as **potential future actors**. They:

- Inherit a blend of parent opinions with variance (sometimes rebellion)
- Inherit claims to parent-held territory
- Can be placed strategically by their parents — second son sent to the
  clergy, daughter married off for alliance, youngest apprenticed to a
  guild master
- Multiple children create **succession tension**

### Inheritance & Succession

When an actor dies, their territory, wealth, contracts, and some social
capital must pass on. **Succession law is a property of culture**, not a
per-actor choice. Changing succession law is a civilization-level shift
requiring enormous expenditure of legitimacy, piety, and prestige — you
are fighting the expectations of every estate simultaneously.

| Succession Type   | Description               | Typical Cultures                 |
| ----------------- | ------------------------- | -------------------------------- |
| **Primogeniture** | Eldest child inherits     | Settled feudal                   |
| **Partition**     | Split among children      | Tribal, early feudal             |
| **Elective**      | Vassals choose successor  | Polish-style, merchant republics |
| **Theocratic**    | Church appoints successor | Prince-bishoprics, theocracies   |
| **Seniority**     | Eldest family member      | Some Celtic, Islamic             |

Different cultures practicing different succession models creates dynamics
at cultural boundaries — a partition culture fractures over generations
while a primogeniture neighbor consolidates.

Succession is where ambition trees collide most violently. Multiple heirs
with claims, ambitious vassals seeing opportunity in a weak successor,
foreign powers pressing their candidate.

**What transfers on death:**

- Territory control (per succession law)
- Wealth and material assets
- Contracts (vassal obligations transfer to successor)
- Some social capital (inherited prestige, inherited legitimacy — at a
  discount, since the new ruler hasn't earned it personally)

**What doesn't transfer:**

- Personal opinions of the deceased (others form new opinions of the heir)
- Active ambitions (heir has their own)
- Personal relationships (contracts persist but trust resets)

### Death

Actors die through:

- **Natural causes** — age, disease
- **Violence** — war, assassination, execution
- **Voluntary exit** — abdication, taking religious vows

Death triggers succession and ripples through every contract the actor held.
A king's death can destabilize an entire realm if the heir is weak or
contested.

## Culture

See **[Culture](culture.md)** for the full design document.

**Summary:** Cultures are a forest with sibling affinity (inverse of
religion's sibling hostility). Already partially implemented — 5 root
families, ~15 leaves, phonetic name generation, transport-frontier
spread. Needs expansion: traditions (succession law, gender roles, etc.)
as the behavioral dimension, peerage labels per culture, actor name
generation, and runtime cultural evolution (assimilation, drift, splits).

## Religion

See **[Religion](religion.md)** for the full design document.

**Summary:** Religions are a forest structure (like cultures) but with
inverted sibling dynamics — schisms produce hostility, not affinity.
Each religion has doctrines (celibacy, worldview, holy war, usury,
monasticism, succession preference) that mechanically differentiate
gameplay. Non-celibate religions produce cleric-nobles who blur estate
boundaries. Religions are procedurally generated at world creation and
evolve through schisms, conversion, and reform during simulation.

## Implications

### Systems That Need to Exist

- **Culture system** — assigns cultures to populations and actors; currently
  nascent from MapGen (`AssignByTransportFrontier`)
- **Religion system** — assigns religions; does not yet exist
- **Actor lifecycle** — birth from upper pools, aging, ambitions, death,
  succession
- **Contract system** — formal relationships between actors
- **Social currency ledger** — tracking per-actor balances scoped by
  culture/religion

### Connections to Existing Economy

- **Ownership** — currently no one "owns" facilities or land. Estates
  introduce ownership: upper commoners own workshops, nobility collects rents,
  clergy holds church lands.
- **Taxation** — the fiscal system exists but taxes flow to abstract
  realm/province treasuries. With actors, taxes flow to the controlling actor.
- **Consumption** — nobility and clergy are new consumer classes, particularly
  of luxury/comfort goods. This creates demand that doesn't currently exist.
- **Facility construction** — currently every county has every facility. With
  actor ownership, facilities are built by actors who invest capital, creating
  geographic specialization.

## Proposed Roadmap

### Dependency Graph

```
Layer 0: Foundations (no dependencies)
├── Culture enhancement (traditions framework on existing forest)
└── Religion (new: forest, doctrines)

Layer 1: Population (depends on Layer 0)
└── Estate populations (split existing pop into 6 pools,
    bootstrap percentages from culture/religion)

Layer 2: Social Fabric (depends on Layers 0-1)
├── Opinions (actor + pool opinion system, scoped by culture/religion)
└── Social currencies (ledger, earning/spending, scoped by culture/religion)

Layer 3: Actors & Peerage (depends on Layers 0-2)
├── Peerage (title ranks, de jure territorial claims, parallel hierarchies)
├── Actor spawning (from upper estate pools)
├── Actor identity (name, estate, culture, religion, title)
└── Territorial control (assign initial de jure + de facto controllers)

Layer 4: Relationships (depends on Layer 3)
├── Contracts (feudal bundles, charters, tithes, trade agreements)
└── Economy integration (ownership, taxation flow to actors, facility investment)

Layer 5: Agency (depends on Layer 4)
├── Ambitions (goal trees, sub-goal decomposition, passive vs. active)
└── Decision-making (evaluation frequency, event triggers)

Layer 6: Lifecycle (depends on Layer 5)
├── Marriage (alliance formation, cultural bridging)
├── Offspring (opinion inheritance/rebellion, claim inheritance, placement)
├── Death & succession (succession law per culture, transfer rules)
└── Events (ambition change, opinion shifts, external shocks)

Layer 7: Conflict (depends on Layers 4-6)
└── Warfare (separate design doc — levies, combat, peace terms, new contracts)
```

### Notes

- **Layers 0-1 are low-risk.** They extend existing systems (culture
  forest, population pools) without breaking anything. Good starting points.
- **Layer 3 is the biggest single step.** Actors are the new primitive that
  everything else hangs off. Getting the data model right here matters most.
- **Layer 4 is where the economy gets rewired.** Ownership and taxation
  flowing to actors rather than abstract treasuries is a significant change
  to existing systems.
- **Layers 5-6 can be built incrementally.** Actors can exist with simple
  passive behavior before the full ambition system is in place. Marriage
  and succession can be added without warfare.
- **Each layer should be playable/observable.** After Layer 1, you can see
  estate population breakdown. After Layer 3, the map shows who controls
  what. After Layer 4, the economy reflects ownership. Avoid building
  multiple layers before anything is visible.

## Related Design Documents

- **[Culture](culture.md)** — culture forest, traditions, naming, spread
- **[Religion](religion.md)** — religion forest, doctrines, clergy, schisms
- **[Warfare](warfare.md)** (TBD) — levies, combat, sieges, occupation,
  peace terms. Wars are initiated through ambitions, funded by social
  currencies, fought with levied manpower from contracts, and resolved by
  creating new contracts.

## Scale & Performance

### Expected Actor Count

A map with ~10 realms, ~100 provinces, ~1000 counties produces roughly:

- ~1000+ noble actors (every territory has a controller, plus unlanded kin)
- Clergy and commoner actors on top of that
- Total likely in the low thousands

This is manageable for data (currencies, opinions, contracts are floats and
lists). The AI/decision-making side is bounded by two factors:

### Decision Frequency

Actor decision-making frequency is **proportional to significance in
society**. A king re-evaluates ambitions more often than an unlanded
younger son. An unlanded noble might exist as a purely passive actor,
consuming no decision budget at all until events force them to act.

Rough model: actors have a "thoughts per month" budget based on their
current role and ambition state. A realm controller with active ambitions
might evaluate weekly. A passive county lord might evaluate quarterly. An
unlanded noble with no ambitions evaluates only on event triggers.

This means the number of actors "thinking hard" at any given time is
perhaps 50-100 out of thousands. And even those don't need daily
re-evaluation — an actor stockpiling prestige checks if anything has
changed that invalidates their plan, not whether to change the plan.

### Opinion Sparsity

The opinion matrix (actors x actors) is potentially large (1000 actors =
1M entries) but in practice is **sparse**. Actors only hold opinions of
actors they've interacted with. A county lord in the south has no opinion
of a county lord in the far north. Default is 0 (neutral). Sparse storage
keeps this manageable.

### Estate Population Bootstrap

At world generation, estate populations are initialized as **fixed
percentages** of each county's population, potentially modified by
culture/religion factors (TBD). The existing commoner pools (unskilled /
craftsman) become lower/upper commoners; nobility and clergy pools are
new additions seeded from these percentages.

## Open Questions

- How does actor spawning work? Probability based on upper-class pool size?
  Events? Family lineage?
- How do contracts change over time? Renegotiation, breach, enforcement?
- The player is an actor within this system — but what estate? Can they
  choose? Are they always nobility?
- How do currencies decay? Do they persist indefinitely or depreciate?
- What is the event system? How are events generated and what triggers them?
- How does ambition selection work on coming of age? Weighted by estate,
  family, personality, circumstances?
- What limits the number of active ambitions? Cognitive capacity? Estate rank?
- How does the "discount" on inherited social capital work? Flat percentage?
  Varies by currency type?
- What determines opinion rebellion vs. inheritance in children? Random
  variance? Events during childhood? Birth order?
- How are marriage candidates selected? Geographic proximity? Political
  value? Estate compatibility?
- At what age do actors come of age, marry, have children, grow old?
  Fixed or culture-dependent?
- What do actors actually do on a tick? Ambition tree evaluation frequency,
  decision budget, event-driven vs. periodic re-evaluation?
- How are social currency pools scoped mechanically? One legitimacy score
  per culture-religion pair the actor rules over? A single blended score?
  Sparse matrix?
- How do contracts with pools work? Pools don't negotiate — does the
  controller set terms unilaterally with pool opinion as the feedback
  mechanism? Or do upper-class actors from the pool negotiate on its behalf?
