using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core;

namespace EconSim.Renderer
{
    /// <summary>
    /// Generates a Unity mesh from a SphereMesh Voronoi tessellation.
    /// Each cell gets a random vertex color from a deterministic palette.
    /// </summary>
    public class SphereView : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private UnityEngine.MeshRenderer meshRenderer;
        private SphereMesh sphereMesh;

        public float Radius => sphereMesh?.Radius ?? 0f;

        public void Generate(WorldGenConfig config)
        {
            sphereMesh = WorldGenPipeline.Generate(config);
            BuildMesh();
        }

        private void BuildMesh()
        {
            if (sphereMesh == null) return;

            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<UnityEngine.MeshRenderer>();

            // Create material if needed
            if (meshRenderer.sharedMaterial == null)
            {
                meshRenderer.sharedMaterial = new Material(Shader.Find("EconSim/VertexColorUnlit"));
            }

            // Build vertex/triangle/color lists
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            int cellCount = sphereMesh.CellCount;

            for (int c = 0; c < cellCount; c++)
            {
                int[] cellVerts = sphereMesh.CellVertices[c];
                if (cellVerts == null || cellVerts.Length < 3)
                    continue;

                Color32 color = CellColor(c);

                // Center vertex
                Vec3 center = sphereMesh.CellCenters[c];
                int centerIdx = vertices.Count;
                vertices.Add(new Vector3(center.X, center.Y, center.Z));
                colors.Add(color);

                // Polygon vertices
                int polyStart = vertices.Count;
                for (int i = 0; i < cellVerts.Length; i++)
                {
                    Vec3 v = sphereMesh.Vertices[cellVerts[i]];
                    vertices.Add(new Vector3(v.X, v.Y, v.Z));
                    colors.Add(color);
                }

                // Fan triangles — CW winding so normals point outward from sphere
                int len = cellVerts.Length;
                for (int i = 0; i < len; i++)
                {
                    int next = (i + 1) % len;
                    triangles.Add(centerIdx);
                    triangles.Add(polyStart + next);
                    triangles.Add(polyStart + i);
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;
            Debug.Log($"SphereView: built mesh with {cellCount} cells, {vertices.Count} vertices, {triangles.Count / 3} triangles");
        }

        /// <summary>
        /// Deterministic pastel color from cell index.
        /// </summary>
        private static Color32 CellColor(int cellIndex)
        {
            // Simple hash for deterministic but varied colors
            uint h = (uint)cellIndex;
            h = ((h >> 16) ^ h) * 0x45d9f3b;
            h = ((h >> 16) ^ h) * 0x45d9f3b;
            h = (h >> 16) ^ h;

            float hue = (h & 0xFFFF) / 65535f;
            float sat = 0.4f + ((h >> 16) & 0xFF) / 255f * 0.3f;
            float val = 0.6f + ((h >> 8) & 0xFF) / 255f * 0.3f;

            Color c = Color.HSVToRGB(hue, sat, val);
            return new Color32(
                (byte)(c.r * 255),
                (byte)(c.g * 255),
                (byte)(c.b * 255),
                255
            );
        }
    }
}
