using UnityEngine;
using EconSim.Core.Data;

namespace EconSim.Renderer
{
    /// <summary>
    /// Manages the water mesh (rivers + water bodies) lifecycle.
    /// Created as a child GameObject of MapView, similar to RealmCapitalMarkers.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterMeshRenderer : MonoBehaviour
    {
        public float RiverMinHalfWidth = WaterMeshBuilder.DefaultRiverMinHalfWidth;
        public float RiverMaxHalfWidth = WaterMeshBuilder.DefaultRiverMaxHalfWidth;

        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");

        private Mesh waterMesh;
        private Material waterMaterial;

        // Stored build parameters for rebuild
        private MapData cachedMapData;
        private float cachedCellScale;
        private MapOverlayManager.NoisyEdgeStyle cachedNoisyEdgeStyle;
        private uint cachedRootSeed;

        // Track previous values for change detection
        private float prevRiverMin;
        private float prevRiverMax;

        public void Initialize(
            MapData mapData,
            float cellScale,
            MapOverlayManager.NoisyEdgeStyle noisyEdgeStyle,
            uint rootSeed)
        {
            cachedMapData = mapData;
            cachedCellScale = cellScale;
            cachedNoisyEdgeStyle = noisyEdgeStyle;
            cachedRootSeed = rootSeed;

            prevRiverMin = RiverMinHalfWidth;
            prevRiverMax = RiverMaxHalfWidth;

            RebuildMesh();
        }

        /// <summary>
        /// Set height displacement parameters to match the terrain shader.
        /// Called each frame by MapView during height scale animation.
        /// </summary>
        public void SetHeightScale(float heightScale, float seaLevel01)
        {
            if (waterMaterial == null)
                return;
            waterMaterial.SetFloat(HeightScaleId, heightScale);
            waterMaterial.SetFloat(SeaLevelId, seaLevel01);
        }

        private void RebuildMesh()
        {
            if (waterMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(waterMesh);
                else
                    DestroyImmediate(waterMesh);
                waterMesh = null;
            }

            waterMesh = WaterMeshBuilder.Build(
                cachedMapData, cachedCellScale,
                cachedNoisyEdgeStyle, cachedRootSeed,
                RiverMinHalfWidth, RiverMaxHalfWidth);

            if (waterMesh == null)
            {
                Debug.Log("WaterMeshRenderer: No water geometry to render.");
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);

            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = waterMesh;

            EnsureMaterial();

            var mr = GetComponent<MeshRenderer>();
            mr.sharedMaterial = waterMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void Update()
        {
            if (cachedMapData == null)
                return;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (RiverMinHalfWidth != prevRiverMin ||
                RiverMaxHalfWidth != prevRiverMax)
            {
                prevRiverMin = RiverMinHalfWidth;
                prevRiverMax = RiverMaxHalfWidth;
                RebuildMesh();
            }
        }

        private void EnsureMaterial()
        {
            if (waterMaterial != null)
                return;

            Shader shader = Shader.Find("EconSim/WaterMesh");
            if (shader == null)
            {
                Debug.LogWarning("WaterMeshRenderer: Could not find EconSim/WaterMesh shader.");
                return;
            }

            waterMaterial = new Material(shader);
        }

        private void OnDestroy()
        {
            if (waterMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(waterMesh);
                else
                    DestroyImmediate(waterMesh);
                waterMesh = null;
            }

            if (waterMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(waterMaterial);
                else
                    DestroyImmediate(waterMaterial);
                waterMaterial = null;
            }
        }
    }
}
