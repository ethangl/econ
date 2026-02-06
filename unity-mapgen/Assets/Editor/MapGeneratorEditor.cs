using UnityEngine;
using UnityEditor;
using MapGen;

[CustomEditor(typeof(MapGenerator))]
public class MapGeneratorEditor : Editor
{
    private bool _showStats = true;
    private bool _showTerrain = true;
    private bool _showClimate = true;
    private bool _showRivers = true;
    private bool _showBiomes = true;
    private bool _showSoil = true;
    private bool _showVegetation = true;
    private bool _showResources = true;

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var generator = (MapGenerator)target;

        // Show derived map dimensions
        var (w, h) = CellMeshGenerator.ComputeMapSize(generator.CellCount, generator.AspectRatio);
        EditorGUILayout.HelpBox($"Map Size: {w:F0} x {h:F0} km ({w * h:F0} km²)", MessageType.Info);

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Map", GUILayout.Height(30)))
        {
            generator.Generate();
            SceneView.RepaintAll();
        }

        var stats = generator.Stats;
        if (!stats.IsValid) return;

        EditorGUILayout.Space();

        _showStats = EditorGUILayout.Foldout(_showStats, "Map Statistics", true, EditorStyles.foldoutHeader);
        if (!_showStats) return;

        EditorGUI.indentLevel++;

        // Terrain
        _showTerrain = EditorGUILayout.Foldout(_showTerrain, "Terrain", true);
        if (_showTerrain)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"{stats.CellCount} cells ({stats.LandCells} land, {stats.WaterCells} water)");
            EditorGUILayout.LabelField($"{stats.MapWidth:F0} x {stats.MapHeight:F0} km");
            EditorGUI.indentLevel--;
        }

        // Climate
        _showClimate = EditorGUILayout.Foldout(_showClimate, "Climate", true);
        if (_showClimate)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"Temp: [{stats.TempMin:F1}°C, {stats.TempMax:F1}°C]");
            EditorGUILayout.LabelField($"Precip (land): [{stats.PrecipLandMin:F1}, {stats.PrecipLandMax:F1}], avg {stats.PrecipLandAvg:F1}");
            EditorGUI.indentLevel--;
        }

        // Rivers
        _showRivers = EditorGUILayout.Foldout(_showRivers, "Rivers", true);
        if (_showRivers)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"{stats.RiverCount} rivers, {stats.RiverSegments} segments");
            EditorGUILayout.LabelField($"{stats.LakeVertices} lake vertices, {stats.LakeCells} lake cells, max flux {stats.MaxFlux:F0}");
            EditorGUI.indentLevel--;
        }

        // Biomes
        _showBiomes = EditorGUILayout.Foldout(_showBiomes, "Biomes", true);
        if (_showBiomes)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < stats.BiomeNames.Length; i++)
            {
                if (stats.BiomeCounts[i] > 0)
                    EditorGUILayout.LabelField(stats.BiomeNames[i], stats.BiomeCounts[i].ToString());
            }
            EditorGUI.indentLevel--;
        }

        // Soil
        _showSoil = EditorGUILayout.Foldout(_showSoil, "Soil", true);
        if (_showSoil)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < stats.SoilNames.Length; i++)
            {
                if (stats.SoilCounts[i] > 0)
                    EditorGUILayout.LabelField(stats.SoilNames[i], stats.SoilCounts[i].ToString());
            }
            EditorGUILayout.LabelField($"Fertility: avg {stats.FertilityAvg:F2}, max {stats.FertilityMax:F2}");
            EditorGUI.indentLevel--;
        }

        // Vegetation
        _showVegetation = EditorGUILayout.Foldout(_showVegetation, "Vegetation", true);
        if (_showVegetation)
        {
            EditorGUI.indentLevel++;
            for (int i = 0; i < stats.VegetationNames.Length; i++)
            {
                if (stats.VegetationCounts[i] > 0)
                    EditorGUILayout.LabelField(stats.VegetationNames[i], stats.VegetationCounts[i].ToString());
            }
            EditorGUILayout.LabelField($"Avg density: {stats.VegetationDensityAvg:F2}");
            EditorGUI.indentLevel--;
        }

        // Resources
        _showResources = EditorGUILayout.Foldout(_showResources, "Resources", true);
        if (_showResources)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Iron", $"{stats.IronCells} cells");
            EditorGUILayout.LabelField("Gold", $"{stats.GoldCells} cells");
            EditorGUILayout.LabelField("Lead", $"{stats.LeadCells} cells");
            EditorGUILayout.LabelField("Salt", $"{stats.SaltCells} cells");
            EditorGUILayout.LabelField("Stone", $"{stats.StoneCells} cells");
            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
    }
}
