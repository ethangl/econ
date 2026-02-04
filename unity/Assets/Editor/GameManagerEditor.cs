using UnityEngine;
using UnityEditor;
using EconSim.Core;

namespace EconSim.Editor
{
    [CustomEditor(typeof(GameManager))]
    public class GameManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Cache", EditorStyles.boldLabel);

            if (GUILayout.Button("Clear Map Cache"))
            {
                ClearMapCache();
            }
        }

        private void ClearMapCache()
        {
            string cacheDir = System.IO.Path.Combine(Application.persistentDataPath, "MapCache");
            if (System.IO.Directory.Exists(cacheDir))
            {
                var files = System.IO.Directory.GetFiles(cacheDir);
                foreach (var file in files)
                {
                    System.IO.File.Delete(file);
                    Debug.Log($"Deleted cache file: {System.IO.Path.GetFileName(file)}");
                }
                Debug.Log($"Cleared {files.Length} cache file(s) from {cacheDir}");
            }
            else
            {
                Debug.Log("No cache directory found.");
            }
        }
    }
}
