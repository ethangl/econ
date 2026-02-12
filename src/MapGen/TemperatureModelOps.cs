using System;

namespace MapGen.Core
{
    /// <summary>
    /// Temperature model: latitude baseline with elevation lapse-rate correction.
    /// </summary>
    public static class TemperatureModelOps
    {
        public static void Compute(ClimateField climate, ElevationField elevation, MapGenConfig config, WorldMetadata world)
        {
            var mesh = climate.Mesh;
            ParallelOps.For(0, mesh.CellCount, i =>
            {
                float latitude = CellLatitude(mesh, i, world);
                float seaLevelTemp = SeaLevelTemperature(latitude, config);
                float elevationMeters = elevation[i] > 0f ? elevation[i] : 0f;
                float lapseCorrection = -config.LapseRateCPerKm * elevationMeters / 1000f;
                climate.TemperatureC[i] = seaLevelTemp + lapseCorrection;
            });
        }

        public static float CellLatitude(CellMesh mesh, int cellIndex, WorldMetadata world)
        {
            float t = mesh.Height > 1e-6f ? mesh.CellCenters[cellIndex].Y / mesh.Height : 0f;
            return world.LatitudeSouth + t * (world.LatitudeNorth - world.LatitudeSouth);
        }

        public static float SeaLevelTemperature(float latitude, MapGenConfig config)
        {
            float absLat = Math.Abs(latitude);

            if (absLat <= 15f)
                return config.EquatorTempC;

            float t = (absLat - 15f) / (90f - 15f);
            if (t > 1f) t = 1f;
            float cosT = (float)Math.Cos(t * Math.PI / 2f);

            float poleTemp = latitude >= 0f ? config.NorthPoleTempC : config.SouthPoleTempC;
            return poleTemp + (config.EquatorTempC - poleTemp) * cosT;
        }
    }
}
