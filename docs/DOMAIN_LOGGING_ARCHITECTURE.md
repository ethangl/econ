# Domain Logging Architecture

## Goal

Add a structured logging system with:

- domain-based categorization (mapgen, shaders, roads, economy, UI, etc.),
- runtime filtering by domain and severity,
- a Unity Inspector control panel to choose which domains are visible,
- minimal runtime overhead when logging is disabled.

---

## Why this is needed

Current plain `Debug.Log` style output is hard to triage because:

- unrelated systems interleave in console output,
- high-volume startup logs drown signal,
- there is no reliable per-domain on/off switch.

Domain logging gives targeted observability without deleting useful diagnostics.

---

## Scope

### In scope

- Shared logging API usable from `src/MapGen`, `src/EconSim.Core`, and Unity layer.
- Domain tags and severity levels.
- Central runtime filter state.
- Unity Inspector panel to toggle domain visibility.
- Optional in-memory log buffer for recent entries.

### Out of scope (for first iteration)

- Remote logging backends.
- Persisting full logs to external observability platforms.
- Replacing every existing log call in one pass.

---

## Domain Model

Use a stable enum/flags set for domains.

Proposed domains:

- `MapGen`
- `HeightmapDSL`
- `Climate`
- `Rivers`
- `Biomes`
- `Population`
- `Political`
- `Economy`
- `Transport`
- `Roads`
- `Renderer`
- `Shaders`
- `Overlay`
- `Selection`
- `UI`
- `Camera`
- `Simulation`
- `Bootstrap`
- `IO`

Severity levels:

- `Trace`
- `Debug`
- `Info`
- `Warn`
- `Error`

Each log event should include:

- timestamp,
- domain,
- severity,
- message,
- optional key-value metadata (small dictionary),
- optional context/source tag.

---

## Architecture

### 1) Core logger (engine-agnostic)

Location:

- `src/EconSim.Core/Diagnostics/` (shared with Unity and mapgen)

Responsibilities:

- expose `Log(domain, level, message, meta?)`,
- check active filter state before expensive formatting,
- dispatch events to one or more sinks.

### 2) Sinks

Initial sinks:

- **UnityConsoleSink**: writes to `Debug.Log/Warning/Error` with domain prefix.
- **RingBufferSink**: stores recent N events for inspection panel.

Later optional sinks:

- file sink,
- sampled perf sink.

### 3) Filter state

Maintain a runtime filter object:

- enabled domains (bitmask),
- minimum severity,
- optional text search,
- optional "Errors only" fast mode.

For fast checks, compile one `IsEnabled(domain, level)` guard.

### 4) Unity Inspector control panel

Implement a small runtime component:

- `LoggingControlPanel` (MonoBehaviour)

Custom editor in:

- `Assets/Editor/LoggingControlPanelEditor.cs`

Panel capabilities:

- toggle all domains,
- toggle individual domains,
- set minimum severity,
- clear ring buffer,
- show most recent entries (domain/severity/message),
- quick buttons: `MapGen only`, `Renderer only`, `Errors only`, `All on`.

Note: user asked "expector"; implement in Unity **Inspector**.

---

## API Sketch

Example call sites:

```csharp
LogDomain.MapGen.Info("Starting generation", ("seed", seed), ("cells", cellCount));
LogDomain.Roads.Debug("Road promoted", ("from", a), ("to", b), ("tier", tier));
LogDomain.Shaders.Warn("Missing texture binding", ("name", "_CellDataTex"));
```

Guard expensive payloads:

```csharp
if (DomainLog.IsEnabled(LogDomain.Biomes, LogLevel.Debug))
{
    DomainLog.Debug(LogDomain.Biomes, $"Biome histogram: {ComputeHistogramText()}");
}
```

---

## Performance Rules

1. Avoid string interpolation unless enabled.
2. Keep per-event allocations minimal.
3. Use ring buffer cap (e.g., 2,000-10,000 events).
4. Support compile-time stripping for very verbose levels in release builds.

Optional compile flags:

- `ECON_TRACE_LOGS`
- `ECON_DEBUG_LOGS`

---

## Threading

Mapgen/core may log off main thread in future.

Design requirement:

- logger API must be thread-safe for enqueue path.
- Unity sink dispatch to `Debug.Log` should be marshaled to main thread if needed.
- Ring buffer writes should be lock-protected or use concurrent queue + periodic drain.

---

## Rollout Plan

### Phase 1 - Foundation

- add domain + level enums,
- add core logger with filter + Unity sink,
- add `LoggingControlPanel` + custom inspector,
- keep existing plain logs intact.

### Phase 2 - High-value adoption

Migrate key systems first:

- mapgen stages,
- overlay/shader setup,
- road generation,
- economy tick summaries.

### Phase 3 - Cleanup and conventions

- replace ad-hoc `Debug.Log` in frequently noisy paths,
- keep errors/warnings in plain Unity console too (with domain prefix),
- add contributor guidance in docs.

---

## Inspector UX Requirements

Minimum UX for first usable panel:

- visible domain checklist with counts (optional),
- level dropdown,
- live update toggle,
- "copy visible logs" button,
- one-click presets.

Nice-to-have:

- collapse by domain,
- per-domain color chips,
- per-domain event counters.

---

## Conventions

- Use concise present-tense messages.
- Include structured fields for IDs (`cellId`, `countyId`, `marketId`) instead of embedding all data into text.
- Do not spam per-cell logs at `Info`.
- Reserve `Trace/Debug` for high-volume diagnostics.

---

## Risks and Mitigations

- **Risk:** logging overhead in hot paths.  
  **Mitigation:** early filter checks and compile-time stripping for trace/debug.

- **Risk:** inconsistent domain usage across team.  
  **Mitigation:** shared enum + short usage guide.

- **Risk:** Unity inspector clutter.  
  **Mitigation:** presets + foldouts + sensible defaults.

---

## Acceptance Criteria

1. Can filter logs by domain from Unity Inspector at runtime.
2. Can set minimum severity globally.
3. Mapgen, roads, and renderer domains each produce distinguishable filtered output.
4. Disabling a domain suppresses its logs immediately.
5. No noticeable frame hitching when logging is mostly disabled.

---

## Decision

Proceed with domain-based logging and inspector control panel.

This is feasible, low-risk, and high leverage for development speed as system complexity grows.
