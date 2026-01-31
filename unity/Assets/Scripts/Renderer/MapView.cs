using System.Collections.Generic;
using UnityEngine;
using EconSim.Core.Data;
using EconSim.Bridge;

namespace EconSim.Renderer
{
    /// <summary>
    /// Main map renderer. Generates and displays the terrain mesh from map data.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MapView : MonoBehaviour
    {
        [Header("Rendering Settings")]
        [SerializeField] private float heightScale = 0.1f;
        [SerializeField] private float cellScale = 0.01f;  // Scale from Azgaar pixels to Unity units
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private bool renderLandOnly = true;

        [Header("Map Mode")]
        [SerializeField] private MapMode currentMode = MapMode.Political;

        [Header("Borders")]
        [SerializeField] private bool enableBorders = true;
        [SerializeField] private Material borderMaterial;

        private MapData mapData;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private BorderRenderer borderRenderer;

        // Cell mesh data
        private List<Vector3> vertices = new List<Vector3>();
        private List<int> triangles = new List<int>();
        private List<Color32> colors = new List<Color32>();

        public enum MapMode
        {
            Political,  // Colored by state (key: 1)
            Province,   // Colored by province (key: 2)
            Terrain,    // Colored by biome (key: 3)
            Height      // Colored by elevation (key: 4)
        }

        public MapMode CurrentMode => currentMode;
        public string CurrentModeName => ModeNames[(int)currentMode];

        private static readonly string[] ModeNames = { "Political", "Province", "Terrain", "Height" };

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void Update()
        {
            // Map mode selection with number keys 1-4
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                SetMapMode(MapMode.Political);
                Debug.Log("Map mode: Political (1)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                SetMapMode(MapMode.Province);
                Debug.Log("Map mode: Province (2)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                SetMapMode(MapMode.Terrain);
                Debug.Log("Map mode: Terrain (3)");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                SetMapMode(MapMode.Height);
                Debug.Log("Map mode: Height (4)");
            }
        }

        public void Initialize(MapData data)
        {
            mapData = data;
            GenerateMesh();
            InitializeBorders();
        }

        private void InitializeBorders()
        {
            if (!enableBorders || mapData == null) return;

            // Create or get BorderRenderer component
            borderRenderer = GetComponent<BorderRenderer>();
            if (borderRenderer == null)
            {
                borderRenderer = gameObject.AddComponent<BorderRenderer>();
            }

            // Set border material if we have one
            if (borderMaterial != null)
            {
                var borderMaterialField = typeof(BorderRenderer).GetField("borderMaterial",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                borderMaterialField?.SetValue(borderRenderer, borderMaterial);
            }

            borderRenderer.Initialize(mapData, cellScale, heightScale);
        }

        public void SetMapMode(MapMode mode)
        {
            if (currentMode != mode)
            {
                currentMode = mode;
                UpdateColors();
                UpdateBorderVisibility();
            }
        }

        private void UpdateBorderVisibility()
        {
            if (borderRenderer == null) return;

            switch (currentMode)
            {
                case MapMode.Political:
                    borderRenderer.SetStateBordersVisible(true);
                    borderRenderer.SetProvinceBordersVisible(false);
                    break;
                case MapMode.Province:
                    borderRenderer.SetStateBordersVisible(true);
                    borderRenderer.SetProvinceBordersVisible(true);
                    break;
                case MapMode.Terrain:
                case MapMode.Height:
                    borderRenderer.SetStateBordersVisible(false);
                    borderRenderer.SetProvinceBordersVisible(false);
                    break;
            }
        }

        public void SetStateBordersVisible(bool visible)
        {
            if (borderRenderer != null)
            {
                borderRenderer.SetStateBordersVisible(visible);
            }
        }

        public void SetProvinceBordersVisible(bool visible)
        {
            if (borderRenderer != null)
            {
                borderRenderer.SetProvinceBordersVisible(visible);
            }
        }

        private void GenerateMesh()
        {
            if (mapData == null)
            {
                Debug.LogError("MapView: No map data to render");
                return;
            }

            Debug.Log($"Generating mesh for {mapData.Cells.Count} cells...");

            vertices.Clear();
            triangles.Clear();
            colors.Clear();

            // Generate triangulated polygons for each cell
            int cellsRendered = 0;
            foreach (var cell in mapData.Cells)
            {
                if (renderLandOnly && !cell.IsLand)
                    continue;

                GenerateCellMesh(cell);
                cellsRendered++;
            }

            Debug.Log($"Generated mesh: {vertices.Count} vertices, {triangles.Count / 3} triangles, {cellsRendered} cells");

            // Create or update mesh
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "MapMesh";
            }
            mesh.Clear();

            // Use 32-bit indices for large meshes
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;

            if (terrainMaterial != null)
            {
                meshRenderer.material = terrainMaterial;
            }

            // Center the map
            CenterMap();
        }

        private void GenerateCellMesh(Cell cell)
        {
            if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                return;

            // Get cell color based on current map mode
            Color32 cellColor = GetCellColor(cell);

            // Get vertex positions for this cell's polygon
            var polyVerts = new List<Vector3>();
            foreach (int vIdx in cell.VertexIndices)
            {
                if (vIdx >= 0 && vIdx < mapData.Vertices.Count)
                {
                    Vector2 pos2D = mapData.Vertices[vIdx].ToUnity();
                    float height = GetCellHeight(cell);
                    polyVerts.Add(new Vector3(
                        pos2D.x * cellScale,
                        height,
                        -pos2D.y * cellScale  // Flip Y to Z, negate for Unity coords
                    ));
                }
            }

            if (polyVerts.Count < 3)
                return;

            // Fan triangulation from cell center
            Vector3 center = Vector3.zero;
            foreach (var v in polyVerts)
                center += v;
            center /= polyVerts.Count;

            // Adjust center height
            float centerHeight = GetCellHeight(cell);
            center.y = centerHeight;

            int centerIdx = vertices.Count;
            vertices.Add(center);
            colors.Add(cellColor);

            // Add polygon vertices and create triangles
            int firstPolyIdx = vertices.Count;
            for (int i = 0; i < polyVerts.Count; i++)
            {
                vertices.Add(polyVerts[i]);
                colors.Add(cellColor);
            }

            // Create fan triangles
            for (int i = 0; i < polyVerts.Count; i++)
            {
                int next = (i + 1) % polyVerts.Count;
                triangles.Add(centerIdx);
                triangles.Add(firstPolyIdx + i);
                triangles.Add(firstPolyIdx + next);
            }
        }

        private float GetCellHeight(Cell cell)
        {
            // Convert height (0-100, sea level 20) to world units
            float normalizedHeight = (cell.Height - mapData.Info.SeaLevel) / 80f;  // -0.25 to 1.0
            return normalizedHeight * heightScale;
        }

        private Color32 GetCellColor(Cell cell)
        {
            switch (currentMode)
            {
                case MapMode.Political:
                    return GetPoliticalColor(cell);
                case MapMode.Province:
                    return GetProvinceColor(cell);
                case MapMode.Terrain:
                    return GetTerrainColor(cell);
                case MapMode.Height:
                    return GetHeightColor(cell);
                default:
                    return new Color32(128, 128, 128, 255);
            }
        }

        private Color32 GetPoliticalColor(Cell cell)
        {
            if (cell.StateId > 0 && mapData.StateById.TryGetValue(cell.StateId, out var state))
            {
                return state.Color.ToUnity();
            }
            return new Color32(200, 200, 200, 255);  // Neutral/unclaimed
        }

        private Color32 GetProvinceColor(Cell cell)
        {
            if (cell.ProvinceId > 0 && mapData.ProvinceById.TryGetValue(cell.ProvinceId, out var province))
            {
                return province.Color.ToUnity();
            }
            return GetPoliticalColor(cell);  // Fall back to state color
        }

        private Color32 GetTerrainColor(Cell cell)
        {
            if (cell.BiomeId >= 0 && cell.BiomeId < mapData.Biomes.Count)
            {
                return mapData.Biomes[cell.BiomeId].Color.ToUnity();
            }
            return new Color32(100, 100, 100, 255);
        }

        private Color32 GetHeightColor(Cell cell)
        {
            // Gradient from blue (low) to green (mid) to brown (high) to white (peaks)
            float t = cell.Height / 100f;

            if (t < 0.2f)  // Water
            {
                float waterT = t / 0.2f;
                return Color32.Lerp(
                    new Color32(0, 30, 80, 255),    // Deep water
                    new Color32(50, 100, 150, 255), // Shallow water
                    waterT
                );
            }
            else if (t < 0.4f)  // Lowlands
            {
                float landT = (t - 0.2f) / 0.2f;
                return Color32.Lerp(
                    new Color32(80, 160, 80, 255),  // Coastal green
                    new Color32(120, 180, 80, 255), // Grassland
                    landT
                );
            }
            else if (t < 0.7f)  // Hills
            {
                float hillT = (t - 0.4f) / 0.3f;
                return Color32.Lerp(
                    new Color32(120, 180, 80, 255), // Grassland
                    new Color32(139, 119, 101, 255), // Brown hills
                    hillT
                );
            }
            else  // Mountains
            {
                float mtT = (t - 0.7f) / 0.3f;
                return Color32.Lerp(
                    new Color32(139, 119, 101, 255), // Brown
                    new Color32(240, 240, 250, 255), // Snow caps
                    mtT
                );
            }
        }

        private void CenterMap()
        {
            if (mapData == null) return;

            // Position the map so it's centered at origin
            float halfWidth = mapData.Info.Width * cellScale * 0.5f;
            float halfHeight = mapData.Info.Height * cellScale * 0.5f;

            transform.position = new Vector3(-halfWidth, 0, halfHeight);
        }

        private void UpdateColors()
        {
            if (mesh == null || mapData == null) return;

            colors.Clear();

            int vertexIndex = 0;
            foreach (var cell in mapData.Cells)
            {
                if (renderLandOnly && !cell.IsLand)
                    continue;

                if (cell.VertexIndices == null || cell.VertexIndices.Count < 3)
                    continue;

                Color32 cellColor = GetCellColor(cell);

                // Center vertex + polygon vertices
                int numVerts = 1 + cell.VertexIndices.Count;
                for (int i = 0; i < numVerts; i++)
                {
                    colors.Add(cellColor);
                }
            }

            mesh.SetColors(colors);
        }

#if UNITY_EDITOR
        [ContextMenu("Regenerate Mesh")]
        private void RegenerateMesh()
        {
            if (mapData != null)
            {
                GenerateMesh();
            }
        }

        [ContextMenu("Set Mode: Political")]
        private void SetModePolitical() => SetMapMode(MapMode.Political);

        [ContextMenu("Set Mode: Province")]
        private void SetModeProvince() => SetMapMode(MapMode.Province);

        [ContextMenu("Set Mode: Terrain")]
        private void SetModeTerrain() => SetMapMode(MapMode.Terrain);

        [ContextMenu("Set Mode: Height")]
        private void SetModeHeight() => SetMapMode(MapMode.Height);

        [ContextMenu("Toggle State Borders")]
        private void ToggleStateBorders()
        {
            if (borderRenderer != null)
            {
                var field = typeof(BorderRenderer).GetField("showStateBorders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    bool current = (bool)field.GetValue(borderRenderer);
                    borderRenderer.SetStateBordersVisible(!current);
                }
            }
        }

        [ContextMenu("Toggle Province Borders")]
        private void ToggleProvinceBorders()
        {
            if (borderRenderer != null)
            {
                var field = typeof(BorderRenderer).GetField("showProvinceBorders",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    bool current = (bool)field.GetValue(borderRenderer);
                    borderRenderer.SetProvinceBordersVisible(!current);
                }
            }
        }
#endif
    }
}
