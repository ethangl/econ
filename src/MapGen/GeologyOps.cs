using System;

namespace MapGen.Core
{
    /// <summary>
    /// Assigns per-cell rock types from Perlin noise + elevation bias + tectonic hints.
    /// </summary>
    public static class GeologyOps
    {
        const float NoiseFrequency = 0.025f; // ~40km wavelength per geological province
        const float ElevationBiasStart = 500f; // meters above sea level
        const float ElevationBiasMax = 2500f;
        const float MaxElevationBias = 0.15f;
        const float ConvergenceBiasScale = 0.20f;
        const float CoastDirectionBiasScale = 0.15f;

        public static void AssignRockTypes(BiomeField biome, ElevationField elevation, MapGenConfig config)
        {
            var mesh = biome.Mesh;
            int n = mesh.CellCount;
            var noise = new Noise(config.GeologySeed);
            float cellSizeKm = config.CellSizeKm;

            // Precompute tectonic bias parameters
            bool hasTectonics = config.Tectonics != null;
            float convergence = hasTectonics ? config.Tectonics.ConvergenceMagnitude : 0f;
            float coastDirX = hasTectonics ? config.Tectonics.CoastDirectionX : 0.5f;
            float coastDirY = hasTectonics ? config.Tectonics.CoastDirectionY : 0.5f;
            // Coast direction as unit vector from map center
            float cdx = coastDirX - 0.5f;
            float cdy = coastDirY - 0.5f;
            float cdLen = (float)Math.Sqrt(cdx * cdx + cdy * cdy);
            if (cdLen > 1e-6f) { cdx /= cdLen; cdy /= cdLen; }
            else { cdx = 0f; cdy = 0f; }

            float mapWidth = mesh.Width;
            float mapHeight = mesh.Height;

            ParallelOps.For(0, n, i =>
            {
                if (!elevation.IsLand(i))
                {
                    biome.Rock[i] = RockType.Sedimentary;
                    return;
                }

                // Sample Perlin noise in world-km space
                var center = mesh.CellCenters[i];
                float wx = center.X * cellSizeKm;
                float wy = center.Y * cellSizeKm;
                float val = noise.Sample01(wx * NoiseFrequency, wy * NoiseFrequency);

                // Elevation bias: higher cells shift toward Volcanic
                float alt = elevation[i];
                if (alt > ElevationBiasStart)
                {
                    float t = Math.Min((alt - ElevationBiasStart) / (ElevationBiasMax - ElevationBiasStart), 1f);
                    val += t * MaxElevationBias;
                }

                // Tectonic biases
                if (hasTectonics)
                {
                    // Convergence: uniform shift toward volcanic
                    val += convergence * ConvergenceBiasScale;

                    // Coast-direction spatial gradient: project normalized cell position
                    // onto coast direction vector. Cells toward the boundary get more volcanic.
                    float nx = mapWidth > 1e-6f ? (center.X / mapWidth - 0.5f) : 0f;
                    float ny = mapHeight > 1e-6f ? (center.Y / mapHeight - 0.5f) : 0f;
                    float proj = nx * cdx + ny * cdy; // [-0.5, 0.5]
                    val += proj * CoastDirectionBiasScale;
                }

                // Classify
                if (val < 0f) val = 0f;
                if (val > 1f) val = 1f;

                if (val < 0.25f)
                    biome.Rock[i] = RockType.Granite;
                else if (val < 0.50f)
                    biome.Rock[i] = RockType.Sedimentary;
                else if (val < 0.75f)
                    biome.Rock[i] = RockType.Limestone;
                else
                    biome.Rock[i] = RockType.Volcanic;
            });
        }
    }
}
