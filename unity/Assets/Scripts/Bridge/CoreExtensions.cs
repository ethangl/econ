using UnityEngine;
using CoreVec2 = EconSim.Core.Common.Vec2;
using CoreColor32 = EconSim.Core.Common.Color32;

namespace EconSim.Bridge
{
    /// <summary>
    /// Extension methods to convert between EconSim.Core types and Unity types.
    /// </summary>
    public static class CoreExtensions
    {
        // Vec2 -> Unity Vector2
        public static Vector2 ToUnity(this CoreVec2 v) => new Vector2(v.X, v.Y);

        // Unity Vector2 -> Vec2
        public static CoreVec2 ToCore(this Vector2 v) => new CoreVec2(v.x, v.y);

        // Core Color32 -> Unity Color32
        public static UnityEngine.Color32 ToUnity(this CoreColor32 c) =>
            new UnityEngine.Color32(c.R, c.G, c.B, c.A);

        // Unity Color32 -> Core Color32
        public static CoreColor32 ToCore(this UnityEngine.Color32 c) =>
            new CoreColor32(c.r, c.g, c.b, c.a);

        // Unity Color -> Core Color32
        public static CoreColor32 ToCore(this Color c) =>
            new CoreColor32(
                (byte)(c.r * 255),
                (byte)(c.g * 255),
                (byte)(c.b * 255),
                (byte)(c.a * 255)
            );

        // Core Color32 -> Unity Color
        public static Color ToUnityColor(this CoreColor32 c) =>
            new Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    }
}
