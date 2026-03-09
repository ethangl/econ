using System;
using System.Collections.Generic;
using UnityEngine;

namespace EconSim.Mapgen4
{
    public sealed class Mapgen4Controller : MonoBehaviour
    {
        sealed class RenderTextureSet
        {
            public int Size;
            public RenderTexture River;
            public RenderTexture Land;
            public RenderTexture Depth;
            public RenderTexture Drape;
        }

        public sealed class ElevationParams
        {
            public int Seed = 187;
            public float Island = 0.5f;
            public float NoisyCoastlines = 0.01f;
            public float HillHeight = 0.02f;
            public float MountainJagged = 0f;
            public float MountainSharpness = 9.8f;
            public float MountainFolds = 0.05f;
            public float OceanDepth = 1.4f;
        }

        public sealed class BiomesParams
        {
            public float WindAngleDeg = 0f;
            public float Raininess = 0.9f;
            public float RainShadow = 0.5f;
            public float Evaporation = 0.5f;
        }

        public sealed class RiversParams
        {
            public float LogMinFlow = 2.7f;
            public float LogRiverWidth = -2.4f;
            public float Flow = 0.2f;
        }

        public sealed class MeshParams
        {
            public float TargetCellCount = 0f;
        }

        public sealed class RenderParams
        {
            public float Zoom = 100f / 480f;
            public float X = 500f;
            public float Y = 500f;
            public float LightAngleDeg = 80f;
            public float Slope = 2f;
            public float Flat = 2.5f;
            public float Ambient = 0.25f;
            public float Overhead = 30f;
            public float TiltDeg = 0f;
            public float RotateDeg = 0f;
            public float MountainHeight = 50f;
            public float OutlineDepth = 1f;
            public float OutlineStrength = 15f;
            public float OutlineThreshold = 0f;
            public float OutlineCoast = 0f;
            public float OutlineWater = 13f;
            public float BiomeColors = 1f;
        }

        const int FinalLayer = 23;
        const int DrapeLayer = 24;
        const int RiverLayer = 25;
        const int LandPassLayer = 26;
        const int DepthPassLayer = 27;
        const float DefaultRenderZoom = 100f / 480f;
        const int MinTextureSize = 2048;
        const int MaxTextureSize = 16384;
        const int PreallocatedMaxTextureSize = 4096;
        const float DisplayAspectRatio = 1f;
        const float DefaultMountainSpacingRatio = Mapgen4Constants.MountainSpacing / Mapgen4Constants.Spacing;
        const float MinTargetCellCount = 5000f;
        const float MaxTargetCellCount = 80000f;

        readonly MeshParams _mesh = new MeshParams();
        readonly ElevationParams _elevation = new ElevationParams();
        readonly BiomesParams _biomes = new BiomesParams();
        readonly RiversParams _rivers = new RiversParams();
        readonly RenderParams _render = new RenderParams();

        UnityEngine.Camera _displayCamera;
        UnityEngine.Camera _riverCamera;
        UnityEngine.Camera _landCamera;
        UnityEngine.Camera _depthCamera;
        UnityEngine.Camera _drapeCamera;

        Material _riverPassMaterial;
        Material _landPassMaterial;
        Material _depthPassMaterial;
        Material _displayMaterial;
        Material _finalMaterial;

        Mesh _landMesh;
        Mesh _riverMesh;
        Mesh _fullscreenMesh;

        RenderTexture _riverTexture;
        RenderTexture _landTexture;
        RenderTexture _depthTexture;
        RenderTexture _drapeTexture;
        Texture2D _colormap;

        Vector2 _scroll;
        string _seedText = "187";
        bool _regenerateData = true;
        bool _updateRender = true;
        bool _isInitialized;
        int _lastScreenWidth;
        int _lastScreenHeight;
        bool _rebuildRuntime = true;
        float _defaultTargetCellCount;
        int _textureSize;
        readonly List<RenderTextureSet> _renderTextureSets = new List<RenderTextureSet>();

        Mapgen4RuntimeData _runtime;

        void Start()
        {
            RebuildRuntime();
            _colormap = Mapgen4Colormap.CreateTexture();
            _displayCamera = EnsureDisplayCamera();
            ConfigureDisplayCamera(_displayCamera, FinalLayer);
            CacheScreenSize();
            _textureSize = CalculateRenderTextureSize();
            PreallocateRenderTextures();
            ApplyRenderTextureSet(_textureSize);

            _riverPassMaterial = CreateMaterial("EconSim/Mapgen4/RiverPass");
            _landPassMaterial = CreateMaterial("EconSim/Mapgen4/LandPass");
            _depthPassMaterial = CreateMaterial("EconSim/Mapgen4/DepthPass");
            _displayMaterial = CreateMaterial("EconSim/Mapgen4/Display");
            _finalMaterial = CreateMaterial("EconSim/Mapgen4/Final");

            _landMesh = new Mesh { name = "Mapgen4LandMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _riverMesh = new Mesh { name = "Mapgen4RiverMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _fullscreenMesh = CreateFullscreenMesh();

            CreatePassObject("Mapgen4DrapePass", DrapeLayer, _landMesh, _displayMaterial);
            CreatePassObject("Mapgen4FinalPass", FinalLayer, _fullscreenMesh, _finalMaterial);
            CreatePassObject("Mapgen4LandPass", LandPassLayer, _landMesh, _landPassMaterial);
            CreatePassObject("Mapgen4DepthPass", DepthPassLayer, _landMesh, _depthPassMaterial);
            CreatePassObject("Mapgen4RiverPass", RiverLayer, _riverMesh, _riverPassMaterial);

            _riverCamera = CreatePassCamera("Mapgen4RiverCamera", RiverLayer, _riverTexture, new Color(0f, 0f, 0f, 0f));
            _landCamera = CreatePassCamera("Mapgen4LandCamera", LandPassLayer, _landTexture, Color.black);
            _depthCamera = CreatePassCamera("Mapgen4DepthCamera", DepthPassLayer, _depthTexture, Color.black);
            _drapeCamera = CreatePassCamera("Mapgen4DrapeCamera", DrapeLayer, _drapeTexture, new Color(0.3f, 0.3f, 0.35f, 1f));

            _isInitialized = true;
            Regenerate();
        }

        void OnDestroy()
        {
            DestroyRenderTextureSets();
            DestroyImmediate(_colormap);
            DestroyImmediate(_riverPassMaterial);
            DestroyImmediate(_landPassMaterial);
            DestroyImmediate(_depthPassMaterial);
            DestroyImmediate(_displayMaterial);
            DestroyImmediate(_finalMaterial);
            DestroyImmediate(_landMesh);
            DestroyImmediate(_riverMesh);
            DestroyImmediate(_fullscreenMesh);
        }

        void Update()
        {
            if (!_isInitialized)
            {
                return;
            }

            if (EnsureRenderTextureSize())
            {
                _updateRender = true;
            }

            if (_regenerateData)
            {
                Regenerate();
            }
            else if (_updateRender)
            {
                UpdateMaterials();
                RenderPasses();
                _updateRender = false;
            }

            SyncDisplayViewportRect();
        }

        void Regenerate()
        {
            if (_rebuildRuntime)
            {
                RebuildRuntime();
            }

            var constraints = Mapgen4Constraints.Generate(_elevation.Seed, _elevation.Island);
            _runtime.Map.AssignElevation(_elevation, constraints);
            _runtime.Map.AssignRainfall(_biomes);
            _runtime.Map.AssignRivers(_rivers);

            Vector2[] elevationRainfall;
            int[] landIndices = Mapgen4Geometry.BuildLandIndices(_runtime.Map, _elevation.MountainFolds, out elevationRainfall);
            UpdateLandMesh(_runtime.VertexPositions, elevationRainfall, landIndices);

            Mapgen4RiverVertex[] riverVertices = Mapgen4Geometry.BuildRiverVertices(_runtime.Mesh, _runtime.Map, _rivers, _runtime.CellSpacing);
            UpdateRiverMesh(riverVertices);

            _regenerateData = false;
            _updateRender = true;
            UpdateMaterials();
            RenderPasses();
            _updateRender = false;
        }

        void UpdateLandMesh(Vector2[] positions, Vector2[] elevationRainfall, int[] indices)
        {
            var vertices = new Vector3[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                vertices[i] = new Vector3(positions[i].x, positions[i].y, 0f);
            }

            _landMesh.Clear();
            _landMesh.vertices = vertices;
            _landMesh.uv = elevationRainfall;
            _landMesh.triangles = indices;
            _landMesh.bounds = new Bounds(new Vector3(500f, 500f, 0f), new Vector3(3000f, 3000f, 50f));
            _landMesh.UploadMeshData(false);
        }

        void UpdateRiverMesh(Mapgen4RiverVertex[] data)
        {
            int count = data.Length;
            var vertices = new Vector3[count];
            var widths = new Vector2[count];
            var barycentrics = new Vector2[count];
            var indices = new int[count];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(data[i].Position.x, data[i].Position.y, 0f);
                widths[i] = data[i].Widths;
                barycentrics[i] = new Vector2(data[i].Barycentric.x, data[i].Barycentric.y);
                indices[i] = i;
            }

            _riverMesh.Clear();
            _riverMesh.vertices = vertices;
            _riverMesh.uv = widths;
            _riverMesh.uv2 = barycentrics;
            _riverMesh.triangles = indices;
            _riverMesh.bounds = new Bounds(new Vector3(500f, 500f, 0f), new Vector3(3000f, 3000f, 50f));
            _riverMesh.UploadMeshData(false);
        }

        void UpdateMaterials()
        {
            Matrix4x4 topdown = CalculateTopdownMatrix();
            Matrix4x4 projection = CalculateProjectionMatrix();
            float lightAngleRadians = Mathf.Deg2Rad * (_render.LightAngleDeg + _render.RotateDeg);
            Vector2 lightAngle = new Vector2(Mathf.Cos(lightAngleRadians), Mathf.Sin(lightAngleRadians));
            Vector2 inverseTextureSize = new Vector2(1.5f / _textureSize, 1.5f / _textureSize);

            _riverPassMaterial.SetMatrix("_TopdownMatrix", topdown);

            _landPassMaterial.SetMatrix("_TopdownMatrix", topdown);
            _landPassMaterial.SetTexture("_WaterTex", _riverTexture);
            _landPassMaterial.SetFloat("_OutlineWater", _render.OutlineWater);

            _depthPassMaterial.SetMatrix("_ProjectionMatrix", projection);

            _displayMaterial.SetMatrix("_ProjectionMatrix", projection);
            _displayMaterial.SetTexture("_ColorMap", _colormap);
            _displayMaterial.SetTexture("_ElevationTex", _landTexture);
            _displayMaterial.SetTexture("_WaterTex", _riverTexture);
            _displayMaterial.SetTexture("_DepthTex", _depthTexture);
            _displayMaterial.SetVector("_LightAngle", lightAngle);
            _displayMaterial.SetVector("_InverseTextureSize", inverseTextureSize);
            _displayMaterial.SetFloat("_Slope", _render.Slope);
            _displayMaterial.SetFloat("_Flat", _render.Flat);
            _displayMaterial.SetFloat("_Ambient", _render.Ambient);
            _displayMaterial.SetFloat("_Overhead", _render.Overhead);
            _displayMaterial.SetFloat("_OutlineDepth", _render.OutlineDepth * 5f * _render.Zoom);
            _displayMaterial.SetFloat("_OutlineStrength", _render.OutlineStrength);
            _displayMaterial.SetFloat("_OutlineThreshold", _render.OutlineThreshold / 1000f);
            _displayMaterial.SetFloat("_OutlineCoast", _render.OutlineCoast);
            _displayMaterial.SetFloat("_OutlineWater", _render.OutlineWater);
            _displayMaterial.SetFloat("_BiomeColors", _render.BiomeColors);

            _finalMaterial.SetTexture("_MainTex", _drapeTexture);
            _finalMaterial.SetVector("_Offset", new Vector2(0.5f / _textureSize, 0.5f / _textureSize));
        }

        void RenderPasses()
        {
            _riverCamera.Render();
            _landCamera.Render();
            _depthCamera.Render();
            _drapeCamera.Render();
        }

        UnityEngine.Camera EnsureDisplayCamera()
        {
            UnityEngine.Camera camera = UnityEngine.Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera").AddComponent<UnityEngine.Camera>();
                camera.tag = "MainCamera";
            }
            return camera;
        }

        void ConfigureDisplayCamera(UnityEngine.Camera camera, int layer)
        {
            camera.orthographic = true;
            camera.orthographicSize = 500f;
            camera.transform.position = new Vector3(500f, 500f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.rect = CalculateDisplayViewportRect();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.3f, 0.35f, 1f);
            camera.cullingMask = 1 << layer;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
        }

        UnityEngine.Camera CreatePassCamera(string name, int layer, RenderTexture target, Color clearColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var camera = go.AddComponent<UnityEngine.Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 500f;
            camera.transform.position = new Vector3(500f, 500f, -10f);
            camera.transform.rotation = Quaternion.identity;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = clearColor;
            camera.cullingMask = 1 << layer;
            camera.targetTexture = target;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 50f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            return camera;
        }

        void CreatePassObject(string name, int layer, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.layer = layer;
            go.transform.SetParent(transform, false);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        Material CreateMaterial(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader not found: {shaderName}");
            }
            return new Material(shader) { hideFlags = HideFlags.DontSave };
        }

        RenderTexture CreateRenderTexture(string name, int size, RenderTextureFormat format)
        {
            var texture = new RenderTexture(size, size, 24, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        bool EnsureRenderTextureSize()
        {
            int desiredTextureSize = CalculateRenderTextureSize();
            if (desiredTextureSize == _textureSize)
            {
                return false;
            }

            _textureSize = desiredTextureSize;
            ApplyRenderTextureSet(_textureSize);
            return true;
        }

        void PreallocateRenderTextures()
        {
            _renderTextureSets.Clear();
            for (int size = MinTextureSize; size <= PreallocatedMaxTextureSize; size *= 2)
            {
                _renderTextureSets.Add(CreateRenderTextureSet(size));
            }
        }

        void ApplyRenderTextureSet(int size)
        {
            RenderTextureSet set = _renderTextureSets.Find(candidate => candidate.Size == size);
            if (set == null)
            {
                set = CreateRenderTextureSet(size);
                _renderTextureSets.Add(set);
            }

            _riverTexture = set.River;
            _landTexture = set.Land;
            _depthTexture = set.Depth;
            _drapeTexture = set.Drape;

            if (_riverCamera != null) _riverCamera.targetTexture = _riverTexture;
            if (_landCamera != null) _landCamera.targetTexture = _landTexture;
            if (_depthCamera != null) _depthCamera.targetTexture = _depthTexture;
            if (_drapeCamera != null) _drapeCamera.targetTexture = _drapeTexture;
        }

        RenderTextureSet CreateRenderTextureSet(int size)
        {
            return new RenderTextureSet
            {
                Size = size,
                River = CreateRenderTexture($"Mapgen4RiverRT_{size}", size, RenderTextureFormat.ARGB32),
                Land = CreateRenderTexture($"Mapgen4LandRT_{size}", size, RenderTextureFormat.ARGBHalf),
                Depth = CreateRenderTexture($"Mapgen4DepthRT_{size}", size, RenderTextureFormat.ARGBHalf),
                Drape = CreateRenderTexture($"Mapgen4DrapeRT_{size}", size, RenderTextureFormat.ARGB32),
            };
        }

        void DestroyRenderTextureSets()
        {
            foreach (RenderTextureSet set in _renderTextureSets)
            {
                DestroyImmediate(set.River);
                DestroyImmediate(set.Land);
                DestroyImmediate(set.Depth);
                DestroyImmediate(set.Drape);
            }

            _renderTextureSets.Clear();
        }

        int CalculateRenderTextureSize()
        {
            float zoomRatio = Mathf.Max(1f, _render.Zoom / DefaultRenderZoom);
            float desiredSize = MinTextureSize * zoomRatio;
            int roundedSize = Mathf.NextPowerOfTwo(Mathf.CeilToInt(desiredSize));
            return Mathf.Clamp(roundedSize, MinTextureSize, MaxTextureSize);
        }

        Mesh CreateFullscreenMesh()
        {
            var mesh = new Mesh { name = "Mapgen4FullscreenMesh" };
            mesh.vertices = new[]
            {
                new Vector3(-2f, 0f, 0f),
                new Vector3(0f, -2f, 0f),
                new Vector3(2f, 2f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(-2f, 0f),
                new Vector2(0f, -2f),
                new Vector2(2f, 2f),
            };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
            mesh.UploadMeshData(false);
            return mesh;
        }

        Rect CalculateDisplayViewportRect()
        {
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float screenAspect = screenWidth / screenHeight;
            if (screenAspect > DisplayAspectRatio)
            {
                float normalizedWidth = DisplayAspectRatio / screenAspect;
                return new Rect(0.5f * (1f - normalizedWidth), 0f, normalizedWidth, 1f);
            }

            float normalizedHeight = screenAspect / DisplayAspectRatio;
            return new Rect(0f, 0.5f * (1f - normalizedHeight), 1f, normalizedHeight);
        }

        void SyncDisplayViewportRect()
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            if (screenWidth == _lastScreenWidth && screenHeight == _lastScreenHeight)
            {
                return;
            }

            CacheScreenSize();
            if (_displayCamera != null)
            {
                _displayCamera.rect = CalculateDisplayViewportRect();
            }
        }

        void CacheScreenSize()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }

        Matrix4x4 CalculateTopdownMatrix()
        {
            Matrix4x4 translate = Matrix4x4.Translate(new Vector3(-1f, -1f, 0f));
            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(1f / 500f, 1f / 500f, 1f));
            return translate * scale;
        }

        Matrix4x4 CalculateProjectionMatrix()
        {
            Matrix4x4 projection =
                Matrix4x4.Rotate(Quaternion.Euler(180f + _render.TiltDeg, 0f, 0f)) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, _render.RotateDeg));

            // gl-matrix's projection[9] corresponds to row 1, column 2.
            projection.m12 = 1f;

            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(_render.Zoom / 100f, _render.Zoom / 100f, _render.MountainHeight * _render.Zoom / 100f));
            Matrix4x4 translate = Matrix4x4.Translate(new Vector3(-_render.X, -_render.Y, 0f));
            return projection * scale * translate;
        }

        void OnGUI()
        {
            if (!_isInitialized)
            {
                return;
            }

            const float panelWidth = 340f;
            Rect panelRect = new Rect(Screen.width - panelWidth - 12f, 12f, panelWidth, Screen.height - 24f);
            GUI.Box(panelRect, GUIContent.none);
            GUILayout.BeginArea(new Rect(panelRect.x + 8f, panelRect.y + 8f, panelRect.width - 16f, panelRect.height - 16f));
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Mapgen4", GUI.skin.GetStyle("box"));
            if (GUILayout.Button("Reset Defaults"))
            {
                ResetDefaults();
            }

            DrawMeshControls();
            DrawElevationControls();
            DrawBiomesControls();
            DrawRiversControls();
            DrawRenderControls();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawElevationControls()
        {
            GUILayout.Label("elevation", GUI.skin.box);
            string nextSeed = DrawTextField("seed", _seedText);
            if (nextSeed != _seedText)
            {
                _seedText = nextSeed;
                if (int.TryParse(nextSeed, out int parsed))
                {
                    parsed = Mathf.Clamp(parsed, 1, 1 << 30);
                    if (parsed != _elevation.Seed)
                    {
                        _elevation.Seed = parsed;
                        MarkGeneratorDirty();
                    }
                }
            }
            DrawSlider("island", ref _elevation.Island, 0f, 1f, true);
            DrawSlider("noisy_coastlines", ref _elevation.NoisyCoastlines, 0f, 0.1f, true);
            DrawSlider("hill_height", ref _elevation.HillHeight, 0f, 0.1f, true);
            DrawSlider("mountain_jagged", ref _elevation.MountainJagged, 0f, 1f, true);
            DrawSlider("mountain_sharpness", ref _elevation.MountainSharpness, 9.1f, 12.5f, true);
            DrawSlider("mountain_folds", ref _elevation.MountainFolds, 0f, 0.5f, true);
            DrawSlider("ocean_depth", ref _elevation.OceanDepth, 1f, 3f, true);
        }

        void DrawMeshControls()
        {
            GUILayout.Label("mesh", GUI.skin.box);
            if (_runtime != null)
            {
                GUILayout.Label($"actual_cells: {_runtime.Mesh.NumSolidRegions}");
                GUILayout.Label($"cell_spacing: {_runtime.CellSpacing:0.00}");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("target_cells", GUILayout.Width(140f));
            float next = GUILayout.HorizontalSlider(_mesh.TargetCellCount, MinTargetCellCount, MaxTargetCellCount, GUILayout.Width(120f));
            GUILayout.Label(Mathf.RoundToInt(next).ToString(), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(next, _mesh.TargetCellCount))
            {
                _mesh.TargetCellCount = Mathf.Round(next);
                MarkRuntimeDirty();
            }
        }

        void DrawBiomesControls()
        {
            GUILayout.Label("biomes", GUI.skin.box);
            DrawSlider("wind_angle_deg", ref _biomes.WindAngleDeg, 0f, 360f, true);
            DrawSlider("raininess", ref _biomes.Raininess, 0f, 2f, true);
            DrawSlider("rain_shadow", ref _biomes.RainShadow, 0.1f, 2f, true);
            DrawSlider("evaporation", ref _biomes.Evaporation, 0f, 1f, true);
        }

        void DrawRiversControls()
        {
            GUILayout.Label("rivers", GUI.skin.box);
            DrawSlider("lg_min_flow", ref _rivers.LogMinFlow, -5f, 5f, true);
            DrawSlider("lg_river_width", ref _rivers.LogRiverWidth, -5f, 5f, true);
            DrawSlider("flow", ref _rivers.Flow, 0f, 1f, true);
        }

        void DrawRenderControls()
        {
            GUILayout.Label("render", GUI.skin.box);
            GUILayout.Label($"rt_size: {_textureSize}");
            DrawSlider("zoom", ref _render.Zoom, 100f / 1000f, 100f / 50f, false);
            DrawSlider("x", ref _render.X, 0f, 1000f, false);
            DrawSlider("y", ref _render.Y, 0f, 1000f, false);
            DrawSlider("light_angle_deg", ref _render.LightAngleDeg, 0f, 360f, false);
            DrawSlider("slope", ref _render.Slope, 0f, 5f, false);
            DrawSlider("flat", ref _render.Flat, 0f, 5f, false);
            DrawSlider("ambient", ref _render.Ambient, 0f, 1f, false);
            DrawSlider("overhead", ref _render.Overhead, 0f, 60f, false);
            DrawSlider("tilt_deg", ref _render.TiltDeg, 0f, 90f, false);
            DrawSlider("rotate_deg", ref _render.RotateDeg, -180f, 180f, false);
            DrawSlider("mountain_height", ref _render.MountainHeight, 0f, 250f, false);
            DrawSlider("outline_depth", ref _render.OutlineDepth, 0f, 2f, false);
            DrawSlider("outline_strength", ref _render.OutlineStrength, 0f, 30f, false);
            DrawSlider("outline_threshold", ref _render.OutlineThreshold, 0f, 100f, false);
            DrawSlider("outline_coast", ref _render.OutlineCoast, 0f, 1f, false);
            DrawSlider("outline_water", ref _render.OutlineWater, 0f, 20f, false);
            DrawSlider("biome_colors", ref _render.BiomeColors, 0f, 1f, false);
        }

        string DrawTextField(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            string next = GUILayout.TextField(value, GUILayout.Width(140f));
            GUILayout.EndHorizontal();
            return next;
        }

        void DrawSlider(string label, ref float value, float min, float max, bool regenerates)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140f));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120f));
            GUILayout.Label(next.ToString("0.000"), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(next, value))
            {
                value = next;
                if (regenerates)
                {
                    MarkGeneratorDirty();
                }
                else
                {
                    _updateRender = true;
                }
            }
        }

        void MarkGeneratorDirty()
        {
            _regenerateData = true;
        }

        void MarkRuntimeDirty()
        {
            _rebuildRuntime = true;
            _regenerateData = true;
        }

        void ResetDefaults()
        {
            _mesh.TargetCellCount = _defaultTargetCellCount > 0f ? _defaultTargetCellCount : EstimateDefaultTargetCellCount();
            _seedText = "187";
            _elevation.Seed = 187;
            _elevation.Island = 0.5f;
            _elevation.NoisyCoastlines = 0.01f;
            _elevation.HillHeight = 0.02f;
            _elevation.MountainJagged = 0f;
            _elevation.MountainSharpness = 9.8f;
            _elevation.MountainFolds = 0.05f;
            _elevation.OceanDepth = 1.4f;
            _biomes.WindAngleDeg = 0f;
            _biomes.Raininess = 0.9f;
            _biomes.RainShadow = 0.5f;
            _biomes.Evaporation = 0.5f;
            _rivers.LogMinFlow = 2.7f;
            _rivers.LogRiverWidth = -2.4f;
            _rivers.Flow = 0.2f;
            _render.Zoom = 100f / 480f;
            _render.X = 500f;
            _render.Y = 500f;
            _render.LightAngleDeg = 80f;
            _render.Slope = 2f;
            _render.Flat = 2.5f;
            _render.Ambient = 0.25f;
            _render.Overhead = 30f;
            _render.TiltDeg = 0f;
            _render.RotateDeg = 0f;
            _render.MountainHeight = 50f;
            _render.OutlineDepth = 1f;
            _render.OutlineStrength = 15f;
            _render.OutlineThreshold = 0f;
            _render.OutlineCoast = 0f;
            _render.OutlineWater = 13f;
            _render.BiomeColors = 1f;
            MarkRuntimeDirty();
        }

        void RebuildRuntime()
        {
            float cellSpacing = CalculateCellSpacing();
            float mountainSpacing = cellSpacing * DefaultMountainSpacingRatio;
            _runtime = Mapgen4RuntimeData.Build(cellSpacing, mountainSpacing);
            _rebuildRuntime = false;

            if (_defaultTargetCellCount <= 0f)
            {
                _defaultTargetCellCount = _runtime.Mesh.NumSolidRegions;
                _mesh.TargetCellCount = _defaultTargetCellCount;
            }
        }

        float CalculateCellSpacing()
        {
            float baselineTarget = _defaultTargetCellCount > 0f ? _defaultTargetCellCount : EstimateDefaultTargetCellCount();
            float clampedTarget = Mathf.Clamp(_mesh.TargetCellCount > 0f ? _mesh.TargetCellCount : baselineTarget, MinTargetCellCount, MaxTargetCellCount);
            return Mapgen4Constants.Spacing * Mathf.Sqrt(baselineTarget / clampedTarget);
        }

        static float EstimateDefaultTargetCellCount()
        {
            float mapArea = Mapgen4Constants.MapSize * Mapgen4Constants.MapSize;
            return mapArea / (Mapgen4Constants.Spacing * Mapgen4Constants.Spacing);
        }
    }
}
