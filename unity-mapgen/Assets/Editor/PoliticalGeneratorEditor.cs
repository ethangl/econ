using UnityEngine;
using UnityEditor;
using MapGen;
using MapGen.Core;

[CustomEditor(typeof(PoliticalGenerator))]
public class PoliticalGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var generator = (PoliticalGenerator)target;
        var data = generator.PoliticalData;

        if (data != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Political", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                $"{data.RealmCount} realms, {data.ProvinceCount} provinces, {data.CountyCount} counties");
            EditorGUILayout.LabelField(
                $"{data.LandmassCount} landmasses ({data.QualifyingLandmasses} qualifying)");

            if (data.RealmCount > 0)
            {
                int assignedCells = 0;
                for (int i = 0; i < data.CellCount; i++)
                    if (data.RealmId[i] > 0) assignedCells++;

                float avgRealm = data.RealmCount > 0 ? (float)assignedCells / data.RealmCount : 0;
                float avgProv = data.ProvinceCount > 0 ? (float)assignedCells / data.ProvinceCount : 0;
                float avgCounty = data.CountyCount > 0 ? (float)assignedCells / data.CountyCount : 0;

                EditorGUILayout.LabelField(
                    $"Avg: {avgRealm:F0} cells/realm, {avgProv:F0} cells/province, {avgCounty:F0} cells/county");
            }

            EditorGUI.indentLevel--;
        }
    }
}
