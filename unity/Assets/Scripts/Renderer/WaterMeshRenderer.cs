using UnityEngine;
using EconSim.Core.Data;

namespace EconSim.Renderer
{
    /// <summary>
    /// Manages the water mesh (rivers + coasts) lifecycle.
    /// Created as a child GameObject of MapView, similar to RealmCapitalMarkers.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterMeshRenderer : MonoBehaviour
    {
        public float RiverMinHalfWidth = WaterMeshBuilder.DefaultRiverMinHalfWidth;
        public float RiverMaxHalfWidth = WaterMeshBuilder.DefaultRiverMaxHalfWidth;
        public float CoastHalfWidth = WaterMeshBuilder.DefaultCoastHalfWidth;

        private Mesh waterMesh;
        private Material waterMaterial;

        // Stored build parameters for rebuild
        private MapData cachedMapData;
        private float cachedCellScale;
        private float cachedGridHeightScale;
        private MapOverlayManager.NoisyEdgeStyle cachedNoisyEdgeStyle;
        private uint cachedRootSeed;

        // Track previous values for change detection
        private float prevRiverMin;
        private float prevRiverMax;
        private float prevCoastWidth;

        public void Initialize(
            MapData mapData,
            float cellScale,
            float gridHeightScale,
            MapOverlayManager.NoisyEdgeStyle noisyEdgeStyle,
            uint rootSeed)
        {
            cachedMapData = mapData;
            cachedCellScale = cellScale;
            cachedGridHeightScale = gridHeightScale;
            cachedNoisyEdgeStyle = noisyEdgeStyle;
            cachedRootSeed = rootSeed;

            prevRiverMin = RiverMinHalfWidth;
            prevRiverMax = RiverMaxHalfWidth;
            prevCoastWidth = CoastHalfWidth;

            RebuildMesh();
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
                cachedMapData, cachedCellScale, cachedGridHeightScale,
                cachedNoisyEdgeStyle, cachedRootSeed,
                RiverMinHalfWidth, RiverMaxHalfWidth, CoastHalfWidth);

            if (waterMesh == null)
            {
                Debug.Log("WaterMeshRenderer: No water edges to render.");
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
                RiverMaxHalfWidth != prevRiverMax ||
                CoastHalfWidth != prevCoastWidth)
            {
                prevRiverMin = RiverMinHalfWidth;
                prevRiverMax = RiverMaxHalfWidth;
                prevCoastWidth = CoastHalfWidth;
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
