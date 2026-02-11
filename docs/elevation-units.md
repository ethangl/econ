# Elevation Units

## Canonical Rule

- Canonical cell elevation is `Cell.SeaRelativeElevation`.
- Canonical unit is signed height relative to sea level.
  - `0` means sea level.
  - `> 0` means above sea level.
  - `< 0` means below sea level.

## Compatibility Field

- Runtime logic reads canonical `SeaRelativeElevation` and validates `HasSeaRelativeElevation = true`.

## Conversion API

Always use `EconSim.Core.Data.Elevation` helpers:

- `ResolveSeaLevel(MapInfo)`
- `GetSeaRelativeHeight(Cell, MapInfo)`
- `GetAbsoluteHeight(Cell, MapInfo)`
- `GetMetersAboveSeaLevel(Cell, MapInfo)`
- `GetSignedMeters(Cell, MapInfo)`
- `GetNormalizedSignedHeight(Cell, MapInfo)`
- `GetNormalizedDepth01(Cell, MapInfo)`
- `NormalizeAbsolute01(float)`
- `AbsoluteToMetersAboveSeaLevel(float, MapInfo)`
- `MetersAboveSeaLevelToAbsolute(float, MapInfo)`
- `SeaRelativeToSignedMeters(float, MapInfo)`
- `SignedMetersToSeaRelative(float, MapInfo)`

Notes:

- `MapInfo.World` metadata is required at runtime.
- Sea level is sourced from `MapInfo.World.SeaLevelHeight`.

## Practical Guidance

- When writing map cells from mapgen output, set:
  - `SeaRelativeElevation`
  - `HasSeaRelativeElevation = true`
- For gameplay thresholds, prefer meter-space comparisons:
  - convert threshold anchors once with `AbsoluteToMetersAboveSeaLevel(legacyAbsoluteThreshold, info)`
  - compare against per-cell meters (`AbsoluteToMetersAboveSeaLevel(GetAbsoluteHeight(cell, info), info)`)
- Keep morphology logic unchanged unless explicitly tuning map generation behavior.
- Current consumers using this pattern:
  - `EconSim.Core.Economy.EconomyInitializer`
  - `EconSim.Core.Transport.TransportGraph`

## World Metadata

- `MapGenResult.World` is emitted by mapgen as explicit world-scale output.
- `MapInfo.World` carries that metadata into EconSim runtime.
- Runtime world-scale normalization utilities live in `EconSim.Core.Data.WorldScale` (distance normalization, map span cost).
- Canonical fields include:
  - `CellSizeKm`
  - `MapWidthKm`, `MapHeightKm`, `MapAreaKm2`
  - `LatitudeSouth`, `LatitudeNorth`
  - `MaxElevationMeters`, `MaxSeaDepthMeters`
  - `MinHeight`, `SeaLevelHeight`, `MaxHeight`

## Enforcement Gates

- `MapGenAdapter.Convert` now enforces canonical elevation invariants:
  - all generated cells must set `HasSeaRelativeElevation = true`
  - absolute elevation must remain in `0..100`
- `MapData.AssertElevationInvariants()` is executed after map conversion.
- `Elevation` helpers throw for non-finite or out-of-range absolute values.
- EditMode regressions include:
  - distribution baseline gates (`landRatio`, `waterRatio`, `riverCellRatio`, elevation `p10/p50/p90`)
  - source-usage guard that fails if production code introduces legacy `cell.Height` field reads.
