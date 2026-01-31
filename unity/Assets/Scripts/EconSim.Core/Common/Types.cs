using System;

namespace EconSim.Core.Common
{
    /// <summary>
    /// 2D vector replacement for Unity's Vector2.
    /// </summary>
    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float X;
        public float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0, 0);
        public static Vec2 One => new Vec2(1, 1);

        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y);
        public float SqrMagnitude => X * X + Y * Y;

        public Vec2 Normalized
        {
            get
            {
                float mag = Magnitude;
                if (mag > 0)
                    return new Vec2(X / mag, Y / mag);
                return Zero;
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float d) => new Vec2(a.X * d, a.Y * d);
        public static Vec2 operator *(float d, Vec2 a) => new Vec2(a.X * d, a.Y * d);
        public static Vec2 operator /(Vec2 a, float d) => new Vec2(a.X / d, a.Y / d);
        public static bool operator ==(Vec2 a, Vec2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Vec2 a, Vec2 b) => a.X != b.X || a.Y != b.Y;

        public static float Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Magnitude;

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>
    /// RGBA color with byte components, matching Unity's Color32.
    /// </summary>
    [Serializable]
    public struct Color32 : IEquatable<Color32>
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public Color32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static Color32 White => new Color32(255, 255, 255, 255);
        public static Color32 Black => new Color32(0, 0, 0, 255);
        public static Color32 Gray => new Color32(128, 128, 128, 255);
        public static Color32 Red => new Color32(255, 0, 0, 255);
        public static Color32 Green => new Color32(0, 255, 0, 255);
        public static Color32 Blue => new Color32(0, 0, 255, 255);

        public static bool operator ==(Color32 a, Color32 b) =>
            a.R == b.R && a.G == b.G && a.B == b.B && a.A == b.A;
        public static bool operator !=(Color32 a, Color32 b) =>
            a.R != b.R || a.G != b.G || a.B != b.B || a.A != b.A;

        public bool Equals(Color32 other) => this == other;
        public override bool Equals(object obj) => obj is Color32 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
        public override string ToString() => $"RGBA({R}, {G}, {B}, {A})";

        /// <summary>
        /// Convert to hex string (#RRGGBB or #RRGGBBAA).
        /// </summary>
        public string ToHex(bool includeAlpha = false)
        {
            return includeAlpha
                ? $"#{R:X2}{G:X2}{B:X2}{A:X2}"
                : $"#{R:X2}{G:X2}{B:X2}";
        }
    }
}
