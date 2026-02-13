# Shader Overlay Debugging Playbook

## Goal

Debug map overlay rendering issues quickly and reproducibly, especially:

- wrong political colors,
- border artifacts,
- hover/selection mismatches,
- water/river masking problems,
- mode-specific visual regressions.

This playbook is for `MapOverlay.shader` + `MapOverlayManager` pipeline debugging.

---

## Relevant Components

- `unity/Assets/Shaders/MapOverlay.shader`
- `unity/Assets/Scripts/Renderer/MapOverlayManager.cs`
- `unity/Assets/Scripts/Renderer/MapView.cs`
- `unity/Assets/Editor/MapOverlayShaderGUI.cs`
- `src/EconSim.Core/Rendering/PoliticalPalette.cs`

Supporting generated textures:

- `_CellDataTex`
- `_HeightmapTex`
- `_RiverMaskTex`
- `_RealmPaletteTex`
- `_MarketPaletteTex`
- `_BiomePaletteTex`
- `_RealmBorderDistTex` / `_ProvinceBorderDistTex` / `_CountyBorderDistTex`
- `_MarketBorderDistTex`
- `_RoadMaskTex`
- `_CellToMarketTex`

---

## Debugging Principles

1. **Freeze the scenario first**
   - fixed seed
   - same map mode
   - same zoom/position
2. **Isolate one layer at a time**
   - terrain -> mode color -> borders -> water -> hover/selection
3. **Verify data before math**
   - incorrect input IDs look like shader math bugs
4. **Prefer domain-typed logs over ad-hoc prints**
5. **Do not fix by tweaking random constants first**
   - identify the failing stage first

---

## Fast Triage Matrix

### Symptom: realm/county color is wrong

Likely causes:

- bad ID decode from `_CellDataTex`
- palette lookup mismatch
- precision/hue-shifting transform in shader path

Checks:

1. Confirm decoded IDs from `MapOverlayManager.GenerateDataTextures`.
2. Confirm palette entry generation in `GeneratePaletteTextures`.
3. Temporarily render raw palette color only (disable border/hover transforms).
4. If issue disappears, isolate transform math (HSV/precision/multiply).

---

### Symptom: borders wrong color or inconsistent with fill

Likely causes:

- border band color transform differs from fill transform
- wrong border distance texture data
- ordering interaction between county/province/realm border overlays

Checks:

1. Visualize each border distance texture independently.
2. Confirm map mode branch and border width/darkening params.
3. Temporarily set border color = base mode color (no darkening) to isolate.
4. Re-enable darkening path only after color parity is verified.

---

### Symptom: hover affects wrong region or wrong hue

Likely causes:

- ID equality mismatch (precision/normalization)
- hover target domain mismatch (realm vs county vs market)
- color transform causing hue drift

Checks:

1. Confirm current mode routes hover to expected ID (`MapView.UpdateHover`).
2. Confirm normalized ID set in `SetHovered*`.
3. Check equality thresholds in shader.
4. Use hue-preserving RGB brighten as baseline behavior.

---

### Symptom: map mode looks "washed out" or too dark

Likely causes:

- gradient parameters too aggressive
- unexpected grayscale/base blend balance
- border darkening compounding with mode shading

Checks:

1. Set `_GradientEdgeDarkening = 0` and `_GradientCenterOpacity = 1` to baseline.
2. Disable borders temporarily.
3. Reintroduce gradient, then borders, one parameter group at a time.

---

### Symptom: rivers/water mask incorrect

Likely causes:

- river mask generation mismatch
- water flag decode issue from packed channel
- composition order (water layer vs mode layer) unintended

Checks:

1. Visualize `_RiverMaskTex` and `_HeightmapTex`.
2. Validate packed-water decode logic path.
3. Check compositing order in fragment path.

---

## Layer Isolation Workflow (Step-by-Step)

When a new visual bug appears, follow this order:

1. **Repro lock**
   - fixed seed, screenshot, mode noted.
2. **Raw data validation**
   - verify source textures and ID ranges from generation code.
3. **Base terrain-only render**
   - disable map mode overlay, borders, hover, selection.
4. **Mode overlay only**
   - enable political/market color without border/hover transforms.
5. **Borders only**
   - enable one border type at a time (county -> province -> realm).
6. **Hover/selection**
   - add interaction transforms last.
7. **Re-enable full stack**
   - verify bug stays fixed under full composition.

This prevents "fixing" the wrong stage.

---

## Runtime Controls (No Code Edits)

Use the built-in runtime tooling:

- Press `0` to enter **Channel Inspector** mode.
- Press `O` to cycle channel views:
  - `CellData R/G/B/A`
  - `Realm/Province/County/Market border distance`
  - `River mask`
  - `Heightmap`
  - `Road mask`
- Press `P` to toggle the on-screen **ID Probe**.
  - Probe shows decoded `realm/province/county/market` IDs under cursor.
  - Probe shows normalized channel values matching `_CellDataTex` packing.

In material Inspector (`MapOverlayShaderGUI`):

- Use the **Debug** foldout to choose `Channel Inspector View` directly.

This workflow should be sufficient for most overlay regressions without source edits.

---

## Logging Guidance (Domain-Based)

Recommended domains:

- `Shaders`
- `Overlay`
- `Renderer`

High-value log points:

- data texture generation summary (size, min/max IDs, counts),
- palette generation summary (realm count, index bounds),
- map mode switches and shader mode integer,
- hover/selection ID writes.

Keep per-pixel/per-cell logs out of `Info`; use `Trace/Debug` with filters.

---

## Common Root-Cause Patterns

1. **Hidden scale assumptions**
   - `/100f`, `20f`, etc. survive after elevation/domain changes.
2. **Packed channel fragility**
   - one bit/offset error in decode cascades into visual anomalies.
3. **Precision mismatch**
   - `fixed` precision + HSV round-trips can shift low-saturation hues.
4. **Order-of-operations regressions**
   - a correct transform applied in the wrong layer still looks wrong.
5. **Mode-routing mismatch**
   - hover/selection ID domain mismatched to current map mode.

---

## Bug Report Template (for Visual Issues)

Use this structure for each new shader bug:

- Seed:
- Mode:
- Camera zoom/location:
- Expected:
- Actual:
- First bad commit (if known):
- Affected layer (terrain/mode/border/water/hover/selection):
- Screenshot(s):
- Data validation status:
- Isolation step where issue first appears:

This reduces back-and-forth dramatically.

---

## Definition of Done for Shader Fixes

Before closing a shader/overlay bug:

1. Root cause identified (data vs math vs ordering vs mode routing).
2. Fix validated in isolation and full composition.
3. No regression in at least:
   - Political mode
   - Biomes mode
   - Market mode
4. Hover and border behavior verified for low-saturation realms.
5. Note added to relevant doc if the failure mode is likely to recur.

---

## Next Suggested Follow-Up

Create `docs/DEBUGGING_PLAYBOOK.md` as a project-wide superset.
This shader playbook should become its rendering section.
