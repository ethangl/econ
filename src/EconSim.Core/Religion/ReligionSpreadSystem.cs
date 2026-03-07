using System;
using System.Collections.Generic;
using EconSim.Core.Common;
using EconSim.Core.Data;
using EconSim.Core.Simulation;
using EconSim.Core.Simulation.Systems;

namespace EconSim.Core.Religious
{
    /// <summary>
    /// Monthly system that models religious spread between counties.
    /// Sources of spread: adjacency pressure, shared-market trade contact, and minority erosion.
    /// Target: 1-5% adherence shift per year (~0.1-0.4% per month).
    /// </summary>
    public class ReligionSpreadSystem : ITickSystem
    {
        public string Name => "ReligionSpread";
        public int TickInterval => SimulationConfig.Intervals.Monthly;

        // --- Tuning constants ---

        /// <summary>Max monthly adherence shift from adjacency pressure per faith.</summary>
        const float AdjacencyRate = 0.003f;

        /// <summary>Max monthly adherence shift from trade contact (shared market).</summary>
        const float TradeRate = 0.001f;

        /// <summary>Monthly erosion rate for very small minorities (below MinorityFloor).</summary>
        const float MinorityErosionRate = 0.002f;

        /// <summary>Adherence below this is subject to erosion toward zero.</summary>
        const float MinorityFloor = 0.05f;

        /// <summary>Adherence below this is snapped to zero (cleanup noise).</summary>
        const float AdherenceEpsilon = 0.001f;

        /// <summary>Minimum adherence change in any county to mark overlay dirty.</summary>
        const float DirtyThreshold = 0.05f;

        // --- Cached state ---
        int[] _countyIds;
        int[][] _countyNeighbors; // countyIndex → neighbor countyIds
        int[][] _marketCounties;  // marketId → countyIds sharing that market
        int _faithCount;
        float[][] _snapshot;      // last adherence values sent to overlay

        public void Initialize(SimulationState state, MapData mapData)
        {
            _countyIds = new int[mapData.Counties.Count];
            for (int i = 0; i < mapData.Counties.Count; i++)
                _countyIds[i] = mapData.Counties[i].Id;

            // Precompute county adjacency
            _countyNeighbors = new int[_countyIds.Length][];
            for (int i = 0; i < _countyIds.Length; i++)
                _countyNeighbors[i] = GetAdjacentCounties(_countyIds[i], mapData);

            // Precompute market → counties
            if (state.Economy?.CountyToMarket != null)
            {
                int maxMarket = 0;
                foreach (int cid in _countyIds)
                {
                    if (cid < state.Economy.CountyToMarket.Length)
                    {
                        int m = state.Economy.CountyToMarket[cid];
                        if (m > maxMarket) maxMarket = m;
                    }
                }

                var marketLists = new List<int>[maxMarket + 1];
                for (int i = 0; i <= maxMarket; i++)
                    marketLists[i] = new List<int>();

                foreach (int cid in _countyIds)
                {
                    if (cid < state.Economy.CountyToMarket.Length)
                    {
                        int m = state.Economy.CountyToMarket[cid];
                        if (m >= 0 && m <= maxMarket)
                            marketLists[m].Add(cid);
                    }
                }

                _marketCounties = new int[maxMarket + 1][];
                for (int i = 0; i <= maxMarket; i++)
                    _marketCounties[i] = marketLists[i].ToArray();
            }

            _faithCount = state.Religion?.FaithCount ?? 0;

            // Take initial snapshot for dirty tracking
            if (state.Religion != null && _faithCount > 0)
            {
                _snapshot = new float[state.Religion.Adherence.Length][];
                for (int ci = 0; ci < _countyIds.Length; ci++)
                {
                    int cid = _countyIds[ci];
                    var adh = state.Religion.Adherence[cid];
                    if (adh != null)
                    {
                        _snapshot[cid] = new float[_faithCount];
                        Array.Copy(adh, _snapshot[cid], _faithCount);
                    }
                }
            }

            SimLog.Log("Religion", $"ReligionSpreadSystem initialized: {_countyIds.Length} counties, {_faithCount} faiths");
        }

        public void Tick(SimulationState state, MapData mapData)
        {
            var religion = state.Religion;
            if (religion == null || _faithCount == 0) return;

            int countyCount = _countyIds.Length;
            int fc = _faithCount;

            // Accumulate deltas in a separate buffer to avoid order-dependent results
            var deltas = new float[religion.Adherence.Length][];

            for (int ci = 0; ci < countyCount; ci++)
            {
                int cid = _countyIds[ci];
                var adh = religion.Adherence[cid];
                if (adh == null) continue;

                if (deltas[cid] == null)
                    deltas[cid] = new float[fc];

                // 1. Adjacency pressure: neighboring counties pull toward their majority
                ApplyAdjacencyPressure(ci, cid, adh, deltas, religion, fc);

                // 2. Trade contact: counties sharing a market exchange religious influence
                ApplyTradePressure(cid, adh, deltas, religion, state, fc);

                // 3. Minority erosion: tiny minorities slowly vanish
                ApplyMinorityErosion(cid, adh, deltas[cid], fc);
            }

            // Apply deltas and check for meaningful change against snapshot
            bool dirty = false;
            for (int ci = 0; ci < countyCount; ci++)
            {
                int cid = _countyIds[ci];
                var adh = religion.Adherence[cid];
                var d = deltas[cid];
                if (adh == null || d == null) continue;

                for (int f = 0; f < fc; f++)
                    adh[f] += d[f];

                ClampAndNormalize(adh, fc);

                // Check against snapshot
                if (!dirty && _snapshot != null && _snapshot[cid] != null)
                {
                    for (int f = 0; f < fc; f++)
                    {
                        float diff = adh[f] - _snapshot[cid][f];
                        if (diff > DirtyThreshold || diff < -DirtyThreshold)
                        {
                            dirty = true;
                            break;
                        }
                    }
                }
            }

            religion.UpdateMajorityFaith();

            if (dirty)
            {
                religion.OverlayDirty = true;
                // Update snapshot
                for (int ci = 0; ci < countyCount; ci++)
                {
                    int cid = _countyIds[ci];
                    var adh = religion.Adherence[cid];
                    if (adh != null && _snapshot != null && _snapshot[cid] != null)
                        Array.Copy(adh, _snapshot[cid], fc);
                }
            }
        }

        void ApplyAdjacencyPressure(int countyIndex, int cid, float[] adh, float[][] deltas,
            ReligionState religion, int fc)
        {
            var neighbors = _countyNeighbors[countyIndex];
            if (neighbors.Length == 0) return;

            // Average neighbor adherence
            var neighborAvg = new float[fc];
            int validNeighbors = 0;

            for (int ni = 0; ni < neighbors.Length; ni++)
            {
                int nid = neighbors[ni];
                var nadh = religion.Adherence[nid];
                if (nadh == null) continue;
                validNeighbors++;
                for (int f = 0; f < fc; f++)
                    neighborAvg[f] += nadh[f];
            }

            if (validNeighbors == 0) return;

            float invCount = 1f / validNeighbors;
            for (int f = 0; f < fc; f++)
                neighborAvg[f] *= invCount;

            // Pull toward neighbor average
            if (deltas[cid] == null) deltas[cid] = new float[fc];
            for (int f = 0; f < fc; f++)
            {
                float diff = neighborAvg[f] - adh[f];
                deltas[cid][f] += diff * AdjacencyRate;
            }
        }

        void ApplyTradePressure(int cid, float[] adh, float[][] deltas,
            ReligionState religion, SimulationState state, int fc)
        {
            if (_marketCounties == null || state.Economy?.CountyToMarket == null) return;
            if (cid >= state.Economy.CountyToMarket.Length) return;

            int marketId = state.Economy.CountyToMarket[cid];
            if (marketId <= 0 || marketId >= _marketCounties.Length) return;

            var peers = _marketCounties[marketId];
            if (peers.Length <= 1) return;

            // Average adherence across market peers (excluding self)
            var marketAvg = new float[fc];
            int peerCount = 0;

            for (int pi = 0; pi < peers.Length; pi++)
            {
                int pid = peers[pi];
                if (pid == cid) continue;
                var padh = religion.Adherence[pid];
                if (padh == null) continue;
                peerCount++;
                for (int f = 0; f < fc; f++)
                    marketAvg[f] += padh[f];
            }

            if (peerCount == 0) return;

            float invCount = 1f / peerCount;
            for (int f = 0; f < fc; f++)
                marketAvg[f] *= invCount;

            if (deltas[cid] == null) deltas[cid] = new float[fc];
            for (int f = 0; f < fc; f++)
            {
                float diff = marketAvg[f] - adh[f];
                deltas[cid][f] += diff * TradeRate;
            }
        }

        void ApplyMinorityErosion(int cid, float[] adh, float[] delta, int fc)
        {
            for (int f = 0; f < fc; f++)
            {
                if (adh[f] > 0f && adh[f] < MinorityFloor)
                    delta[f] -= Math.Min(adh[f], MinorityErosionRate);
            }
        }

        static void ClampAndNormalize(float[] adh, int fc)
        {
            float sum = 0f;
            for (int f = 0; f < fc; f++)
            {
                if (adh[f] < AdherenceEpsilon)
                    adh[f] = 0f;
                else if (adh[f] < 0f)
                    adh[f] = 0f;

                sum += adh[f];
            }

            // If over 1.0, scale down proportionally
            if (sum > 1f)
            {
                float inv = 1f / sum;
                for (int f = 0; f < fc; f++)
                    adh[f] *= inv;
            }
        }

        static int[] GetAdjacentCounties(int countyId, MapData mapData)
        {
            if (!mapData.CountyById.TryGetValue(countyId, out var county))
                return Array.Empty<int>();

            var adj = new HashSet<int>();
            foreach (int cellId in county.CellIds)
            {
                if (!mapData.CellById.TryGetValue(cellId, out var cell)) continue;
                foreach (int nid in cell.NeighborIds)
                {
                    if (mapData.CellById.TryGetValue(nid, out var neighbor)
                        && neighbor.CountyId > 0 && neighbor.CountyId != countyId)
                        adj.Add(neighbor.CountyId);
                }
            }

            var result = new int[adj.Count];
            adj.CopyTo(result);
            return result;
        }
    }
}
