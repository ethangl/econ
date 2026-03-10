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
using EconSim.Core.Economy.V4;
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
            _dumpScope = string.IsNullOrEmpty(cmd.scope) ? "all" : cmd.scope;

            var pending = new PendingOperation
            {
                action = "generate_and_run",
                targetDay = 1 + months * 30,
                startDay = 1,
                seed = seed,
                cellCount = cellCount,
                template = cmd.template ?? "Continents",
                needsGenerate = true,
                scope = _dumpScope
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
            SetSnapshotCapture(enabled: true, resetSeries: true);
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
            SetSnapshotCapture(enabled: false, resetSeries: false);
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
            SetSnapshotCapture(enabled: false, resetSeries: false);
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
            if (scope == "all" || scope == "economy")
                WriteEconomy(j, st);
            if (scope == "all" || scope == "trade")
                WriteTrade(j, st);
            if (scope == "all" || scope == "facilities")
                WriteFacilities(j, st);
            if (scope == "all" || scope == "v4")
                WriteEconomyV4(j, st);

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
                j.KV("category", d.Category.ToString());
                j.KV("need", d.Need.ToString());
                j.KV("consumptionPerPop", d.ConsumptionPerPop);
                j.KV("basePrice", d.BasePrice);
                j.KV("isTradeable", d.IsTradeable);
                j.KV("isPreciousMetal", d.IsPreciousMetal);
                j.KV("spoilageRate", d.SpoilageRate);
                j.KV("unitWeight", d.UnitWeight);
                j.KV("targetStockPerPop", d.TargetStockPerPop);
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
                    if (ce != null) totalPop += ce.Population;
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
            j.KV("marketCount", st.Economy?.Markets != null ? st.Economy.Markets.Length - 1 : 0);

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
            if (econ == null || econ.Counties == null)
            {
                j.KV("countyCount", 0);
                j.ObjClose();
                return;
            }

            // Init stats
            int countyCount = 0;
            float[] sumProd = new float[Goods.Count];
            float[] minProd = new float[Goods.Count];
            float[] maxProd = new float[Goods.Count];
            for (int g = 0; g < Goods.Count; g++)
            {
                minProd[g] = float.MaxValue;
                maxProd[g] = float.MinValue;
            }
            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;
                countyCount++;
                for (int g = 0; g < Goods.Count; g++)
                {
                    sumProd[g] += ce.Productivity[g];
                    if (ce.Productivity[g] < minProd[g]) minProd[g] = ce.Productivity[g];
                    if (ce.Productivity[g] > maxProd[g]) maxProd[g] = ce.Productivity[g];
                }
            }

            j.KV("countyCount", countyCount);
            // Backward compat: food productivity
            j.KV("avgProductivity", countyCount > 0 ? sumProd[0] / countyCount : 0f);
            j.KV("minProductivity", countyCount > 0 ? minProd[0] : 0f);
            j.KV("maxProductivity", countyCount > 0 ? maxProd[0] : 0f);

            // Per-good productivity
            string[] goodNames = Goods.Names;
            j.Key("productivityByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
            {
                j.Key(goodNames[g]); j.ObjOpen();
                j.KV("avg", countyCount > 0 ? sumProd[g] / countyCount : 0f);
                j.KV("min", countyCount > 0 ? minProd[g] : 0f);
                j.KV("max", countyCount > 0 ? maxProd[g] : 0f);
                j.ObjClose();
            }
            j.ObjClose();

            // Time series
            j.Key("timeSeries"); j.ArrOpen();
            foreach (var snap in econ.TimeSeries)
            {
                j.ObjOpen();
                j.KV("day", snap.Day);
                j.KV("totalStock", snap.TotalStock);
                j.KV("totalProduction", snap.TotalProduction);
                j.KV("totalConsumption", snap.TotalConsumption);
                j.KV("totalUnmetNeed", snap.TotalUnmetNeed);
                j.KV("surplusCounties", snap.SurplusCounties);
                j.KV("deficitCounties", snap.DeficitCounties);
                j.KV("shortfallCounties", snap.ShortfallCounties);
                j.KV("minStock", snap.MinStock);
                j.KV("maxStock", snap.MaxStock);
                j.KV("medianProductivity", snap.MedianProductivity);
                j.KV("ducalRelief", snap.TotalDucalRelief);
                j.KV("provincialStockpile", snap.TotalProvincialStockpile);
                j.KV("royalStockpile", snap.TotalRoyalStockpile);
                j.KV("monetaryTaxToProvince", snap.TotalMonetaryTaxToProvince);
                j.KV("monetaryTaxToRealm", snap.TotalMonetaryTaxToRealm);
                j.KV("provinceAdminCost", snap.TotalProvinceAdminCost);
                j.KV("realmAdminCost", snap.TotalRealmAdminCost);
                j.KV("granaryRequisitionCrowns", snap.TotalGranaryRequisitionCrowns);
                j.KV("treasury", snap.TotalTreasury);
                j.KV("countyTreasury", snap.TotalCountyTreasury);
                j.KV("provinceTreasury", snap.TotalProvinceTreasury);
                j.KV("domesticTreasury", snap.TotalDomesticTreasury);
                j.KV("goldMinted", snap.TotalGoldMinted);
                j.KV("silverMinted", snap.TotalSilverMinted);
                j.KV("crownsMinted", snap.TotalCrownsMinted);
                j.KV("tradeSpending", snap.TotalTradeSpending);
                j.KV("tradeRevenue", snap.TotalTradeRevenue);
                j.KV("intraProvTradeSpending", snap.TotalIntraProvTradeSpending);
                j.KV("intraProvTradeRevenue", snap.TotalIntraProvTradeRevenue);
                j.KV("crossProvTradeSpending", snap.TotalCrossProvTradeSpending);
                j.KV("crossProvTradeRevenue", snap.TotalCrossProvTradeRevenue);
                j.KV("tradeTollsPaid", snap.TotalTradeTollsPaid);
                j.KV("tradeTollsCollected", snap.TotalTradeTollsCollected);
                j.KV("crossMarketTradeSpending", snap.TotalCrossMarketTradeSpending);
                j.KV("crossMarketTradeRevenue", snap.TotalCrossMarketTradeRevenue);
                j.KV("crossMarketTollsPaid", snap.TotalCrossMarketTollsPaid);
                j.KV("crossMarketTariffsPaid", snap.TotalCrossMarketTariffsPaid);
                j.KV("crossMarketTariffsCollected", snap.TotalCrossMarketTariffsCollected);
                j.KV("marketFeesCollected", snap.TotalMarketFeesCollected);
                j.KV("transportCostsPaid", snap.TotalTransportCostsPaid);

                // Virtual market
                j.KV("vmImportSpending", snap.TotalVMImportSpending);
                j.KV("vmExportRevenue", snap.TotalVMExportRevenue);
                j.KV("vmTariffsPaid", snap.TotalVMTariffsPaid);

                // Population dynamics
                j.KV("totalPopulation", snap.TotalPopulation);
                j.KV("totalBirths", snap.TotalBirths);
                j.KV("totalDeaths", snap.TotalDeaths);
                j.KV("avgBasicSatisfaction", snap.AvgBasicSatisfaction);
                j.KV("minBasicSatisfaction", snap.MinBasicSatisfaction);
                j.KV("maxBasicSatisfaction", snap.MaxBasicSatisfaction);
                j.KV("avgSatisfaction", snap.AvgSatisfaction);
                j.KV("minSatisfaction", snap.MinSatisfaction);
                j.KV("maxSatisfaction", snap.MaxSatisfaction);
                j.KV("countiesInDistress", snap.CountiesInDistress);

                if (snap.MarketPrices != null)
                {
                    j.Key("marketPrices"); j.ArrOpen();
                    for (int g = 0; g < snap.MarketPrices.Length; g++)
                        j.Val(snap.MarketPrices[g]);
                    j.ArrClose();
                }
                if (snap.PerMarketPrices != null)
                {
                    j.Key("perMarketPrices"); j.ObjOpen();
                    for (int m = 1; m < snap.PerMarketPrices.Length; m++)
                    {
                        if (snap.PerMarketPrices[m] == null) continue;
                        j.Key(m.ToString()); j.ArrOpen();
                        for (int g = 0; g < snap.PerMarketPrices[m].Length; g++)
                            j.Val(snap.PerMarketPrices[m][g]);
                        j.ArrClose();
                    }
                    j.ObjClose();
                }
                if (snap.TotalTradeImportsByGood != null)
                {
                    j.Key("tradeImportsByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalTradeImportsByGood.Length; g++)
                        j.Val(snap.TotalTradeImportsByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalTradeExportsByGood != null)
                {
                    j.Key("tradeExportsByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalTradeExportsByGood.Length; g++)
                        j.Val(snap.TotalTradeExportsByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalRealmDeficitByGood != null)
                {
                    j.Key("realmDeficitByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalRealmDeficitByGood.Length; g++)
                        j.Val(snap.TotalRealmDeficitByGood[g]);
                    j.ArrClose();
                }

                if (snap.TotalDucalReliefByGood != null)
                {
                    j.Key("ducalReliefByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalDucalReliefByGood.Length; g++)
                        j.Val(snap.TotalDucalReliefByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalGranaryRequisitionedByGood != null)
                {
                    j.Key("granaryRequisitionedByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalGranaryRequisitionedByGood.Length; g++)
                        j.Val(snap.TotalGranaryRequisitionedByGood[g]);
                    j.ArrClose();
                }

                if (snap.TotalIntraProvTradeBoughtByGood != null)
                {
                    j.Key("intraProvTradeBoughtByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalIntraProvTradeBoughtByGood.Length; g++)
                        j.Val(snap.TotalIntraProvTradeBoughtByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalIntraProvTradeSoldByGood != null)
                {
                    j.Key("intraProvTradeSoldByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalIntraProvTradeSoldByGood.Length; g++)
                        j.Val(snap.TotalIntraProvTradeSoldByGood[g]);
                    j.ArrClose();
                }

                if (snap.TotalCrossProvTradeBoughtByGood != null)
                {
                    j.Key("crossProvTradeBoughtByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalCrossProvTradeBoughtByGood.Length; g++)
                        j.Val(snap.TotalCrossProvTradeBoughtByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalCrossProvTradeSoldByGood != null)
                {
                    j.Key("crossProvTradeSoldByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalCrossProvTradeSoldByGood.Length; g++)
                        j.Val(snap.TotalCrossProvTradeSoldByGood[g]);
                    j.ArrClose();
                }

                if (snap.TotalCrossMarketTradeBoughtByGood != null)
                {
                    j.Key("crossMarketTradeBoughtByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalCrossMarketTradeBoughtByGood.Length; g++)
                        j.Val(snap.TotalCrossMarketTradeBoughtByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalCrossMarketTradeSoldByGood != null)
                {
                    j.Key("crossMarketTradeSoldByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalCrossMarketTradeSoldByGood.Length; g++)
                        j.Val(snap.TotalCrossMarketTradeSoldByGood[g]);
                    j.ArrClose();
                }

                if (snap.TotalVMImportedByGood != null)
                {
                    j.Key("vmImportedByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalVMImportedByGood.Length; g++)
                        j.Val(snap.TotalVMImportedByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalVMExportedByGood != null)
                {
                    j.Key("vmExportedByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalVMExportedByGood.Length; g++)
                        j.Val(snap.TotalVMExportedByGood[g]);
                    j.ArrClose();
                }
                if (snap.VMStock != null)
                {
                    j.Key("vmStockByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.VMStock.Length; g++)
                        j.Val(snap.VMStock[g]);
                    j.ArrClose();
                }
                if (snap.VMSellPrice != null)
                {
                    j.Key("vmSellPriceByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.VMSellPrice.Length; g++)
                        j.Val(snap.VMSellPrice[g]);
                    j.ArrClose();
                }

                // Per-good breakdowns
                if (snap.TotalStockByGood != null)
                {
                    j.Key("stockByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalStockByGood.Length; g++)
                        j.Val(snap.TotalStockByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalProductionByGood != null)
                {
                    j.Key("productionByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalProductionByGood.Length; g++)
                        j.Val(snap.TotalProductionByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalConsumptionByGood != null)
                {
                    j.Key("consumptionByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalConsumptionByGood.Length; g++)
                        j.Val(snap.TotalConsumptionByGood[g]);
                    j.ArrClose();
                }
                if (snap.TotalUnmetNeedByGood != null)
                {
                    j.Key("unmetNeedByGood"); j.ArrOpen();
                    for (int g = 0; g < snap.TotalUnmetNeedByGood.Length; g++)
                        j.Val(snap.TotalUnmetNeedByGood[g]);
                    j.ArrClose();
                }

                j.ObjClose();
            }
            j.ArrClose();

            j.ObjClose();
        }

        static void WriteTrade(JW j, SimulationState st)
        {
            j.Key("trade"); j.ObjOpen();

            var econ = st.Economy;
            if (econ == null || econ.Provinces == null)
            {
                j.KV("provinceCount", 0);
                j.ObjClose();
                return;
            }

            string[] goodNames = Goods.Names;
            const int F = (int)GoodType.Wheat;

            // County-level stats
            int countyCount = 0;
            float[] totalRelief = new float[Goods.Count];
            float[] totalGranaryReq = new float[Goods.Count];
            float totalMonetaryTax = 0f;
            float totalGranaryReqCrowns = 0f;
            int reliefReceivingCounties = 0;
            float totalCountyTreasury = 0f;

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce = econ.Counties[i];
                if (ce == null) continue;
                countyCount++;
                for (int g = 0; g < Goods.Count; g++)
                {
                    totalRelief[g] += ce.Relief[g];
                    totalGranaryReq[g] += ce.GranaryRequisitioned[g];
                }
                totalMonetaryTax += ce.MonetaryTaxPaid;
                totalGranaryReqCrowns += ce.GranaryRequisitionCrownsReceived;
                if (ce.Relief[F] > 0f) reliefReceivingCounties++;
                totalCountyTreasury += ce.Treasury;
            }

            // Intra-province trade aggregates
            float[] totalTradeBought = new float[Goods.Count];
            float[] totalTradeSold = new float[Goods.Count];
            float totalTradeSpending = 0f, totalTradeRevenue = 0f;

            // Cross-province trade aggregates
            float[] totalCPTradeBought = new float[Goods.Count];
            float[] totalCPTradeSold = new float[Goods.Count];
            float totalCPTradeSpending = 0f, totalCPTradeRevenue = 0f;
            float totalTollsPaid = 0f;

            // Cross-realm trade aggregates
            float[] totalCRTradeBought = new float[Goods.Count];
            float[] totalCRTradeSold = new float[Goods.Count];
            float totalCRTradeSpending = 0f, totalCRTradeRevenue = 0f;
            float totalCRTollsPaid = 0f, totalCRTariffsPaid = 0f;

            // Virtual market aggregates
            float[] totalVMBought = new float[Goods.Count];
            float[] totalVMSold = new float[Goods.Count];
            float totalVMSpending = 0f, totalVMRevenue = 0f;
            float totalVMTariffs = 0f;

            // Market fee aggregates
            float totalMarketFeesCollected = 0f;
            float totalTransportCostsPaid = 0f;

            for (int i = 0; i < econ.Counties.Length; i++)
            {
                var ce2 = econ.Counties[i];
                if (ce2 == null) continue;
                for (int g = 0; g < Goods.Count; g++)
                {
                    totalTradeBought[g] += ce2.TradeBought[g];
                    totalTradeSold[g] += ce2.TradeSold[g];
                    totalCPTradeBought[g] += ce2.CrossProvTradeBought[g];
                    totalCPTradeSold[g] += ce2.CrossProvTradeSold[g];
                    totalCRTradeBought[g] += ce2.CrossMarketTradeBought[g];
                    totalCRTradeSold[g] += ce2.CrossMarketTradeSold[g];
                    totalVMBought[g] += ce2.VirtualMarketBought[g];
                    totalVMSold[g] += ce2.VirtualMarketSold[g];
                }
                totalTradeSpending += ce2.TradeCrownsSpent;
                totalTradeRevenue += ce2.TradeCrownsEarned;
                totalCPTradeSpending += ce2.CrossProvTradeCrownsSpent;
                totalCPTradeRevenue += ce2.CrossProvTradeCrownsEarned;
                totalTollsPaid += ce2.TradeTollsPaid;
                totalCRTradeSpending += ce2.CrossMarketTradeCrownsSpent;
                totalCRTradeRevenue += ce2.CrossMarketTradeCrownsEarned;
                totalCRTollsPaid += ce2.CrossMarketTollsPaid;
                totalCRTariffsPaid += ce2.CrossMarketTariffsPaid;
                totalMarketFeesCollected += ce2.MarketFeesReceived;
                totalTransportCostsPaid += ce2.TransportCostsPaid;
                totalVMSpending += ce2.VirtualMarketCrownsSpent;
                totalVMRevenue += ce2.VirtualMarketCrownsEarned;
                totalVMTariffs += ce2.VirtualMarketTariffsPaid;
            }

            j.KV("countyCount", countyCount);
            j.KV("totalDucalRelief", totalRelief[F]);
            j.KV("reliefReceivingCounties", reliefReceivingCounties);
            j.KV("totalMonetaryTaxToProvince", totalMonetaryTax);
            j.KV("totalGranaryRequisitionCrowns", totalGranaryReqCrowns);

            // Per-good county relief + granary
            j.Key("ducalReliefByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalRelief[g]);
            j.ObjClose();
            j.Key("granaryRequisitionedByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalGranaryReq[g]);
            j.ObjClose();

            // Intra-province trade per-good
            j.Key("intraProvTradeBoughtByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalTradeBought[g]);
            j.ObjClose();
            j.Key("intraProvTradeSoldByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalTradeSold[g]);
            j.ObjClose();
            j.KV("intraProvTradeSpending", totalTradeSpending);
            j.KV("intraProvTradeRevenue", totalTradeRevenue);

            // Cross-province trade per-good
            j.Key("crossProvTradeBoughtByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalCPTradeBought[g]);
            j.ObjClose();
            j.Key("crossProvTradeSoldByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalCPTradeSold[g]);
            j.ObjClose();
            j.KV("crossProvTradeSpending", totalCPTradeSpending);
            j.KV("crossProvTradeRevenue", totalCPTradeRevenue);
            j.KV("tradeTollsPaid", totalTollsPaid);

            // Province toll revenue
            float totalTollsCollected = 0f;
            for (int i = 0; i < econ.Provinces.Length; i++)
            {
                var pe2 = econ.Provinces[i];
                if (pe2 != null) totalTollsCollected += pe2.TradeTollsCollected;
            }
            j.KV("tradeTollsCollected", totalTollsCollected);

            // Cross-market trade per-good
            j.Key("crossMarketTradeBoughtByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalCRTradeBought[g]);
            j.ObjClose();
            j.Key("crossMarketTradeSoldByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalCRTradeSold[g]);
            j.ObjClose();
            j.KV("crossMarketTradeSpending", totalCRTradeSpending);
            j.KV("crossMarketTradeRevenue", totalCRTradeRevenue);
            j.KV("crossMarketTollsPaid", totalCRTollsPaid);
            j.KV("crossMarketTariffsPaid", totalCRTariffsPaid);

            // Realm tariff revenue
            float totalTariffsCollected = 0f;
            if (econ.Realms != null)
            {
                for (int i = 0; i < econ.Realms.Length; i++)
                {
                    var re2 = econ.Realms[i];
                    if (re2 != null) totalTariffsCollected += re2.TradeTariffsCollected;
                }
            }
            j.KV("tradeTariffsCollected", totalTariffsCollected);
            j.KV("marketFeesCollected", totalMarketFeesCollected);
            j.KV("totalTransportCostsPaid", totalTransportCostsPaid);
            j.KV("marketCount", econ.Markets != null ? econ.Markets.Length - 1 : 0);

            // Virtual overseas market
            j.Key("virtualMarket"); j.ObjOpen();
            var vm = econ.VirtualMarket;
            if (vm != null)
            {
                j.KV("enabled", true);
                j.KV("overseasSurcharge", vm.OverseasSurcharge);
                j.Key("tradedGoods"); j.ArrOpen();
                foreach (int g in vm.TradedGoods)
                    j.Val(goodNames[g]);
                j.ArrClose();

                j.Key("stockByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], vm.Stock[g]);
                j.ObjClose();
                j.Key("sellPriceByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], vm.SellPrice[g]);
                j.ObjClose();
                j.Key("buyPriceByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], vm.BuyPrice[g]);
                j.ObjClose();
                j.Key("targetStockByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], vm.TargetStock[g]);
                j.ObjClose();
                j.Key("replenishRateByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], vm.ReplenishRate[g]);
                j.ObjClose();
                j.Key("maxStockByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], vm.MaxStock[g]);
                j.ObjClose();

                // County-level VM aggregates
                j.Key("importedByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], totalVMBought[g]);
                j.ObjClose();
                j.Key("exportedByGood"); j.ObjOpen();
                foreach (int g in vm.TradedGoods)
                    j.KV(goodNames[g], totalVMSold[g]);
                j.ObjClose();
                j.KV("importSpending", totalVMSpending);
                j.KV("exportRevenue", totalVMRevenue);
                j.KV("tariffsPaid", totalVMTariffs);

                // Port cost distribution
                int reachable = 0;
                float minPort = float.MaxValue, maxPort = 0f, sumPort = 0f;
                for (int i = 0; i < vm.CountyPortCost.Length; i++)
                {
                    float cost = vm.CountyPortCost[i];
                    if (cost < float.MaxValue / 2)
                    {
                        reachable++;
                        if (cost < minPort) minPort = cost;
                        if (cost > maxPort) maxPort = cost;
                        sumPort += cost;
                    }
                }
                j.KV("reachableCounties", reachable);
                j.KV("minPortCost", reachable > 0 ? minPort : 0f);
                j.KV("maxPortCost", maxPort);
                j.KV("avgPortCost", reachable > 0 ? sumPort / reachable : 0f);
            }
            else
            {
                j.KV("enabled", false);
            }
            j.ObjClose();

            // Per-province summary (granary + monetary)
            j.Key("provinces"); j.ArrOpen();
            float[] totalProvStockpile = new float[Goods.Count];
            float totalProvinceTreasury = 0f;
            float totalProvMonetaryTaxCollected = 0f;
            float totalProvMonetaryTaxToRealm = 0f;
            float totalProvAdminCost = 0f;
            float totalProvGranarySpent = 0f;
            for (int i = 0; i < econ.Provinces.Length; i++)
            {
                var pe = econ.Provinces[i];
                if (pe == null) continue;
                for (int g = 0; g < Goods.Count; g++)
                    totalProvStockpile[g] += pe.Stockpile[g];
                totalProvinceTreasury += pe.Treasury;
                totalProvMonetaryTaxCollected += pe.MonetaryTaxCollected;
                totalProvMonetaryTaxToRealm += pe.MonetaryTaxPaidToRealm;
                totalProvAdminCost += pe.AdminCrownsCost;
                totalProvGranarySpent += pe.GranaryRequisitionCrownsSpent;

                j.ObjOpen();
                j.KV("id", i);
                j.KV("treasury", pe.Treasury);
                j.KV("monetaryTaxCollected", pe.MonetaryTaxCollected);
                j.KV("monetaryTaxPaidToRealm", pe.MonetaryTaxPaidToRealm);
                j.KV("adminCrownsCost", pe.AdminCrownsCost);
                j.KV("granaryRequisitionCrownsSpent", pe.GranaryRequisitionCrownsSpent);
                j.Key("granaryByGood"); j.ObjOpen();
                for (int g = 0; g < Goods.Count; g++)
                    j.KV(goodNames[g], pe.Stockpile[g]);
                j.ObjClose();
                j.ObjClose();
            }
            j.ArrClose();
            j.KV("totalProvincialStockpile", totalProvStockpile[F]);
            j.Key("provincialStockpileByGood"); j.ObjOpen();
            for (int g = 0; g < Goods.Count; g++)
                j.KV(goodNames[g], totalProvStockpile[g]);
            j.ObjClose();
            j.KV("totalProvMonetaryTaxCollected", totalProvMonetaryTaxCollected);
            j.KV("totalProvMonetaryTaxToRealm", totalProvMonetaryTaxToRealm);
            j.KV("totalProvAdminCost", totalProvAdminCost);
            j.KV("totalProvGranarySpent", totalProvGranarySpent);

            // Per-realm summary (per-good stockpiles)
            if (econ.Realms != null)
            {
                j.Key("realms"); j.ArrOpen();
                float[] totalRealmStockpile = new float[Goods.Count];
                float totalTreasury = 0f;
                for (int i = 0; i < econ.Realms.Length; i++)
                {
                    var re = econ.Realms[i];
                    if (re == null) continue;
                    for (int g = 0; g < Goods.Count; g++)
                        totalRealmStockpile[g] += re.Stockpile[g];
                    totalTreasury += re.Treasury;

                    j.ObjOpen();
                    j.KV("id", i);
                    j.KV("stockpile", re.Stockpile[F]);
                    j.Key("stockpileByGood"); j.ObjOpen();
                    for (int g = 0; g < Goods.Count; g++)
                        j.KV(goodNames[g], re.Stockpile[g]);
                    j.ObjClose();
                    j.KV("treasury", re.Treasury);
                    j.KV("goldMinted", re.GoldMinted);
                    j.KV("silverMinted", re.SilverMinted);
                    j.KV("crownsMinted", re.CrownsMinted);
                    j.KV("monetaryTaxCollected", re.MonetaryTaxCollected);
                    j.KV("adminCrownsCost", re.AdminCrownsCost);
                    j.KV("tradeSpending", re.TradeSpending);
                    j.KV("tradeRevenue", re.TradeRevenue);
                    j.KV("tradeTariffsCollected", re.TradeTariffsCollected);
                    j.Key("tradeImportsByGood"); j.ObjOpen();
                    for (int g = 0; g < Goods.Count; g++)
                        j.KV(goodNames[g], re.TradeImports[g]);
                    j.ObjClose();
                    j.Key("tradeExportsByGood"); j.ObjOpen();
                    for (int g = 0; g < Goods.Count; g++)
                        j.KV(goodNames[g], re.TradeExports[g]);
                    j.ObjClose();
                    j.Key("deficitByGood"); j.ObjOpen();
                    for (int g = 0; g < Goods.Count; g++)
                        j.KV(goodNames[g], re.Deficit[g]);
                    j.ObjClose();
                    j.ObjClose();
                }
                j.ArrClose();
                j.KV("totalRoyalStockpile", totalRealmStockpile[F]);
                j.Key("royalStockpileByGood"); j.ObjOpen();
                for (int g = 0; g < Goods.Count; g++)
                    j.KV(goodNames[g], totalRealmStockpile[g]);
                j.ObjClose();
                j.KV("totalTreasury", totalTreasury);
                j.KV("totalCountyTreasury", totalCountyTreasury);
                j.KV("totalProvinceTreasury", totalProvinceTreasury);
                j.KV("totalDomesticTreasury", totalCountyTreasury + totalProvinceTreasury + totalTreasury);
            }

            // Religious tithes
            var religion = st.Religion;
            if (religion?.Parishes != null)
            {
                j.Key("tithes"); j.ObjOpen();

                float totalTithePaid = 0f;
                float totalParishTreasury = 0f;
                float totalDioceseTreasury = 0f;
                float totalArchdioceseTreasury = 0f;
                int parishCount = 0;
                int dioceseCount = 0;
                int archdioceseCount = 0;

                for (int p = 1; p < religion.Parishes.Length; p++)
                {
                    var parish = religion.Parishes[p];
                    if (parish == null) continue;
                    parishCount++;
                    totalParishTreasury += parish.Treasury;
                }

                if (religion.Dioceses != null)
                {
                    for (int d = 1; d < religion.Dioceses.Length; d++)
                    {
                        var diocese = religion.Dioceses[d];
                        if (diocese == null) continue;
                        dioceseCount++;
                        totalDioceseTreasury += diocese.Treasury;
                    }
                }

                if (religion.Archdioceses != null)
                {
                    for (int a = 1; a < religion.Archdioceses.Length; a++)
                    {
                        var arch = religion.Archdioceses[a];
                        if (arch == null) continue;
                        archdioceseCount++;
                        totalArchdioceseTreasury += arch.Treasury;
                    }
                }

                // Sum county tithe payments
                if (econ.Counties != null)
                {
                    for (int i = 0; i < econ.Counties.Length; i++)
                    {
                        var ce = econ.Counties[i];
                        if (ce != null) totalTithePaid += ce.TithePaid;
                    }
                }

                j.KV("totalTithePaid", totalTithePaid);
                j.KV("totalParishTreasury", totalParishTreasury);
                j.KV("totalDioceseTreasury", totalDioceseTreasury);
                j.KV("totalArchdioceseTreasury", totalArchdioceseTreasury);
                j.KV("totalChurchTreasury", totalParishTreasury + totalDioceseTreasury + totalArchdioceseTreasury);
                j.KV("parishCount", parishCount);
                j.KV("dioceseCount", dioceseCount);
                j.KV("archdioceseCount", archdioceseCount);
                j.KV("faithCount", religion.FaithCount);

                // Per-faith breakdown
                j.Key("perFaith"); j.ArrOpen();
                for (int f = 0; f < religion.FaithCount; f++)
                {
                    j.ObjOpen();
                    j.KV("faithIndex", f);
                    j.KV("religionId", f < religion.FaithIndexToReligion.Length ? religion.FaithIndexToReligion[f] : -1);

                    float faithParishTreasury = 0f;
                    int faithParishes = 0;
                    for (int p = 1; p < religion.Parishes.Length; p++)
                    {
                        var parish = religion.Parishes[p];
                        if (parish == null || parish.FaithIndex != f) continue;
                        faithParishes++;
                        faithParishTreasury += parish.Treasury;
                    }

                    float faithDioceseTreasury = 0f;
                    int faithDioceses = 0;
                    if (religion.Dioceses != null)
                    {
                        for (int d = 1; d < religion.Dioceses.Length; d++)
                        {
                            var diocese = religion.Dioceses[d];
                            if (diocese == null || diocese.FaithIndex != f) continue;
                            faithDioceses++;
                            faithDioceseTreasury += diocese.Treasury;
                        }
                    }

                    float faithArchTreasury = 0f;
                    int faithArchs = 0;
                    if (religion.Archdioceses != null)
                    {
                        for (int a = 1; a < religion.Archdioceses.Length; a++)
                        {
                            var arch = religion.Archdioceses[a];
                            if (arch == null || arch.FaithIndex != f) continue;
                            faithArchs++;
                            faithArchTreasury += arch.Treasury;
                        }
                    }

                    j.KV("parishes", faithParishes);
                    j.KV("dioceses", faithDioceses);
                    j.KV("archdioceses", faithArchs);
                    j.KV("parishTreasury", faithParishTreasury);
                    j.KV("dioceseTreasury", faithDioceseTreasury);
                    j.KV("archdioceseTreasury", faithArchTreasury);
                    j.KV("totalTreasury", faithParishTreasury + faithDioceseTreasury + faithArchTreasury);
                    j.ObjClose();
                }
                j.ArrClose();

                j.ObjClose();
            }

            j.ObjClose();
        }

        static void WriteFacilities(JW j, SimulationState st)
        {
            j.Key("facilities"); j.ObjOpen();

            var econ = st.Economy;
            if (econ?.Facilities == null || econ.Facilities.Length == 0)
            {
                j.KV("totalCount", 0);
                j.ObjClose();
                return;
            }

            j.KV("totalCount", econ.Facilities.Length);

            // Aggregate by type
            float totalWorkers = 0f;
            var countByType = new Dictionary<int, int>();
            var throughputByType = new Dictionary<int, float>();
            var workersByType = new Dictionary<int, float>();
            foreach (var fac in econ.Facilities)
            {
                totalWorkers += fac.Workforce;
                int t = (int)fac.Type;
                countByType.TryGetValue(t, out int c);
                countByType[t] = c + 1;
                throughputByType.TryGetValue(t, out float tp);
                throughputByType[t] = tp + fac.Throughput;
                workersByType.TryGetValue(t, out float w);
                workersByType[t] = w + fac.Workforce;
            }

            j.KV("totalWorkers", totalWorkers);
            j.Key("byType"); j.ObjOpen();
            foreach (var kv in countByType)
            {
                var def = Facilities.Defs[kv.Key];
                j.Key(def.Name); j.ObjOpen();
                j.KV("count", kv.Value);
                j.Key("inputs"); j.ArrOpen();
                for (int ii = 0; ii < def.Inputs.Length; ii++)
                {
                    j.ObjOpen();
                    j.KV("good", Goods.Names[(int)def.Inputs[ii].Good]);
                    j.KV("amount", def.Inputs[ii].Amount);
                    j.ObjClose();
                }
                j.ArrClose();
                j.KV("outputGood", Goods.Names[(int)def.OutputGood]);
                j.KV("outputAmount", def.OutputAmount);
                j.KV("maxLaborFraction", def.MaxLaborFraction);
                j.KV("expectedThroughput", kv.Value * def.OutputAmount);
                throughputByType.TryGetValue(kv.Key, out float actualTp);
                workersByType.TryGetValue(kv.Key, out float actualWk);
                j.KV("actualThroughput", actualTp);
                j.KV("actualWorkers", actualWk);
                j.ObjClose();
            }
            j.ObjClose();

            j.ObjClose();
        }

        static void WriteEconomyV4(JW j, SimulationState st)
        {
            j.Key("economyV4"); j.ObjOpen();

            var econ = st.EconomyV4;
            if (econ == null)
            {
                j.KV("initialized", false);
                j.ObjClose();
                return;
            }

            j.KV("initialized", true);
            j.KV("goodCount", GoodsV4.Count);
            j.KV("facilityCount", FacilitiesV4.Count);
            j.KV("marketCount", econ.MarketCount);

            // Goods metadata
            j.Key("goods"); j.ArrOpen();
            for (int g = 0; g < GoodsV4.Count; g++)
            {
                var d = GoodsV4.Defs[g];
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
            for (int f = 0; f < FacilitiesV4.Count; f++)
            {
                var d = FacilitiesV4.Defs[f];
                j.ObjOpen();
                j.KV("name", d.Name);
                j.KV("output", GoodsV4.Names[(int)d.Output]);
                j.KV("throughputPerCapita", d.ThroughputPerCapita);
                j.Key("inputs"); j.ArrOpen();
                foreach (var inp in d.Inputs)
                {
                    j.ObjOpen();
                    j.KV("good", GoodsV4.Names[(int)inp.Good]);
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
            float[] totalProduction = new float[GoodsV4.Count];
            float[] totalConsumption = new float[GoodsV4.Count];
            float[] totalSurplus = new float[GoodsV4.Count];
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

                for (int g = 0; g < GoodsV4.Count; g++)
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
            for (int g = 0; g < GoodsV4.Count; g++)
                if (totalProduction[g] > 0f) j.KV(GoodsV4.Names[g], totalProduction[g]);
            j.ObjClose();

            j.Key("consumption"); j.ObjOpen();
            for (int g = 0; g < GoodsV4.Count; g++)
                if (totalConsumption[g] > 0f) j.KV(GoodsV4.Names[g], totalConsumption[g]);
            j.ObjClose();

            j.Key("surplus"); j.ObjOpen();
            for (int g = 0; g < GoodsV4.Count; g++)
                if (totalSurplus[g] != 0f) j.KV(GoodsV4.Names[g], totalSurplus[g]);
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
            j.ObjClose();

            // Phase 2: noble satisfaction
            j.Key("nobleSatisfaction"); j.ObjOpen();
            j.KV("upperNobleMean", nobleSatCount > 0 ? upperNobleSatSum / nobleSatCount : 0f);
            j.KV("lowerNobleMean", nobleSatCount > 0 ? lowerNobleSatSum / nobleSatCount : 0f);
            j.KV("counties", nobleSatCount);
            j.ObjClose();

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

                j.Key("production"); j.ObjOpen();
                for (int g = 0; g < GoodsV4.Count; g++)
                    if (ce.Production[g] > 0f) j.KV(GoodsV4.Names[g], ce.Production[g]);
                j.ObjClose();

                j.Key("surplus"); j.ObjOpen();
                for (int g = 0; g < GoodsV4.Count; g++)
                    if (ce.Surplus[g] != 0f) j.KV(GoodsV4.Names[g], ce.Surplus[g]);
                j.ObjClose();

                j.ObjClose();
            }
            j.ArrClose();

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
                for (int g = 0; g < GoodsV4.Count; g++)
                    if (market.ClearingPrice[g] > 0f)
                        j.KV(GoodsV4.Names[g], market.ClearingPrice[g]);
                j.ObjClose();

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

        static void SetSnapshotCapture(bool enabled, bool resetSeries)
        {
            if (!EditorApplication.isPlaying || GameManager.Instance?.Simulation == null)
                return;

            var economy = GameManager.Instance.Simulation.GetState()?.Economy;
            if (economy == null)
                return;

            economy.CaptureSnapshots = enabled;
            if (enabled && resetSeries)
                economy.TimeSeries = new RingBuffer<EconomySnapshot>(economy.TimeSeries.Capacity);
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
            public string scope;
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
