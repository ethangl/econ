using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using EconSim.Core.Common;
using EconSim.Core;
using EconSim.Core.Data;
using EconSim.Core.Economy;
using EconSim.Core.Simulation;
using EconSim.Core.Religious;
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
        static string _dumpScope = "all";

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
            _dumpScope = string.IsNullOrEmpty(p.scope) ? "all" : p.scope;

            if (_needsGenerate)
            {
                _pendingConfig = new MapGenConfig
                {
                    Seed = p.seed,
                    CellCount = p.cellCount,
                    Template = ParseTemplate(p.template),
                    Latitude = p.latitude != 0f ? p.latitude : 50f
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
            _dumpScope = string.IsNullOrEmpty(cmd.scope) ? "all" : cmd.scope;

            float latitude = cmd.latitude != 0f ? cmd.latitude : 50f;

            var pending = new PendingOperation
            {
                action = "generate_and_run",
                targetDay = 1 + months * 30,
                startDay = 1,
                seed = seed,
                cellCount = cellCount,
                template = cmd.template ?? "Continents",
                needsGenerate = true,
                scope = _dumpScope,
                latitude = latitude
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
            _dumpScope = string.IsNullOrEmpty(cmd.scope) ? "all" : cmd.scope;
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
                needsGenerate = false,
                scope = _dumpScope
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
                        DumpState(_dumpScope);
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

            if (scope == "all")
                WriteGoodsMetadata(j);

            if (scope == "all" || scope == "summary")
            {
                WriteSummary(j, st, mapData);
                WritePerformance(j, st);
            }
            if (scope == "all" || scope == "roads")
                WriteRoads(j, st);
            if (scope == "all" || scope == "economy" || scope == "v4")
                WriteEconomy(j, st);

            j.ObjClose();
            File.WriteAllText(OutputPath, j.ToString());
        }

        static void WriteGoodsMetadata(JW j)
        {
            j.Key("goods"); j.ArrOpen();
            for (int i = 0; i < Goods.Count; i++)
            {
                var d = Goods.Defs[i];
                j.ObjOpen();
                j.KV("index", i);
                j.KV("name", d.Name);
                j.KV("tier", d.Tier.ToString());
                j.KV("value", d.Value);
                j.KV("bulk", d.Bulk);
                j.ObjClose();
            }
            j.ArrClose();
        }

        static void WriteSummary(JW j, SimulationState st, MapData mapData)
        {
            j.Key("summary"); j.ObjOpen();

            // Use dynamic population from economy state (reflects births/deaths/migration)
            float totalPop = 0;
            if (st.Economy?.Counties != null)
            {
                foreach (var ce in st.Economy.Counties)
                    if (ce != null) totalPop += ce.TotalPopulation;
            }
            else if (mapData?.Cells != null)
            {
                foreach (var cell in mapData.Cells)
                    totalPop += cell.Population;
            }

            j.KV("totalPopulation", totalPop);
            j.KV("totalCounties", mapData?.Counties?.Count ?? 0);
            j.KV("totalProvinces", mapData?.Provinces?.Count ?? 0);
            j.KV("totalRealms", mapData?.Realms?.Count ?? 0);
            j.KV("marketCount", st.Economy?.MarketCount ?? 0);

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

        static void WriteEconomy(JW j, SimulationState st)
        {
            j.Key("economy"); j.ObjOpen();

            var econ = st.Economy;
            if (econ == null)
            {
                j.KV("initialized", false);
                j.ObjClose();
                return;
            }

            j.KV("initialized", true);
            j.KV("goodCount", Goods.Count);
            j.KV("facilityCount", Facilities.Count);
            j.KV("marketCount", econ.MarketCount);

            // Phase timing (last tick, ms)
            j.Key("phaseTiming"); j.ObjOpen();
            j.KV("generateOrders", econ.PhaseGenerateOrdersMs);
            j.KV("resolveMarkets", econ.PhaseResolveMarketsMs);
            j.KV("updateMoney", econ.PhaseUpdateMoneyMs);
            j.KV("updateSatisfaction", econ.PhaseUpdateSatisfactionMs);
            j.KV("updatePopulation", econ.PhaseUpdatePopulationMs);
            j.KV("total", econ.PhaseGenerateOrdersMs + econ.PhaseResolveMarketsMs +
                econ.PhaseUpdateMoneyMs + econ.PhaseUpdateSatisfactionMs + econ.PhaseUpdatePopulationMs);
            j.ObjClose();

            // Goods metadata
            j.Key("goods"); j.ArrOpen();
            for (int g = 0; g < Goods.Count; g++)
            {
                var d = Goods.Defs[g];
                j.ObjOpen();
                j.KV("index", g);
                j.KV("name", d.Name);
                j.KV("tier", d.Tier.ToString());
                j.KV("value", d.Value);
                j.KV("bulk", d.Bulk);
                j.ObjClose();
            }
            j.ArrClose();

            // Facilities metadata
            j.Key("facilities"); j.ArrOpen();
            for (int f = 0; f < Facilities.Count; f++)
            {
                var d = Facilities.Defs[f];
                j.ObjOpen();
                j.KV("name", d.Name);
                j.KV("output", Goods.Names[(int)d.Output]);
                j.KV("throughputPerCapita", d.ThroughputPerCapita);
                j.Key("inputs"); j.ArrOpen();
                foreach (var inp in d.Inputs)
                {
                    j.ObjOpen();
                    j.KV("good", Goods.Names[(int)inp.Good]);
                    j.KV("ratio", inp.Ratio);
                    j.ObjClose();
                }
                j.ArrClose();
                j.ObjClose();
            }
            j.ArrClose();

            // County summary
            int countyCount = 0;
            float totalPop = 0f;
            float totalM = 0f;
            float totalUpperNobleTreasury = 0f;
            float totalLowerNobleTreasury = 0f;
            float totalUpperClergyTreasury = 0f;
            int deficitCount = 0;
            float[] totalProduction = new float[Goods.Count];
            float[] totalConsumption = new float[Goods.Count];
            float[] totalSurplus = new float[Goods.Count];
            float satisfactionSum = 0f;
            float satisfactionMin = float.MaxValue;
            float satisfactionMax = float.MinValue;
            int satisfactionCount = 0;
            // Phase 2 aggregates
            float totalUpperNobleSpend = 0f;
            float totalUpperNobleIncome = 0f;
            float totalLowerNobleSpend = 0f;
            float totalSerfFoodProvided = 0f;
            float upperNobleSatSum = 0f;
            float lowerNobleSatSum = 0f;
            int nobleSatCount = 0;
            // Phase 3 aggregates
            float totalUpperCommonerCoin = 0f;
            float totalUpperCommonerIncome = 0f;
            float totalUpperCommonerSpend = 0f;
            float totalTaxRevenue = 0f;
            float totalTitheRevenue = 0f;
            float totalUpperClergySpend = 0f;
            float totalUpperClergyIncome = 0f;
            float totalLowerClergySpend = 0f;
            float totalLowerClergyIncome = 0f;
            float totalLowerClergyCoin = 0f;
            float totalTariffRevenue = 0f;
            float upperCommonerSatSum = 0f;
            float upperClergySatSum = 0f;
            float lowerClergySatSum = 0f;
            int upperCommonerSatCount = 0;
            int clergySatCount = 0;
            // Phase 5: population dynamics aggregates
            float totalBirths = 0f;
            float totalDeaths = 0f;
            float totalNetMigration = 0f;
            float totalLCPop = 0f, totalUCPop = 0f;
            float totalLNPop = 0f, totalUNPop = 0f;
            float totalLClPop = 0f, totalUClPop = 0f;
            // Phase 5: satisfaction breakdown aggregates
            float survivalSatSum = 0f, religionSatSum = 0f, economicSatSum = 0f;
            int breakdownCount = 0;
            int migGainCount = 0, migLoseCount = 0;
            // Dedicated satisfaction: per-class min/max/sum/count
            float lcSatMin = float.MaxValue, lcSatMax = float.MinValue, lcSatSum2 = 0f; int lcSatN = 0;
            float ucSatMin = float.MaxValue, ucSatMax = float.MinValue, ucSatSum2 = 0f; int ucSatN = 0;
            float lnSatMin = float.MaxValue, lnSatMax = float.MinValue, lnSatSum2 = 0f; int lnSatN = 0;
            float unSatMin = float.MaxValue, unSatMax = float.MinValue, unSatSum2 = 0f; int unSatN = 0;
            float lclSatMin = float.MaxValue, lclSatMax = float.MinValue, lclSatSum2 = 0f; int lclSatN = 0;
            float uclSatMin = float.MaxValue, uclSatMax = float.MinValue, uclSatSum2 = 0f; int uclSatN = 0;
            // Facility output tracking
            float[] facilityTotalOutput = new float[Facilities.Count];
            float[] facilityFillSum = new float[Facilities.Count];
            int[] facilityActiveCount = new int[Facilities.Count];

            foreach (var ce in econ.Counties)
            {
                if (ce == null) continue;
                countyCount++;
                totalPop += ce.TotalPopulation;
                totalM += ce.MoneySupply;
                totalUpperNobleTreasury += ce.UpperNobleTreasury;
                totalLowerNobleTreasury += ce.LowerNobleTreasury;
                totalUpperClergyTreasury += ce.UpperClergyTreasury;
                if (ce.FoodDeficit) deficitCount++;

                for (int g = 0; g < Goods.Count; g++)
                {
                    totalProduction[g] += ce.Production[g];
                    totalConsumption[g] += ce.Consumption[g];
                    totalSurplus[g] += ce.Surplus[g];
                }

                if (ce.LowerCommonerPop > 0f)
                {
                    satisfactionSum += ce.LowerCommonerSatisfaction;
                    if (ce.LowerCommonerSatisfaction < satisfactionMin)
                        satisfactionMin = ce.LowerCommonerSatisfaction;
                    if (ce.LowerCommonerSatisfaction > satisfactionMax)
                        satisfactionMax = ce.LowerCommonerSatisfaction;
                    satisfactionCount++;
                }

                // Phase 2 tracking
                totalUpperNobleSpend += ce.UpperNobleSpend;
                totalUpperNobleIncome += ce.UpperNobleIncome;
                totalLowerNobleSpend += ce.LowerNobleSpend;
                totalSerfFoodProvided += ce.SerfFoodProvided;
                if (ce.UpperNobilityPop > 0f)
                {
                    upperNobleSatSum += ce.UpperNobilitySatisfaction;
                    lowerNobleSatSum += ce.LowerNobilitySatisfaction;
                    nobleSatCount++;
                }

                // Phase 3 tracking
                totalUpperCommonerCoin += ce.UpperCommonerCoin;
                totalUpperCommonerIncome += ce.UpperCommonerIncome;
                totalUpperCommonerSpend += ce.UpperCommonerSpend;
                totalTaxRevenue += ce.TaxRevenue;
                totalTitheRevenue += ce.TitheRevenue;
                totalUpperClergySpend += ce.UpperClergySpend;
                totalUpperClergyIncome += ce.UpperClergyIncome;
                totalLowerClergySpend += ce.LowerClergySpend;
                totalLowerClergyIncome += ce.LowerClergyIncome;
                totalLowerClergyCoin += ce.LowerClergyCoin;
                totalTariffRevenue += ce.TariffRevenue;
                if (ce.UpperCommonerPop > 0f)
                {
                    upperCommonerSatSum += ce.UpperCommonerSatisfaction;
                    upperCommonerSatCount++;

                    // Per-facility output and fill rate
                    float popPerFac = ce.UpperCommonerPop / Facilities.Count;
                    for (int f = 0; f < Facilities.Count; f++)
                    {
                        var fac = Facilities.Defs[f];
                        float fillRate = 1.0f;
                        for (int inp = 0; inp < fac.Inputs.Length; inp++)
                        {
                            int inputGoodId = (int)fac.Inputs[inp].Good;
                            fillRate = System.Math.Min(fillRate, ce.FacilityInputGoodFill[inputGoodId]);
                        }
                        float output = popPerFac * fac.ThroughputPerCapita * fillRate;
                        facilityTotalOutput[f] += output;
                        facilityFillSum[f] += fillRate;
                        if (fillRate > 0.001f) facilityActiveCount[f]++;
                    }
                }
                if (ce.UpperClergyPop > 0f)
                {
                    upperClergySatSum += ce.UpperClergySatisfaction;
                    lowerClergySatSum += ce.LowerClergySatisfaction;
                    clergySatCount++;
                }

                // Dedicated per-class satisfaction tracking
                if (ce.LowerCommonerPop > 0f)
                {
                    float s = ce.LowerCommonerSatisfaction;
                    lcSatSum2 += s; lcSatN++;
                    if (s < lcSatMin) lcSatMin = s;
                    if (s > lcSatMax) lcSatMax = s;
                }
                if (ce.UpperCommonerPop > 0f)
                {
                    float s = ce.UpperCommonerSatisfaction;
                    ucSatSum2 += s; ucSatN++;
                    if (s < ucSatMin) ucSatMin = s;
                    if (s > ucSatMax) ucSatMax = s;
                }
                if (ce.LowerNobilityPop > 0f)
                {
                    float s = ce.LowerNobilitySatisfaction;
                    lnSatSum2 += s; lnSatN++;
                    if (s < lnSatMin) lnSatMin = s;
                    if (s > lnSatMax) lnSatMax = s;
                }
                if (ce.UpperNobilityPop > 0f)
                {
                    float s = ce.UpperNobilitySatisfaction;
                    unSatSum2 += s; unSatN++;
                    if (s < unSatMin) unSatMin = s;
                    if (s > unSatMax) unSatMax = s;
                }
                if (ce.LowerClergyPop > 0f)
                {
                    float s = ce.LowerClergySatisfaction;
                    lclSatSum2 += s; lclSatN++;
                    if (s < lclSatMin) lclSatMin = s;
                    if (s > lclSatMax) lclSatMax = s;
                }
                if (ce.UpperClergyPop > 0f)
                {
                    float s = ce.UpperClergySatisfaction;
                    uclSatSum2 += s; uclSatN++;
                    if (s < uclSatMin) uclSatMin = s;
                    if (s > uclSatMax) uclSatMax = s;
                }

                // Phase 5: population dynamics
                totalBirths += ce.Births;
                totalDeaths += ce.Deaths;
                totalNetMigration += System.Math.Abs(ce.NetMigration);
                totalLCPop += ce.LowerCommonerPop;
                totalUCPop += ce.UpperCommonerPop;
                totalLNPop += ce.LowerNobilityPop;
                totalUNPop += ce.UpperNobilityPop;
                totalLClPop += ce.LowerClergyPop;
                totalUClPop += ce.UpperClergyPop;
                survivalSatSum += ce.SurvivalSatisfaction;
                religionSatSum += ce.ReligionSatisfaction;
                economicSatSum += ce.EconomicSatisfaction;
                breakdownCount++;
                if (ce.NetMigration > 0.001f) migGainCount++;
                else if (ce.NetMigration < -0.001f) migLoseCount++;
            }
            j.KV("countyCount", countyCount);
            j.KV("totalPopulation", totalPop);
            j.KV("totalMoneySupply", totalM);
            j.KV("totalUpperNobleTreasury", totalUpperNobleTreasury);
            j.KV("totalLowerNobleTreasury", totalLowerNobleTreasury);
            j.KV("totalUpperClergyTreasury", totalUpperClergyTreasury);
            j.KV("foodDeficitCounties", deficitCount);

            // Phase 1: aggregate production/consumption/surplus
            j.Key("production"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                if (totalProduction[g] > 0f) j.KV(Goods.Names[g], totalProduction[g]);
            j.ObjClose();

            j.Key("consumption"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                if (totalConsumption[g] > 0f) j.KV(Goods.Names[g], totalConsumption[g]);
            j.ObjClose();

            j.Key("surplus"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                if (totalSurplus[g] != 0f) j.KV(Goods.Names[g], totalSurplus[g]);
            j.ObjClose();

            // Phase 1: survival satisfaction summary
            j.Key("survivalSatisfaction"); j.ObjOpen();
            j.KV("mean", satisfactionCount > 0 ? satisfactionSum / satisfactionCount : 0f);
            j.KV("min", satisfactionCount > 0 ? satisfactionMin : 0f);
            j.KV("max", satisfactionCount > 0 ? satisfactionMax : 0f);
            j.KV("counties", satisfactionCount);
            j.ObjClose();

            // Phase 2: coin flows
            j.Key("coinFlows"); j.ObjOpen();
            j.KV("totalCoinInSystem", totalUpperNobleTreasury + totalLowerNobleTreasury + totalUpperClergyTreasury + totalM);
            j.KV("totalUpperNobleSpend", totalUpperNobleSpend);
            j.KV("totalUpperNobleIncome", totalUpperNobleIncome);
            j.KV("totalLowerNobleSpend", totalLowerNobleSpend);
            j.KV("totalSerfFoodProvided", totalSerfFoodProvided);
            j.KV("totalTariffRevenue", totalTariffRevenue);
            j.ObjClose();

            // Phase 2: noble satisfaction
            j.Key("nobleSatisfaction"); j.ObjOpen();
            j.KV("upperNobleMean", nobleSatCount > 0 ? upperNobleSatSum / nobleSatCount : 0f);
            j.KV("lowerNobleMean", nobleSatCount > 0 ? lowerNobleSatSum / nobleSatCount : 0f);
            j.KV("counties", nobleSatCount);
            j.ObjClose();

            // Phase 3: upper commoner economy
            j.Key("upperCommonerEconomy"); j.ObjOpen();
            j.KV("totalCoin", totalUpperCommonerCoin);
            j.KV("totalIncome", totalUpperCommonerIncome);
            j.KV("totalSpend", totalUpperCommonerSpend);
            j.KV("taxRevenue", totalTaxRevenue);
            j.KV("titheRevenue", totalTitheRevenue);
            j.KV("satisfactionMean", upperCommonerSatCount > 0 ? upperCommonerSatSum / upperCommonerSatCount : 0f);
            j.KV("counties", upperCommonerSatCount);
            j.ObjClose();

            // Phase 3: clergy economy
            j.Key("clergyEconomy"); j.ObjOpen();
            j.KV("upperClergyTreasury", totalUpperClergyTreasury);
            j.KV("upperClergyIncome", totalUpperClergyIncome);
            j.KV("upperClergySpend", totalUpperClergySpend);
            j.KV("lowerClergyCoin", totalLowerClergyCoin);
            j.KV("lowerClergyIncome", totalLowerClergyIncome);
            j.KV("lowerClergySpend", totalLowerClergySpend);
            j.KV("upperClergySatMean", clergySatCount > 0 ? upperClergySatSum / clergySatCount : 0f);
            j.KV("lowerClergySatMean", clergySatCount > 0 ? lowerClergySatSum / clergySatCount : 0f);
            j.KV("counties", clergySatCount);
            j.ObjClose();

            // Phase 5: population dynamics
            j.Key("populationDynamics"); j.ObjOpen();
            j.KV("initialTotalPop", econ.InitialTotalPopulation);
            j.KV("currentTotalPop", totalPop);
            float growthPct = econ.InitialTotalPopulation > 0f
                ? (totalPop - econ.InitialTotalPopulation) / econ.InitialTotalPopulation * 100f : 0f;
            j.KV("growthPercent", growthPct);
            j.KV("dailyBirths", totalBirths);
            j.KV("dailyDeaths", totalDeaths);
            j.KV("dailyNetGrowth", totalBirths - totalDeaths);
            float annualRate = totalPop > 0f ? (totalBirths - totalDeaths) / totalPop * 360f * 100f : 0f;
            j.KV("annualGrowthRatePercent", annualRate);
            j.KV("dailyMigrationVolume", totalNetMigration);
            j.KV("countiesGaining", migGainCount);
            j.KV("countiesLosing", migLoseCount);
            j.Key("popByClass"); j.ObjOpen();
            j.KV("lowerCommoner", totalLCPop);
            j.KV("upperCommoner", totalUCPop);
            j.KV("lowerNobility", totalLNPop);
            j.KV("upperNobility", totalUNPop);
            j.KV("lowerClergy", totalLClPop);
            j.KV("upperClergy", totalUClPop);
            j.ObjClose();
            j.Key("satisfactionBreakdown"); j.ObjOpen();
            j.KV("survivalMean", breakdownCount > 0 ? survivalSatSum / breakdownCount : 0f);
            j.KV("religionMean", breakdownCount > 0 ? religionSatSum / breakdownCount : 0f);
            j.KV("economicMean", breakdownCount > 0 ? economicSatSum / breakdownCount : 0f);
            j.KV("stabilityPlaceholder", 1.0f);
            j.KV("governancePlaceholder", 0.7f);
            j.ObjClose();
            j.ObjClose();

            // Dedicated satisfaction section — per-class breakdown
            j.Key("satisfaction"); j.ObjOpen();
            WriteSatClass(j, "lowerCommoner", lcSatSum2, lcSatMin, lcSatMax, lcSatN);
            WriteSatClass(j, "upperCommoner", ucSatSum2, ucSatMin, ucSatMax, ucSatN);
            WriteSatClass(j, "lowerNobility", lnSatSum2, lnSatMin, lnSatMax, lnSatN);
            WriteSatClass(j, "upperNobility", unSatSum2, unSatMin, unSatMax, unSatN);
            WriteSatClass(j, "lowerClergy", lclSatSum2, lclSatMin, lclSatMax, lclSatN);
            WriteSatClass(j, "upperClergy", uclSatSum2, uclSatMin, uclSatMax, uclSatN);
            j.Key("components"); j.ObjOpen();
            j.KV("survivalMean", breakdownCount > 0 ? survivalSatSum / breakdownCount : 0f);
            j.KV("survivalWeight", 0.40f);
            j.KV("religionMean", breakdownCount > 0 ? religionSatSum / breakdownCount : 0f);
            j.KV("religionWeight", 0.25f);
            j.KV("stabilityMean", 1.0f);
            j.KV("stabilityWeight", 0.20f);
            j.KV("economicMean", breakdownCount > 0 ? economicSatSum / breakdownCount : 0f);
            j.KV("economicWeight", 0.10f);
            j.KV("governanceMean", 0.7f);
            j.KV("governanceWeight", 0.05f);
            j.ObjClose();
            j.ObjClose();

            // Phase 3: facility throughput
            j.Key("facilities_throughput"); j.ArrOpen();
            for (int f = 0; f < Facilities.Count; f++)
            {
                var fac = Facilities.Defs[f];
                j.ObjOpen();
                j.KV("name", fac.Name);
                j.KV("output", Goods.Names[(int)fac.Output]);
                j.KV("totalDailyOutput", facilityTotalOutput[f]);
                j.KV("meanFillRate", countyCount > 0 ? facilityFillSum[f] / countyCount : 0f);
                j.KV("activeCounties", facilityActiveCount[f]);
                j.ObjClose();
            }
            j.ArrClose();

            // Per-county top deficit/surplus (sample: worst 10 deficit + best 10 surplus)
            j.Key("countyDetails"); j.ArrOpen();
            // Collect county IDs sorted by satisfaction
            var countyIds = new System.Collections.Generic.List<int>();
            for (int ci = 0; ci < econ.Counties.Length; ci++)
                if (econ.Counties[ci] != null && econ.Counties[ci].LowerCommonerPop > 0f)
                    countyIds.Add(ci);
            countyIds.Sort((a, b) => econ.Counties[a].LowerCommonerSatisfaction.CompareTo(
                econ.Counties[b].LowerCommonerSatisfaction));

            // Worst 10 + best 10
            int sampleSize = System.Math.Min(10, countyIds.Count);
            var sampleIds = new System.Collections.Generic.HashSet<int>();
            for (int s = 0; s < sampleSize; s++) sampleIds.Add(countyIds[s]);
            for (int s = System.Math.Max(0, countyIds.Count - sampleSize); s < countyIds.Count; s++)
                sampleIds.Add(countyIds[s]);

            foreach (int ci in sampleIds)
            {
                var ce = econ.Counties[ci];
                j.ObjOpen();
                j.KV("countyId", ci);
                j.KV("lowerCommonerPop", ce.LowerCommonerPop);
                j.KV("satisfaction", ce.LowerCommonerSatisfaction);
                j.KV("foodDeficit", ce.FoodDeficit);
                j.KV("upperNobleTreasury", ce.UpperNobleTreasury);
                j.KV("lowerNobleTreasury", ce.LowerNobleTreasury);
                j.KV("upperNobleSpend", ce.UpperNobleSpend);
                j.KV("upperNobleIncome", ce.UpperNobleIncome);
                j.KV("serfFoodProvided", ce.SerfFoodProvided);
                j.KV("upperNobleSatisfaction", ce.UpperNobilitySatisfaction);
                j.KV("lowerNobleSatisfaction", ce.LowerNobilitySatisfaction);
                j.KV("upperCommonerCoin", ce.UpperCommonerCoin);
                j.KV("upperCommonerIncome", ce.UpperCommonerIncome);
                j.KV("upperCommonerSpend", ce.UpperCommonerSpend);
                j.KV("upperCommonerSatisfaction", ce.UpperCommonerSatisfaction);
                j.KV("upperClergyTreasury", ce.UpperClergyTreasury);
                j.KV("lowerClergyCoin", ce.LowerClergyCoin);
                j.KV("taxRevenue", ce.TaxRevenue);
                j.KV("titheRevenue", ce.TitheRevenue);
                j.KV("tariffRevenue", ce.TariffRevenue);
                j.KV("births", ce.Births);
                j.KV("deaths", ce.Deaths);
                j.KV("netMigration", ce.NetMigration);
                j.KV("survivalSat", ce.SurvivalSatisfaction);
                j.KV("religionSat", ce.ReligionSatisfaction);
                j.KV("economicSat", ce.EconomicSatisfaction);

                j.Key("production"); j.ObjOpen();
                for (int g = 0; g < Goods.Count; g++)
                    if (ce.Production[g] > 0f) j.KV(Goods.Names[g], ce.Production[g]);
                j.ObjClose();

                j.Key("surplus"); j.ObjOpen();
                for (int g = 0; g < Goods.Count; g++)
                    if (ce.Surplus[g] != 0f) j.KV(Goods.Names[g], ce.Surplus[g]);
                j.ObjClose();

                j.ObjClose();
            }
            j.ArrClose();

            // Trade flows: scan order books for Trade orders and aggregate per market pair × good
            j.Key("tradeFlows"); j.ArrOpen();
            {
                // Build a dictionary of trade flows: (srcMarket, dstMarket, good) → (postedVolume, filledVolume, value)
                var tradeDict = new System.Collections.Generic.Dictionary<(int, int, int), (float posted, float filled, float value)>();
                for (int m = 1; m <= econ.MarketCount; m++)
                {
                    var market = econ.Markets[m];
                    for (int i = 0; i < market.Orders.Count; i++)
                    {
                        var o = market.Orders[i];
                        if (o.Source != OrderSource.Trade) continue;

                        if (o.Side == OrderSide.Sell)
                        {
                            // Sell order in importing market m, SourceMarketId = exporting market
                            int srcMarket = o.SourceMarketId;
                            int dstMarket = m;
                            var key = (srcMarket, dstMarket, o.GoodId);
                            float val = o.FilledQuantity * market.ClearingPrice[o.GoodId];
                            if (tradeDict.TryGetValue(key, out var existing))
                                tradeDict[key] = (existing.posted + o.Quantity, existing.filled + o.FilledQuantity, existing.value + val);
                            else
                                tradeDict[key] = (o.Quantity, o.FilledQuantity, val);
                        }
                    }
                }

                float totalTradeVolume = 0f;
                float totalTradeValue = 0f;
                foreach (var kv in tradeDict)
                {
                    if (kv.Value.filled <= 0f) continue;
                    totalTradeVolume += kv.Value.filled;
                    totalTradeValue += kv.Value.value;
                    j.ObjOpen();
                    j.KV("from", kv.Key.Item1);
                    j.KV("to", kv.Key.Item2);
                    j.KV("good", Goods.Names[kv.Key.Item3]);
                    j.KV("posted", kv.Value.posted);
                    j.KV("filled", kv.Value.filled);
                    j.KV("value", kv.Value.value);
                    j.ObjClose();
                }
                j.ArrClose();
                j.KV("totalTradeVolume", totalTradeVolume);
                j.KV("totalTradeValue", totalTradeValue);
                j.KV("totalTariffRevenue", totalTariffRevenue);
            }

            // Per-market summary
            j.Key("markets"); j.ArrOpen();
            for (int m = 1; m <= econ.MarketCount; m++)
            {
                var market = econ.Markets[m];
                j.ObjOpen();
                j.KV("id", market.Id);
                j.KV("hubCountyId", market.HubCountyId);
                j.KV("hubRealmId", market.HubRealmId);
                j.KV("counties", market.CountyIds.Count);
                j.KV("priceLevel", market.PriceLevel);
                j.KV("totalM", market.TotalMoneySupply);
                j.KV("totalQ", market.TotalRealOutput);

                // Clearing prices (only non-zero)
                j.Key("clearingPrices"); j.ObjOpen();
                for (int g = 0; g < Goods.Count; g++)
                    if (market.ClearingPrice[g] > 0f)
                        j.KV(Goods.Names[g], market.ClearingPrice[g]);
                j.ObjClose();

                j.ObjClose();
            }
            j.ArrClose();

            j.ObjClose();
        }

        static void WriteSatClass(JW j, string name, float sum, float min, float max, int count)
        {
            j.Key(name); j.ObjOpen();
            j.KV("mean", count > 0 ? sum / count : 0f);
            j.KV("min", count > 0 ? min : 0f);
            j.KV("max", count > 0 ? max : 0f);
            j.KV("counties", count);
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
            public float latitude;
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
            public string scope;
            public float latitude;
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
