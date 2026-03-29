using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Generates large-scale zonal winds plus curl-noise perturbations on the coarse sphere mesh.
    /// </summary>
    public static class WindOps
    {
        const float TransitionWidthDeg = 5f;
        const float NoiseLacunarity = 2f;
        const float NoisePersistence = 0.5f;
        const float GradientStep = 0.01f;

        public static void Generate(SphereMesh mesh, TectonicData tectonics, WorldGenConfig config)
        {
            int cellCount = mesh.CellCount;
            var wind = new Vec3[cellCount];
            var speed = new float[cellCount];
            var noise = new Noise3D(config.Seed + 1009);

            float maxSpeed = 0f;
            float totalSpeed = 0f;

            for (int c = 0; c < cellCount; c++)
            {
                Vec3 normal = mesh.CellCenters[c].Normalized;
                BuildLocalBasis(normal, out Vec3 east, out Vec3 north);

                float latDeg = (float)(Math.Asin(Math.Clamp(normal.Y, -1f, 1f)) * 180.0 / Math.PI);
                Vec3 zonal = BuildZonalWind(latDeg, east, north);
                Vec3 curl = BuildCurlWind(noise, normal, config);

                Vec3 combined = ProjectOntoTangent(zonal + curl * config.WindNoiseAmplitude, normal);
                float magnitude = combined.Magnitude;
                wind[c] = combined;
                speed[c] = magnitude;
                totalSpeed += magnitude;
                if (magnitude > maxSpeed)
                    maxSpeed = magnitude;
            }

            if (maxSpeed > 1e-6f)
            {
                float invMax = 1f / maxSpeed;
                for (int c = 0; c < cellCount; c++)
                {
                    wind[c] = wind[c] * invMax;
                    speed[c] *= invMax;
                }
            }

            tectonics.CellWind = wind;
            tectonics.CellWindSpeed = speed;

            float avgSpeed = cellCount > 0 ? totalSpeed / cellCount : 0f;
            Console.WriteLine($"    Wind: avg speed {avgSpeed:F2}, max {maxSpeed:F2}");
        }

        static Vec3 BuildZonalWind(float latitudeDeg, Vec3 east, Vec3 north)
        {
            float absLat = Math.Abs(latitudeDeg);
            bool northHemisphere = latitudeDeg >= 0f;

            Vec3 band0 = northHemisphere
                ? Normalize2D(east, north, -1f, -1f) // SW
                : Normalize2D(east, north, -1f, 1f);  // NW
            Vec3 band1 = northHemisphere
                ? Normalize2D(east, north, 1f, 1f)    // NE
                : Normalize2D(east, north, 1f, -1f);  // SE
            Vec3 band2 = band0;

            if (absLat <= 30f - TransitionWidthDeg)
                return band0;
            if (absLat < 30f + TransitionWidthDeg)
                return InterpolateDirection(east, north, band0, band1,
                    (absLat - (30f - TransitionWidthDeg)) / (2f * TransitionWidthDeg));
            if (absLat <= 60f - TransitionWidthDeg)
                return band1;
            if (absLat < 60f + TransitionWidthDeg)
                return InterpolateDirection(east, north, band1, band2,
                    (absLat - (60f - TransitionWidthDeg)) / (2f * TransitionWidthDeg));
            return band2;
        }

        static Vec3 BuildCurlWind(Noise3D noise, Vec3 normal, WorldGenConfig config)
        {
            float freq = config.WindNoiseFrequency;
            float px = normal.X * freq;
            float py = normal.Y * freq;
            float pz = normal.Z * freq;
            float e = GradientStep;

            float dx = (SampleFractal(noise, px + e, py, pz, config) - SampleFractal(noise, px - e, py, pz, config)) / (2f * e);
            float dy = (SampleFractal(noise, px, py + e, pz, config) - SampleFractal(noise, px, py - e, pz, config)) / (2f * e);
            float dz = (SampleFractal(noise, px, py, pz + e, config) - SampleFractal(noise, px, py, pz - e, config)) / (2f * e);

            return ProjectOntoTangent(Vec3.Cross(normal, new Vec3(dx, dy, dz)), normal);
        }

        static float SampleFractal(Noise3D noise, float x, float y, float z, WorldGenConfig config)
        {
            int octaves = Math.Max(1, config.WindNoiseOctaves);
            return noise.Fractal(x, y, z, octaves, NoiseLacunarity, NoisePersistence);
        }

        static Vec3 Normalize2D(Vec3 east, Vec3 north, float eastWeight, float northWeight)
        {
            return (east * eastWeight + north * northWeight).Normalized;
        }

        static Vec3 InterpolateDirection(Vec3 east, Vec3 north, Vec3 a, Vec3 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            float aAngle = MathF.Atan2(Vec3.Dot(a, north), Vec3.Dot(a, east));
            float bAngle = MathF.Atan2(Vec3.Dot(b, north), Vec3.Dot(b, east));
            float delta = WrapAngle(bAngle - aAngle);

            // Opposing belt vectors are ambiguous under shortest-arc interpolation;
            // pick a consistent half-turn instead of collapsing through a zero vector.
            if (Math.Abs(Math.Abs(delta) - MathF.PI) < 1e-4f)
                delta = MathF.PI;

            float angle = aAngle + delta * t;
            return (east * MathF.Cos(angle) + north * MathF.Sin(angle)).Normalized;
        }

        static float WrapAngle(float angle)
        {
            while (angle <= -MathF.PI)
                angle += 2f * MathF.PI;
            while (angle > MathF.PI)
                angle -= 2f * MathF.PI;
            return angle;
        }

        internal static Vec3 ProjectOntoTangent(Vec3 vector, Vec3 normal)
        {
            return vector - normal * Vec3.Dot(vector, normal);
        }

        internal static void BuildLocalBasis(Vec3 normal, out Vec3 east, out Vec3 north)
        {
            east = new Vec3(-normal.Z, 0f, normal.X);
            if (east.SqrMagnitude < 1e-8f)
                east = new Vec3(1f, 0f, 0f);
            east = east.Normalized;
            north = Vec3.Cross(east, normal).Normalized;
        }
    }
}
