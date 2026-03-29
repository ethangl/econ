using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WorldGen.Core;

namespace WorldGen.Cli.Lib
{
    /// <summary>
    /// Stamps conical stratovolcano profiles onto the heightmap at each volcanic arc peak.
    /// Applied after the main render pipeline (Metal or CPU) and before sharpen.
    /// </summary>
    public static class VolcanicArcDetail
    {
        // Cone base radius in km. Oversized vs real stratovolcanoes (~15 km)
        // so peaks are visible on a global map.
        const float ConeRadiusKm = 40f;

        // Earth circumference at equator for equirectangular projection
        const float EarthCircumferenceKm = 40075f;

        // Peak height in normalized elevation units (modulated by per-peak intensity)
        const float PeakHeight = 0.15f;

        public static void Apply(Image<L16> image, TectonicData tectonics, SphereMesh mesh, int seed)
        {
            if (tectonics.VolcanicArcs == null || tectonics.VolcanicArcs.Length == 0)
                return;

            int w = image.Width;
            int h = image.Height;

            var noise = new Noise3D(seed + 900);
            float noiseFreq = 8f; // high frequency for small-scale variation

            // Pixels per km at equator
            float pxPerKm = w / EarthCircumferenceKm;
            float coneRadiusPx = ConeRadiusKm * pxPerKm;

            if (!image.DangerousTryGetSinglePixelMemory(out var pixels))
                return;

            var span = pixels.Span;

            foreach (var arc in tectonics.VolcanicArcs)
            {
                foreach (var peak in arc.Peaks)
                {
                    StampCone(span, w, h, peak, coneRadiusPx, noise, noiseFreq);
                }
            }
        }

        static void StampCone(Span<L16> pixels, int w, int h,
            VolcanoPeakData peak, float baseRadiusPx, Noise3D noise, float noiseFreq)
        {
            Vec3 p = peak.Position.Normalized;
            float lat = (float)Math.Asin(p.Y);
            float lon = (float)Math.Atan2(p.Z, p.X);

            // Equirectangular projection: lon [-pi,pi] -> x [0,w], lat [pi/2,-pi/2] -> y [0,h]
            float centerXf = (lon / MathF.PI + 1f) * 0.5f * w;
            float centerYf = (0.5f - lat / MathF.PI) * h;
            int centerX = (int)centerXf;
            int centerY = (int)centerYf;

            // Scale horizontal radius by 1/cos(lat) for equirectangular distortion
            float cosLat = MathF.Cos(lat);
            float radiusX = cosLat > 0.05f ? baseRadiusPx / cosLat : baseRadiusPx * 20f;
            float radiusY = baseRadiusPx;

            int rx = (int)MathF.Ceiling(radiusX) + 1;
            int ry = (int)MathF.Ceiling(radiusY) + 1;

            float peakElev = PeakHeight * peak.Intensity;

            for (int dy = -ry; dy <= ry; dy++)
            {
                int py = centerY + dy;
                if (py < 0 || py >= h)
                    continue;

                for (int dx = -rx; dx <= rx; dx++)
                {
                    int px = (centerX + dx + w) % w; // wrap horizontally

                    // Normalized distance (0 at center, 1 at edge)
                    float nx = dx / radiusX;
                    float ny = dy / radiusY;
                    float dist2 = nx * nx + ny * ny;
                    if (dist2 >= 1f)
                        continue;

                    float dist = MathF.Sqrt(dist2);

                    // Quadratic falloff for natural cone shape
                    float falloff = (1f - dist) * (1f - dist);

                    // Noise modulation for irregularity
                    float noiseVal = noise.Sample(
                        p.X * noiseFreq + dx * 0.1f,
                        p.Y * noiseFreq + dy * 0.1f,
                        p.Z * noiseFreq);
                    float modulation = 1f + 0.3f * noiseVal;

                    float elevAdd = peakElev * falloff * modulation;
                    if (elevAdd <= 0f)
                        continue;

                    int idx = py * w + px;
                    float current = pixels[idx].PackedValue / 65535f;
                    float result = current + elevAdd;
                    pixels[idx] = new L16((ushort)Math.Clamp(result * 65535f, 0f, 65535f));
                }
            }
        }
    }
}
