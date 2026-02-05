using UnityEngine;
using UnityEditor;
using MapGen;

[CustomEditor(typeof(HeightmapGenerator))]
public class HeightmapGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var generator = (HeightmapGenerator)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Heightmap"))
        {
            generator.Generate();
            SceneView.RepaintAll();
        }

        // Show heightmap stats if available
        if (generator.HeightGrid != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Heightmap Stats", EditorStyles.boldLabel);

            var grid = generator.HeightGrid;
            var (land, water) = grid.CountLandWater();

            EditorGUILayout.LabelField("Cells", grid.CellCount.ToString());
            EditorGUILayout.LabelField("Land", $"{land} ({grid.LandRatio():P0})");
            EditorGUILayout.LabelField("Water", water.ToString());
        }
    }
}
