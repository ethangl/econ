using UnityEngine;
using UnityEditor;

namespace MapGen.Editor
{
    [CustomEditor(typeof(CellMeshGenerator))]
    public class CellMeshGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (CellMeshGenerator)target;

            EditorGUILayout.Space();

            if (GUILayout.Button("Generate"))
            {
                generator.Generate();
                SceneView.RepaintAll();
            }

            if (generator.Mesh != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Mesh Stats", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"Cells: {generator.Mesh.CellCount}");
                EditorGUILayout.LabelField($"Vertices: {generator.Mesh.VertexCount}");
                EditorGUILayout.LabelField($"Edges: {generator.Mesh.EdgeCount}");
            }
        }
    }
}
