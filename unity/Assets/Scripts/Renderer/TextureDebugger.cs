using System.IO;
using UnityEngine;

namespace EconSim.Renderer
{
    /// <summary>
    /// Utility for saving generated textures to disk for debugging.
    /// </summary>
    public static class TextureDebugger
    {
        private static readonly string DebugFolder = Path.Combine(Application.dataPath, "..", "debug");
        public static bool Enabled = false;

        public static void SaveTexture(Texture2D tex, string name)
        {
            if (!Enabled) return;
            if (tex == null) return;

            Directory.CreateDirectory(DebugFolder);
            string path = Path.Combine(DebugFolder, $"{name}.png");

            // Handle non-readable or float textures
            Texture2D readable = MakeReadable(tex);
            File.WriteAllBytes(path, readable.EncodeToPNG());
            if (readable != tex)
            {
                if (Application.isPlaying)
                    Object.Destroy(readable);
                else
                    Object.DestroyImmediate(readable);
            }

            Debug.Log($"TextureDebugger: Saved {path}");
        }

        private static Texture2D MakeReadable(Texture2D tex)
        {
            if (tex.isReadable && tex.format == TextureFormat.RGBA32)
                return tex;

            RenderTexture rt = RenderTexture.GetTemporary(tex.width, tex.height);
            Graphics.Blit(tex, rt);

            Texture2D copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt;
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return copy;
        }
    }
}
