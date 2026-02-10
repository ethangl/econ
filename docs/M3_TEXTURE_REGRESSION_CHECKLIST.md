# M3-S4 Texture Regression and Validation Checklist

## Objective

Provide a concrete, repeatable signoff checklist for Milestone `M3-S4` so M3 exits only when texture architecture changes are verified for determinism, interaction safety, and visual stability.

This checklist is the execution companion to:

- `docs/BACKLOG.md` (`M3-S4`)
- `docs/MAP_TEXTURE_ARCHITECTURE.md` (Debug and Validation Requirements + Migration Plan)

Status snapshot (February 10, 2026):

- `M3-S4` requirements are defined in backlog and architecture docs.
- This checklist defines exact test matrix, required artifacts, and pass/fail gates.

Execution snapshot (February 10, 2026):

- Automated EditMode suite (`EconSim.EditModeTests`) is green: 36 passed, 0 failed, 0 skipped.
- Filtered automated M3 gate (`Category=M3Regression`) is green: 33 passed, 0 failed, 0 skipped.
- Implemented and passing: fixed-seed texture hash determinism, mode/economy refresh regression coverage (pre-`ModeColorResolve` path), and low-saturation color stability checks.
- Pending for final M3 signoff: direct `ModeColorResolve` invalidation assertions after resolved texture integration, plus manual Channel Inspector and ID Probe validation artifacts for all baseline cases.

---

## Scope of M3-S4

`M3-S4` is complete only when all of the following pass:

1. fixed-seed texture regression (golden map checksums/stat snapshots),
2. resolve invalidation regression (mode switch + relevant data changes),
3. color stability regression (low-saturation hue class preserved under hover/borders),
4. channel inspector + ID probe validation for all new core texture schemas.

---

## Baseline Matrix (Required)

Use the canonical fixed-seed baseline cases from:

- `unity/Assets/Tests/EditMode/MapGenRegressionBaselines.json`

Current required matrix:

1. seed `12345`, template `LowIsland`
2. seed `424242`, template `Continents`
3. seed `8675309`, template `Pangea`

Run all M3-S4 checks on all three baseline cases unless a check explicitly states otherwise.

---

## Required Automated Checks

### A) Existing mapgen regression harness must stay green

- [ ] `unity/Assets/Tests/EditMode/MapGenRegressionTests.cs` passes in EditMode.
- [ ] No baseline JSON schema changes were introduced without deliberate review.

Purpose:

- Confirms texture architecture work did not destabilize generation invariants before texture-level checks are interpreted.

### B) Texture golden regression (new/extended)

- [ ] For each baseline case, capture deterministic texture fingerprints for:
  - Political IDs texture
  - Geography Base texture
  - Heightmap
  - River mask
  - Realm/Province/County border distance textures
  - Market border distance texture (after economy assignment)
  - `ModeColorResolve` output for at least one resolved mode
- [ ] Fingerprints are compared against committed golden values.
- [ ] Any accepted intentional delta updates the golden baseline and changelog note.

Implementation target:

- Add/extend EditMode tests under `unity/Assets/Tests/EditMode/` (for example `MapTextureRegressionTests.cs`).
- Canonical hash baseline file: `unity/Assets/Tests/EditMode/MapTextureRegressionHashBaselines.json`.
- Baseline refresh mode (intentional updates only): run tests with env var `M3_UPDATE_TEXTURE_BASELINES=1`, then review and commit baseline JSON deltas.
- Current implementation:
  - `unity/Assets/Tests/EditMode/MapTextureRegressionTests.cs`
  - `unity/Assets/Tests/EditMode/TextureTestHarness.cs`

Fingerprint guidance:

- Prefer stable hash of raw pixel bytes + dimensions + format metadata.
- Do not hash encoded PNG bytes if compression/metadata can vary by platform.

### C) Resolve invalidation regression (new)

- [ ] Validate resolve reruns on map mode switch.
- [ ] Validate resolve reruns on relevant domain-data change.
- [ ] Validate no stale `ModeColorResolve` output remains after either trigger.

Minimum scenarios:

1. switch Political -> Terrain -> Political and verify refresh each transition,
2. mutate a mode-driving input (for example market assignment) and verify refresh without full map rebuild.

Implementation target:

- Add EditMode/PlayMode coverage under `unity/Assets/Tests/` (for example `ModeResolveRegressionTests.cs`).
- Current implementation (pre-`ModeColorResolve` path):
  - `unity/Assets/Tests/EditMode/ModeResolveRegressionTests.cs`
  - coverage includes mode-switch routing, economy-data refresh, idempotence, round-trip persistence, and no unexpected backing-texture mutation across mode switches.
  - when `ModeColorResolve` is introduced, add direct assertions on resolve-texture invalidation/regeneration.

### D) Color stability regression (new)

- [ ] Low-saturation political colors remain in expected hue class after border darkening.
- [ ] Hover highlight remains hue-preserving on low-saturation samples.
- [ ] No mode-specific hue drift is introduced in Political, County, and Market modes.

Implementation target:

- Add targeted assertions for representative palette samples (especially near-gray and desaturated colors).
- Current implementation: `unity/Assets/Tests/EditMode/MapColorStabilityTests.cs`.

---

## Required Manual Validation

Manual checks are required even when automated tests pass.

### A) Channel Inspector coverage

- [ ] Enter Channel Inspector mode (`0`) and cycle views (`O`) for each baseline case.
- [ ] Verify Political IDs and Geography channels display expected semantic separation.
- [ ] Verify border distance, river mask, and heightmap channels remain plausible and aligned.

Automated pre-checks:

- `unity/Assets/Tests/EditMode/ModeResolveRegressionTests.cs`
  - `SetChannelDebugView_UpdatesShaderDebugViewProperty(...)`
  - `ModeSwitches_DoNotMutateBackingTextures()`
  - `EconomyTextureUpdates_PersistAcrossModeSwitchRoundTrips()`

Reference:

- `docs/debug/SHADER_OVERLAY_DEBUGGING.md`

### B) ID Probe coverage

- [ ] Enable ID Probe (`P`) and sample land + water cells in each baseline case.
- [ ] Confirm decoded IDs are coherent with visible map regions.
- [ ] Confirm hover target routing is mode-appropriate and mode-independent where required.

Automated pre-checks:

- `unity/Assets/Tests/EditMode/ModeResolveRegressionTests.cs`
  - `SelectionSetters_ClearOtherChannels_AndNormalizeIds()`
  - `HoverSetters_ClearOtherChannels_AndNormalizeIds()`
  - `InteractionIntensitySetters_ClampValues()`
  - `UpdateCellData_MutatesOnlyCellDataTexture()`
  - `UpdateCellData_InvalidCellId_DoesNotChangeTextures()`

---

## Artifacts to Capture Per Run

Create one artifact bundle per checklist run (for example by date or branch):

1. test runner output summary (pass/fail),
2. texture fingerprint output (actual vs golden),
3. screenshots for Political/Terrain/County/Market modes,
4. notes for any accepted intentional deltas.

Debug texture PNG exports can be captured from:

- `debug/*.png` (written by `unity/Assets/Scripts/Renderer/TextureDebugger.cs`).

---

## Signoff Gate (M3 Exit)

Do not mark `M3` complete until all checks below are true:

- [ ] A/B/C/D automated checks pass for all required baseline cases.
- [ ] Manual Channel Inspector + ID Probe checks pass for all required baseline cases.
- [ ] Any accepted deltas are documented (what changed, why acceptable, new baseline reference).
- [ ] `docs/BACKLOG.md` status for `M3-S4` is updated accordingly.

If any item fails, `M3` remains open and `M4` work is blocked by policy.

---

## Suggested Execution Order

1. Run mapgen harness (A).
2. Run texture golden regression (B).
3. Run resolve invalidation regression (C).
4. Run color stability regression (D).
5. Perform manual inspector/probe validation.
6. Capture artifacts and record signoff.

Fast automated gate run:

- Use the `M3Regression` NUnit category in EditMode to execute the M3-S4 automated suites as one filtered run (`MapGenRegressionTests`, `MapTextureRegressionTests`, `ModeResolveRegressionTests`, `MapColorStabilityTests`).
