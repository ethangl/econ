using System;

namespace EconSim.Core.Diagnostics
{
    [Flags]
    public enum LogDomain
    {
        None = 0,
        MapGen = 1 << 0,
        HeightmapDsl = 1 << 1,
        Climate = 1 << 2,
        Rivers = 1 << 3,
        Biomes = 1 << 4,
        Population = 1 << 5,
        Political = 1 << 6,
        Economy = 1 << 7,
        Transport = 1 << 8,
        Roads = 1 << 9,
        Renderer = 1 << 10,
        Shaders = 1 << 11,
        Overlay = 1 << 12,
        Selection = 1 << 13,
        UI = 1 << 14,
        Camera = 1 << 15,
        Simulation = 1 << 16,
        Bootstrap = 1 << 17,
        IO = 1 << 18,
        All = ~0
    }
}
