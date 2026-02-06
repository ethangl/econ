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
        [System.NonSerialized] public HeightmapTemplateType Template = HeightmapTemplateType.LowIsland;
        [System.NonSerialized] public int HeightmapSeed = 42;

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

        private string GetScript()
        {
            return HeightmapTemplates.GetTemplate(Template.ToString());
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
