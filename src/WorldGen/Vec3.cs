using System;

namespace WorldGen.Core
{
    /// <summary>
    /// Simple 3D vector for engine-independent spherical geometry.
    /// Mirrors MapGen's Vec2 pattern.
    /// </summary>
    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 v, float s) => new Vec3(v.X * s, v.Y * s, v.Z * s);
        public static Vec3 operator *(float s, Vec3 v) => new Vec3(v.X * s, v.Y * s, v.Z * s);
        public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public Vec3 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-9f ? new Vec3(X / m, Y / m, Z / m) : new Vec3(0, 0, 0);
            }
        }

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;
        public static float SqrDistance(Vec3 a, Vec3 b) => (a - b).SqrMagnitude;

        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4})";
    }
}
