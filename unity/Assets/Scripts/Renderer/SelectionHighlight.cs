using UnityEngine;
using EconSim.Core.Data;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Renders an outline around the currently selected cell.
    /// </summary>
    public class SelectionHighlight : MonoBehaviour
    {
        [Header("Highlight Settings")]
        [SerializeField] private float outlineWidth = 0.025f;
        [SerializeField] private float heightOffset = 0.02f;
        [SerializeField] private Color outlineColor = new Color(1f, 0.9f, 0.2f, 1f);  // Bright yellow

        [Header("References")]
        [SerializeField] private MapView mapView;
        [SerializeField] private Material outlineMaterial;

        private MapData mapData;
        private float cellScale;
        private float heightScale;

        private GameObject outlineObject;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh outlineMesh;

        private int selectedCellId = -1;

        public void Initialize(MapData data, float cellScale, float heightScale)
        {
            this.mapData = data;
            this.cellScale = cellScale;
            this.heightScale = heightScale;

            SetupMeshObject();

            // Subscribe to cell click events
            if (mapView != null)
            {
                mapView.OnCellClicked += OnCellClicked;
            }
        }

        private void OnDestroy()
        {
            if (mapView != null)
            {
                mapView.OnCellClicked -= OnCellClicked;
            }
        }

        private void SetupMeshObject()
        {
            outlineObject = new GameObject("SelectionOutline");
            outlineObject.transform.SetParent(transform, false);
            meshFilter = outlineObject.AddComponent<MeshFilter>();
            meshRenderer = outlineObject.AddComponent<MeshRenderer>();
            meshRenderer.material = outlineMaterial;
            outlineObject.SetActive(false);
        }

        private void OnCellClicked(int cellId)
        {
            if (cellId < 0 || mapData == null)
            {
                // Clicked on nothing - hide outline
                ClearSelection();
                return;
            }

            if (!mapData.CellById.TryGetValue(cellId, out var cell))
            {
                ClearSelection();
                return;
            }

            selectedCellId = cellId;
            GenerateOutline(cell);
            outlineObject.SetActive(true);
        }

        public void ClearSelection()
        {
            selectedCellId = -1;
            if (outlineObject != null)
            {
                outlineObject.SetActive(false);
            }
        }

        private void GenerateOutline(Cell cell)
        {
            if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                return;

            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var colors = new System.Collections.Generic.List<Color32>();

            Color32 col32 = outlineColor;
            float halfWidth = outlineWidth / 2f;

            // Get cell height
            float cellHeight = GetCellHeight(cell) + heightOffset;

            // Get polygon vertices in order
            var polyVerts = new System.Collections.Generic.List<Vector3>();
            foreach (int vIdx in cell.VertexIndices)
            {
                if (vIdx >= 0 && vIdx < mapData.Vertices.Count)
                {
                    Vector2 pos2D = mapData.Vertices[vIdx].ToUnity();
                    polyVerts.Add(new Vector3(
                        pos2D.x * cellScale,
                        cellHeight,
                        pos2D.y * cellScale
                    ));
                }
            }

            if (polyVerts.Count < 3)
                return;

            // Create quad strip around the polygon perimeter
            for (int i = 0; i < polyVerts.Count; i++)
            {
                Vector3 current = polyVerts[i];
                Vector3 next = polyVerts[(i + 1) % polyVerts.Count];

                Vector3 direction = (next - current).normalized;
                Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

                if (perpendicular.sqrMagnitude < 0.001f)
                {
                    perpendicular = Vector3.Cross(direction, Vector3.forward).normalized;
                }

                Vector3 offset = perpendicular * halfWidth;

                int baseIdx = vertices.Count;

                // Quad vertices for this edge
                vertices.Add(current - offset);
                vertices.Add(current + offset);
                vertices.Add(next + offset);
                vertices.Add(next - offset);

                colors.Add(col32);
                colors.Add(col32);
                colors.Add(col32);
                colors.Add(col32);

                // Two triangles
                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            // Create or update mesh
            if (outlineMesh == null)
            {
                outlineMesh = new Mesh();
                outlineMesh.name = "SelectionOutline";
            }
            outlineMesh.Clear();
            outlineMesh.SetVertices(vertices);
            outlineMesh.SetTriangles(triangles, 0);
            outlineMesh.SetColors(colors);
            outlineMesh.RecalculateNormals();
            outlineMesh.RecalculateBounds();

            meshFilter.mesh = outlineMesh;
        }

        private float GetCellHeight(Cell cell)
        {
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;
            return normalizedHeight * heightScale;
        }

        public int SelectedCellId => selectedCellId;
    }
}
