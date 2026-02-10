Shader "EconSim/MapOverlay"
{
    Properties
    {
        // Heightmap for terrain (Phase 6)
        _HeightmapTex ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Float) = 3
        _SeaLevel ("Sea Level", Float) = 0.2
        _UseHeightDisplacement ("Use Height Displacement", Int) = 0

        // River mask (Phase 8) - knocks out rivers from land, showing water underneath
        _RiverMaskTex ("River Mask", 2D) = "black" {}

        // Data texture: R=RealmId, G=ProvinceId, B=BiomeId+WaterFlag, A=CountyId (normalized to 0-1)
        _CellDataTex ("Cell Data", 2D) = "black" {}

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

        // Map mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 5=terrain/biome, 6=soil
        _MapMode ("Map Mode", Int) = 0

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

        // Road overlay (market mode only, direct mask: 0=no road, 1=road)
        _RoadMaskTex ("Road Mask", 2D) = "black" {}
        _RoadDarkening ("Road Darkening", Range(0, 1)) = 0.4

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
            float4 _HeightmapTex_TexelSize;  // (1/width, 1/height, width, height)
            float _HeightScale;
            float _SeaLevel;
            int _UseHeightDisplacement;

            sampler2D _RiverMaskTex;  // River mask (1 = river, 0 = not river)

            sampler2D _CellDataTex;
            float4 _CellDataTex_TexelSize;  // (1/width, 1/height, width, height)

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
            float _RoadDarkening;

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

            // ========================================================================
            // Utility functions
            // ========================================================================

            // Sample cell data at a UV position with point filtering
            float4 SampleCellData(float2 uv)
            {
                // Clamp to valid range
                uv = saturate(uv);
                return tex2D(_CellDataTex, uv);
            }

            // Look up color from palette texture (256 entries)
            // normalizedId is id / 65535
            float3 LookupPaletteColor(sampler2D palette, float normalizedId)
            {
                float id = normalizedId * 65535.0;
                float paletteU = (clamp(round(id), 0, 255) + 0.5) / 256.0;
                return tex2D(palette, float2(paletteU, 0.5)).rgb;
            }

            // Integer hash function for deterministic variance (returns 0-1)
            // Uses multiplicative hashing for better distribution with sequential inputs
            float hash(float n)
            {
                uint h = uint(n);
                h ^= h >> 16;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                h *= 0xc2b2ae35u;
                h ^= h >> 16;
                return float(h & 0x7FFFFFFFu) / float(0x7FFFFFFF);
            }

            // 2D hash for noise functions (different from integer hash above)
            float hash2d(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // Value noise for water shimmer
            float valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                // Cubic interpolation for smoother result
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = hash2d(i);
                float b = hash2d(i + float2(1.0, 0.0));
                float c = hash2d(i + float2(0.0, 1.0));
                float d = hash2d(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 2-octave FBM for water shimmer
            float fbm2(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                value += valueNoise(p) * amplitude;
                p *= 2.0;
                amplitude *= 0.5;
                value += valueNoise(p) * amplitude;

                return value;
            }

            // Convert color to grayscale using perceptual luminance weights
            float3 ToGrayscale(float3 color)
            {
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                return float3(luma, luma, luma);
            }

            // ========================================================================
            // Layer 1: Terrain (always rendered, seabed visible under water)
            // ========================================================================

            float3 ComputeTerrain(float2 uv, bool isCellWater, float biomeId, float height, float riverMask)
            {
                float3 terrain;

                if (isCellWater)
                {
                    // Seabed: sand hue darkening with depth (50% to 5% value)
                    float depthT = saturate((_SeaLevel - height) / max(_WaterDepthRange, 0.001));
                    depthT = sqrt(depthT);  // Stretch — actual ocean depths cluster in low range
                    float3 sandHue = float3(0.76, 0.70, 0.50);
                    terrain = sandHue * lerp(0.25, 0.05, depthT);
                }
                else
                {
                    // Land: biome-elevation matrix
                    float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
                    float biomeRaw = clamp(biomeId * 65535.0, 0, 63);
                    float biomeU = (biomeRaw + 0.5) / 64.0;
                    terrain = tex2D(_BiomeMatrixTex, float2(biomeU, landHeight)).rgb;

                    // River darkening: wet soil effect under rivers
                    terrain *= lerp(1.0, 1.0 - _RiverDarken, riverMask);
                }

                return terrain;
            }

            // ========================================================================
            // Layer 1 override: Height gradient (mode 0 debug viz)
            // ========================================================================

            float3 ComputeHeightGradient(bool isCellWater, float height, float riverMask)
            {
                float3 result;

                if (isCellWater)
                {
                    // Water gradient for height mode: deep to shallow blue
                    float waterT = height / max(_SeaLevel, 0.001);
                    result = lerp(
                        float3(0.08, 0.2, 0.4),   // Deep water
                        float3(0.2, 0.4, 0.6),    // Shallow water
                        waterT
                    );
                }
                else
                {
                    // Land gradient: green -> brown -> white
                    float landT = (height - _SeaLevel) / (1.0 - _SeaLevel);
                    if (landT < 0.3)
                    {
                        float t = landT / 0.3;
                        result = lerp(
                            float3(0.31, 0.63, 0.31),  // Coastal green
                            float3(0.47, 0.71, 0.31),  // Grassland
                            t
                        );
                    }
                    else if (landT < 0.6)
                    {
                        float t = (landT - 0.3) / 0.3;
                        result = lerp(
                            float3(0.47, 0.71, 0.31),  // Grassland
                            float3(0.55, 0.47, 0.4),   // Brown hills
                            t
                        );
                    }
                    else
                    {
                        float t = (landT - 0.6) / 0.4;
                        result = lerp(
                            float3(0.55, 0.47, 0.4),   // Brown hills
                            float3(0.94, 0.94, 0.98),  // Snow caps
                            t
                        );
                    }
                }

                return result;
            }

            // ========================================================================
            // Layer 2: Map mode overlay (political/market paint, alpha=0 on water)
            // ========================================================================

            float4 ComputeMapMode(float2 uv, bool isCellWater, bool isRiver, float height, float realmId, float provinceId, float countyId, float marketId)
            {
                // No map mode overlay on water, rivers, height mode (0), terrain mode (5), or soil mode (6)
                if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 5 || _MapMode == 6)
                    return float4(0, 0, 0, 0);

                // Grayscale terrain for multiply blending
                float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
                float3 grayTerrain = float3(landHeight, landHeight, landHeight);

                float3 modeColor;
                float edgeProximity;

                if (_MapMode >= 1 && _MapMode <= 3)
                {
                    // Political modes (1=realm, 2=province, 3=county)
                    float3 politicalColor = LookupPaletteColor(_RealmPaletteTex, realmId);
                    float realmDist = tex2D(_RealmBorderDistTex, uv).r * 255.0;
                    edgeProximity = saturate(realmDist / _GradientRadius);

                    // Multiply blend and gradient
                    float3 multiplied = grayTerrain * politicalColor;
                    float3 edgeColor = lerp(politicalColor, multiplied, _GradientEdgeDarkening);
                    float3 centerColor = lerp(grayTerrain, politicalColor, _GradientCenterOpacity);
                    modeColor = lerp(edgeColor, centerColor, edgeProximity);

                    // County border band overlay (thinnest, lightest — drawn first)
                    float countyBorderDist = tex2D(_CountyBorderDistTex, uv).r * 255.0;
                    float countyBorderAA = fwidth(countyBorderDist);
                    float countyBorderFactor = 1.0 - smoothstep(_CountyBorderWidth - countyBorderAA, _CountyBorderWidth + countyBorderAA, countyBorderDist);
                    if (countyBorderFactor > 0.001)
                    {
                        float3 countyBorderColor = politicalColor * (1.0 - _CountyBorderDarkening);
                        modeColor = lerp(modeColor, countyBorderColor, countyBorderFactor);
                    }

                    // Province border band overlay (thinner, lighter — drawn on top of county borders)
                    float provinceBorderDist = tex2D(_ProvinceBorderDistTex, uv).r * 255.0;
                    float provinceBorderAA = fwidth(provinceBorderDist);
                    float provinceBorderFactor = 1.0 - smoothstep(_ProvinceBorderWidth - provinceBorderAA, _ProvinceBorderWidth + provinceBorderAA, provinceBorderDist);
                    if (provinceBorderFactor > 0.001)
                    {
                        float3 provinceBorderColor = politicalColor * (1.0 - _ProvinceBorderDarkening);
                        modeColor = lerp(modeColor, provinceBorderColor, provinceBorderFactor);
                    }

                    // Realm border band overlay (distance texture + smoothstep AA, on top of province borders)
                    float realmBorderDist = tex2D(_RealmBorderDistTex, uv).r * 255.0;
                    float borderAA = fwidth(realmBorderDist);
                    float borderFactor = 1.0 - smoothstep(_RealmBorderWidth - borderAA, _RealmBorderWidth + borderAA, realmBorderDist);
                    if (borderFactor > 0.001)
                    {
                        float3 borderColor = politicalColor * (1.0 - _RealmBorderDarkening);
                        modeColor = lerp(modeColor, borderColor, borderFactor);
                    }
                }
                else if (_MapMode == 4)
                {
                    // Market mode
                    float3 marketColor = LookupPaletteColor(_MarketPaletteTex, marketId);
                    float marketDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
                    edgeProximity = saturate(marketDist / _GradientRadius);

                    float3 multiplied = grayTerrain * marketColor;
                    float3 edgeColor = lerp(marketColor, multiplied, _GradientEdgeDarkening);
                    float3 centerColor = lerp(grayTerrain, marketColor, _GradientCenterOpacity);
                    modeColor = lerp(edgeColor, centerColor, edgeProximity);

                    // Road overlay: multiply-darken terrain where roads exist
                    // Road mask is direct coverage (0=no road, 1=road), bilinear-filtered for AA
                    float roadMask = tex2D(_RoadMaskTex, uv).r;
                    if (roadMask > 0.01)
                    {
                        modeColor *= lerp(1.0, 1.0 - _RoadDarkening, roadMask);
                    }

                    // Market zone border band overlay (distance texture + smoothstep AA)
                    float marketBorderDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
                    float marketBorderAA = fwidth(marketBorderDist);
                    float marketBorderFactor = 1.0 - smoothstep(_MarketBorderWidth - marketBorderAA, _MarketBorderWidth + marketBorderAA, marketBorderDist);
                    if (marketBorderFactor > 0.001)
                    {
                        float3 marketBorderColor = marketColor * (1.0 - _MarketBorderDarkening);
                        modeColor = lerp(modeColor, marketBorderColor, marketBorderFactor);
                    }
                }
                else
                {
                    return float4(0, 0, 0, 0);
                }

                return float4(modeColor, 1.0);
            }

            // ========================================================================
            // Layer 3: Water (transparent, depth-based opacity)
            // ========================================================================

            void ComputeWater(bool isCellWater, float height, float riverMask, float2 worldUV, out float3 waterColor, out float waterAlpha)
            {
                waterColor = float3(0, 0, 0);
                waterAlpha = 0;

                // No water layer if not ocean and not river
                if (!isCellWater && riverMask < 0.01)
                    return;

                if (isCellWater)
                {
                    // Ocean/lake: single color, flat opacity
                    waterColor = _WaterDeepColor.rgb;

                    // Animated shimmer
                    float time = _Time.y * _ShimmerSpeed;
                    float2 uv1 = worldUV + float2(time, time * 0.7);
                    float2 uv2 = worldUV * 1.3 + float2(-time * 0.8, time * 0.5);
                    float shimmer = (fbm2(uv1) + fbm2(uv2)) * 0.5;
                    float brightness = 1.0 + (shimmer - 0.5) * 2.0 * _ShimmerIntensity;
                    waterColor *= brightness;

                    waterAlpha = _WaterDeepAlpha;
                }
                else
                {
                    // River on land: shallow water color, alpha modulated by mask
                    waterColor = _WaterShallowColor.rgb;
                    waterAlpha = _WaterShallowAlpha * riverMask;
                }
            }

            // ========================================================================
            // Fragment shader: layered compositing
            // ========================================================================

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.dataUV;

                // ---- Data sampling preamble (unchanged) ----

                float4 centerData = SampleCellData(uv);
                float realmId = centerData.r;
                float provinceId = centerData.g;
                float countyId = centerData.a;

                float countyIdRaw = countyId * 65535.0;
                float marketU = (clamp(round(countyIdRaw), 0, 16383) + 0.5) / 16384.0;
                float marketId = tex2D(_CellToMarketTex, float2(marketU, 0.5)).r;

                float packedBiome = centerData.b * 65535.0;
                bool isCellWater = packedBiome >= 32000.0;
                // Land: packed = biomeId*8 + soilId; Water: packed = 32768 + biomeId
                float landPacked = packedBiome - (isCellWater ? 32768.0 : 0.0);
                float biomeId = (isCellWater ? landPacked : floor(landPacked / 8.0)) / 65535.0;
                int soilId = isCellWater ? 0 : ((int)landPacked) % 8;

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
                        float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));

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

                float4 mapMode = ComputeMapMode(uv, isCellWater, isRiver, height, realmId, provinceId, countyId, marketId);
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

                // ---- Layer 4: Selection / hover (operates on composited color) ----

                float3 finalColor = afterWater;

                // Selection region test (for dimming non-selected areas)
                bool isInSelection = false;
                if (_SelectedRealmId >= 0)
                    isInSelection = !isWater && abs(realmId - _SelectedRealmId) < 0.00001;
                else if (_SelectedProvinceId >= 0)
                    isInSelection = !isWater && abs(provinceId - _SelectedProvinceId) < 0.00001;
                else if (_SelectedMarketId >= 0)
                    isInSelection = !isWater && abs(marketId - _SelectedMarketId) < 0.00001;
                else if (_SelectedCountyId >= 0)
                    isInSelection = !isWater && abs(countyId - _SelectedCountyId) < 0.00001;

                // Hover effect
                bool isHovered = (!isWater) && (
                    (_HoveredRealmId >= 0 && abs(realmId - _HoveredRealmId) < 0.00001) ||
                    (_HoveredProvinceId >= 0 && abs(provinceId - _HoveredProvinceId) < 0.00001) ||
                    (_HoveredCountyId >= 0 && abs(countyId - _HoveredCountyId) < 0.00001) ||
                    (_HoveredMarketId >= 0 && abs(marketId - _HoveredMarketId) < 0.00001));
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
