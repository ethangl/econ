using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    public enum ColorMode
    {
        Heightmap,
        Soil,
        Biome,
        CellIndex
    }

    /// <summary>
    /// Debug visualization for CellMesh using Unity's GL immediate mode.
    /// </summary>
    [RequireComponent(typeof(CellMeshGenerator))]
    public class CellMeshVisualizer : MonoBehaviour
    {
        [Header("Display Options")]
        [System.NonSerialized] public bool ShowCells = true;
        public bool ShowEdges = false;
        [System.NonSerialized] public bool ShowCenters = false;
        [System.NonSerialized] public bool ShowVertices = false;

        [Header("Coloring")]
        public ColorMode ColorMode = ColorMode.Heightmap;

        [System.NonSerialized] public Color CellColor = new Color(0.2f, 0.4f, 0.8f, 1f);
        public Color EdgeColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        [System.NonSerialized] public Color CenterColor = Color.red;
        [System.NonSerialized] public Color VertexColor = Color.green;

        private CellMeshGenerator _generator;
        private HeightmapGenerator _heightmapGenerator;
        private BiomeGenerator _biomeGenerator;
        private Material _glMaterial;

        // Soil palette (8 types, indexed by SoilType enum)
        private static readonly Color[] SoilColors = new Color[]
        {
            new Color(0.60f, 0.65f, 0.72f), // Permafrost - blue-gray
            new Color(0.90f, 0.88f, 0.85f), // Saline - white/pale
            new Color(0.55f, 0.55f, 0.53f), // Lithosol - rocky gray
            new Color(0.30f, 0.22f, 0.14f), // Alluvial - dark brown
            new Color(0.82f, 0.75f, 0.50f), // Aridisol - sandy yellow
            new Color(0.78f, 0.38f, 0.18f), // Laterite - red-orange
            new Color(0.50f, 0.44f, 0.38f), // Podzol - gray-brown
            new Color(0.15f, 0.12f, 0.10f), // Chernozem - near-black
        };

        // Vegetation palette (7 types, indexed by VegetationType enum)
        private static readonly Color[] VegetationColors = new Color[]
        {
            new Color(0f, 0f, 0f, 0f),      // None - transparent
            new Color(0.55f, 0.60f, 0.50f),  // LichenMoss - muted gray-green
            new Color(0.65f, 0.70f, 0.30f),  // Grass - gold-green
            new Color(0.50f, 0.52f, 0.32f),  // Shrub - dusty olive
            new Color(0.28f, 0.55f, 0.22f),  // DeciduousForest - green
            new Color(0.15f, 0.35f, 0.30f),  // ConiferousForest - dark blue-green
            new Color(0.10f, 0.42f, 0.18f),  // BroadleafForest - deep green
        };

        void Awake()
        {
            _generator = GetComponent<CellMeshGenerator>();
            _heightmapGenerator = GetComponent<HeightmapGenerator>();
            _biomeGenerator = GetComponent<BiomeGenerator>();

            // Create simple material for GL drawing
            var shader = Shader.Find("Hidden/Internal-Colored");
            _glMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _glMaterial.SetInt("_ZWrite", 0);
        }

        void OnRenderObject()
        {
            var mesh = _generator?.Mesh;
            if (mesh == null) return;

            _glMaterial.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(transform.localToWorldMatrix);

            if (ShowCells)
                DrawCells(mesh);

            if (ShowEdges)
                DrawEdges(mesh);

            if (ShowCenters)
                DrawPoints(mesh.CellCenters, CenterColor, 2f);

            if (ShowVertices)
                DrawPoints(mesh.Vertices, VertexColor, 1.5f);

            GL.PopMatrix();
        }

        private void DrawCells(CellMesh mesh)
        {
            GL.Begin(GL.TRIANGLES);

            if (_heightmapGenerator == null)
                _heightmapGenerator = GetComponent<HeightmapGenerator>();
            if (_biomeGenerator == null)
                _biomeGenerator = GetComponent<BiomeGenerator>();

            var heightGrid = _heightmapGenerator?.HeightGrid;
            var biomeData = _biomeGenerator?.BiomeData;

            for (int c = 0; c < mesh.CellCount; c++)
            {
                var verts = mesh.CellVertices[c];
                if (verts == null || verts.Length < 3) continue;

                Color color = GetCellColor(c, heightGrid, biomeData);
                color.a = 1f;
                GL.Color(color);

                // Fan triangulation from first vertex
                var v0 = ToVector3(mesh.Vertices[verts[0]]);
                for (int i = 1; i < verts.Length - 1; i++)
                {
                    var v1 = ToVector3(mesh.Vertices[verts[i]]);
                    var v2 = ToVector3(mesh.Vertices[verts[i + 1]]);

                    GL.Vertex(v0);
                    GL.Vertex(v1);
                    GL.Vertex(v2);
                }
            }

            GL.End();
        }

        private Color GetCellColor(int c, HeightGrid heightGrid, BiomeData biomeData)
        {
            bool isWater = heightGrid != null && heightGrid.IsWater(c);

            switch (ColorMode)
            {
                case ColorMode.Soil:
                case ColorMode.Biome:
                    if (biomeData == null || isWater)
                        return GetWaterOrFallback(c, heightGrid);

                    Color soil = SoilColors[(int)biomeData.Soil[c]];
                    if (ColorMode == ColorMode.Soil)
                        return soil;

                    // Biome: soil + vegetation composite
                    Color veg = VegetationColors[(int)biomeData.Vegetation[c]];
                    float density = biomeData.VegetationDensity[c];
                    return Color.Lerp(soil, veg, density);

                case ColorMode.Heightmap:
                    if (heightGrid != null)
                        return GetHeightColor(heightGrid.Heights[c]);
                    goto case ColorMode.CellIndex;

                case ColorMode.CellIndex:
                default:
                    float hue = (c * 0.618034f) % 1f;
                    return Color.HSVToRGB(hue, 0.5f, 0.7f);
            }
        }

        /// <summary>
        /// For Soil/Biome modes: water cells use height color if available, else default blue.
        /// </summary>
        private Color GetWaterOrFallback(int c, HeightGrid heightGrid)
        {
            if (heightGrid != null)
                return GetHeightColor(heightGrid.Heights[c]);
            return new Color(0.1f, 0.2f, 0.5f);
        }

        /// <summary>
        /// Get color for a height value.
        /// Water: blue shades, Land: green to brown to white.
        /// </summary>
        private Color GetHeightColor(float height)
        {
            const float seaLevel = HeightGrid.SeaLevel;
            const float maxHeight = HeightGrid.MaxHeight;

            if (height <= seaLevel)
            {
                // Water: dark blue (deep) to light blue (shallow)
                float t = height / seaLevel;
                return Color.Lerp(new Color(0.05f, 0.1f, 0.3f), new Color(0.2f, 0.4f, 0.7f), t);
            }
            else
            {
                // Land: green (low) to brown (mid) to white (high)
                float t = (height - seaLevel) / (maxHeight - seaLevel);
                if (t < 0.3f)
                {
                    // Green lowlands
                    return Color.Lerp(new Color(0.2f, 0.5f, 0.2f), new Color(0.4f, 0.6f, 0.3f), t / 0.3f);
                }
                else if (t < 0.6f)
                {
                    // Brown hills
                    return Color.Lerp(new Color(0.4f, 0.6f, 0.3f), new Color(0.5f, 0.4f, 0.25f), (t - 0.3f) / 0.3f);
                }
                else if (t < 0.85f)
                {
                    // Gray mountains
                    return Color.Lerp(new Color(0.5f, 0.4f, 0.25f), new Color(0.6f, 0.6f, 0.6f), (t - 0.6f) / 0.25f);
                }
                else
                {
                    // Snow caps
                    return Color.Lerp(new Color(0.6f, 0.6f, 0.6f), Color.white, (t - 0.85f) / 0.15f);
                }
            }
        }

        private void DrawEdges(CellMesh mesh)
        {
            GL.Begin(GL.LINES);
            GL.Color(EdgeColor);

            for (int e = 0; e < mesh.EdgeCount; e++)
            {
                var (v0, v1) = mesh.EdgeVertices[e];
                GL.Vertex(ToVector3(mesh.Vertices[v0]));
                GL.Vertex(ToVector3(mesh.Vertices[v1]));
            }

            GL.End();
        }

        private void DrawPoints(Vec2[] points, Color color, float size)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);

            foreach (var p in points)
            {
                float x = p.X;
                float y = p.Y;
                GL.Vertex3(x - size, y - size, 0);
                GL.Vertex3(x + size, y - size, 0);
                GL.Vertex3(x + size, y + size, 0);
                GL.Vertex3(x - size, y + size, 0);
            }

            GL.End();
        }

        private Vector3 ToVector3(Vec2 v) => new Vector3(v.X, v.Y, 0);
    }
}
