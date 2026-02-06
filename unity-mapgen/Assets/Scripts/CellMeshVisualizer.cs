using UnityEngine;
using MapGen.Core;

namespace MapGen
{
    public enum ColorMode
    {
        // Base modes
        Heightmap, Soil, Biome, CellIndex,
        // Climate
        Temperature, Precipitation,
        // Derived inputs
        Slope, SaltEffect, Loess, CellFlux,
        // Geology
        RockType,
        // Soil detail
        Fertility,
        // Biome detail
        BiomeType, Habitability,
        // Vegetation
        VegetationType, VegetationDensity,
        // Economy
        Subsistence, MovementCost
    }

    /// <summary>
    /// Debug visualization for CellMesh using Unity's GL immediate mode.
    /// All overlay modes consolidated here — no gizmos needed.
    /// </summary>
    [RequireComponent(typeof(CellMeshGenerator))]
    public class CellMeshVisualizer : MonoBehaviour
    {
        [Header("Display Options")]
        [System.NonSerialized] public bool ShowCells = true;
        public bool ShowEdges = false;
        [System.NonSerialized] public bool ShowCenters = false;
        [System.NonSerialized] public bool ShowVertices = false;

        [Header("Coloring")]
        public ColorMode ColorMode = ColorMode.Heightmap;

        [System.NonSerialized] public Color CellColor = new Color(0.2f, 0.4f, 0.8f, 1f);
        public Color EdgeColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        [System.NonSerialized] public Color CenterColor = Color.red;
        [System.NonSerialized] public Color VertexColor = Color.green;

        private CellMeshGenerator _generator;
        private HeightmapGenerator _heightmapGenerator;
        private ClimateGenerator _climateGenerator;
        private BiomeGenerator _biomeGenerator;
        private Material _glMaterial;

        // Cached heatmap ranges for temperature/precipitation (computed from data)
        private float _tempMin, _tempMax;
        private float _precipLandMin, _precipLandMax;
        private bool _climateCacheValid;

        // Soil palette (8 types, indexed by SoilType enum)
        private static readonly Color[] SoilColors = new Color[]
        {
            new Color(0.60f, 0.65f, 0.72f), // Permafrost - blue-gray
            new Color(0.90f, 0.88f, 0.85f), // Saline - white/pale
            new Color(0.55f, 0.55f, 0.53f), // Lithosol - rocky gray
            new Color(0.30f, 0.22f, 0.14f), // Alluvial - dark brown
            new Color(0.82f, 0.75f, 0.50f), // Aridisol - sandy yellow
            new Color(0.78f, 0.38f, 0.18f), // Laterite - red-orange
            new Color(0.50f, 0.44f, 0.38f), // Podzol - gray-brown
            new Color(0.15f, 0.12f, 0.10f), // Chernozem - near-black
        };

        // Vegetation palette (7 types, indexed by VegetationType enum)
        private static readonly Color[] VegetationColors = new Color[]
        {
            new Color(0.70f, 0.65f, 0.55f), // None - bare ground
            new Color(0.55f, 0.60f, 0.50f), // LichenMoss - muted gray-green
            new Color(0.70f, 0.75f, 0.30f), // Grass - gold-green
            new Color(0.50f, 0.55f, 0.30f), // Shrub - dusty olive
            new Color(0.30f, 0.60f, 0.20f), // DeciduousForest - green
            new Color(0.15f, 0.35f, 0.30f), // ConiferousForest - dark blue-green
            new Color(0.10f, 0.40f, 0.15f), // BroadleafForest - deep green
        };

        // 18 biome colors - visually distinct for overlay
        private static readonly Color[] BiomeColors = new Color[]
        {
            new Color(0.85f, 0.92f, 0.98f), // Glacier - ice white-blue
            new Color(0.70f, 0.75f, 0.65f), // Tundra - gray-green
            new Color(0.95f, 0.93f, 0.85f), // SaltFlat - off-white
            new Color(0.45f, 0.60f, 0.45f), // CoastalMarsh - muted green
            new Color(0.60f, 0.58f, 0.55f), // AlpineBarren - gray
            new Color(0.55f, 0.58f, 0.40f), // MountainShrub - olive
            new Color(0.40f, 0.65f, 0.25f), // Floodplain - bright green
            new Color(0.30f, 0.50f, 0.40f), // Wetland - teal
            new Color(0.92f, 0.82f, 0.45f), // HotDesert - sand yellow
            new Color(0.75f, 0.72f, 0.60f), // ColdDesert - pale brown
            new Color(0.70f, 0.65f, 0.40f), // Scrubland - dusty olive
            new Color(0.10f, 0.45f, 0.15f), // TropicalRainforest - deep green
            new Color(0.25f, 0.50f, 0.20f), // TropicalDryForest - medium green
            new Color(0.75f, 0.70f, 0.30f), // Savanna - gold-green
            new Color(0.20f, 0.35f, 0.30f), // BorealForest - dark blue-green
            new Color(0.25f, 0.55f, 0.20f), // TemperateForest - green
            new Color(0.65f, 0.72f, 0.35f), // Grassland - gold-green
            new Color(0.35f, 0.55f, 0.25f), // Woodland - medium green
        };

        // Rock type colors (4 types)
        private static readonly Color[] RockColors = new Color[]
        {
            new Color(0.55f, 0.55f, 0.55f), // Granite - gray
            new Color(0.76f, 0.70f, 0.50f), // Sedimentary - sandy
            new Color(0.85f, 0.85f, 0.75f), // Limestone - pale cream
            new Color(0.40f, 0.25f, 0.25f), // Volcanic - dark red-brown
        };

        private static readonly Color WaterColor = new Color(0.2f, 0.2f, 0.25f);

        void Awake()
        {
            CacheComponents();

            // Create simple material for GL drawing
            var shader = Shader.Find("Hidden/Internal-Colored");
            _glMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _glMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _glMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _glMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _glMaterial.SetInt("_ZWrite", 0);
        }

        private void CacheComponents()
        {
            _generator = GetComponent<CellMeshGenerator>();
            _heightmapGenerator = GetComponent<HeightmapGenerator>();
            _climateGenerator = GetComponent<ClimateGenerator>();
            _biomeGenerator = GetComponent<BiomeGenerator>();
        }

        void OnRenderObject()
        {
            var mesh = _generator?.Mesh;
            if (mesh == null) return;

            _glMaterial.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(transform.localToWorldMatrix);

            if (ShowCells)
                DrawCells(mesh);

            if (ShowEdges)
                DrawEdges(mesh);

            if (ShowCenters)
                DrawPoints(mesh.CellCenters, CenterColor, 2f);

            if (ShowVertices)
                DrawPoints(mesh.Vertices, VertexColor, 1.5f);

            GL.PopMatrix();
        }

        private void DrawCells(CellMesh mesh)
        {
            GL.Begin(GL.TRIANGLES);

            if (_heightmapGenerator == null || _climateGenerator == null || _biomeGenerator == null)
                CacheComponents();

            var heightGrid = _heightmapGenerator?.HeightGrid;
            var biomeData = _biomeGenerator?.BiomeData;
            var climateData = _climateGenerator?.ClimateData;

            // Refresh cached climate ranges when needed
            if (NeedsClimateRange() && climateData != null && heightGrid != null)
                ComputeClimateRanges(mesh, climateData, heightGrid);

            for (int c = 0; c < mesh.CellCount; c++)
            {
                var verts = mesh.CellVertices[c];
                if (verts == null || verts.Length < 3) continue;

                Color color = GetCellColor(c, heightGrid, biomeData, climateData);
                color.a = 1f;
                GL.Color(color);

                // Fan triangulation from first vertex
                var v0 = ToVector3(mesh.Vertices[verts[0]]);
                for (int i = 1; i < verts.Length - 1; i++)
                {
                    var v1 = ToVector3(mesh.Vertices[verts[i]]);
                    var v2 = ToVector3(mesh.Vertices[verts[i + 1]]);

                    GL.Vertex(v0);
                    GL.Vertex(v1);
                    GL.Vertex(v2);
                }
            }

            GL.End();
        }

        private bool NeedsClimateRange()
        {
            return !_climateCacheValid &&
                   (ColorMode == ColorMode.Temperature || ColorMode == ColorMode.Precipitation);
        }

        private void ComputeClimateRanges(CellMesh mesh, ClimateData climate, HeightGrid heights)
        {
            var (tMin, tMax) = climate.TemperatureRange();
            _tempMin = tMin;
            _tempMax = tMax;

            float pMin = float.MaxValue, pMax = float.MinValue;
            for (int i = 0; i < mesh.CellCount; i++)
            {
                if (heights.IsWater(i)) continue;
                float p = climate.Precipitation[i];
                if (p < pMin) pMin = p;
                if (p > pMax) pMax = p;
            }
            _precipLandMin = pMin == float.MaxValue ? 0 : pMin;
            _precipLandMax = pMax == float.MinValue ? 1 : pMax;
            _climateCacheValid = true;
        }

        /// <summary>
        /// Invalidate cached ranges (call when data changes, e.g. after regeneration).
        /// </summary>
        public void InvalidateCache()
        {
            _climateCacheValid = false;
        }

        private Color GetCellColor(int c, HeightGrid heightGrid, BiomeData biomeData, ClimateData climateData)
        {
            bool isWater = heightGrid != null && heightGrid.IsWater(c);

            switch (ColorMode)
            {
                // --- Original modes ---
                case ColorMode.Heightmap:
                    if (heightGrid != null)
                        return GetHeightColor(heightGrid.Heights[c]);
                    goto case ColorMode.CellIndex;

                case ColorMode.Soil:
                    if (biomeData == null || isWater) return GetWaterOrFallback(c, heightGrid);
                    return SoilColors[(int)biomeData.Soil[c]];

                case ColorMode.Biome:
                    if (biomeData == null || isWater) return GetWaterOrFallback(c, heightGrid);
                    // Composite: soil + vegetation by density
                    Color soil = SoilColors[(int)biomeData.Soil[c]];
                    Color veg = VegetationColors[(int)biomeData.Vegetation[c]];
                    return Color.Lerp(soil, veg, biomeData.VegetationDensity[c]);

                case ColorMode.CellIndex:
                    float hue = (c * 0.618034f) % 1f;
                    return Color.HSVToRGB(hue, 0.5f, 0.7f);

                // --- Climate ---
                case ColorMode.Temperature:
                    if (climateData == null) return WaterColor;
                    // Temperature uses blue(cold)->white->red(hot), includes water cells
                    return GetHeatmapColor(climateData.Temperature[c], _tempMin, _tempMax);

                case ColorMode.Precipitation:
                    if (climateData == null || isWater) return WaterColor;
                    return GetHeatmapColor(climateData.Precipitation[c], _precipLandMin, _precipLandMax);

                // --- Derived inputs (heatmaps) ---
                case ColorMode.Slope:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.Slope[c], 0f, 1f);

                case ColorMode.SaltEffect:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.SaltEffect[c], 0f, 1f);

                case ColorMode.Loess:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.Loess[c], 0f, 1f);

                case ColorMode.CellFlux:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.CellFlux[c], 0f, 1f);

                // --- Geology (categorical) ---
                case ColorMode.RockType:
                    if (biomeData == null || isWater) return WaterColor;
                    return RockColors[(int)biomeData.Rock[c]];

                // --- Soil detail ---
                case ColorMode.Fertility:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.Fertility[c], 0f, 1f);

                // --- Biome detail (categorical) ---
                case ColorMode.BiomeType:
                    if (biomeData == null || isWater) return WaterColor;
                    return BiomeColors[(int)biomeData.Biome[c]];

                case ColorMode.Habitability:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.Habitability[c], 0f, 100f);

                // --- Vegetation ---
                case ColorMode.VegetationType:
                    if (biomeData == null || isWater) return WaterColor;
                    return VegetationColors[(int)biomeData.Vegetation[c]];

                case ColorMode.VegetationDensity:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.VegetationDensity[c], 0f, 1f);

                // --- Economy ---
                case ColorMode.Subsistence:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.Subsistence[c], 0f, 1f);

                case ColorMode.MovementCost:
                    if (biomeData == null || isWater) return WaterColor;
                    return GetHeatmapColor(biomeData.MovementCost[c], 1f, 8f);

                default:
                    return WaterColor;
            }
        }

        /// <summary>
        /// Red(low) -> white(mid) -> blue(high) heatmap gradient.
        /// </summary>
        private Color GetHeatmapColor(float value, float min, float max)
        {
            float range = max - min;
            if (range < 1e-6f) range = 1f;

            float t = (value - min) / range;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            if (t < 0.5f)
                return Color.Lerp(new Color(0.9f, 0.1f, 0.1f), Color.white, t * 2f);
            else
                return Color.Lerp(Color.white, new Color(0.1f, 0.2f, 0.9f), (t - 0.5f) * 2f);
        }

        private Color GetWaterOrFallback(int c, HeightGrid heightGrid)
        {
            if (heightGrid != null)
                return GetHeightColor(heightGrid.Heights[c]);
            return new Color(0.1f, 0.2f, 0.5f);
        }

        private Color GetHeightColor(float height)
        {
            const float seaLevel = HeightGrid.SeaLevel;
            const float maxHeight = HeightGrid.MaxHeight;

            if (height <= seaLevel)
            {
                float t = height / seaLevel;
                return Color.Lerp(new Color(0.05f, 0.1f, 0.3f), new Color(0.2f, 0.4f, 0.7f), t);
            }
            else
            {
                float t = (height - seaLevel) / (maxHeight - seaLevel);
                if (t < 0.3f)
                    return Color.Lerp(new Color(0.2f, 0.5f, 0.2f), new Color(0.4f, 0.6f, 0.3f), t / 0.3f);
                else if (t < 0.6f)
                    return Color.Lerp(new Color(0.4f, 0.6f, 0.3f), new Color(0.5f, 0.4f, 0.25f), (t - 0.3f) / 0.3f);
                else if (t < 0.85f)
                    return Color.Lerp(new Color(0.5f, 0.4f, 0.25f), new Color(0.6f, 0.6f, 0.6f), (t - 0.6f) / 0.25f);
                else
                    return Color.Lerp(new Color(0.6f, 0.6f, 0.6f), Color.white, (t - 0.85f) / 0.15f);
            }
        }

        private void DrawEdges(CellMesh mesh)
        {
            GL.Begin(GL.LINES);
            GL.Color(EdgeColor);

            for (int e = 0; e < mesh.EdgeCount; e++)
            {
                var (v0, v1) = mesh.EdgeVertices[e];
                GL.Vertex(ToVector3(mesh.Vertices[v0]));
                GL.Vertex(ToVector3(mesh.Vertices[v1]));
            }

            GL.End();
        }

        private void DrawPoints(Vec2[] points, Color color, float size)
        {
            GL.Begin(GL.QUADS);
            GL.Color(color);

            foreach (var p in points)
            {
                float x = p.X;
                float y = p.Y;
                GL.Vertex3(x - size, y - size, 0);
                GL.Vertex3(x + size, y - size, 0);
                GL.Vertex3(x + size, y + size, 0);
                GL.Vertex3(x - size, y + size, 0);
            }

            GL.End();
        }

        private Vector3 ToVector3(Vec2 v) => new Vector3(v.X, v.Y, 0);
    }
}
