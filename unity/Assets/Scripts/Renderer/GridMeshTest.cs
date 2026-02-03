using UnityEngine;
using EconSim.Core.Import;
using EconSim.Core.Data;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EconSim.Renderer
{
    /// <summary>
    /// Minimal test script to verify grid mesh can sample the data texture correctly.
    /// Creates a flat grid mesh and displays it in county mode to verify UV mapping.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class GridMeshTest : MonoBehaviour
    {
        // Hardcoded paths - do NOT use [SerializeField] until feature is complete
        private const string MapFileName = "preston.json";
        private const string MaterialPath = "Assets/Materials/MapMaterila.mat";  // Note: typo in actual filename
        private const float CellScale = 0.01f;

        // Set to true to use a simple debug material instead of the terrain shader
        private const bool UseDebugMaterial = false;

        // Grid resolution: 0.1x source map (192x108 for 1920x1080)
        private const int GridWidth = 192;
        private const int GridHeight = 108;

        private Material terrainMaterial;
        private MapData mapData;
        private MapOverlayManager overlayManager;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;

        void Start()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            LoadMapData();
            LoadMaterial();
            InitializeOverlays();
            GenerateGridMesh();
            SetCountyMode();
            SetupCamera();
        }

        /// <summary>
        /// Configure the main camera to view the mesh correctly.
        /// </summary>
        private void SetupCamera()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                Debug.LogError("GridMeshTest: No main camera found!");
                return;
            }

            // Position camera to look down at the map center
            float worldWidth = mapData.Info.Width * CellScale;
            float worldHeight = mapData.Info.Height * CellScale;

            cam.transform.position = new Vector3(worldWidth / 2f, 20f, -worldHeight / 2f);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            cam.orthographic = true;
            cam.orthographicSize = worldHeight / 2f + 0.5f;  // Half height + margin
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.cullingMask = -1;  // Everything

            Debug.Log($"GridMeshTest: Camera configured at ({cam.transform.position}), ortho size {cam.orthographicSize}");
        }

        private void OnDestroy()
        {
            overlayManager?.Dispose();
            if (mesh != null)
                Destroy(mesh);
        }

        /// <summary>
        /// Load map data from the reference folder (same approach as GameManager).
        /// </summary>
        private void LoadMapData()
        {
            // Application.dataPath is unity/Assets, so go up two levels to reach project root
            string filePath = System.IO.Path.Combine(Application.dataPath, "..", "..", "reference", MapFileName);

            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"GridMeshTest: Map file not found: {filePath}");
                return;
            }

            var azgaarMap = AzgaarParser.ParseFile(filePath);
            mapData = MapConverter.Convert(azgaarMap);

            Debug.Log($"GridMeshTest: Loaded map {mapData.Info.Name} ({mapData.Info.Width}x{mapData.Info.Height})");
        }

        /// <summary>
        /// Load the terrain material (editor-only, since material not in Resources).
        /// </summary>
        private void LoadMaterial()
        {
            if (UseDebugMaterial)
            {
                // Create a simple unlit material for debugging
                terrainMaterial = new Material(Shader.Find("Unlit/Color"));
                terrainMaterial.color = Color.magenta;
                meshRenderer.material = terrainMaterial;
                Debug.Log("GridMeshTest: Using debug unlit material (magenta)");
                return;
            }

#if UNITY_EDITOR
            var loadedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (loadedMaterial == null)
            {
                Debug.LogError($"GridMeshTest: Failed to load material: {MaterialPath}");
                return;
            }
            // Use sharedMaterial to avoid creating an instance - we want the overlay manager
            // to set textures on the same material the renderer uses
            meshRenderer.sharedMaterial = loadedMaterial;
            terrainMaterial = meshRenderer.sharedMaterial;
            Debug.Log($"GridMeshTest: Loaded terrain material, shader: {terrainMaterial.shader.name}");
#else
            Debug.LogWarning("GridMeshTest: Material loading only works in editor");
#endif
        }

        /// <summary>
        /// Initialize overlay manager (generates data textures).
        /// </summary>
        private void InitializeOverlays()
        {
            if (mapData == null || terrainMaterial == null)
            {
                Debug.LogError("GridMeshTest: Cannot initialize overlays - missing map data or material");
                return;
            }

            // Use resolution multiplier of 4 (same as default MapView)
            overlayManager = new MapOverlayManager(mapData, terrainMaterial, 4);
            Debug.Log("GridMeshTest: Initialized overlay manager");
        }

        /// <summary>
        /// Generate a flat grid mesh with correct UVs for data texture sampling.
        /// </summary>
        private void GenerateGridMesh()
        {
            if (mapData == null)
            {
                Debug.LogError("GridMeshTest: Cannot generate mesh - missing map data");
                return;
            }

            mesh = new Mesh();
            mesh.name = "GridMeshTest";

            // World size (matching existing scale from MapView)
            float worldWidth = mapData.Info.Width * CellScale;   // 14.4 units for 1440 width
            float worldHeight = mapData.Info.Height * CellScale; // 8.1 units for 810 height

            // +1 to vertex count because we need corners (grid cells have 4 corners each)
            int vertCountX = GridWidth + 1;
            int vertCountY = GridHeight + 1;
            int totalVerts = vertCountX * vertCountY;

            var vertices = new Vector3[totalVerts];
            var uv0 = new Vector2[totalVerts];  // UV0 for heightmap (Unity coords, Y-flipped)
            var uv1 = new Vector2[totalVerts];  // UV1 for data texture (Azgaar coords)

            // Generate vertices and UVs
            for (int y = 0; y <= GridHeight; y++)
            {
                for (int x = 0; x <= GridWidth; x++)
                {
                    int idx = y * vertCountX + x;

                    // Normalized position (0-1 range)
                    float u = (float)x / GridWidth;
                    float v = (float)y / GridHeight;

                    // World position: X increases right, Z decreases (more negative) as we go down
                    // This matches the existing Voronoi mesh convention
                    float worldX = u * worldWidth;
                    float worldZ = -v * worldHeight;  // Negate Y for Unity Z axis

                    vertices[idx] = new Vector3(worldX, 0f, worldZ);

                    // UV0 for heightmap: Y-flipped to match Unity texture coordinates
                    // Heightmap texture was Y-flipped during generation (Azgaar y=0 at top -> Unity y=height-1)
                    uv0[idx] = new Vector2(u, 1f - v);

                    // UV1 for data texture: Azgaar coordinates (no flip)
                    // Data texture is in Azgaar coordinates where y=0 is at top
                    uv1[idx] = new Vector2(u, v);
                }
            }

            // Generate triangles (two per grid cell)
            int totalTris = GridWidth * GridHeight * 6;  // 2 triangles * 3 indices each
            var triangles = new int[totalTris];
            int triIdx = 0;

            for (int y = 0; y < GridHeight; y++)
            {
                for (int x = 0; x < GridWidth; x++)
                {
                    // Vertex indices for this quad
                    int bl = y * vertCountX + x;         // Bottom-left
                    int br = bl + 1;                      // Bottom-right
                    int tl = (y + 1) * vertCountX + x;   // Top-left
                    int tr = tl + 1;                      // Top-right

                    // Triangle 1: BL, TR, TL (reversed winding for top-down view)
                    triangles[triIdx++] = bl;
                    triangles[triIdx++] = tr;
                    triangles[triIdx++] = tl;

                    // Triangle 2: BL, BR, TR (reversed winding for top-down view)
                    triangles[triIdx++] = bl;
                    triangles[triIdx++] = br;
                    triangles[triIdx++] = tr;
                }
            }

            // Default vertex colors - dark ocean blue for water areas
            var colors = new Color32[totalVerts];
            var oceanColor = new Color32(30, 50, 90, 255);  // Dark ocean blue
            for (int i = 0; i < totalVerts; i++)
                colors[i] = oceanColor;

            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.uv = uv0;    // UV0 for heightmap (shader texcoord0)
            mesh.uv2 = uv1;   // UV1 for data texture (shader texcoord1)
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            meshFilter.mesh = mesh;

            Debug.Log($"GridMeshTest: Generated {GridWidth}x{GridHeight} grid mesh ({totalVerts} verts, {totalTris/3} tris)");
            Debug.Log($"GridMeshTest: World size: {worldWidth}x{worldHeight} units");
        }

        /// <summary>
        /// Set shader to county mode (mode 3) to verify cell-level sampling works.
        /// </summary>
        private void SetCountyMode()
        {
            if (overlayManager == null)
            {
                Debug.LogError("GridMeshTest: Cannot set mode - missing overlay manager");
                return;
            }

            overlayManager.SetMapMode(MapView.MapMode.County);
            overlayManager.SetStateBordersVisible(true);
            overlayManager.SetProvinceBordersVisible(true);

            // Enable height displacement for 3D terrain
            overlayManager.SetHeightDisplacementEnabled(true);
            overlayManager.SetHeightScale(3f);
            overlayManager.SetSeaLevel(0.2f);

            Debug.Log("GridMeshTest: Set to County mode with borders and height displacement");
        }

    }
}
