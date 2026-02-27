using System.Collections.Generic;
using UnityEngine;
using WorldGen.Core;

namespace EconSim.Renderer
{
    public enum SphereViewMode
    {
        Plates,
        Elevation
    }

    /// <summary>
    /// Generates a Unity mesh from a WorldGenResult Voronoi tessellation.
    /// Tab toggles between plate and elevation coloring.
    /// </summary>
    public class SphereView : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private UnityEngine.MeshRenderer meshRenderer;
        private WorldGenResult result;
        private Mesh unityMesh;
        private SphereViewMode viewMode = SphereViewMode.Plates;

        public float Radius => result?.Mesh?.Radius ?? 0f;
        public SphereViewMode ViewMode => viewMode;

        public void Generate(WorldGenConfig config)
        {
            result = WorldGenPipeline.Generate(config);
            BuildMesh();
        }

        private void Update()
        {
            if (result == null) return;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                viewMode = viewMode == SphereViewMode.Plates
                    ? SphereViewMode.Elevation
                    : SphereViewMode.Plates;
                RebuildColors();
                Debug.Log($"SphereView: switched to {viewMode} mode");
            }
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

            // Build vertex/triangle lists (colors applied separately)
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            int cellCount = mesh.CellCount;

            for (int c = 0; c < cellCount; c++)
            {
                int[] cellVerts = mesh.CellVertices[c];
                if (cellVerts == null || cellVerts.Length < 3)
                    continue;

                // Center vertex
                Vec3 center = mesh.CellCenters[c];
                int centerIdx = vertices.Count;
                vertices.Add(new Vector3(center.X, center.Y, center.Z));

                // Polygon vertices
                int polyStart = vertices.Count;
                for (int i = 0; i < cellVerts.Length; i++)
                {
                    Vec3 v = mesh.Vertices[cellVerts[i]];
                    vertices.Add(new Vector3(v.X, v.Y, v.Z));
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

            unityMesh = new Mesh();
            unityMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            unityMesh.SetVertices(vertices);
            unityMesh.SetTriangles(triangles, 0);

            // Apply colors for current mode
            unityMesh.SetColors(BuildColors());
            unityMesh.RecalculateNormals();
            unityMesh.RecalculateBounds();

            meshFilter.mesh = unityMesh;
            Debug.Log($"SphereView: built mesh with {cellCount} cells, {result.Tectonics.PlateCount} plates, {vertices.Count} vertices, {triangles.Count / 3} triangles");
        }

        private void RebuildColors()
        {
            if (unityMesh == null || result == null) return;
            unityMesh.SetColors(BuildColors());
        }

        private List<Color32> BuildColors()
        {
            var mesh = result.Mesh;
            var colors = new List<Color32>();
            int cellCount = mesh.CellCount;

            Color32[] palette = viewMode == SphereViewMode.Plates
                ? BuildPlatePalette(result.Tectonics.PlateCount)
                : null;

            for (int c = 0; c < cellCount; c++)
            {
                int[] cellVerts = mesh.CellVertices[c];
                if (cellVerts == null || cellVerts.Length < 3)
                    continue;

                Color32 color = viewMode == SphereViewMode.Plates
                    ? palette[result.Tectonics.CellPlate[c]]
                    : ElevationColor(result.Tectonics.CellElevation[c]);

                // Center vertex + polygon vertices
                int vertCount = 1 + cellVerts.Length;
                for (int i = 0; i < vertCount; i++)
                    colors.Add(color);
            }

            return colors;
        }

        /// <summary>
        /// Elevation color ramp: deep blue (0.0) → medium blue (0.4) → green (0.4) → brown (0.7) → white (1.0)
        /// </summary>
        private static Color32 ElevationColor(float elev)
        {
            // Sea level at ~0.4
            if (elev < 0.4f)
            {
                // Deep blue (0.0) to medium blue (0.4)
                float t = elev / 0.4f;
                byte r = (byte)(10 * t);
                byte g = (byte)(30 + 70 * t);
                byte b = (byte)(80 + 140 * t);
                return new Color32(r, g, b, 255);
            }
            else if (elev < 0.55f)
            {
                // Green (land near sea level)
                float t = (elev - 0.4f) / 0.15f;
                byte r = (byte)(30 + 80 * t);
                byte g = (byte)(140 + 40 * t);
                byte b = (byte)(40 - 10 * t);
                return new Color32(r, g, b, 255);
            }
            else if (elev < 0.75f)
            {
                // Green → brown (highlands)
                float t = (elev - 0.55f) / 0.2f;
                byte r = (byte)(110 + 50 * t);
                byte g = (byte)(180 - 80 * t);
                byte b = (byte)(30 + 10 * t);
                return new Color32(r, g, b, 255);
            }
            else
            {
                // Brown → white (mountains/peaks)
                float t = (elev - 0.75f) / 0.25f;
                if (t > 1f) t = 1f;
                byte r = (byte)(160 + 95 * t);
                byte g = (byte)(100 + 155 * t);
                byte b = (byte)(40 + 215 * t);
                return new Color32(r, g, b, 255);
            }
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
