Shader "EconSim/MapOverlay"
{
    Properties
    {
        // Heightmap for terrain (Phase 6)
        _HeightmapTex ("Heightmap", 2D) = "gray" {}
        _ReliefNormalTex ("Relief Normal", 2D) = "bump" {}
        _ReliefNormalStrength ("Relief Normal Strength", Range(0, 1)) = 1
        _ReliefShadeStrength ("Relief Shade Strength", Range(0, 1)) = 0.35
        _ReliefAmbient ("Relief Ambient", Range(0, 1)) = 0.65
        _ReliefLightDir ("Relief Light Direction", Vector) = (0.4, 0.85, 0.3, 0)
        _HeightScale ("Height Scale", Float) = 3
        _SeaLevel ("Sea Level", Float) = 0.2
        _UseHeightDisplacement ("Use Height Displacement", Int) = 0

        // River mask (Phase 8) - knocks out rivers from land, showing water underneath
        _RiverMaskTex ("River Mask", 2D) = "black" {}

        // Split core textures (M3-S1)
        _PoliticalIdsTex ("Political IDs", 2D) = "black" {}
        _GeographyBaseTex ("Geography Base", 2D) = "black" {}
        // Legacy packed texture kept for migration compatibility.
        _CellDataTex ("Cell Data (Legacy)", 2D) = "black" {}

        // Resolved mode color texture (M3-S3)
        _ModeColorResolve ("Mode Color Resolve", 2D) = "black" {}
        _UseModeColorResolve ("Use Mode Color Resolve", Int) = 1

        // Cell to market mapping (dynamic, updated when economy changes)
        _CellToMarketTex ("Cell To Market", 2D) = "black" {}

        // Color palettes (256 entries each)
        _RealmPaletteTex ("Realm Palette", 2D) = "white" {}
        _MarketPaletteTex ("Market Palette", 2D) = "white" {}
        _BiomePaletteTex ("Biome Palette", 2D) = "white" {}

        // Biome-elevation matrix (64x64: biome x elevation)
        _BiomeMatrixTex ("Biome Elevation Matrix", 2D) = "white" {}

        // Selection highlight (only one should be >= 0 at a time)
        _SelectedRealmId ("Selected Realm ID (normalized)", Float) = -1
        _SelectedProvinceId ("Selected Province ID (normalized)", Float) = -1
        _SelectedCountyId ("Selected County ID (normalized)", Float) = -1
        _SelectedMarketId ("Selected Market ID (normalized)", Float) = -1
        _SelectionDimming ("Selection Dimming", Range(0, 1)) = 0.5
        _SelectionDesaturation ("Selection Desaturation", Range(0, 1)) = 0

        // Hover highlight (only one should be >= 0 at a time, separate from selection)
        _HoveredRealmId ("Hovered Realm ID (normalized)", Float) = -1
        _HoveredProvinceId ("Hovered Province ID (normalized)", Float) = -1
        _HoveredCountyId ("Hovered County ID (normalized)", Float) = -1
        _HoveredMarketId ("Hovered Market ID (normalized)", Float) = -1
        _HoverIntensity ("Hover Intensity", Range(0, 1)) = 0

        // Soil overlay (mode 6)
        _SoilHeightFloor ("Soil Height Floor", Range(0, 1)) = 0
        _SoilColor0 ("Permafrost", Color) = (0.80, 0.85, 0.90, 1)
        _SoilColor1 ("Saline", Color) = (0.92, 0.90, 0.82, 1)
        _SoilColor2 ("Lithosol", Color) = (0.70, 0.68, 0.65, 1)
        _SoilColor3 ("Alluvial", Color) = (0.55, 0.42, 0.28, 1)
        _SoilColor4 ("Aridisol", Color) = (0.88, 0.78, 0.58, 1)
        _SoilColor5 ("Laterite", Color) = (0.82, 0.42, 0.25, 1)
        _SoilColor6 ("Podzol", Color) = (0.62, 0.58, 0.48, 1)
        _SoilColor7 ("Chernozem", Color) = (0.38, 0.33, 0.27, 1)

        // Map mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 5=terrain/biome, 6=soil, 7=channel inspector
        _MapMode ("Map Mode", Int) = 0
        _DebugView ("Debug View", Int) = 0

        // Gradient fill style (edge-to-center fade for political/market modes)
        _GradientRadius ("Gradient Radius (pixels)", Range(5, 100)) = 40
        _GradientEdgeDarkening ("Gradient Edge Darkening", Range(0, 1)) = 0.5
        _GradientCenterOpacity ("Gradient Center Opacity", Range(0, 1)) = 0.5

        // Realm border (world-space, in texels of data texture)
        _RealmBorderDistTex ("Realm Border Distance", 2D) = "white" {}
        _RealmBorderWidth ("Realm Border Width", Range(0, 4)) = 1
        _RealmBorderDarkening ("Realm Border Darkening", Range(0, 1)) = 0.5

        // Province border (thinner, lighter than realm)
        _ProvinceBorderDistTex ("Province Border Distance", 2D) = "white" {}
        _ProvinceBorderWidth ("Province Border Width", Range(0, 4)) = 0.7
        _ProvinceBorderDarkening ("Province Border Darkening", Range(0, 1)) = 0.35

        // County border (thinnest, lightest)
        _CountyBorderDistTex ("County Border Distance", 2D) = "white" {}
        _CountyBorderWidth ("County Border Width", Range(0, 4)) = 0.5
        _CountyBorderDarkening ("County Border Darkening", Range(0, 1)) = 0.25

        // Market zone border (same style as realm borders)
        _MarketBorderDistTex ("Market Border Distance", 2D) = "white" {}
        _MarketBorderWidth ("Market Border Width", Range(0, 4)) = 1
        _MarketBorderDarkening ("Market Border Darkening", Range(0, 1)) = 0.5

        // Path overlay (market mode only, direct mask: 0=no path, 1=path)
        _RoadMaskTex ("Road Mask", 2D) = "black" {}
        _PathOpacity ("Path Opacity", Range(0, 1)) = 0.75
        _PathDashLength ("Path Dash Length", Range(0.1, 20)) = 1.8
        _PathGapLength ("Path Gap Length", Range(0.1, 20)) = 2.4
        _PathWidth ("Path Width", Range(0.2, 4)) = 0.8

        // Water layer properties
        _WaterShallowColor ("Water Shallow Color", Color) = (0.25, 0.55, 0.65, 1)
        _WaterDeepColor ("Water Deep Color", Color) = (0.06, 0.12, 0.25, 1)
        _WaterDepthRange ("Water Depth Range", Float) = 0.2
        _WaterShallowAlpha ("Water Shallow Alpha", Range(0, 1)) = 0.5
        _WaterDeepAlpha ("Water Deep Alpha", Range(0, 1)) = 0.95
        _RiverDepth ("River Depth", Range(0, 0.5)) = 0.1
        _RiverDarken ("River Darken", Range(0, 1)) = 0.3
        _ShimmerScale ("Shimmer Scale", Float) = 0.02
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.03
        _ShimmerIntensity ("Shimmer Intensity", Range(0, 0.2)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        CGINCLUDE
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 vertexColor : COLOR;
                float2 dataUV : TEXCOORD0;    // Unified UV for all textures (Y-up coordinates)
                float2 worldUV : TEXCOORD1;   // World-space UV for consistent shimmer scale
            };

            sampler2D _HeightmapTex;
            sampler2D _ReliefNormalTex;
            float4 _HeightmapTex_TexelSize;  // (1/width, 1/height, width, height)
            float _ReliefNormalStrength;
            float _ReliefShadeStrength;
            float _ReliefAmbient;
            float4 _ReliefLightDir;
            float _HeightScale;
            float _SeaLevel;
            int _UseHeightDisplacement;

            sampler2D _RiverMaskTex;  // River mask (1 = river, 0 = not river)

            sampler2D _PoliticalIdsTex;
            sampler2D _GeographyBaseTex;
            sampler2D _CellDataTex; // Legacy compatibility path.
            sampler2D _ModeColorResolve;
            int _UseModeColorResolve;

            sampler2D _CellToMarketTex;  // 16384x1 texture mapping cellId -> marketId

            sampler2D _RealmPaletteTex;
            sampler2D _MarketPaletteTex;
            sampler2D _BiomePaletteTex;
            sampler2D _BiomeMatrixTex;
            float _SoilHeightFloor;
            fixed4 _SoilColor0;
            fixed4 _SoilColor1;
            fixed4 _SoilColor2;
            fixed4 _SoilColor3;
            fixed4 _SoilColor4;
            fixed4 _SoilColor5;
            fixed4 _SoilColor6;
            fixed4 _SoilColor7;

            int _MapMode;
            int _DebugView;
            float _GradientRadius;
            float _GradientEdgeDarkening;
            float _GradientCenterOpacity;
            sampler2D _RealmBorderDistTex;
            float _RealmBorderWidth;
            float _RealmBorderDarkening;
            sampler2D _ProvinceBorderDistTex;
            float _ProvinceBorderWidth;
            float _ProvinceBorderDarkening;
            sampler2D _CountyBorderDistTex;
            float _CountyBorderWidth;
            float _CountyBorderDarkening;
            sampler2D _MarketBorderDistTex;
            float _MarketBorderWidth;
            float _MarketBorderDarkening;

            sampler2D _RoadMaskTex;
            float _PathOpacity;

            // Water layer uniforms
            fixed4 _WaterShallowColor;
            fixed4 _WaterDeepColor;
            float _WaterDepthRange;
            float _WaterShallowAlpha;
            float _WaterDeepAlpha;
            float _RiverDepth;
            float _RiverDarken;
            float _ShimmerScale;
            float _ShimmerSpeed;
            float _ShimmerIntensity;

            float _SelectedRealmId;
            float _SelectedProvinceId;
            float _SelectedCountyId;
            float _SelectedMarketId;
            float _SelectionDimming;
            float _SelectionDesaturation;

            float _HoveredRealmId;
            float _HoveredProvinceId;
            float _HoveredCountyId;
            float _HoveredMarketId;
            float _HoverIntensity;

            v2f vert(appdata v)
            {
                v2f o;

                float4 vertex = v.vertex;

                // Vertex displacement from heightmap
                if (_UseHeightDisplacement > 0)
                {
                    float height = tex2Dlod(_HeightmapTex, float4(v.texcoord.xy, 0, 0)).r;
                    vertex.y = (height - _SeaLevel) * _HeightScale;
                }

                o.pos = UnityObjectToClipPos(vertex);
                o.vertexColor = v.color;
                // Single UV for all textures (Y-up coordinates, unified)
                o.dataUV = v.texcoord.xy;

                // World UV for shimmer (consistent scale regardless of mesh UVs)
                float3 worldPos = mul(unity_ObjectToWorld, vertex).xyz;
                o.worldUV = worldPos.xz * _ShimmerScale;

                return o;
            }

            #include "MapOverlay.Common.cginc"
            #include "MapOverlay.Composite.cginc"
            #include "MapOverlay.ResolveModes.cginc"

            // ========================================================================
            // Fragment shader: layered compositing
            // ========================================================================

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.dataUV;

                // ---- Data sampling preamble ----

                float4 politicalIds = SamplePoliticalIds(uv);
                float4 geographyBase = SampleGeographyBase(uv);
                float realmId = politicalIds.r;
                float provinceId = politicalIds.g;
                float countyId = politicalIds.b;

                if (_MapMode == 7)
                {
                    return fixed4(ComputeChannelInspector(uv, politicalIds, geographyBase), 1);
                }

                float marketId = LookupMarketIdFromCounty(countyId);

                float biomeId = geographyBase.r;
                int soilId = (int)clamp(round(geographyBase.g * 65535.0), 0.0, 7.0);
                bool isCellWater = geographyBase.a >= 0.5;

                float riverMask = tex2D(_RiverMaskTex, IN.dataUV).r;
                bool isRiver = riverMask > 0.5;

                // isWater combines both sources (used for selection/hover land checks)
                bool isWater = isCellWater || isRiver;

                float height = tex2D(_HeightmapTex, IN.dataUV).r;

                // ---- Layer 1: Terrain ----

                float3 terrain;
                if (_MapMode == 0)
                {
                    // Height gradient mode: override terrain with debug viz
                    terrain = ComputeHeightGradient(isCellWater, height, riverMask);
                }
                else if (_MapMode == 6)
                {
                    // Soil mode: grayscale heightmap × soil color
                    if (isCellWater)
                    {
                        terrain = ComputeTerrain(uv, isCellWater, biomeId, height, riverMask);
                    }
                    else
                    {
                        float landHeight = NormalizeLandHeight(height);

                        float3 soilColor;
                        if (soilId <= 0) soilColor = _SoilColor0.rgb;
                        else if (soilId == 1) soilColor = _SoilColor1.rgb;
                        else if (soilId == 2) soilColor = _SoilColor2.rgb;
                        else if (soilId == 3) soilColor = _SoilColor3.rgb;
                        else if (soilId == 4) soilColor = _SoilColor4.rgb;
                        else if (soilId == 5) soilColor = _SoilColor5.rgb;
                        else if (soilId == 6) soilColor = _SoilColor6.rgb;
                        else soilColor = _SoilColor7.rgb;

                        float brightness = lerp(_SoilHeightFloor, 1.0, landHeight);
                        terrain = soilColor * brightness;
                    }
                }
                else
                {
                    terrain = ComputeTerrain(uv, isCellWater, biomeId, height, riverMask);
                }

                // ---- Layer 2: Map mode ----

                float4 mapMode;
                if (_UseModeColorResolve > 0)
                {
                    float3 resolvedBase = tex2D(_ModeColorResolve, uv).rgb;
                    mapMode = ComputeMapModeFromResolvedBase(uv, isCellWater, isRiver, height, resolvedBase);
                }
                else
                {
                    mapMode = ComputeMapMode(uv, isCellWater, isRiver, height, realmId, provinceId, countyId, marketId);
                }
                float3 afterMapMode = lerp(terrain, mapMode.rgb, mapMode.a);

                // ---- Layer 3: Water ----

                float3 waterColor;
                float waterAlpha;

                if (_MapMode == 0)
                {
                    // Height mode: no water overlay (already has its own water colors)
                    waterColor = float3(0, 0, 0);
                    waterAlpha = 0;
                }
                else
                {
                    ComputeWater(isCellWater, height, riverMask, IN.worldUV, waterColor, waterAlpha);
                }

                float3 afterWater = lerp(afterMapMode, waterColor, waterAlpha);
                float3 relitColor = ApplyReliefShading(afterWater, uv, isWater);

                // ---- Layer 4: Selection / hover (operates on composited color) ----

                float3 finalColor = relitColor;

                // Selection region test (for dimming non-selected areas)
                bool isInSelection = false;
                if (_SelectedRealmId >= 0)
                    isInSelection = !isWater && IdEquals(realmId, _SelectedRealmId);
                else if (_SelectedProvinceId >= 0)
                    isInSelection = !isWater && IdEquals(provinceId, _SelectedProvinceId);
                else if (_SelectedMarketId >= 0)
                    isInSelection = !isWater && IdEquals(marketId, _SelectedMarketId);
                else if (_SelectedCountyId >= 0)
                    isInSelection = !isWater && IdEquals(countyId, _SelectedCountyId);

                // Hover effect
                bool isHovered = (!isWater) && (
                    (_HoveredRealmId >= 0 && IdEquals(realmId, _HoveredRealmId)) ||
                    (_HoveredProvinceId >= 0 && IdEquals(provinceId, _HoveredProvinceId)) ||
                    (_HoveredCountyId >= 0 && IdEquals(countyId, _HoveredCountyId)) ||
                    (_HoveredMarketId >= 0 && IdEquals(marketId, _HoveredMarketId)));
                if (isHovered && _HoverIntensity > 0)
                {
                    // Hue-preserving hover highlight: brighten in RGB space only.
                    // Avoid HSV round-trip to prevent hue drift on low-saturation colors.
                    float boost = 1.0 + 0.25 * _HoverIntensity;
                    finalColor = saturate(finalColor * boost);
                }

                // Selection dimming
                bool hasSelection = _SelectedRealmId >= 0 || _SelectedProvinceId >= 0 || _SelectedMarketId >= 0 || _SelectedCountyId >= 0;
                if (hasSelection && !isInSelection && !isWater)
                {
                    float3 gray = ToGrayscale(finalColor);
                    finalColor = lerp(finalColor, gray, _SelectionDesaturation);
                    finalColor *= _SelectionDimming;
                }

                return fixed4(finalColor, 1);
            }
            // Stencil fragment: marks border band pixels for political and market modes
            fixed4 frag_stencil(v2f IN) : SV_Target
            {
                if (_MapMode >= 1 && _MapMode <= 3)
                {
                    float realmBorderDist = tex2D(_RealmBorderDistTex, IN.dataUV).r * 255.0;
                    float provinceBorderDist = tex2D(_ProvinceBorderDistTex, IN.dataUV).r * 255.0;
                    float countyBorderDist = tex2D(_CountyBorderDistTex, IN.dataUV).r * 255.0;
                    if (realmBorderDist >= _RealmBorderWidth && provinceBorderDist >= _ProvinceBorderWidth && countyBorderDist >= _CountyBorderWidth) discard;
                }
                else if (_MapMode == 4)
                {
                    float marketBorderDist = tex2D(_MarketBorderDistTex, IN.dataUV).r * 255.0;
                    if (marketBorderDist >= _MarketBorderWidth) discard;
                }
                else
                {
                    discard;
                }

                return fixed4(0, 0, 0, 0);
            }
        ENDCG

        // Pass 0: Main rendering
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            ENDCG
        }

        // Pass 1: Stencil mask for realm border band
        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_stencil
            #pragma target 3.5
            ENDCG
        }
    }
    FallBack "Diffuse"
    CustomEditor "EconSim.Editor.MapOverlayShaderGUI"
}
