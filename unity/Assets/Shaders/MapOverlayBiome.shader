Shader "EconSim/MapOverlayBiome"
{
    Properties
    {
        // Heightmap for terrain (Phase 6)
        _HeightmapTex ("Heightmap", 2D) = "gray" {}
        _ReliefNormalTex ("Relief Normal", 2D) = "bump" {}
        _ReliefNormalStrength ("Relief Normal Strength", Range(0, 1)) = 0.75
        _ReliefShadeStrength ("Relief Shade Strength", Range(0, 1)) = 0.28
        _ReliefAmbient ("Relief Ambient", Range(0, 1)) = 0.65
        _ReliefLightDir ("Relief Light Direction", Vector) = (0.4, 0.85, 0.3, 0)
        _HeightScale ("Height Scale", Float) = 0.2
        _SeaLevel ("Sea Level (Normalized)", Float) = 0.2
        _UseHeightDisplacement ("Use Height Displacement", Int) = 0

        // River mask (Phase 8) - knocks out rivers from land, showing water underneath
        _RiverMaskTex ("River Mask", 2D) = "black" {}

        // Split core textures (M3-S1)
        // Sampler budget note:
        // Metal compiles this shader with a strict fragment sampler limit (16).
        // Do not add new fragment-sampled textures casually.
        // Overlay composition intentionally reuses _CellDataTex.
        _PoliticalIdsTex ("Political IDs", 2D) = "black" {}
        _GeographyBaseTex ("Geography Base", 2D) = "black" {}
        _VegetationTex ("Vegetation Data", 2D) = "black" {}
        // Legacy packed texture kept for migration compatibility.
        _CellDataTex ("Cell Data (Legacy)", 2D) = "black" {}

        _OverlayOpacity ("Overlay Opacity", Range(0, 1)) = 0.65
        _OverlayEnabled ("Overlay Enabled", Int) = 0

        // Biome palette (kept for shared terrain helper compatibility).
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

        // Biomes overlay (mode 6, vertex-blended)
        _SoilHeightFloor ("Soil Height Floor", Range(0, 1)) = 0
        _SoilBlendRadius ("Soil Blend Radius (texels)", Range(0.25, 6)) = 1.0
        _SoilBlendSharpness ("Soil Blend Sharpness", Range(0.5, 6)) = 1.4
        _SoilColor0 ("Permafrost", Color) = (0.80, 0.85, 0.90, 1)
        _SoilColor1 ("Saline", Color) = (0.92, 0.90, 0.82, 1)
        _SoilColor2 ("Lithosol", Color) = (0.70, 0.68, 0.65, 1)
        _SoilColor3 ("Alluvial", Color) = (0.55, 0.42, 0.28, 1)
        _SoilColor4 ("Aridisol", Color) = (0.88, 0.78, 0.58, 1)
        _SoilColor5 ("Laterite", Color) = (0.82, 0.42, 0.25, 1)
        _SoilColor6 ("Podzol", Color) = (0.62, 0.58, 0.48, 1)
        _SoilColor7 ("Chernozem", Color) = (0.38, 0.33, 0.27, 1)

        _VegetationStippleOpacity ("Vegetation Stipple Opacity", Range(0, 1)) = 0.8
        _VegetationStippleScale ("Vegetation Stipple Scale (texels)", Range(1, 12)) = 3
        _VegetationStippleJitter ("Vegetation Stipple Jitter", Range(0, 1)) = 0.4
        _VegetationCoverageContrast ("Vegetation Coverage Contrast", Range(0.5, 2)) = 1
        _VegetationStippleSoftness ("Vegetation Stipple Softness", Range(0.5, 2.5)) = 1
        _VegetationColor0 ("Vegetation None", Color) = (0.0, 0.0, 0.0, 1)
        _VegetationColor1 ("Vegetation Lichen/Moss", Color) = (0.52, 0.62, 0.44, 1)
        _VegetationColor2 ("Vegetation Grass", Color) = (0.48, 0.63, 0.27, 1)
        _VegetationColor3 ("Vegetation Shrub", Color) = (0.41, 0.52, 0.25, 1)
        _VegetationColor4 ("Vegetation Deciduous", Color) = (0.27, 0.45, 0.19, 1)
        _VegetationColor5 ("Vegetation Coniferous", Color) = (0.17, 0.33, 0.20, 1)
        _VegetationColor6 ("Vegetation Broadleaf", Color) = (0.18, 0.37, 0.14, 1)

        // Map mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 6=biomes, 7=channel inspector, 8=local transport, 9=market transport
        _MapMode ("Map Mode", Int) = 0
        _DebugView ("Debug View", Int) = 0

        // Water layer properties
        _WaterShallowColor ("Shallow Water Color", Color) = (0.25, 0.55, 0.65, 1)
        _WaterDeepColor ("Deep Water Color", Color) = (0.06, 0.12, 0.25, 1)
        _WaterAbsorption ("Water Absorption (RGB)", Color) = (2.2, 0.9, 0.35, 1)
        _WaterOpticalDepth ("Water Optical Depth", Range(0.1, 8)) = 3.0
        _WaterDepthExponent ("Water Depth Exponent", Range(0.2, 3)) = 1.35
        _WaterRefractionStrength ("Seabed Refraction", Range(0, 0.02)) = 0.0035
        _WaterRefractionScale ("Refraction Scale", Float) = 0.035
        _WaterRefractionSpeed ("Refraction Speed", Float) = 0.02
        _WaterShallowAlpha ("River Alpha", Range(0, 1)) = 0.5
        _ShimmerScale ("Shimmer Scale", Float) = 0.02
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.03
        _ShimmerIntensity ("Shimmer Intensity", Range(0, 0.2)) = 0.08
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
                float2 worldUV : TEXCOORD1;   // World-space UV for consistent shimmer scale
            };

            // Sampler budget note (Metal):
            // Keep total fragment samplers <= 16 for this shader.
            // Overlay uses _CellDataTex to avoid introducing another sampler.
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
            float4 _GeographyBaseTex_TexelSize;  // (1/width, 1/height, width, height)
            TEXTURE2D(_VegetationTex);
            SAMPLER(sampler_VegetationTex);
            TEXTURE2D(_CellDataTex); // Legacy compatibility path.
            SAMPLER(sampler_CellDataTex);
            TEXTURE2D(_BiomePaletteTex);
            SAMPLER(sampler_BiomePaletteTex);

            CBUFFER_START(UnityPerMaterial)
                float _ReliefNormalStrength;
                float _ReliefShadeStrength;
                float _ReliefAmbient;
                float4 _ReliefLightDir;
                float _HeightScale;
                float _SeaLevel;
                int _UseHeightDisplacement;

                float _OverlayOpacity;
                int _OverlayEnabled;

                float _SoilHeightFloor;
                float _SoilBlendRadius;
                float _SoilBlendSharpness;
                half4 _SoilColor0;
                half4 _SoilColor1;
                half4 _SoilColor2;
                half4 _SoilColor3;
                half4 _SoilColor4;
                half4 _SoilColor5;
                half4 _SoilColor6;
                half4 _SoilColor7;
                half4 _VegetationColor0;
                half4 _VegetationColor1;
                half4 _VegetationColor2;
                half4 _VegetationColor3;
                half4 _VegetationColor4;
                half4 _VegetationColor5;
                half4 _VegetationColor6;
                float _VegetationStippleOpacity;
                float _VegetationStippleScale;
                float _VegetationStippleJitter;
                float _VegetationCoverageContrast;
                float _VegetationStippleSoftness;

                int _MapMode;
                int _DebugView;

                // Water layer uniforms
                half4 _WaterShallowColor;
                half4 _WaterDeepColor;
                half4 _WaterAbsorption;
                float _WaterOpticalDepth;
                float _WaterDepthExponent;
                float _WaterRefractionStrength;
                float _WaterRefractionScale;
                float _WaterRefractionSpeed;
                float _WaterShallowAlpha;
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

                // World UV for shimmer (consistent scale regardless of mesh UVs)
                float3 worldPos = TransformObjectToWorld(vertex.xyz);
                o.worldUV = worldPos.xz * _ShimmerScale;

                return o;
            }

            #include "MapOverlay.Common.cginc"
            #define MAP_OVERLAY_DISABLE_CHANNEL_INSPECTOR 1
            #include "MapOverlay.Composite.cginc"

            float3 SoilColorFromId(int soilId)
            {
                if (soilId <= 0) return _SoilColor0.rgb;
                if (soilId == 1) return _SoilColor1.rgb;
                if (soilId == 2) return _SoilColor2.rgb;
                if (soilId == 3) return _SoilColor3.rgb;
                if (soilId == 4) return _SoilColor4.rgb;
                if (soilId == 5) return _SoilColor5.rgb;
                if (soilId == 6) return _SoilColor6.rgb;
                return _SoilColor7.rgb;
            }

            int DecodeSoilIdFromGeography(float4 geographyBase)
            {
                return (int)clamp(round(geographyBase.g * 65535.0), 0.0, 7.0);
            }

            int DecodeVegetationType(float2 uv)
            {
                float vegetationType = tex2D(_VegetationTex, uv).r;
                return (int)clamp(round(vegetationType * 65535.0), 0.0, 6.0);
            }

            float DecodeVegetationDensity(float2 uv)
            {
                return saturate(tex2D(_VegetationTex, uv).g);
            }

            float3 VegetationColorFromId(int vegetationId)
            {
                if (vegetationId <= 0) return _VegetationColor0.rgb;
                if (vegetationId == 1) return _VegetationColor1.rgb;
                if (vegetationId == 2) return _VegetationColor2.rgb;
                if (vegetationId == 3) return _VegetationColor3.rgb;
                if (vegetationId == 4) return _VegetationColor4.rgb;
                if (vegetationId == 5) return _VegetationColor5.rgb;
                return _VegetationColor6.rgb;
            }

            float ComputeVegetationStippleMask(float2 uv, float vegetationCoverage, int vegetationType)
            {
                float coverage = saturate(vegetationCoverage);
                if (coverage <= 0.0001)
                    return 0.0;
                if (coverage >= 0.9999)
                    return 1.0;

                float contrast = max(_VegetationCoverageContrast, 0.5);
                coverage = saturate((coverage - 0.5) * contrast + 0.5);

                float cellSize = max(_VegetationStippleScale, 1.0);
                float2 texelPos = uv * _GeographyBaseTex_TexelSize.zw;
                float2 tilePos = texelPos / cellSize;
                float2 tile = floor(tilePos);
                float2 local = frac(tilePos) - 0.5;

                float seed = float(vegetationType) * 19.37;
                float2 jitterRand = float2(
                    hash2d(tile + float2(17.0 + seed, 59.0 + seed)),
                    hash2d(tile + float2(83.0 + seed, 29.0 + seed)));
                float2 jitter = (jitterRand - 0.5) * (0.6 * saturate(_VegetationStippleJitter));
                local -= jitter;

                float occupancyRand = hash2d(tile + float2(111.0 + seed, 7.0 + seed));
                float occupancy = saturate(coverage + (occupancyRand - 0.5) * 0.35 * saturate(_VegetationStippleJitter));

                // Area-proportional dot growth with overlap headroom at high density.
                float radius = 0.72 * sqrt(occupancy);
                float dist = length(local);
                float aa = max(fwidth(dist), 1e-4) * max(_VegetationStippleSoftness, 0.5);

                return 1.0 - smoothstep(radius - aa, radius + aa, dist);
            }

            void AccumulateBlendSoilSample(
                float2 sampleUv,
                float weight,
                inout float w0,
                inout float w1,
                inout float w2,
                inout float w3,
                inout float w4,
                inout float w5,
                inout float w6,
                inout float w7)
            {
                float4 sampleGeo = SampleGeographyBase(sampleUv);
                if (sampleGeo.a >= 0.5)
                    return;

                int sampleSoilId = DecodeSoilIdFromGeography(sampleGeo);
                if (sampleSoilId <= 0) w0 += weight;
                else if (sampleSoilId == 1) w1 += weight;
                else if (sampleSoilId == 2) w2 += weight;
                else if (sampleSoilId == 3) w3 += weight;
                else if (sampleSoilId == 4) w4 += weight;
                else if (sampleSoilId == 5) w5 += weight;
                else if (sampleSoilId == 6) w6 += weight;
                else w7 += weight;
            }

            float3 ComputeBlendedSoilColor(float2 uv, int centerSoilId)
            {
                float2 texelStep = _GeographyBaseTex_TexelSize.xy * max(_SoilBlendRadius, 0.25);
                float w0 = 0.0;
                float w1 = 0.0;
                float w2 = 0.0;
                float w3 = 0.0;
                float w4 = 0.0;
                float w5 = 0.0;
                float w6 = 0.0;
                float w7 = 0.0;

                // Neighborhood accumulation preserving categorical composition.
                AccumulateBlendSoilSample(uv, 3.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(-texelStep.x, 0), 2.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(texelStep.x, 0), 2.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(0, -texelStep.y), 2.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(0, texelStep.y), 2.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(-texelStep.x, -texelStep.y), 1.5, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(texelStep.x, -texelStep.y), 1.5, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(-texelStep.x, texelStep.y), 1.5, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(texelStep.x, texelStep.y), 1.5, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(-2.0 * texelStep.x, 0), 1.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(2.0 * texelStep.x, 0), 1.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(0, -2.0 * texelStep.y), 1.0, w0, w1, w2, w3, w4, w5, w6, w7);
                AccumulateBlendSoilSample(uv + float2(0, 2.0 * texelStep.y), 1.0, w0, w1, w2, w3, w4, w5, w6, w7);

                float sharpness = max(_SoilBlendSharpness, 0.5);
                w0 = w0 > 0.0 ? pow(w0, sharpness) : 0.0;
                w1 = w1 > 0.0 ? pow(w1, sharpness) : 0.0;
                w2 = w2 > 0.0 ? pow(w2, sharpness) : 0.0;
                w3 = w3 > 0.0 ? pow(w3, sharpness) : 0.0;
                w4 = w4 > 0.0 ? pow(w4, sharpness) : 0.0;
                w5 = w5 > 0.0 ? pow(w5, sharpness) : 0.0;
                w6 = w6 > 0.0 ? pow(w6, sharpness) : 0.0;
                w7 = w7 > 0.0 ? pow(w7, sharpness) : 0.0;

                float weightSum = w0 + w1 + w2 + w3 + w4 + w5 + w6 + w7;
                if (weightSum <= 1e-5)
                    return SoilColorFromId(centerSoilId);

                float invWeight = 1.0 / weightSum;
                float3 blendColor = _SoilColor0.rgb * (w0 * invWeight);
                blendColor += _SoilColor1.rgb * (w1 * invWeight);
                blendColor += _SoilColor2.rgb * (w2 * invWeight);
                blendColor += _SoilColor3.rgb * (w3 * invWeight);
                blendColor += _SoilColor4.rgb * (w4 * invWeight);
                blendColor += _SoilColor5.rgb * (w5 * invWeight);
                blendColor += _SoilColor6.rgb * (w6 * invWeight);
                blendColor += _SoilColor7.rgb * (w7 * invWeight);
                return blendColor;
            }

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

                float marketId = 0.0;

                float biomeId = geographyBase.r;
                int soilId = DecodeSoilIdFromGeography(geographyBase);
                bool isCellWater = geographyBase.a >= 0.5;

                float riverMask = tex2D(_RiverMaskTex, IN.dataUV).r;
                bool isRiver = riverMask > 0.5;

                // isWater combines both sources (used for selection/hover land checks)
                bool isWater = isCellWater || isRiver;

                float height = tex2D(_HeightmapTex, IN.dataUV).r;

                // ---- Layer 1: Terrain ----

                float3 terrain;
                if (isCellWater)
                {
                    terrain = ComputeTerrain(uv, isCellWater, biomeId, height);
                }
                else
                {
                    float landHeight = NormalizeLandHeight(height);
                    float3 soilColor = ComputeBlendedSoilColor(uv, soilId);
                    int vegetationType = DecodeVegetationType(uv);
                    float vegetationCoverage = DecodeVegetationDensity(uv);
                    if (vegetationType <= 0)
                        vegetationCoverage = 0.0;
                    float3 vegetationColor = VegetationColorFromId(vegetationType);
                    float stippleMask = ComputeVegetationStippleMask(uv, vegetationCoverage, vegetationType);
                    float3 soilLayer = soilColor;
                    float3 stippleLayer = vegetationColor * (stippleMask * saturate(_VegetationStippleOpacity));
                    float3 blendedColor = saturate(soilLayer + stippleLayer);
                    float brightness = lerp(_SoilHeightFloor, 1.0, landHeight);
                    terrain = blendedColor * brightness;
                }

                // ---- Layer 3: Water ----

                float3 afterWater = ComputeWater(isCellWater, height, riverMask, uv, IN.worldUV, terrain);
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

                if (_OverlayEnabled > 0 && !isWater)
                {
                    float4 overlayColor = tex2D(_CellDataTex, uv);
                    float overlayAlpha = saturate(_OverlayOpacity * overlayColor.a);
                    finalColor = lerp(finalColor, overlayColor.rgb, overlayAlpha);
                }

                return half4(finalColor, 1);
            }
            // Biome style does not render political/market border-band stencil.
            half4 frag_stencil(v2f IN) : SV_Target
            {
                discard;
                return half4(0, 0, 0, 0); // Unreachable; required by some compilers.
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
