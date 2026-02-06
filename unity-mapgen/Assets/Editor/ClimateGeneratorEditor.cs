using UnityEngine;
using UnityEditor;
using MapGen;

[CustomEditor(typeof(ClimateGenerator))]
public class ClimateGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var generator = (ClimateGenerator)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Climate"))
        {
            generator.Generate();
            SceneView.RepaintAll();
        }

        // Show climate stats if available
        if (generator.ClimateData != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Climate Stats", EditorStyles.boldLabel);

            var data = generator.ClimateData;
            var (tMin, tMax) = data.TemperatureRange();
            var (pMin, pMax) = data.PrecipitationRange();

            EditorGUILayout.LabelField("Cells", data.CellCount.ToString());
            EditorGUILayout.LabelField("Temperature", $"{tMin:F1}°C to {tMax:F1}°C");
            EditorGUILayout.LabelField("Precipitation", $"{pMin:F0} to {pMax:F0}");

            if (generator.Config != null)
            {
                var config = generator.Config;
                EditorGUILayout.LabelField("Latitude", $"{config.LatitudeSouth:F1}° to {config.LatitudeNorth:F1}°");
                EditorGUILayout.LabelField("Scale", "1 mesh unit = 1 km");
            }
        }
    }
}
