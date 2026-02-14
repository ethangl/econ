# Render Performance Baseline

This document captures a reference render-frame baseline for `Assets/Scenes/Main.unity` after the URP migration.

## Latest Baseline

- Capture date (UTC): `2026-02-14T16:19:18.5747500Z`
- Scene: `Assets/Scenes/Main.unity`
- Pipeline: `URP` (`unity/Assets/Materials/EconSimURP.asset`)
- Map source: cached map (`unity/debug/last-map/map_payload.json`)
- Warmup frames: `240`
- Sample frames: `600`

Frame-time stats:

- Average frame time: `6.225 ms`
- P50 frame time: `6.203 ms`
- P95 frame time: `6.561 ms`
- Min frame time: `5.55 ms`
- Max frame time: `22.274 ms`
- Average FPS: `160.648`

CPU/GPU frame timing (Unity `FrameTimingManager`):

- CPU avg / p50 / p95: `6.225 / 6.192 / 6.54 ms`
- GPU avg / p50 / p95: `3.113 / 2.934 / 4.129 ms`

Raw capture artifact:

- Git-tracked snapshot: `docs/perf/render_baseline_main_2026-02-14.json`
- Local capture output: `unity/debug/perf/render_baseline_main_20260214_161918.json`

## Re-Capture Procedure

1. Load `Assets/Scenes/Main.unity`.
2. Add `EconSim.Core.RenderBaselineSampler` to the `GameManager` object.
3. Press Play.
4. Wait for `[RenderBaseline] COMPLETE ...` in the Unity Console.
5. Read the new JSON artifact in `unity/debug/perf/`.
6. Remove `RenderBaselineSampler` from `GameManager` after capture.

## Notes

- Baselines should be compared on similar hardware, editor version, and quality settings.
- Treat this as a trend/comparison anchor, not an absolute target.
