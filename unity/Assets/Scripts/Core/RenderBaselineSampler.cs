using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace EconSim.Core
{
    /// <summary>
    /// One-shot runtime sampler for render frame-time baselines in Main scene.
    /// Attaches temporarily, loads cached map if needed, records frame timings, and writes JSON output.
    /// </summary>
    public sealed class RenderBaselineSampler : MonoBehaviour
    {
        private const int WarmupFrames = 240;
        private const int SampleFrames = 600;
        private const float ReadyTimeoutSeconds = 120f;

        private bool _running;

        private void Start()
        {
            if (_running)
                return;

            StartCoroutine(RunSample());
        }

        private IEnumerator RunSample()
        {
            _running = true;

            if (!GameManager.IsMapReady)
            {
                GameManager gm = GameManager.Instance;
                if (gm != null && GameManager.HasLastMapCache)
                {
                    Debug.Log("[RenderBaseline] Loading cached map for baseline run...");
                    gm.LoadLastMap();
                }
            }

            float deadline = Time.realtimeSinceStartup + ReadyTimeoutSeconds;
            while (!GameManager.IsMapReady && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!GameManager.IsMapReady)
            {
                Debug.LogError("[RenderBaseline] Map did not become ready in time.");
                yield break;
            }

            for (int i = 0; i < WarmupFrames; i++)
                yield return null;

            var frameMsSamples = new List<double>(SampleFrames);
            var cpuMsSamples = new List<double>(SampleFrames);
            var gpuMsSamples = new List<double>(SampleFrames);
            var timings = new FrameTiming[1];

            for (int i = 0; i < SampleFrames; i++)
            {
                yield return null;

                frameMsSamples.Add(Time.unscaledDeltaTime * 1000.0);

                FrameTimingManager.CaptureFrameTimings();
                uint frameTimingCount = FrameTimingManager.GetLatestTimings(1, timings);
                if (frameTimingCount > 0)
                {
                    if (timings[0].cpuFrameTime > 0.0)
                        cpuMsSamples.Add(timings[0].cpuFrameTime);
                    if (timings[0].gpuFrameTime > 0.0)
                        gpuMsSamples.Add(timings[0].gpuFrameTime);
                }
            }

            Stats frameStats = Stats.FromSamples(frameMsSamples);
            Stats cpuStats = Stats.FromSamples(cpuMsSamples);
            Stats gpuStats = Stats.FromSamples(gpuMsSamples);
            double avgFps = frameStats.Average > 0.0 ? 1000.0 / frameStats.Average : 0.0;

            string outputDirectory = Path.Combine(Application.dataPath, "..", "debug", "perf");
            Directory.CreateDirectory(outputDirectory);

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string outputPath = Path.Combine(outputDirectory, $"render_baseline_main_{timestamp}.json");
            File.WriteAllText(outputPath, BuildJson(frameStats, cpuStats, gpuStats, avgFps, cpuMsSamples.Count, gpuMsSamples.Count));

            Debug.Log(
                $"[RenderBaseline] COMPLETE avgFrameMs={frameStats.Average:0.###} p50={frameStats.P50:0.###} p95={frameStats.P95:0.###} avgFps={avgFps:0.##} " +
                $"cpuSamples={cpuMsSamples.Count} gpuSamples={gpuMsSamples.Count} output={outputPath}");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private static string BuildJson(
            Stats frameStats,
            Stats cpuStats,
            Stats gpuStats,
            double avgFps,
            int cpuSampleCount,
            int gpuSampleCount)
        {
            return "{\n" +
                $"  \"capturedAtUtc\": \"{DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)}\",\n" +
                $"  \"scene\": \"Assets/Scenes/Main.unity\",\n" +
                $"  \"warmupFrames\": {WarmupFrames},\n" +
                $"  \"sampleFrames\": {SampleFrames},\n" +
                "  \"frameMs\": {\n" +
                $"    \"average\": {Fmt(frameStats.Average)},\n" +
                $"    \"p50\": {Fmt(frameStats.P50)},\n" +
                $"    \"p95\": {Fmt(frameStats.P95)},\n" +
                $"    \"min\": {Fmt(frameStats.Min)},\n" +
                $"    \"max\": {Fmt(frameStats.Max)}\n" +
                "  },\n" +
                $"  \"averageFps\": {Fmt(avgFps)},\n" +
                "  \"cpuFrameMs\": {\n" +
                $"    \"sampleCount\": {cpuSampleCount},\n" +
                $"    \"average\": {Fmt(cpuStats.Average)},\n" +
                $"    \"p50\": {Fmt(cpuStats.P50)},\n" +
                $"    \"p95\": {Fmt(cpuStats.P95)}\n" +
                "  },\n" +
                "  \"gpuFrameMs\": {\n" +
                $"    \"sampleCount\": {gpuSampleCount},\n" +
                $"    \"average\": {Fmt(gpuStats.Average)},\n" +
                $"    \"p50\": {Fmt(gpuStats.P50)},\n" +
                $"    \"p95\": {Fmt(gpuStats.P95)}\n" +
                "  }\n" +
                "}\n";
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private readonly struct Stats
        {
            public readonly double Average;
            public readonly double P50;
            public readonly double P95;
            public readonly double Min;
            public readonly double Max;

            public Stats(double average, double p50, double p95, double min, double max)
            {
                Average = average;
                P50 = p50;
                P95 = p95;
                Min = min;
                Max = max;
            }

            public static Stats FromSamples(List<double> samples)
            {
                if (samples == null || samples.Count == 0)
                    return new Stats(0, 0, 0, 0, 0);

                double min = double.MaxValue;
                double max = double.MinValue;
                double sum = 0;

                for (int i = 0; i < samples.Count; i++)
                {
                    double value = samples[i];
                    sum += value;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                var sorted = new List<double>(samples);
                sorted.Sort();
                double p50 = Percentile(sorted, 0.50);
                double p95 = Percentile(sorted, 0.95);
                double average = sum / samples.Count;
                return new Stats(average, p50, p95, min, max);
            }

            private static double Percentile(List<double> sorted, double q)
            {
                if (sorted.Count == 0)
                    return 0;

                if (q <= 0) return sorted[0];
                if (q >= 1) return sorted[sorted.Count - 1];

                double index = q * (sorted.Count - 1);
                int lo = (int)Math.Floor(index);
                int hi = (int)Math.Ceiling(index);
                if (lo == hi)
                    return sorted[lo];

                double t = index - lo;
                return sorted[lo] + ((sorted[hi] - sorted[lo]) * t);
            }
        }
    }
}
