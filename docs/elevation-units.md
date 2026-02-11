# Elevation Units

## Canonical Rule

- Canonical cell elevation is `Cell.SeaRelativeElevation`.
- Canonical unit is signed height relative to sea level.
  - `0` means sea level.
  - `> 0` means above sea level.
  - `< 0` means below sea level.

## Compatibility Field

- `Cell.Height` is retained for compatibility with older call sites.
- New logic should not read `Cell.Height` directly for gameplay or rendering decisions.

## Conversion API

Always use `EconSim.Core.Data.Elevation` helpers:

- `ResolveSeaLevel(MapInfo)`
- `GetSeaRelativeHeight(Cell, MapInfo)`
- `GetAbsoluteHeight(Cell, MapInfo)`
- `GetMetersASL(Cell, MapInfo)`
- `GetSignedMeters(Cell, MapInfo)`
- `NormalizeAbsolute01(float)`
- `AbsoluteToMetersASL(float, MapInfo)`
- `MetersASLToAbsolute(float, MapInfo)`
- `SeaRelativeToSignedMeters(float, MapInfo)`
- `SignedMetersToSeaRelative(float, MapInfo)`

Notes:
- `ResolveSeaLevel(MapInfo)` prefers `MapInfo.World.SeaLevelHeight` when present and valid, then falls back to legacy `MapInfo.SeaLevel`, then default `20`.

## Practical Guidance

- When writing map cells from mapgen output, set:
  - `SeaRelativeElevation`
  - `HasSeaRelativeElevation = true`
- For gameplay thresholds, prefer meter-space comparisons:
  - convert threshold anchors once with `AbsoluteToMetersASL(legacyAbsoluteThreshold, info)`
  - compare against per-cell meters (`AbsoluteToMetersASL(GetAbsoluteHeight(cell, info), info)`)
- Keep morphology logic unchanged unless explicitly tuning map generation behavior.
- Current consumers using this pattern:
  - `EconSim.Core.Economy.EconomyInitializer`
  - `EconSim.Core.Transport.TransportGraph`

## World Metadata

- `MapGenResult.World` is emitted by mapgen as explicit world-scale output.
- `MapInfo.World` carries that metadata into EconSim runtime.
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
- `MapData.AssertElevationInvariants(requireCanonical: true)` is executed after map conversion.
- `Elevation` helpers throw for non-finite or out-of-range absolute values.
- EditMode regressions include:
  - distribution baseline gates (`landRatio`, `waterRatio`, `riverCellRatio`, elevation `p10/p50/p90`)
  - source-usage guard that fails if production code reads raw `cell.Height` outside `MapData` helpers.

## CI Gate

- GitHub Actions workflow: `.github/workflows/ci.yml`
- PR gate runs:
  - `dotnet build unity/EconSim.EditModeTests.csproj`
  - Unity EditMode tests via `game-ci/unity-test-runner`
- Required repo secret:
  - `UNITY_LICENSE` (and optionally `UNITY_EMAIL` / `UNITY_PASSWORD` based on license activation mode)
