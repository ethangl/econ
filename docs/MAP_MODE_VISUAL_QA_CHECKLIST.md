# Map Mode Visual QA Checklist

Use this checklist after shader/material/render-style changes.

## Setup

1. Launch the Unity project and generate a new map.
2. Start in Political mode (`1`) with camera centered over mixed land/water.
3. Pick one coastline region and one inland region for repeated checks.

## Mode-to-Style Contract

1. Political / Province / County / Market / Transport Cost / Market Access must render in `Flat` style.
2. Biomes must render in `Biome` style.
3. Entering and exiting Biomes must switch style immediately (no missing textures, no pink material fallback).

## Height Displacement Contract

1. Non-Biome modes must animate toward `_HeightScale = 0`.
2. Biomes must animate toward `_HeightScale = 0.3`.
3. Switch between `1` and `2` repeatedly and verify smooth transitions with no snapping.

## Water Contract

1. In Biomes mode, water and rivers must use biome water shading.
2. In all Flat modes, biome-style water shading must not appear.
3. Confirm coastlines remain readable in both styles.

## Overlay Contract

1. Political family modes: overlay cycling via `1` should still work.
2. Non-political modes should not show unintended political overlays.
3. Selection/hover highlight must track the correct domain for each mode.

## Border Contract

1. Political family modes show realm/province/county borders correctly.
2. Market mode shows market borders correctly.
3. No border flicker when switching modes quickly.

## Smoke Regression

1. Toggle to Channel Inspector (`0`) and cycle debug channels with `O`.
2. Return to each mode and verify colors/selection still behave correctly.
3. Regenerate map once and repeat spot checks to catch data-dependent issues.

## Pass Criteria

1. No visual terrain relief in Flat modes.
2. Biomes is the only mode with visible relief + biome terrain/water shading.
3. No missing materials, no null-reference errors, no style mismatch during mode transitions.
