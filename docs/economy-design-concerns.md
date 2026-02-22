# Economy Design Concerns

Issues identified during documentation of the economy algorithms. Organized
by severity and type.

## Logical / Structural

### No transport costs in trade

The static transport backbone generates roads for visual rendering, but trade
is pure pool-matching within each scope tier. A county on one coast trades with
a county on the opposite coast at the same cost as its neighbor. Distance
doesn't matter for goods movement, only for road visuals.

### Single market county collects all market fees

The 2% market fee on all trade at every scope level flows to one county (the
first realm's capital). Cross-realm trade between realms B and C still pays
market fees to realm A's capital. This is a significant wealth concentration
toward that one county.

### Money supply has no drain

Minting creates crowns. Admin wages recirculate to counties. Tolls and tariffs
recirculate to provinces/realms which spend them on admin wages which go back
to counties. There's no money destruction (no goods cost crowns to produce, no
decay of money). If precious metal production exceeds what's needed, county
treasuries inflate over time. But prices aren't driven by money supply —
they're driven by physical supply/demand — so excess money accumulates without
visible effect except making trade less treasury-constrained. Historically this
mismatch (quantity theory of money) was a big deal.

## Realism

### No seasonality

Production is constant year-round. Medieval agriculture was intensely seasonal
— surplus at harvest, scarcity in late winter. This flattens out what was
historically the most important economic rhythm.

### No weather, disease, or war shocks

The economy is purely endogenous — nothing external can disrupt it. Historically
these were the dominant drivers of economic cycles.

## Significance TBD

### Growth rates are high for pre-industrial

At full satisfaction: births ~0.45%/month, deaths ~0.25%/month, net ~2.4%/year.
Medieval Europe was more like 0.1-0.5%/year. The equilibrium (zero growth) is
around BasicSatisfaction ≈ 0.56, which is reasonable as a game mechanic but the
max growth is ahistorically fast.

## Minor / Incidental Lack of Realism

### Staple pool treats goods as perfectly substitutable by weight

1 kg of cheese (~3500 kcal) fills the same food budget as 1 kg of stockfish
(~2500 kcal) or 1 kg of wheat (~3400 kcal). The caloric and nutritional
differences are significant.

### Every county has every facility

A desert with zero timber hosts a Carpenter, Charcoal Burner, and Brewery (if
no barley). They sit idle at zero throughput — harmless computationally, but
means there's no concept of facility construction, investment, or geographic
specialization in manufacturing.

## Minor / Implementation Detail

### Workforce fraction uses stale data

Extraction's `workforce_fraction = (pop - facilityWorkers) / pop` reads
`FacilityWorkers` from the previous tick (it's computed after extraction in
the same loop). On tick 1, facilityWorkers is 0, so 100% of the population
does extraction.

### Facility processing order matters

Pass 2 processes facilities by enum order. Smelter (enum 2) runs before
CharcoalBurner (enum 4), so charcoal's `FacilityInputNeed` already includes
smelter demand when the charcoal burner reads it. This is a deliberate
chain-ordering dependency.
