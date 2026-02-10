using System.Collections.Generic;
using EconSim.Core.Diagnostics;
using UnityEngine;

namespace EconSim.Core
{
    public class LoggingControlPanel : MonoBehaviour
    {
        [SerializeField] private int ringBufferCapacity = 2000;
        [SerializeField] private LogLevel minimumLevel = LogLevel.Info;
        [SerializeField] private LogDomain enabledDomains = LogDomain.All;
        [SerializeField] private int recentEntryLimit = 25;

        private static readonly LogDomain[] MapGenPresetDomains =
        {
            LogDomain.MapGen,
            LogDomain.HeightmapDsl,
            LogDomain.Climate,
            LogDomain.Rivers,
            LogDomain.Biomes,
            LogDomain.Population,
            LogDomain.Political,
            LogDomain.Bootstrap,
            LogDomain.IO
        };

        private static readonly LogDomain[] RendererPresetDomains =
        {
            LogDomain.Renderer,
            LogDomain.Shaders,
            LogDomain.Overlay,
            LogDomain.Selection,
            LogDomain.UI,
            LogDomain.Camera
        };

        public int RingBufferCapacity => ringBufferCapacity;
        public LogLevel MinimumLevel => minimumLevel;
        public LogDomain EnabledDomains => enabledDomains;
        public int RecentEntryLimit => recentEntryLimit;

        private void Awake()
        {
            ApplyRuntimeSettings();
        }

        private void OnValidate()
        {
            if (ringBufferCapacity < 16)
            {
                ringBufferCapacity = 16;
            }

            if (recentEntryLimit < 1)
            {
                recentEntryLimit = 1;
            }

            if (Application.isPlaying)
            {
                ApplyRuntimeSettings();
            }
        }

        public void ApplyRuntimeSettings()
        {
            DomainLoggingBootstrap.Initialize(ringBufferCapacity);
            DomainLog.SetFilter(enabledDomains, minimumLevel);
        }

        public void ToggleDomain(LogDomain domain, bool enabled)
        {
            if (enabled)
            {
                enabledDomains |= domain;
            }
            else
            {
                enabledDomains &= ~domain;
            }

            ApplyRuntimeSettings();
        }

        public bool IsDomainEnabled(LogDomain domain)
        {
            return (enabledDomains & domain) != 0;
        }

        public void SetMinimumLevel(LogLevel level)
        {
            minimumLevel = level;
            ApplyRuntimeSettings();
        }

        public void SetEnabledDomains(LogDomain domains)
        {
            enabledDomains = domains;
            ApplyRuntimeSettings();
        }

        public void UseAllOnPreset()
        {
            minimumLevel = LogLevel.Info;
            enabledDomains = LogDomain.All;
            ApplyRuntimeSettings();
        }

        public void UseErrorsOnlyPreset()
        {
            minimumLevel = LogLevel.Error;
            enabledDomains = LogDomain.All;
            ApplyRuntimeSettings();
        }

        public void UseMapGenOnlyPreset()
        {
            minimumLevel = LogLevel.Debug;
            enabledDomains = Combine(MapGenPresetDomains);
            ApplyRuntimeSettings();
        }

        public void UseRendererOnlyPreset()
        {
            minimumLevel = LogLevel.Debug;
            enabledDomains = Combine(RendererPresetDomains);
            ApplyRuntimeSettings();
        }

        public void ClearRingBuffer()
        {
            DomainLoggingBootstrap.RingBufferSink?.Clear();
        }

        public List<DomainLogEvent> GetRecentEntries()
        {
            return DomainLoggingBootstrap.RingBufferSink?.Snapshot(recentEntryLimit)
                ?? new List<DomainLogEvent>();
        }

        private static LogDomain Combine(IReadOnlyList<LogDomain> domains)
        {
            LogDomain result = LogDomain.None;
            for (int i = 0; i < domains.Count; i++)
            {
                result |= domains[i];
            }

            return result;
        }
    }
}
