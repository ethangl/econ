using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using EconSim.Core;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Transport;
using MapGen.Core;

namespace EconSim.Editor
{
    /// <summary>
    /// Editor-only debug bridge for automated analysis.
    ///
    /// Protocol:
    ///   1. Write command JSON to {ProjectRoot}/econ_debug_cmd.json
    ///   2. Trigger via menu: Tools/EconDebug/Execute Command
    ///   3. Poll {ProjectRoot}/econ_debug_status.json for completion
    ///   4. Read results from {ProjectRoot}/econ_debug_output.json
    ///
    /// Commands:
    ///   generate_and_run  { seed, cellCount, template, months }
    ///   run_months         { months }
    ///   dump               { scope: all|summary|roads }
    ///   status             (writes current sim status)
    ///   cancel             (cancels pending async operation)
    /// </summary>
    [InitializeOnLoad]
    public static class EconDebugBridge
    {
        // ── File Paths ─────────────────────────────────────────────

        static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);
        static string CmdPath => Path.Combine(ProjectRoot, "econ_debug_cmd.json");
        static string OutputPath => Path.Combine(ProjectRoot, "econ_debug_output.json");
        static string StatusPath => Path.Combine(ProjectRoot, "econ_debug_status.json");
        static string PendingPath => Path.Combine(ProjectRoot, "econ_debug_pending.json");

        // ── State ──────────────────────────────────────────────────

        enum BridgeState { Idle, WaitingForPlayMode, WaitingForMap, Running }

        static BridgeState _state = BridgeState.Idle;
        static int _targetDay;
        static int _startDay;
        static bool _needsGenerate;
        static MapGenConfig _pendingConfig;

        // ── Domain Reload Survival ─────────────────────────────────

        static EconDebugBridge()
        {
            if (!File.Exists(PendingPath)) return;

            try
            {
                var json = File.ReadAllText(PendingPath);
                var pending = JsonUtility.FromJson<PendingOperation>(json);
                RestoreFromPending(pending);
                EditorApplication.update += OnUpdate;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EconDebug] Failed to restore pending operation: {e.Message}");
                CleanupAll();
            }
        }

        static void RestoreFromPending(PendingOperation p)
        {
            _targetDay = p.targetDay;
            _startDay = p.startDay;
            _needsGenerate = p.needsGenerate;

            if (_needsGenerate)
            {
                _pendingConfig = new MapGenConfig
                {
                    Seed = p.seed,
                    CellCount = p.cellCount,
                    Template = ParseTemplate(p.template)
                };
                _state = BridgeState.WaitingForMap;
            }
            else
            {
                _pendingConfig = null;
                _state = BridgeState.Running;
            }
        }

        static void WritePending(PendingOperation op)
        {
            File.WriteAllText(PendingPath, JsonUtility.ToJson(op, true));
        }

        // ── Menu Items ─────────────────────────────────────────────

        [MenuItem("Tools/EconDebug/Execute Command")]
        public static void ExecuteCommand()
        {
            if (!File.Exists(CmdPath))
            {
                WriteStatus("error", "No command file at " + CmdPath);
                return;
            }

            try
            {
                var json = File.ReadAllText(CmdPath);
                var cmd = JsonUtility.FromJson<DebugCommand>(json);
                Dispatch(cmd);
            }
            catch (Exception e)
            {
                WriteStatus("error", $"Parse error: {e.Message}");
            }
        }

        [MenuItem("Tools/EconDebug/Dump State")]
        public static void DumpStateMenu()
        {
            if (!EnsureReady()) return;
            DumpState("all");
            WriteStatus("complete", "State dumped");
        }

        [MenuItem("Tools/EconDebug/Cancel")]
        public static void CancelMenu()
        {
            StopActiveRun();
            CleanupAll();
            WriteStatus("idle", "Cancelled");
        }

        // ── Dispatch ───────────────────────────────────────────────

        static void Dispatch(DebugCommand cmd)
        {
            switch (cmd.action)
            {
                case "generate_and_run":
                    CmdGenerateAndRun(cmd);
                    break;

                case "run_months":
                    CmdRunMonths(cmd);
                    break;

                case "dump":
                    if (!EnsureReady()) return;
                    DumpState(string.IsNullOrEmpty(cmd.scope) ? "all" : cmd.scope);
                    WriteStatus("complete", "Dump written");
                    break;

                case "status":
                    WriteCurrentStatus();
                    break;

                case "cancel":
                    StopActiveRun();
                    CleanupAll();
                    WriteStatus("idle", "Cancelled");
                    break;

                default:
                    WriteStatus("error", $"Unknown action: {cmd.action}");
                    break;
            }
        }

        // ── generate_and_run ───────────────────────────────────────

        static void CmdGenerateAndRun(DebugCommand cmd)
        {
            int seed = cmd.seed > 0 ? cmd.seed : UnityEngine.Random.Range(1, int.MaxValue);
            int cellCount = cmd.cellCount > 0 ? cmd.cellCount : 60000;
            int months = cmd.months > 0 ? cmd.months : 6;

            var pending = new PendingOperation
            {
                action = "generate_and_run",
                targetDay = 1 + months * 30,
                startDay = 1,
                seed = seed,
                cellCount = cellCount,
                template = cmd.template ?? "Continents",
                needsGenerate = true
            };

            WritePending(pending);
            RestoreFromPending(pending);
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;

            if (EditorApplication.isPlaying)
            {
                // Already in play mode — generate now if GameManager is ready,
                // otherwise let update loop wait for it.
                WriteStatus("starting", $"Preparing run (seed={seed}, {months}mo)...");
                if (GameManager.Instance != null)
                    DoGenerate();
            }
            else
            {
                // Enter play mode and keep update callback for domain-reload-disabled flows.
                _state = BridgeState.WaitingForPlayMode;
                WriteStatus("starting", $"Entering play mode (seed={seed}, {months}mo)...");
                EditorApplication.isPlaying = true;
            }
        }

        static void DoGenerate()
        {
            _state = BridgeState.WaitingForMap;
            WriteStatus("generating",
                $"Generating map (seed={_pendingConfig.Seed}, cells={_pendingConfig.CellCount}, " +
                $"template={_pendingConfig.Template})...");

            try
            {
                GameManager.Instance.GenerateMap(_pendingConfig);
                _needsGenerate = false;

                if (GameManager.IsMapReady)
                    StartRun();
            }
            catch (Exception e)
            {
                CleanupAll();
                WriteStatus("error", $"Map generation failed: {e.Message}");
            }
        }

        // ── run_months ─────────────────────────────────────────────

        static void CmdRunMonths(DebugCommand cmd)
        {
            if (!EditorApplication.isPlaying || !GameManager.IsMapReady)
            {
                WriteStatus("error", "Must be in play mode with map loaded. Use generate_and_run.");
                return;
            }

            int months = cmd.months > 0 ? cmd.months : 6;
            var sim = GameManager.Instance.Simulation;
            int currentDay = sim.GetState().CurrentDay;

            _startDay = currentDay;
            _targetDay = currentDay + months * 30;
            _needsGenerate = false;
            _pendingConfig = null;

            var pending = new PendingOperation
            {
                action = "run_months",
                targetDay = _targetDay,
                startDay = _startDay,
                needsGenerate = false
            };
            WritePending(pending);

            StartRun();
        }

        static void StartRun()
        {
            var sim = GameManager.Instance.Simulation;
            _state = BridgeState.Running;
            sim.TimeScale = SimulationConfig.Speed.Hyper;
            sim.IsPaused = false;

            WriteStatus("running", $"Running from day {_startDay} to day {_targetDay}...");

            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        // ── Update Callback ────────────────────────────────────────

        static void OnUpdate()
        {
            switch (_state)
            {
                case BridgeState.WaitingForPlayMode:
                    if (!EditorApplication.isPlaying) return;
                    if (_needsGenerate && GameManager.Instance != null)
                        DoGenerate();
                    else if (!_needsGenerate && GameManager.IsMapReady)
                        StartRun();
                    break;

                case BridgeState.WaitingForMap:
                    if (!EditorApplication.isPlaying)
                    {
                        CleanupAll();
                        WriteStatus("error", "Play mode exited during generation");
                        return;
                    }
                    if (GameManager.Instance != null && _needsGenerate)
                        DoGenerate();
                    else if (GameManager.IsMapReady)
                        StartRun();
                    break;

                case BridgeState.Running:
                    if (!EditorApplication.isPlaying)
                    {
                        CleanupAll();
                        WriteStatus("error", "Play mode exited during run");
                        return;
                    }
                    if (!GameManager.IsMapReady) return;

                    var simState = GameManager.Instance.Simulation.GetState();
                    int currentDay = simState.CurrentDay;
                    if (currentDay >= _targetDay)
                    {
                        GameManager.Instance.Simulation.IsPaused = true;
                        DumpState("all");
                        CleanupAll();
                        WriteStatus("complete", $"Run complete at day {currentDay}");
                    }
                    break;

                case BridgeState.Idle:
                    EditorApplication.update -= OnUpdate;
                    break;
            }
        }

        // ── Cleanup ────────────────────────────────────────────────

        static void CleanupAll()
        {
            _state = BridgeState.Idle;
            _needsGenerate = false;
            _pendingConfig = null;
            EditorApplication.update -= OnUpdate;
            if (File.Exists(PendingPath)) File.Delete(PendingPath);
        }

        static void StopActiveRun()
        {
            if (_state != BridgeState.Running)
                return;
            if (!EditorApplication.isPlaying || GameManager.Instance?.Simulation == null)
                return;

            var sim = GameManager.Instance.Simulation;
            sim.IsPaused = true;
            sim.TimeScale = SimulationConfig.Speed.Normal;
        }

        // ── Status ─────────────────────────────────────────────────

        static void WriteStatus(string state, string message)
        {
            var j = new JW();
            j.ObjOpen();
            j.KV("state", state);
            j.KV("message", message);

            if (EditorApplication.isPlaying && GameManager.IsMapReady)
            {
                var simState = GameManager.Instance.Simulation.GetState();
                j.KV("currentDay", simState.CurrentDay);
                FormatDate(j, simState.CurrentDay);
                if (_targetDay > 0)
                    j.KV("targetDay", _targetDay);
            }

            j.KV("timestamp", DateTime.UtcNow.ToString("o"));
            j.ObjClose();
            File.WriteAllText(StatusPath, j.ToString());
        }

        static void WriteCurrentStatus()
        {
            if (!EditorApplication.isPlaying)
            {
                WriteStatus("idle", "Not in play mode");
                return;
            }
            if (!GameManager.IsMapReady)
            {
                WriteStatus("waiting", "Map not ready");
                return;
            }

            var sim = GameManager.Instance.Simulation;
            var st = sim.GetState();
            var j = new JW();
            j.ObjOpen();
            j.KV("state", _state == BridgeState.Running ? "running" : "idle");
            j.KV("currentDay", st.CurrentDay);
            FormatDate(j, st.CurrentDay);
            j.KV("isPaused", sim.IsPaused);
            j.KV("timeScale", st.TimeScale);
            j.KV("totalTicks", st.TotalTicksProcessed);
            if (_state == BridgeState.Running)
                j.KV("targetDay", _targetDay);
            j.KV("timestamp", DateTime.UtcNow.ToString("o"));
            j.ObjClose();
            File.WriteAllText(StatusPath, j.ToString());
        }

        // ── State Dump ─────────────────────────────────────────────

        static void DumpState(string scope)
        {
            var sim = GameManager.Instance.Simulation;
            var st = sim.GetState();
            var mapData = GameManager.Instance.MapData;

            var j = new JW();
            j.ObjOpen();

            // Header
            j.KV("day", st.CurrentDay);
            FormatDate(j, st.CurrentDay);
            j.KV("totalTicks", st.TotalTicksProcessed);
            j.KV("timestamp", DateTime.UtcNow.ToString("o"));

            if (scope == "all" || scope == "summary")
            {
                WriteSummary(j, st, mapData);
                WritePerformance(j, st);
            }
            if (scope == "all" || scope == "roads")
                WriteRoads(j, st);

            j.ObjClose();
            File.WriteAllText(OutputPath, j.ToString());
        }

        static void WriteSummary(JW j, SimulationState st, MapData mapData)
        {
            j.Key("summary"); j.ObjOpen();

            int totalPop = 0;
            if (mapData?.Cells != null)
            {
                foreach (var cell in mapData.Cells)
                    totalPop += (int)cell.Population;
            }

            j.KV("totalPopulation", totalPop);
            j.KV("totalCounties", mapData?.Counties?.Count ?? 0);
            j.KV("totalProvinces", mapData?.Provinces?.Count ?? 0);
            j.KV("totalRealms", mapData?.Realms?.Count ?? 0);

            // Road stats
            if (st.Roads != null)
            {
                var allRoads = st.Roads.GetAllRoads();
                j.KV("pathSegments", allRoads.Count(r => r.Item3 == RoadTier.Path));
                j.KV("roadSegments", allRoads.Count(r => r.Item3 == RoadTier.Road));
            }

            j.ObjClose();
        }

        static void WritePerformance(JW j, SimulationState st)
        {
            j.Key("performance"); j.ObjOpen();

            var perf = st.Performance;
            if (perf == null)
            {
                j.KV("tickSamples", 0);
                j.KV("avgTickMs", 0f);
                j.KV("maxTickMs", 0f);
                j.KV("lastTickMs", 0f);
                j.Key("systems"); j.ObjOpen();
                j.ObjClose();
                j.ObjClose();
                return;
            }

            j.KV("tickSamples", perf.TickSamples);
            j.KV("avgTickMs", perf.AvgTickMs);
            j.KV("maxTickMs", perf.MaxTickMs);
            j.KV("lastTickMs", perf.LastTickMs);

            j.Key("systems"); j.ObjOpen();
            foreach (var system in perf.Systems.Values.OrderByDescending(s => s.AvgMs).ThenBy(s => s.Name))
            {
                j.Key(system.Name); j.ObjOpen();
                j.KV("tickInterval", system.TickInterval);
                j.KV("invocations", system.InvocationCount);
                j.KV("avgMs", system.AvgMs);
                j.KV("maxMs", system.MaxMs);
                j.KV("lastMs", system.LastMs);
                j.KV("totalMs", system.TotalMs);
                j.ObjClose();
            }
            j.ObjClose();

            j.ObjClose();
        }

        static void WriteRoads(JW j, SimulationState st)
        {
            j.Key("roads"); j.ObjOpen();

            if (st.Roads == null)
            {
                j.KV("totalSegments", 0);
                j.ObjClose();
                return;
            }

            var allRoads = st.Roads.GetAllRoads();
            j.KV("totalSegments", allRoads.Count);
            j.KV("paths", allRoads.Count(r => r.Item3 == RoadTier.Path));
            j.KV("roads", allRoads.Count(r => r.Item3 == RoadTier.Road));

            float totalTraffic = 0f;
            foreach (var r in allRoads)
                totalTraffic += st.Roads.GetTraffic(r.Item1, r.Item2);
            j.KV("totalTraffic", totalTraffic);

            // Top 20 busiest segments
            j.Key("busiestSegments"); j.ArrOpen();
            foreach (var r in allRoads.OrderByDescending(r => st.Roads.GetTraffic(r.Item1, r.Item2)).Take(20))
            {
                j.ObjOpen();
                j.KV("cellA", r.Item1);
                j.KV("cellB", r.Item2);
                j.KV("tier", r.Item3.ToString());
                j.KV("traffic", st.Roads.GetTraffic(r.Item1, r.Item2));
                j.ObjClose();
            }
            j.ArrClose();

            j.ObjClose();
        }

        // ── Helpers ────────────────────────────────────────────────

        static bool EnsureReady()
        {
            if (!EditorApplication.isPlaying)
            {
                WriteStatus("error", "Not in play mode");
                return false;
            }
            if (!GameManager.IsMapReady)
            {
                WriteStatus("error", "Map not ready");
                return false;
            }
            return true;
        }

        static void FormatDate(JW j, int day)
        {
            j.KV("year", day / 360 + 1);
            j.KV("month", (day % 360) / 30 + 1);
            j.KV("dayOfMonth", day % 30 + 1);
        }

        static HeightmapTemplateType ParseTemplate(string template)
        {
            if (string.IsNullOrEmpty(template))
                return HeightmapTemplateType.Continents;
            if (Enum.TryParse<HeightmapTemplateType>(template, true, out var result))
                return result;
            Debug.LogWarning($"[EconDebug] Unknown template '{template}', defaulting to Continents");
            return HeightmapTemplateType.Continents;
        }

        // ── Data Classes ───────────────────────────────────────────

        [Serializable]
        class DebugCommand
        {
            public string action;
            public int months;
            public int seed;
            public int cellCount;
            public string template;
            public string scope;
        }

        [Serializable]
        class PendingOperation
        {
            public string action;
            public int targetDay;
            public int startDay;
            public int seed;
            public int cellCount;
            public string template;
            public bool needsGenerate;
        }

        // ── Minimal JSON Writer ────────────────────────────────────
        //
        // Unity doesn't guarantee Newtonsoft.Json, and JsonUtility can't
        // serialize dictionaries. This tiny writer handles the output needs.

        class JW
        {
            readonly StringBuilder _sb = new StringBuilder(8192);
            readonly Stack<bool> _hasItem = new Stack<bool>();
            int _indent;
            bool _afterKey;

            void NL() { _sb.Append('\n'); _sb.Append(' ', _indent * 2); }

            void BeforeValue()
            {
                if (_afterKey) { _afterKey = false; return; }
                if (_hasItem.Count > 0)
                {
                    if (_hasItem.Peek()) _sb.Append(',');
                    _hasItem.Pop();
                    _hasItem.Push(true);
                    NL();
                }
            }

            public void ObjOpen()  { BeforeValue(); _sb.Append('{'); _indent++; _hasItem.Push(false); }
            public void ObjClose() { _indent--; _hasItem.Pop(); NL(); _sb.Append('}'); }
            public void ArrOpen()  { BeforeValue(); _sb.Append('['); _indent++; _hasItem.Push(false); }
            public void ArrClose() { _indent--; _hasItem.Pop(); NL(); _sb.Append(']'); }

            public void Key(string k)
            {
                if (_hasItem.Peek()) _sb.Append(',');
                _hasItem.Pop();
                _hasItem.Push(true);
                NL();
                WriteString(k);
                _sb.Append(": ");
                _afterKey = true;
            }

            public void Val(string v) { if (v == null) { Null(); return; } BeforeValue(); WriteString(v); }
            public void Val(int v)    { BeforeValue(); _sb.Append(v); }
            public void Val(float v)  { BeforeValue(); _sb.Append(v.ToString("G6", CultureInfo.InvariantCulture)); }
            public void Val(bool v)   { BeforeValue(); _sb.Append(v ? "true" : "false"); }
            public void Null()        { BeforeValue(); _sb.Append("null"); }

            public void KV(string k, string v) { Key(k); Val(v); }
            public void KV(string k, int v)    { Key(k); Val(v); }
            public void KV(string k, float v)  { Key(k); Val(v); }
            public void KV(string k, bool v)   { Key(k); Val(v); }

            void WriteString(string s)
            {
                _sb.Append('"');
                foreach (char c in s)
                {
                    switch (c)
                    {
                        case '"':  _sb.Append("\\\""); break;
                        case '\\': _sb.Append("\\\\"); break;
                        case '\n': _sb.Append("\\n"); break;
                        case '\r': _sb.Append("\\r"); break;
                        case '\t': _sb.Append("\\t"); break;
                        default:   _sb.Append(c); break;
                    }
                }
                _sb.Append('"');
            }

            public override string ToString() => _sb.ToString();
        }
    }
}
