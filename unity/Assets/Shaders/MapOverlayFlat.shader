Shader "EconSim/MapOverlayFlat"
{
    Properties
    {
        // Heightmap for terrain (Phase 6)
        _HeightmapTex ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Float) = 0.2
        _SeaLevel ("Sea Level (Normalized)", Float) = 0.2
        _UseHeightDisplacement ("Use Height Displacement", Int) = 0

        // River mask (Phase 8) - knocks out rivers from land, showing water underneath
        _RiverMaskTex ("River Mask", 2D) = "black" {}

        // Split core textures (M3-S1)
        // Sampler budget note:
        // Metal compiles this shader with a strict fragment sampler limit (16).
        // Do not add new fragment-sampled textures casually.
        // Overlay composition uses _OverlayTex, and marketId is packed in _ModeColorResolve.a.
        _PoliticalIdsTex ("Political IDs", 2D) = "black" {}
        _GeographyBaseTex ("Geography Base", 2D) = "black" {}
        _VegetationTex ("Vegetation Data", 2D) = "black" {}
        _OverlayTex ("Overlay Layer", 2D) = "black" {}

        // Resolved mode color texture (M3-S3)
        _ModeColorResolve ("Mode Color Resolve", 2D) = "black" {}
        _UseModeColorResolve ("Use Mode Color Resolve", Int) = 1
        _OverlayOpacity ("Overlay Opacity", Range(0, 1)) = 0.65
        _OverlayEnabled ("Overlay Enabled", Int) = 0

        // Cell to market mapping (dynamic, updated when economy changes)
        _CellToMarketTex ("Cell To Market", 2D) = "black" {}

        // Color palettes (256 entries each)
        _RealmPaletteTex ("Realm Palette", 2D) = "white" {}
        _MarketPaletteTex ("Market Palette", 2D) = "white" {}
        _BiomePaletteTex ("Biome Palette", 2D) = "white" {}

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

        // Map mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 6=biomes, 7=channel inspector, 8=local transport, 9=market transport
        _MapMode ("Map Mode", Int) = 0
        _DebugView ("Debug View", Int) = 0

        // Gradient fill style (edge-to-center fade for political/market modes)
        _GradientRadius ("Gradient Radius (pixels)", Range(5, 100)) = 40
        _GradientEdgeDarkening ("Gradient Edge Darkening", Range(0, 1)) = 0.5

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
        _WaterShallowColor ("Shallow Water Color", Color) = (0.25, 0.55, 0.65, 1)
        _WaterShallowAlpha ("River Alpha", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
            };

            // Sampler budget note (Metal):
            // Keep total fragment samplers <= 16 for this shader.
            // Overlay uses _OverlayTex to avoid introducing another sampler.
            TEXTURE2D(_HeightmapTex);
            SAMPLER(sampler_HeightmapTex);
            TEXTURE2D(_ReliefNormalTex);
            SAMPLER(sampler_ReliefNormalTex);
            float4 _HeightmapTex_TexelSize;  // (1/width, 1/height, width, height)

            TEXTURE2D(_RiverMaskTex);  // River mask (1 = river, 0 = not river)
            SAMPLER(sampler_RiverMaskTex);

            TEXTURE2D(_PoliticalIdsTex);
            SAMPLER(sampler_PoliticalIdsTex);
            TEXTURE2D(_GeographyBaseTex);
            SAMPLER(sampler_GeographyBaseTex);
            TEXTURE2D(_VegetationTex);
            SAMPLER(sampler_VegetationTex);
            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);
            TEXTURE2D(_ModeColorResolve);
            SAMPLER(sampler_ModeColorResolve);
            TEXTURE2D(_CellToMarketTex);
            SAMPLER(sampler_CellToMarketTex);

            TEXTURE2D(_RealmPaletteTex);
            SAMPLER(sampler_RealmPaletteTex);
            TEXTURE2D(_MarketPaletteTex);
            SAMPLER(sampler_MarketPaletteTex);
            TEXTURE2D(_BiomePaletteTex);
            SAMPLER(sampler_BiomePaletteTex);
            TEXTURE2D(_RealmBorderDistTex);
            SAMPLER(sampler_RealmBorderDistTex);
            TEXTURE2D(_ProvinceBorderDistTex);
            SAMPLER(sampler_ProvinceBorderDistTex);
            TEXTURE2D(_CountyBorderDistTex);
            SAMPLER(sampler_CountyBorderDistTex);
            TEXTURE2D(_MarketBorderDistTex);
            SAMPLER(sampler_MarketBorderDistTex);

            TEXTURE2D(_RoadMaskTex);
            SAMPLER(sampler_RoadMaskTex);

            CBUFFER_START(UnityPerMaterial)
                float _HeightScale;
                float _SeaLevel;
                int _UseHeightDisplacement;

                int _UseModeColorResolve;
                float _OverlayOpacity;
                int _OverlayEnabled;

                int _MapMode;
                int _DebugView;
                float _GradientRadius;
                float _GradientEdgeDarkening;
                float _RealmBorderWidth;
                float _RealmBorderDarkening;
                float _ProvinceBorderWidth;
                float _ProvinceBorderDarkening;
                float _CountyBorderWidth;
                float _CountyBorderDarkening;
                float _MarketBorderWidth;
                float _MarketBorderDarkening;
                float _PathOpacity;

                // Water layer uniforms
                half4 _WaterShallowColor;
                float _WaterShallowAlpha;

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
            CBUFFER_END

            // Bridge legacy sampling calls to SRP texture/sampler macros.
            #define tex2D(tex, uv) SAMPLE_TEXTURE2D(tex, sampler##tex, uv)
            #define tex2Dlod(tex, coord) SAMPLE_TEXTURE2D_LOD(tex, sampler##tex, (coord).xy, (coord).w)

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

                o.pos = TransformObjectToHClip(vertex.xyz);
                o.vertexColor = v.color;
                // Single UV for all textures (Y-up coordinates, unified)
                o.dataUV = v.texcoord.xy;

                return o;
            }

            #include "MapOverlay.Common.cginc"
            #define MAP_OVERLAY_DISABLE_WATER_VOLUME 1
            #include "MapOverlay.Composite.cginc"
            #include "MapOverlay.ResolveModes.cginc"

            // ========================================================================
            // Fragment shader: layered compositing
            // ========================================================================

            half4 frag(v2f IN) : SV_Target
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
                    return half4(ComputeChannelInspector(uv, politicalIds, geographyBase), 1);
                }

                float marketId = 0.0;

                bool isCellWater = geographyBase.a >= 0.5;

                float riverMask = tex2D(_RiverMaskTex, IN.dataUV).r;
                bool isRiver = riverMask > 0.5;

                // isWater combines both sources (used for selection/hover land checks)
                bool isWater = isCellWater || isRiver;

                float height = tex2D(_HeightmapTex, IN.dataUV).r;

                // ---- Layer 1: Terrain ----

                float3 terrain = _MapMode == 0
                    ? ComputeHeightGradient(isCellWater, height, riverMask)
                    : float3(0.0, 0.0, 0.0);

                // ---- Layer 2: Map mode ----

                float4 mapMode;
                if (_UseModeColorResolve > 0)
                {
                    float4 resolvedMode = tex2D(_ModeColorResolve, uv);
                    float3 resolvedBase = resolvedMode.rgb;
                    marketId = resolvedMode.a;
                    mapMode = ComputeMapModeFromResolvedBase(uv, isCellWater, isRiver, height, resolvedBase);
                }
                else
                {
                    marketId = 0.0;
                    mapMode = ComputeMapMode(uv, isCellWater, isRiver, height, realmId, provinceId, countyId, marketId);
                }
                float3 afterMapMode;
                if (!isWater && mapMode.a > 0.001)
                {
                    // Flat land modes are pure mode color (no terrain layer).
                    afterMapMode = mapMode.rgb;
                }
                else
                {
                    // Preserve terrain substrate for water/rivers and debug fallback modes.
                    afterMapMode = terrain;
                }

                // ---- Layer 3: Water ----

                if (_MapMode == 0)
                {
                    // Height mode: no water overlay (already has its own water colors)
                    // Keep the height-mode water gradient untouched.
                    // (ComputeWater is only for normal overlay compositing.)
                    // NOP
                }
                else
                {
                    // Flat style water tinting (no volumetric/refraction/shimmer).
                    if (isCellWater)
                    {
                        afterMapMode = _WaterShallowColor.rgb;
                    }
                    else if (riverMask > 0.01)
                    {
                        float riverAlpha = _WaterShallowAlpha * riverMask;
                        afterMapMode = lerp(afterMapMode, _WaterShallowColor.rgb, riverAlpha);
                    }
                }
                float3 afterWater = afterMapMode;
                float3 relitColor = afterWater;

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

                if (_OverlayEnabled > 0 && !isWater)
                {
                    float4 overlayColor = tex2D(_OverlayTex, uv);
                    float overlayAlpha = saturate(_OverlayOpacity * overlayColor.a);
                    finalColor = lerp(finalColor, overlayColor.rgb, overlayAlpha);
                }

                return half4(finalColor, 1);
            }
            // Stencil fragment: marks border band pixels for political and market modes
            half4 frag_stencil(v2f IN) : SV_Target
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

                return half4(0, 0, 0, 0);
            }
        ENDHLSL

        // Pass 0: Main rendering
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            ENDHLSL
        }

        // Pass 1: Stencil mask for realm border band
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag_stencil
            #pragma target 3.5
            ENDHLSL
        }
    }
    FallBack "Diffuse"
    CustomEditor "EconSim.Editor.MapOverlayShaderGUI"
}
