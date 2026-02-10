using System;

namespace MapGen.Core
{
    /// <summary>
    /// Temperature computation: latitude gradient + altitude lapse rate.
    /// </summary>
    public static class TemperatureOps
    {
        /// <summary>
        /// Compute temperature for all cells.
        /// Cell Y position → latitude → sea-level temp → altitude correction.
        /// </summary>
        public static void Compute(ClimateData climate, HeightGrid heights, WorldConfig config)
        {
            var mesh = climate.Mesh;

            for (int i = 0; i < mesh.CellCount; i++)
            {
                float lat = CellLatitude(mesh, i, config);
                float seaLevelTemp = SeaLevelTemperature(lat, config);
                float elevationM = config.HeightToMeters(heights.Heights[i], heights.Domain);
                float lapseCorrection = -config.LapseRate * elevationM / 1000f;

                climate.Temperature[i] = seaLevelTemp + lapseCorrection;
            }
        }

        /// <summary>
        /// Convert cell Y position to latitude (degrees).
        /// Unity coords: Y=0 is bottom (south), Y=Height is top (north).
        /// So Y=0 → LatitudeSouth, Y=Height → LatitudeNorth.
        /// </summary>
        public static float CellLatitude(CellMesh mesh, int cellIndex, WorldConfig config)
        {
            float t = mesh.CellCenters[cellIndex].Y / mesh.Height;
            return config.LatitudeSouth + t * (config.LatitudeNorth - config.LatitudeSouth);
        }

        /// <summary>
        /// Latitude to sea-level temperature.
        /// Cosine gradient from equator with tropical plateau within ±15°.
        /// </summary>
        public static float SeaLevelTemperature(float latitude, WorldConfig config)
        {
            float absLat = Math.Abs(latitude);

            // Tropical plateau: flat temperature within ±15° of equator
            if (absLat <= 15f)
                return config.EquatorTemp;

            // Cosine falloff from 15° to pole
            float t = (absLat - 15f) / (90f - 15f); // 0 at 15°, 1 at pole
            t = Math.Min(t, 1f);
            float cosT = (float)Math.Cos(t * Math.PI / 2f); // 1 at tropics, 0 at pole

            // Interpolate between equator temp and appropriate pole temp
            float poleTemp = latitude >= 0 ? config.NorthPoleTemp : config.SouthPoleTemp;
            return poleTemp + (config.EquatorTemp - poleTemp) * cosT;
        }
    }
}
