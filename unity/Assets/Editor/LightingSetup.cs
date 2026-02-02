using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class LightingSetup
{
    [MenuItem("Tools/Enable Ambient Lighting")]
    public static void EnableAmbientLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.white;
        Debug.Log("Ambient lighting enabled: Flat mode, white color");
    }
    
    [MenuItem("Tools/Disable Ambient Lighting")]
    public static void DisableAmbientLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientLight = Color.gray;
        Debug.Log("Ambient lighting disabled: Skybox mode");
    }
}
