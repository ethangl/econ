using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Debug visualization for CellMesh using Unity's GL immediate mode.
    /// </summary>
    [RequireComponent(typeof(CellMeshGenerator))]
    public class CellMeshVisualizer : MonoBehaviour
    {
        [Header("Display Options")]
        public bool ShowCells = true;
        public bool ShowEdges = false;
        public bool ShowCenters = false;
        public bool ShowVertices = false;

        [Header("Coloring")]
        [Tooltip("Use heightmap colors if HeightmapGenerator is present")]
        public bool UseHeightmapColors = true;

        public Color CellColor = new Color(0.2f, 0.4f, 0.8f, 1f);
        public Color EdgeColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        public Color CenterColor = Color.red;
        public Color VertexColor = Color.green;

        private CellMeshGenerator _generator;
        private HeightmapGenerator _heightmapGenerator;
        private Material _glMaterial;

        void Awake()
        {
            _generator = GetComponent<CellMeshGenerator>();
            _heightmapGenerator = GetComponent<HeightmapGenerator>();

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

            var heightGrid = UseHeightmapColors ? _heightmapGenerator?.HeightGrid : null;

            for (int c = 0; c < mesh.CellCount; c++)
            {
                var verts = mesh.CellVertices[c];
                if (verts == null || verts.Length < 3) continue;

                Color color;
                if (heightGrid != null)
                {
                    // Heightmap coloring
                    color = GetHeightColor(heightGrid.Heights[c]);
                }
                else
                {
                    // Simple color variation per cell
                    float hue = (c * 0.618034f) % 1f; // Golden ratio for distribution
                    color = Color.HSVToRGB(hue, 0.5f, 0.7f);
                }
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
