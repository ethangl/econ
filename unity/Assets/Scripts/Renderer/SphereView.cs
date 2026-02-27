using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core;

namespace EconSim.Renderer
{
    /// <summary>
    /// Generates a Unity mesh from a WorldGenResult Voronoi tessellation.
    /// Colors cells by tectonic plate assignment.
    /// </summary>
    public class SphereView : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private UnityEngine.MeshRenderer meshRenderer;
        private WorldGenResult result;

        public float Radius => result?.Mesh?.Radius ?? 0f;

        public void Generate(WorldGenConfig config)
        {
            result = WorldGenPipeline.Generate(config);
            BuildMesh();
        }

        private void BuildMesh()
        {
            if (result?.Mesh == null) return;
            var mesh = result.Mesh;

            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();
            if (meshRenderer == null)
                meshRenderer = GetComponent<UnityEngine.MeshRenderer>();

            // Create material if needed
            if (meshRenderer.sharedMaterial == null)
            {
                meshRenderer.sharedMaterial = new Material(Shader.Find("EconSim/VertexColorUnlit"));
            }

            // Build plate color palette
            Color32[] platePalette = BuildPlatePalette(result.Tectonics.PlateCount);

            // Build vertex/triangle/color lists
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var colors = new List<Color32>();

            int cellCount = mesh.CellCount;

            for (int c = 0; c < cellCount; c++)
            {
                int[] cellVerts = mesh.CellVertices[c];
                if (cellVerts == null || cellVerts.Length < 3)
                    continue;

                int plateId = result.Tectonics.CellPlate[c];
                Color32 color = platePalette[plateId];

                // Center vertex
                Vec3 center = mesh.CellCenters[c];
                int centerIdx = vertices.Count;
                vertices.Add(new Vector3(center.X, center.Y, center.Z));
                colors.Add(color);

                // Polygon vertices
                int polyStart = vertices.Count;
                for (int i = 0; i < cellVerts.Length; i++)
                {
                    Vec3 v = mesh.Vertices[cellVerts[i]];
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

            var unityMesh = new Mesh();
            unityMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            unityMesh.SetVertices(vertices);
            unityMesh.SetTriangles(triangles, 0);
            unityMesh.SetColors(colors);
            unityMesh.RecalculateNormals();
            unityMesh.RecalculateBounds();

            meshFilter.mesh = unityMesh;
            Debug.Log($"SphereView: built mesh with {cellCount} cells, {result.Tectonics.PlateCount} plates, {vertices.Count} vertices, {triangles.Count / 3} triangles");
        }

        /// <summary>
        /// Build a palette of evenly-spaced hues for plate coloring.
        /// </summary>
        private static Color32[] BuildPlatePalette(int plateCount)
        {
            var palette = new Color32[plateCount];
            for (int i = 0; i < plateCount; i++)
            {
                Color c = Color.HSVToRGB((float)i / plateCount, 0.6f, 0.8f);
                palette[i] = new Color32(
                    (byte)(c.r * 255),
                    (byte)(c.g * 255),
                    (byte)(c.b * 255),
                    255
                );
            }
            return palette;
        }
    }
}
