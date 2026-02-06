using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    /// <summary>
    /// Unity component for generating and visualizing heightmaps.
    /// Works with CellMeshGenerator to produce terrain.
    /// </summary>
    [RequireComponent(typeof(CellMeshGenerator))]
    public class HeightmapGenerator : MonoBehaviour
    {
        [Header("Template")]
        [Tooltip("Predefined template to use")]
        public HeightmapTemplateType Template = HeightmapTemplateType.LowIsland;

        [Tooltip("Random seed for heightmap generation")]
        public int HeightmapSeed = 42;

        [Header("Custom Script")]
        [Tooltip("Custom DSL script (overrides template if not empty)")]
        [TextArea(5, 15)]
        public string CustomScript = "";

        [Header("Debug")]
        [Tooltip("Show height values in scene view")]
        public bool ShowDebugHeights = false;

        private CellMeshGenerator _meshGenerator;
        private HeightGrid _heightGrid;

        public HeightGrid HeightGrid => _heightGrid;

        void Awake()
        {
            _meshGenerator = GetComponent<CellMeshGenerator>();
        }

        /// <summary>
        /// Generate heightmap for the current cell mesh.
        /// </summary>
        public void Generate()
        {
            if (_meshGenerator == null)
                _meshGenerator = GetComponent<CellMeshGenerator>();

            if (_meshGenerator == null || _meshGenerator.Mesh == null)
            {
                Debug.LogError("HeightmapGenerator: No cell mesh available. Generate mesh first.");
                return;
            }

            var mesh = _meshGenerator.Mesh;
            _heightGrid = new HeightGrid(mesh);

            // Get script from template or custom
            string script = GetScript();
            if (string.IsNullOrWhiteSpace(script))
            {
                Debug.LogWarning("HeightmapGenerator: No script to execute.");
                return;
            }

            // Execute the DSL script
            try
            {
                HeightmapDSL.Execute(_heightGrid, script, HeightmapSeed);

                var (land, water) = _heightGrid.CountLandWater();
                float landRatio = _heightGrid.LandRatio();
                Debug.Log($"Heightmap generated: {land} land, {water} water ({landRatio:P0})");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"HeightmapGenerator: Script error - {e.Message}");
            }
        }

        /// <summary>
        /// Get the script to execute (custom or from template).
        /// </summary>
        private string GetScript()
        {
            if (!string.IsNullOrWhiteSpace(CustomScript))
                return CustomScript;

            return HeightmapTemplates.GetTemplate(Template.ToString());
        }

        void OnDrawGizmosSelected()
        {
            if (!ShowDebugHeights || _heightGrid == null || _meshGenerator?.Mesh == null)
                return;

            var mesh = _meshGenerator.Mesh;
            float maxH = HeightGrid.MaxHeight;

            for (int i = 0; i < mesh.CellCount; i++)
            {
                Vec2 center = mesh.CellCenters[i];
                float h = _heightGrid.Heights[i];

                // Color: blue (water) to green to brown to white (high elevation)
                Color color;
                if (h <= HeightGrid.SeaLevel)
                {
                    // Water: dark blue to light blue
                    float wt = h / HeightGrid.SeaLevel;
                    color = Color.Lerp(new Color(0, 0, 0.3f), new Color(0.2f, 0.4f, 0.8f), wt);
                }
                else
                {
                    // Land: green to brown to white
                    float lt = (h - HeightGrid.SeaLevel) / (maxH - HeightGrid.SeaLevel);
                    if (lt < 0.5f)
                        color = Color.Lerp(new Color(0.2f, 0.6f, 0.2f), new Color(0.5f, 0.35f, 0.2f), lt * 2f);
                    else
                        color = Color.Lerp(new Color(0.5f, 0.35f, 0.2f), Color.white, (lt - 0.5f) * 2f);
                }

                Gizmos.color = color;
                Gizmos.DrawSphere(new Vector3(center.X, center.Y, 0), 6f);
            }
        }
    }

    /// <summary>
    /// Available heightmap templates.
    /// </summary>
    public enum HeightmapTemplateType
    {
        Volcano,
        LowIsland,
        Archipelago,
        Continents,
        Pangea,
        HighIsland,
        Atoll,
        Peninsula,
        Mediterranean,
        Isthmus,
        Shattered,
        Taklamakan,
        OldWorld,
        Fractious
    }
}
