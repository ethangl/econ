using UnityEngine;

namespace EconSim.Renderer
{
    [ExecuteAlways]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class ScreenNoiseOverlay : MonoBehaviour
    {
        [Range(0f, 1f)]
        public float intensity = 0.08f;

        private Material _material;

        private void OnEnable()
        {
            Debug.Log("[ScreenNoiseOverlay] Enabled on " + gameObject.name);
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (_material == null)
            {
                var shader = Shader.Find("EconSim/ScreenNoiseOverlay");
                if (shader == null)
                {
                    Debug.LogError("[ScreenNoiseOverlay] Shader not found!");
                    Graphics.Blit(source, destination);
                    return;
                }
                _material = new Material(shader);
                Debug.Log("[ScreenNoiseOverlay] Material created, intensity=" + intensity);
            }

            _material.SetFloat("_NoiseIntensity", intensity);

            Graphics.Blit(source, destination, _material);
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                if (Application.isPlaying)
                    Destroy(_material);
                else
                    DestroyImmediate(_material);
            }
        }
    }
}
