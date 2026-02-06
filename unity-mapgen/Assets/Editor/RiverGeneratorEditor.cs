using UnityEngine;
using UnityEditor;
using MapGen;

[CustomEditor(typeof(RiverGenerator))]
public class RiverGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var generator = (RiverGenerator)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Rivers"))
        {
            generator.Generate();
            SceneView.RepaintAll();
        }

        if (generator.RiverData != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("River Stats", EditorStyles.boldLabel);

            var data = generator.RiverData;

            EditorGUILayout.LabelField("Rivers", data.Rivers.Length.ToString());

            int totalSegments = 0;
            foreach (var r in data.Rivers)
                totalSegments += r.Vertices.Length - 1;
            EditorGUILayout.LabelField("River Segments", totalSegments.ToString());

            int lakes = 0;
            for (int v = 0; v < data.VertexCount; v++)
            {
                if (data.IsLake(v))
                    lakes++;
            }
            EditorGUILayout.LabelField("Lake Vertices", lakes.ToString());

            if (data.Rivers.Length > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Largest Rivers", EditorStyles.boldLabel);

                var sorted = new System.Collections.Generic.List<MapGen.Core.River>(data.Rivers);
                sorted.Sort((a, b) => b.Discharge.CompareTo(a.Discharge));

                int count = System.Math.Min(5, sorted.Count);
                for (int i = 0; i < count; i++)
                {
                    var r = sorted[i];
                    EditorGUILayout.LabelField(
                        $"  #{r.Id}",
                        $"discharge={r.Discharge:F0}, vertices={r.Vertices.Length}");
                }
            }
        }
    }
}
