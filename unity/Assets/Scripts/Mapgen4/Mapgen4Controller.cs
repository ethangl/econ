using System;
using UnityEngine;

namespace EconSim.Mapgen4
{
    public sealed class Mapgen4Controller : MonoBehaviour
    {
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

        const int DisplayLayer = 23;
        const int RiverLayer = 24;
        const int LandPassLayer = 25;
        const int DepthPassLayer = 26;
        const int TextureSize = 2048;

        readonly ElevationParams _elevation = new ElevationParams();
        readonly BiomesParams _biomes = new BiomesParams();
        readonly RiversParams _rivers = new RiversParams();
        readonly RenderParams _render = new RenderParams();

        UnityEngine.Camera _displayCamera;
        UnityEngine.Camera _riverCamera;
        UnityEngine.Camera _landCamera;
        UnityEngine.Camera _depthCamera;

        Material _riverPassMaterial;
        Material _landPassMaterial;
        Material _depthPassMaterial;
        Material _displayMaterial;

        Mesh _landMesh;
        Mesh _riverMesh;

        RenderTexture _riverTexture;
        RenderTexture _landTexture;
        RenderTexture _depthTexture;
        Texture2D _colormap;

        Vector2 _scroll;
        string _seedText = "187";
        bool _regenerateData = true;
        bool _updateRender = true;
        bool _isInitialized;

        Mapgen4RuntimeData _runtime;

        void Start()
        {
            _runtime = Mapgen4RuntimeData.Build();
            _colormap = Mapgen4Colormap.CreateTexture();
            _displayCamera = EnsureDisplayCamera();
            ConfigureDisplayCamera(_displayCamera, DisplayLayer);

            _riverTexture = CreateRenderTexture("Mapgen4RiverRT", RenderTextureFormat.ARGB32);
            _landTexture = CreateRenderTexture("Mapgen4LandRT", RenderTextureFormat.ARGBHalf);
            _depthTexture = CreateRenderTexture("Mapgen4DepthRT", RenderTextureFormat.ARGBHalf);

            _riverPassMaterial = CreateMaterial("EconSim/Mapgen4/RiverPass");
            _landPassMaterial = CreateMaterial("EconSim/Mapgen4/LandPass");
            _depthPassMaterial = CreateMaterial("EconSim/Mapgen4/DepthPass");
            _displayMaterial = CreateMaterial("EconSim/Mapgen4/Display");

            _landMesh = new Mesh { name = "Mapgen4LandMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _riverMesh = new Mesh { name = "Mapgen4RiverMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };

            CreatePassObject("Mapgen4Display", DisplayLayer, _landMesh, _displayMaterial);
            CreatePassObject("Mapgen4LandPass", LandPassLayer, _landMesh, _landPassMaterial);
            CreatePassObject("Mapgen4DepthPass", DepthPassLayer, _landMesh, _depthPassMaterial);
            CreatePassObject("Mapgen4RiverPass", RiverLayer, _riverMesh, _riverPassMaterial);

            _riverCamera = CreatePassCamera("Mapgen4RiverCamera", RiverLayer, _riverTexture, new Color(0f, 0f, 0f, 0f));
            _landCamera = CreatePassCamera("Mapgen4LandCamera", LandPassLayer, _landTexture, Color.black);
            _depthCamera = CreatePassCamera("Mapgen4DepthCamera", DepthPassLayer, _depthTexture, Color.black);

            _isInitialized = true;
            Regenerate();
        }

        void OnDestroy()
        {
            DestroyImmediate(_riverTexture);
            DestroyImmediate(_landTexture);
            DestroyImmediate(_depthTexture);
            DestroyImmediate(_colormap);
            DestroyImmediate(_riverPassMaterial);
            DestroyImmediate(_landPassMaterial);
            DestroyImmediate(_depthPassMaterial);
            DestroyImmediate(_displayMaterial);
            DestroyImmediate(_landMesh);
            DestroyImmediate(_riverMesh);
        }

        void Update()
        {
            if (!_isInitialized)
            {
                return;
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
        }

        void Regenerate()
        {
            var constraints = Mapgen4Constraints.Generate(_elevation.Seed, _elevation.Island);
            _runtime.Map.AssignElevation(_elevation, constraints);
            _runtime.Map.AssignRainfall(_biomes);
            _runtime.Map.AssignRivers(_rivers);

            Vector2[] elevationRainfall;
            int[] landIndices = Mapgen4Geometry.BuildLandIndices(_runtime.Map, _elevation.MountainFolds, out elevationRainfall);
            UpdateLandMesh(_runtime.VertexPositions, elevationRainfall, landIndices);

            Mapgen4RiverVertex[] riverVertices = Mapgen4Geometry.BuildRiverVertices(_runtime.Mesh, _runtime.Map, _rivers);
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
            Vector2 inverseTextureSize = new Vector2(1.5f / TextureSize, 1.5f / TextureSize);

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
        }

        void RenderPasses()
        {
            _riverCamera.Render();
            _landCamera.Render();
            _depthCamera.Render();
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

        RenderTexture CreateRenderTexture(string name, RenderTextureFormat format)
        {
            var texture = new RenderTexture(TextureSize, TextureSize, 24, format, RenderTextureReadWrite.Linear)
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

        Matrix4x4 CalculateTopdownMatrix()
        {
            Matrix4x4 translate = Matrix4x4.Translate(new Vector3(-1f, -1f, 0f));
            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(1f / 500f, 1f / 500f, 1f));
            return translate * scale;
        }

        Matrix4x4 CalculateProjectionMatrix()
        {
            Matrix4x4 rotateX = Matrix4x4.Rotate(Quaternion.Euler(180f + _render.TiltDeg, 0f, 0f));
            Matrix4x4 rotateZ = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, _render.RotateDeg));
            Matrix4x4 oblique = Matrix4x4.identity;
            oblique.m21 = 1f;
            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(_render.Zoom / 100f, _render.Zoom / 100f, _render.MountainHeight * _render.Zoom / 100f));
            Matrix4x4 translate = Matrix4x4.Translate(new Vector3(-_render.X, -_render.Y, 0f));
            return rotateX * rotateZ * oblique * scale * translate;
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

        void ResetDefaults()
        {
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
            MarkGeneratorDirty();
        }
    }
}
