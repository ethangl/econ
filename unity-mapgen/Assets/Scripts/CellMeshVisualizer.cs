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
        public bool ShowCells = true;
        public bool ShowEdges = true;
        public bool ShowCenters = false;
        public bool ShowVertices = false;

        public Color CellColor = new Color(0.2f, 0.4f, 0.8f, 0.3f);
        public Color EdgeColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        public Color CenterColor = Color.red;
        public Color VertexColor = Color.green;

        private CellMeshGenerator _generator;
        private Material _glMaterial;

        void Awake()
        {
            _generator = GetComponent<CellMeshGenerator>();

            // Create simple material for GL drawing
            var shader = Shader.Find("Hidden/Internal-Colored");
            _glMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
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

            for (int c = 0; c < mesh.CellCount; c++)
            {
                var verts = mesh.CellVertices[c];
                if (verts == null || verts.Length < 3) continue;

                // Simple color variation per cell
                float hue = (c * 0.618034f) % 1f; // Golden ratio for distribution
                Color color = Color.HSVToRGB(hue, 0.5f, 0.7f);
                color.a = CellColor.a;
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
