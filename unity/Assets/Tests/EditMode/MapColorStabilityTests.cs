using NUnit.Framework;
using UnityEngine;

namespace EconSim.Tests
{
    [TestFixture]
    [Category("M3Regression")]
    public class MapColorStabilityTests
    {
        // Low-saturation colors representative of historically problematic hue-drift cases.
        private static readonly Color[] LowSaturationSamples =
        {
            new Color(0.42f, 0.40f, 0.39f),
            new Color(0.53f, 0.50f, 0.47f),
            new Color(0.48f, 0.52f, 0.50f),
            new Color(0.58f, 0.55f, 0.52f),
            new Color(0.36f, 0.40f, 0.44f)
        };

        // Keep aligned with shader property defaults and UI tuning ranges.
        private static readonly float[] BorderDarkeningFactors = { 0.25f, 0.35f, 0.50f };
        private static readonly float[] HoverIntensities = { 0.25f, 0.50f, 1.00f };

        [Test]
        public void BorderDarkening_PreservesHueClass_ForLowSaturationSamples()
        {
            for (int i = 0; i < LowSaturationSamples.Length; i++)
            {
                Color source = LowSaturationSamples[i];
                int sourceHueClass = HueClass(source);

                for (int d = 0; d < BorderDarkeningFactors.Length; d++)
                {
                    float darkening = BorderDarkeningFactors[d];
                    Color shaded = ApplyBorderDarkening(source, darkening);
                    int shadedHueClass = HueClass(shaded);

                    Assert.That(shadedHueClass, Is.EqualTo(sourceHueClass),
                        $"Hue class drift under border darkening={darkening} for source={source}");
                }
            }
        }

        [Test]
        public void HoverBrighten_PreservesHueClass_WhenNotClipped()
        {
            for (int i = 0; i < LowSaturationSamples.Length; i++)
            {
                Color source = LowSaturationSamples[i];
                int sourceHueClass = HueClass(source);

                for (int h = 0; h < HoverIntensities.Length; h++)
                {
                    float hoverIntensity = HoverIntensities[h];
                    Color hovered = ApplyHoverBrighten(source, hoverIntensity);

                    // If clip occurs, hue can legitimately shift; this test targets the non-clipped path.
                    bool clipped = hovered.r >= 0.999f || hovered.g >= 0.999f || hovered.b >= 0.999f;
                    if (clipped)
                        continue;

                    int hoveredHueClass = HueClass(hovered);
                    Assert.That(hoveredHueClass, Is.EqualTo(sourceHueClass),
                        $"Hue class drift under hoverIntensity={hoverIntensity} for source={source}");
                }
            }
        }

        private static Color ApplyBorderDarkening(Color color, float darkening)
        {
            float multiplier = 1f - darkening;
            return new Color(
                Mathf.Clamp01(color.r * multiplier),
                Mathf.Clamp01(color.g * multiplier),
                Mathf.Clamp01(color.b * multiplier),
                1f);
        }

        private static Color ApplyHoverBrighten(Color color, float intensity)
        {
            float boost = 1f + 0.25f * intensity;
            return new Color(
                Mathf.Clamp01(color.r * boost),
                Mathf.Clamp01(color.g * boost),
                Mathf.Clamp01(color.b * boost),
                1f);
        }

        private static int HueClass(Color color)
        {
            Color.RGBToHSV(color, out float hue, out _, out _);
            float degrees = hue * 360f;
            return Mathf.FloorToInt(degrees / 30f);
        }
    }
}
