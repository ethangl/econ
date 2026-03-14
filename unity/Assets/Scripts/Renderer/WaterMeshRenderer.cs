using UnityEngine;
using EconSim.Core.Data;

namespace EconSim.Renderer
{
    /// <summary>
    /// Manages the water mesh (rivers + water bodies) lifecycle.
    /// Supports two render modes: flat (stencil knockout) and biome (volumetric water).
    /// Created as a child GameObject of MapView, similar to RealmCapitalMarkers.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class WaterMeshRenderer : MonoBehaviour
    {
        public float RiverMinHalfWidth = WaterMeshBuilder.DefaultRiverMinHalfWidth;
        public float RiverMaxHalfWidth = WaterMeshBuilder.DefaultRiverMaxHalfWidth;
        public float Expand = 0.003f;

        private static readonly int HeightScaleId = Shader.PropertyToID("_HeightScale");
        private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
        private static readonly int MapWorldSizeId = Shader.PropertyToID("_MapWorldSize");
        private static readonly int HeightmapTexId = Shader.PropertyToID("_HeightmapTex");
        private static readonly int GeographyBaseTexId = Shader.PropertyToID("_GeographyBaseTex");

        private Mesh waterMesh;
        private Material flatMaterial;
        private Material biomeMaterial;
        private bool isBiomeMode;

        // Stored build parameters for rebuild
        private MapData cachedMapData;
        private float cachedCellScale;

        // Track previous values for change detection
        private float prevRiverMin;
        private float prevRiverMax;
        private float prevExpand;

        public void Initialize(MapData mapData, float cellScale)
        {
            cachedMapData = mapData;
            cachedCellScale = cellScale;

            prevRiverMin = RiverMinHalfWidth;
            prevRiverMax = RiverMaxHalfWidth;
            prevExpand = Expand;

            RebuildMesh();
        }

        /// <summary>
        /// Switch between flat (stencil) and biome (volumetric) rendering.
        /// </summary>
        public void SetBiomeMode(bool biome)
        {
            isBiomeMode = biome;
            ApplyActiveMaterial();
        }

        /// <summary>
        /// Set height displacement parameters to match the terrain shader.
        /// Called each frame by MapView during height scale animation.
        /// </summary>
        public void SetHeightScale(float heightScale, float seaLevel01)
        {
            if (flatMaterial != null)
            {
                flatMaterial.SetFloat(HeightScaleId, heightScale);
                flatMaterial.SetFloat(SeaLevelId, seaLevel01);
            }
            if (biomeMaterial != null)
            {
                biomeMaterial.SetFloat(HeightScaleId, heightScale);
                biomeMaterial.SetFloat(SeaLevelId, seaLevel01);
            }
        }

        /// <summary>
        /// Copy heightmap and geography textures from the terrain material
        /// so the biome water shader can sample them for volumetric rendering.
        /// </summary>
        public void SyncBiomeTextures(Material terrainMaterial)
        {
            if (biomeMaterial == null || terrainMaterial == null)
                return;

            var heightmap = terrainMaterial.GetTexture(HeightmapTexId);
            if (heightmap != null)
                biomeMaterial.SetTexture(HeightmapTexId, heightmap);

            var geography = terrainMaterial.GetTexture(GeographyBaseTexId);
            if (geography != null)
                biomeMaterial.SetTexture(GeographyBaseTexId, geography);
        }

        /// <summary>
        /// Set map world dimensions so the biome shader can compute data UVs from world position.
        /// </summary>
        public void SetMapWorldSize(float worldWidth, float worldHeight)
        {
            if (biomeMaterial != null)
                biomeMaterial.SetVector(MapWorldSizeId, new Vector4(worldWidth, worldHeight, 0, 0));
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
                cachedMapData, cachedCellScale, Expand,
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

            EnsureMaterials();
            ApplyActiveMaterial();
        }

        private void ApplyActiveMaterial()
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr == null) return;

            mr.sharedMaterial = isBiomeMode ? biomeMaterial : flatMaterial;
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
                Expand != prevExpand)
            {
                prevRiverMin = RiverMinHalfWidth;
                prevRiverMax = RiverMaxHalfWidth;
                prevExpand = Expand;
                RebuildMesh();
            }
        }

        private void EnsureMaterials()
        {
            if (flatMaterial == null)
            {
                Shader flatShader = Shader.Find("EconSim/WaterMesh");
                if (flatShader != null)
                    flatMaterial = new Material(flatShader);
                else
                    Debug.LogWarning("WaterMeshRenderer: Could not find EconSim/WaterMesh shader.");
            }

            if (biomeMaterial == null)
            {
                Shader biomeShader = Shader.Find("EconSim/WaterMeshBiome");
                if (biomeShader != null)
                {
                    biomeMaterial = new Material(biomeShader);
                    // Set map world size if we have cached data
                    if (cachedMapData != null)
                    {
                        float w = cachedMapData.Info.Width * cachedCellScale;
                        float h = cachedMapData.Info.Height * cachedCellScale;
                        biomeMaterial.SetVector(MapWorldSizeId, new Vector4(w, h, 0, 0));
                    }
                }
                else
                {
                    Debug.LogWarning("WaterMeshRenderer: Could not find EconSim/WaterMeshBiome shader.");
                }
            }
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

            if (flatMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(flatMaterial);
                else
                    DestroyImmediate(flatMaterial);
                flatMaterial = null;
            }

            if (biomeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(biomeMaterial);
                else
                    DestroyImmediate(biomeMaterial);
                biomeMaterial = null;
            }
        }
    }
}
