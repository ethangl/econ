using UnityEngine;
using UnityEditor;
using MapGen;
using MapGen.Core;

[CustomEditor(typeof(BiomeGenerator))]
public class BiomeGeneratorEditor : Editor
{
    private int _hoveredCell = -1;
    private string _hoverText;

    void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var generator = (BiomeGenerator)target;

        if (generator.BiomeData != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Hover over cells in Scene view for details", EditorStyles.miniLabel);
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        var generator = target as BiomeGenerator;
        if (generator == null) return;

        var biomeData = generator.BiomeData;
        if (biomeData == null) return;

        var mesh = biomeData.Mesh;
        if (mesh == null) return;

        // Convert mouse position to world XY
        Vector2 mousePos = Event.current.mousePosition;
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePos);
        // Flat map at Z=0: solve ray.origin.z + t * ray.direction.z = 0
        float t = -ray.origin.z / ray.direction.z;
        Vector2 worldPos = new Vector2(
            ray.origin.x + t * ray.direction.x,
            ray.origin.y + t * ray.direction.y
        );

        // Find nearest cell (Voronoi = nearest center)
        int nearest = -1;
        float nearestDist = float.MaxValue;
        for (int i = 0; i < mesh.CellCount; i++)
        {
            float dx = mesh.CellCenters[i].X - worldPos.x;
            float dy = mesh.CellCenters[i].Y - worldPos.y;
            float dist = dx * dx + dy * dy;
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = i;
            }
        }

        if (nearest < 0) return;

        // Only rebuild string when cell changes
        if (nearest != _hoveredCell)
        {
            _hoveredCell = nearest;
            _hoverText = FormatCellInfo(nearest, biomeData, generator);
        }

        // Draw status bar at bottom of scene view
        Handles.BeginGUI();
        var rect = new Rect(0, sceneView.position.height - 48, sceneView.position.width, 20);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(rect, " " + _hoverText, EditorStyles.miniLabel);
        Handles.EndGUI();

        // Force repaint on mouse move so the bar updates
        if (Event.current.type == EventType.MouseMove)
            sceneView.Repaint();
    }

    private static string FormatCellInfo(int cell, BiomeData data, BiomeGenerator gen)
    {
        var heightGrid = gen.GetComponent<HeightmapGenerator>()?.HeightGrid;
        bool isWater = heightGrid != null && heightGrid.IsWater(cell);

        if (isWater)
        {
            float h = heightGrid != null ? heightGrid.Heights[cell] : 0f;
            return $"Cell {cell}  |  Water  |  Height: {h:F1}";
        }

        var soil = data.Soil[cell];
        var biome = data.Biome[cell];
        var veg = data.Vegetation[cell];
        float density = data.VegetationDensity[cell];
        float fertility = data.Fertility[cell];
        float habit = data.Habitability[cell];
        float height = heightGrid != null ? heightGrid.Heights[cell] : 0f;

        var sb = new System.Text.StringBuilder();
        sb.Append($"Cell {cell}  |  {biome}  |  {soil} soil  |  {veg} ({density:P0})  |  Fertility: {fertility:F2}  |  Habitability: {habit:F0}  |  Height: {height:F1}");

        // Append non-zero resources
        if (data.IronAbundance[cell] > 0.01f) sb.Append($"  |  Iron: {data.IronAbundance[cell]:F2}");
        if (data.GoldAbundance[cell] > 0.01f) sb.Append($"  |  Gold: {data.GoldAbundance[cell]:F2}");
        if (data.LeadAbundance[cell] > 0.01f) sb.Append($"  |  Lead: {data.LeadAbundance[cell]:F2}");
        if (data.SaltAbundance[cell] > 0.01f) sb.Append($"  |  Salt: {data.SaltAbundance[cell]:F2}");
        if (data.StoneAbundance[cell] > 0.01f) sb.Append($"  |  Stone: {data.StoneAbundance[cell]:F2}");

        return sb.ToString();
    }
}
