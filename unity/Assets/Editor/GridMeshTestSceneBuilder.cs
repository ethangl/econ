using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using EconSim.Renderer;

namespace EconSim.Editor
{
    /// <summary>
    /// Creates a test scene for GridMeshTest. Use Window > EconSim > Create Grid Mesh Test Scene.
    /// </summary>
    public static class GridMeshTestSceneBuilder
    {
        [MenuItem("Window/EconSim/Create Grid Mesh Test Scene")]
        public static void CreateTestScene()
        {
            // Create a new empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 1. Create Camera (looking down at map area)
            var cameraObj = new GameObject("Main Camera");
            cameraObj.tag = "MainCamera";
            var camera = cameraObj.AddComponent<UnityEngine.Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.19f, 0.30f, 0.47f);  // Ocean blue
            camera.orthographic = true;
            camera.orthographicSize = 6f;  // ~8.1 / 2 + margin to see whole map height
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            // Position camera to look down at the map center
            // Map is ~14.4 x 8.1 units, so center is around (7.2, 0, -4.05)
            cameraObj.transform.position = new Vector3(7.2f, 20f, -4.05f);
            cameraObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);  // Look straight down

            // 2. Create Directional Light
            var lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.intensity = 1.5f;
            light.shadows = LightShadows.Soft;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Set ambient light
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0f;

            // 3. Create GridMeshTest object
            var testObj = new GameObject("GridMeshTest");
            testObj.AddComponent<MeshFilter>();
            testObj.AddComponent<MeshRenderer>();
            testObj.AddComponent<GridMeshTest>();

            // Mark scene as dirty so it prompts to save
            EditorSceneManager.MarkSceneDirty(scene);

            // Save the scene
            string scenePath = "Assets/Scenes/GridMeshTest.unity";
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);

            if (saved)
            {
                Debug.Log($"GridMeshTest scene created and saved to {scenePath}");
                Debug.Log("Press Play to run the test. Expected result: Map colored by county with province borders.");
            }
            else
            {
                Debug.LogWarning("Scene created but not saved. Save manually to Assets/Scenes/GridMeshTest.unity");
            }
        }
    }
}
