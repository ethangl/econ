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

        // Data texture: R=StateId, G=ProvinceId, B=BiomeId+WaterFlag, A=CountyId (normalized to 0-1)
        _CellDataTex ("Cell Data", 2D) = "black" {}

        // Cell to market mapping (dynamic, updated when economy changes)
        _CellToMarketTex ("Cell To Market", 2D) = "black" {}

        // Color palettes (256 entries each)
        _StatePaletteTex ("State Palette", 2D) = "white" {}
        _MarketPaletteTex ("Market Palette", 2D) = "white" {}
        _BiomePaletteTex ("Biome Palette", 2D) = "white" {}

        // Biome-elevation matrix (64x64: biome × elevation)
        _BiomeMatrixTex ("Biome Elevation Matrix", 2D) = "white" {}

        // Selection highlight (only one should be >= 0 at a time)
        _SelectedStateId ("Selected State ID (normalized)", Float) = -1
        _SelectedProvinceId ("Selected Province ID (normalized)", Float) = -1
        _SelectedCountyId ("Selected County ID (normalized)", Float) = -1
        _SelectedMarketId ("Selected Market ID (normalized)", Float) = -1
        _SelectionBorderColor ("Selection Border Color", Color) = (1, 0.9, 0.2, 1)
        _SelectionBorderWidth ("Selection Border Width (pixels)", Range(1, 6)) = 3.0
        _SelectionDimming ("Selection Dimming", Range(0, 1)) = 0.5
        _SelectionDesaturation ("Selection Desaturation", Range(0, 1)) = 0

        // Hover highlight (only one should be >= 0 at a time, separate from selection)
        _HoveredStateId ("Hovered State ID (normalized)", Float) = -1
        _HoveredProvinceId ("Hovered Province ID (normalized)", Float) = -1
        _HoveredCountyId ("Hovered County ID (normalized)", Float) = -1
        _HoveredMarketId ("Hovered Market ID (normalized)", Float) = -1
        _HoverIntensity ("Hover Intensity", Range(0, 1)) = 0

        // Map mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 5=terrain/biome
        _MapMode ("Map Mode", Int) = 0

        // Gradient fill style (edge-to-center fade for political/market modes)
        _GradientRadius ("Gradient Radius (pixels)", Range(5, 100)) = 40
        _GradientEdgeDarkening ("Gradient Edge Darkening", Range(0, 1)) = 0.5
        _GradientCenterOpacity ("Gradient Center Opacity", Range(0, 1)) = 0.5

        // State border (world-space, in texels of data texture)
        _StateBorderWidth ("State Border Width (texels)", Range(10, 200)) = 80
        _StateBorderOpacity ("State Border Opacity", Range(0, 1)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 texcoord1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 vertexColor : COLOR;
                float2 dataUV : TEXCOORD0;    // UV for sampling data texture (Azgaar coordinates)
                float2 heightUV : TEXCOORD1;  // UV for sampling heightmap (Unity coordinates, Y-flipped)
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

            sampler2D _StatePaletteTex;
            sampler2D _MarketPaletteTex;
            sampler2D _BiomePaletteTex;
            sampler2D _BiomeMatrixTex;

            // Water color constant
            static const fixed3 WATER_COLOR = fixed3(0.12, 0.2, 0.35);

            int _MapMode;
            float _GradientRadius;
            float _GradientEdgeDarkening;
            float _GradientCenterOpacity;
            float _StateBorderWidth;
            float _StateBorderOpacity;

            float _SelectedStateId;
            float _SelectedProvinceId;
            float _SelectedCountyId;
            float _SelectedMarketId;
            fixed4 _SelectionBorderColor;
            float _SelectionBorderWidth;
            float _SelectionDimming;
            float _SelectionDesaturation;

            float _HoveredStateId;
            float _HoveredProvinceId;
            float _HoveredCountyId;
            float _HoveredMarketId;
            float _HoverIntensity;

            // 16 sample directions for smoother AA (8 cardinal + 8 at 22.5° offsets)
            static const float2 sampleDirs[16] = {
                float2(1, 0), float2(0.924, 0.383), float2(0.707, 0.707), float2(0.383, 0.924),
                float2(0, 1), float2(-0.383, 0.924), float2(-0.707, 0.707), float2(-0.924, 0.383),
                float2(-1, 0), float2(-0.924, -0.383), float2(-0.707, -0.707), float2(-0.383, -0.924),
                float2(0, -1), float2(0.383, -0.924), float2(0.707, -0.707), float2(0.924, -0.383)
            };

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
                // UV0 for heightmap (Unity coordinates, Y-flipped)
                o.heightUV = v.texcoord.xy;
                // UV1 for data texture (Azgaar coordinates)
                o.dataUV = v.texcoord1.xy;

                return o;
            }

            // Sample cell data at a UV position with point filtering
            float4 SampleCellData(float2 uv)
            {
                // Clamp to valid range
                uv = saturate(uv);
                return tex2D(_CellDataTex, uv);
            }

            // Look up color from palette texture (256 entries)
            // normalizedId is id / 65535
            fixed3 LookupPaletteColor(sampler2D palette, float normalizedId)
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

            // Photoshop Overlay blend mode (per channel)
            // If base < 0.5: 2 * base * blend
            // If base >= 0.5: 1 - 2 * (1 - base) * (1 - blend)
            float OverlayBlend(float base, float blend)
            {
                return base < 0.5
                    ? 2.0 * base * blend
                    : 1.0 - 2.0 * (1.0 - base) * (1.0 - blend);
            }

            fixed3 OverlayBlend3(fixed3 base, fixed3 blend)
            {
                return fixed3(
                    OverlayBlend(base.r, blend.r),
                    OverlayBlend(base.g, blend.g),
                    OverlayBlend(base.b, blend.b)
                );
            }

            // RGB to HSV conversion
            float3 rgb2hsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            // HSV to RGB conversion
            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
            }

            // Convert color to grayscale using perceptual luminance weights
            fixed3 ToGrayscale(fixed3 color)
            {
                float luma = dot(color, float3(0.299, 0.587, 0.114));
                return fixed3(luma, luma, luma);
            }

            // Derive province color from state color with HSV variance
            fixed3 DeriveProvinceColor(fixed3 stateColor, float provinceId)
            {
                float3 hsv = rgb2hsv(stateColor);

                // Apply deterministic variance based on province ID (±relative to parent)
                // Use large prime offsets to decorrelate H, S, V
                float hVar = (hash(provinceId + 73856093.0) - 0.5) * 0.10;  // ±0.05 hue
                float sVar = (hash(provinceId + 19349663.0) - 0.5) * 0.14;  // ±0.07 saturation
                float vVar = (hash(provinceId + 83492791.0) - 0.5) * 0.14;  // ±0.07 value

                hsv.x = frac(hsv.x + hVar);  // Wrap hue
                hsv.y = clamp(hsv.y + sVar, 0.15, 0.95);  // Allow full range around parent
                hsv.z = clamp(hsv.z + vVar, 0.25, 0.95);  // Allow full range around parent

                return hsv2rgb(hsv);
            }

            // Derive county color from province color with HSV variance
            fixed3 DeriveCountyColor(fixed3 provinceColor, float countyId)
            {
                float3 hsv = rgb2hsv(provinceColor);

                // County variance (±relative to parent)
                // Uses large prime offsets to decorrelate H, S, V
                float hVar = (hash(countyId + 15485863.0) - 0.5) * 0.10;  // ±0.05 hue
                float sVar = (hash(countyId + 32452843.0) - 0.5) * 0.14;  // ±0.07 saturation
                float vVar = (hash(countyId + 49979687.0) - 0.5) * 0.14;  // ±0.07 value

                hsv.x = frac(hsv.x + hVar);
                hsv.y = clamp(hsv.y + sVar, 0.15, 0.95);  // Allow full range around parent
                hsv.z = clamp(hsv.z + vVar, 0.25, 0.95);  // Allow full range around parent

                return hsv2rgb(hsv);
            }

            // Calculate edge proximity for political gradient effect
            // Returns 0 at edges (near different region, water, or river), 1 deep in interior
            // channel: 0=state (R), 1=province (G), 2=county (A)
            float CalculateEdgeProximity(float2 uv, float2 heightUV, float centerValue, bool centerIsWater, int channel, float maxRadius, float uvPerPixel)
            {
                // Water has no gradient
                if (centerIsWater) return 1;

                // Sample at increasing radii to find distance to edge
                // Use fewer samples per ring for performance (8 directions instead of 16)
                static const float2 sampleDirs8[8] = {
                    float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                    float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707)
                };

                // Sample at 8 radii: 6.25% to 50% of max radius (tighter gradient near edges)
                float radii[8] = {
                    maxRadius * 0.0625, maxRadius * 0.125, maxRadius * 0.1875, maxRadius * 0.25,
                    maxRadius * 0.3125, maxRadius * 0.375, maxRadius * 0.4375, maxRadius * 0.5
                };

                float minEdgeDistance = maxRadius * 0.5;

                for (int r = 0; r < 8; r++)
                {
                    float radius = radii[r];

                    for (int i = 0; i < 8; i++)
                    {
                        float2 sampleUV = uv + sampleDirs8[i] * uvPerPixel * radius;
                        float2 sampleHeightUV = heightUV + sampleDirs8[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        // Check if sample is water (cell water flag)
                        float samplePackedBiome = sampleData.b * 65535.0;
                        bool sampleIsWater = samplePackedBiome >= 32000.0;

                        // Check if sample is river
                        float sampleRiver = tex2D(_RiverMaskTex, sampleHeightUV).r;
                        bool sampleIsRiver = sampleRiver > 0.5;

                        if (sampleIsWater || sampleIsRiver)
                        {
                            // Water/river counts as edge
                            minEdgeDistance = min(minEdgeDistance, radius);
                            continue;
                        }

                        float sampleValue;
                        if (channel == 0) sampleValue = sampleData.r;       // State
                        else if (channel == 1) sampleValue = sampleData.g;  // Province
                        else sampleValue = sampleData.a;                    // County

                        // Check if different region
                        if (abs(centerValue - sampleValue) > 0.00001)
                        {
                            minEdgeDistance = min(minEdgeDistance, radius);
                        }
                    }
                }

                // Convert distance to proximity factor (0 at edge, 1 in interior)
                // Scale by 0.5 since we only sample up to half the max radius
                return saturate(minEdgeDistance / (maxRadius * 0.5));
            }

            // Calculate proximity to state borders ONLY (ignores water/rivers)
            // Returns 0 at state boundaries, 1 deep in interior
            // Uses world-space (UV) sampling, not screen-space
            float CalculateStateBorderProximity(float2 uv, float centerStateId, bool centerIsWater, float borderWidthUV)
            {
                // Water has no border
                if (centerIsWater) return 1;

                static const float2 sampleDirs8[8] = {
                    float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                    float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707)
                };

                // Sample at 8 radii from 12.5% to 100% of border width (UV units)
                float radii[8] = {
                    borderWidthUV * 0.125, borderWidthUV * 0.25, borderWidthUV * 0.375, borderWidthUV * 0.5,
                    borderWidthUV * 0.625, borderWidthUV * 0.75, borderWidthUV * 0.875, borderWidthUV
                };

                float minEdgeDistance = borderWidthUV;

                for (int r = 0; r < 8; r++)
                {
                    float radius = radii[r];

                    for (int i = 0; i < 8; i++)
                    {
                        float2 sampleUV = uv + sampleDirs8[i] * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        // Check if sample is water - skip it (don't count as edge)
                        float samplePackedBiome = sampleData.b * 65535.0;
                        bool sampleIsWater = samplePackedBiome >= 32000.0;
                        if (sampleIsWater) continue;

                        // Only check state ID difference
                        float sampleStateId = sampleData.r;
                        if (abs(centerStateId - sampleStateId) > 0.00001)
                        {
                            minEdgeDistance = min(minEdgeDistance, radius);
                        }
                    }
                }

                return saturate(minEdgeDistance / borderWidthUV);
            }

            // Look up market ID for a cell
            float GetMarketIdForCell(float cellIdNorm)
            {
                float cellIdRaw = cellIdNorm * 65535.0;
                float marketU = (clamp(round(cellIdRaw), 0, 16383) + 0.5) / 16384.0;
                return tex2D(_CellToMarketTex, float2(marketU, 0.5)).r;
            }

            // Calculate edge proximity for political modes (state boundaries, water, rivers)
            // Returns 0 at edges, 1 deep in interior
            float CalculatePoliticalEdgeProximity(float2 uv, float2 heightUV, float centerStateId, bool centerIsWater, float maxRadius, float uvPerPixel)
            {
                if (centerIsWater) return 1;

                static const float2 dirs8[8] = {
                    float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                    float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707)
                };

                float radii[8] = {
                    maxRadius * 0.0625, maxRadius * 0.125, maxRadius * 0.1875, maxRadius * 0.25,
                    maxRadius * 0.3125, maxRadius * 0.375, maxRadius * 0.4375, maxRadius * 0.5
                };

                float minEdgeDistance = maxRadius * 0.5;

                for (int r = 0; r < 8; r++)
                {
                    float radius = radii[r];
                    for (int i = 0; i < 8; i++)
                    {
                        float2 sampleUV = uv + dirs8[i] * uvPerPixel * radius;
                        float2 sampleHeightUV = heightUV + dirs8[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        float samplePackedBiome = sampleData.b * 65535.0;
                        bool sampleIsWater = samplePackedBiome >= 32000.0;

                        float sampleRiver = tex2D(_RiverMaskTex, sampleHeightUV).r;
                        bool sampleIsRiver = sampleRiver > 0.5;

                        if (sampleIsWater || sampleIsRiver)
                        {
                            minEdgeDistance = min(minEdgeDistance, radius);
                            continue;
                        }

                        float sampleStateId = sampleData.r;
                        if (abs(centerStateId - sampleStateId) > 0.00001)
                        {
                            minEdgeDistance = min(minEdgeDistance, radius);
                        }
                    }
                }

                return saturate(minEdgeDistance / (maxRadius * 0.5));
            }

            // Calculate edge proximity for market zones (requires cell-to-market lookup)
            // Returns 0 at edges (near different market, water, or river), 1 deep in interior
            float CalculateMarketEdgeProximity(float2 uv, float2 heightUV, float centerMarketId, bool centerIsWater, float maxRadius, float uvPerPixel)
            {
                // Water has no gradient
                if (centerIsWater) return 1;

                // Sample at increasing radii to find distance to edge
                static const float2 sampleDirs8[8] = {
                    float2(1, 0), float2(0.707, 0.707), float2(0, 1), float2(-0.707, 0.707),
                    float2(-1, 0), float2(-0.707, -0.707), float2(0, -1), float2(0.707, -0.707)
                };

                // Sample at 8 radii: 6.25% to 50% of max radius (tighter gradient near edges)
                float radii[8] = {
                    maxRadius * 0.0625, maxRadius * 0.125, maxRadius * 0.1875, maxRadius * 0.25,
                    maxRadius * 0.3125, maxRadius * 0.375, maxRadius * 0.4375, maxRadius * 0.5
                };

                float minEdgeDistance = maxRadius * 0.5;

                for (int r = 0; r < 8; r++)
                {
                    float radius = radii[r];

                    for (int i = 0; i < 8; i++)
                    {
                        float2 sampleUV = uv + sampleDirs8[i] * uvPerPixel * radius;
                        float2 sampleHeightUV = heightUV + sampleDirs8[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        // Check if sample is water (cell water flag)
                        float samplePackedBiome = sampleData.b * 65535.0;
                        bool sampleIsWater = samplePackedBiome >= 32000.0;

                        // Check if sample is river
                        float sampleRiver = tex2D(_RiverMaskTex, sampleHeightUV).r;
                        bool sampleIsRiver = sampleRiver > 0.5;

                        if (sampleIsWater || sampleIsRiver)
                        {
                            // Water/river counts as edge
                            minEdgeDistance = min(minEdgeDistance, radius);
                            continue;
                        }

                        // Look up market ID for sampled cell
                        float sampleMarketId = GetMarketIdForCell(sampleData.a);

                        // Check if different market
                        if (abs(centerMarketId - sampleMarketId) > 0.00001)
                        {
                            minEdgeDistance = min(minEdgeDistance, radius);
                        }
                    }
                }

                // Convert distance to proximity factor (0 at edge, 1 in interior)
                return saturate(minEdgeDistance / (maxRadius * 0.5));
            }

            // Calculate anti-aliased selection border for state/province/county
            // channel: 0=state (R), 1=province (G), 2=county (A)
            // Returns border coverage (0-1) for the current pixel
            // Only draws borders between land cells - skips water cells entirely
            float CalculateSelectionBorderAA(float2 uv, float centerValue, float selectedId, bool centerIsWater, int channel, float borderWidth, float uvPerPixel)
            {
                // No selection borders on water
                if (centerIsWater) return 0;

                // Only calculate if this pixel is in the selected region
                if (abs(centerValue - selectedId) > 0.00001) return 0;

                float innerRadius = max(0.0, borderWidth - 1.0);
                float outerRadius = borderWidth + 0.5;

                float totalWeight = 0;
                float borderWeight = 0;

                float radii[3] = { innerRadius, borderWidth, outerRadius };
                float weights[3] = { 0.5, 1.0, 0.25 };

                for (int r = 0; r < 3; r++)
                {
                    float radius = radii[r];
                    float weight = weights[r];

                    if (radius < 0.1) continue;

                    for (int i = 0; i < 16; i++)
                    {
                        float2 sampleUV = uv + sampleDirs[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        // Check if sampled cell is water - treat as border (edge of selection at coastline)
                        float samplePackedBiome = sampleData.b * 65535.0;
                        bool sampleIsWater = samplePackedBiome >= 32000.0;

                        if (sampleIsWater)
                        {
                            // Water is "different" - creates selection border at coastline
                            borderWeight += weight;
                            totalWeight += weight;
                            continue;
                        }

                        float sampleValue;
                        if (channel == 0) sampleValue = sampleData.r;       // State
                        else if (channel == 1) sampleValue = sampleData.g;  // Province
                        else sampleValue = sampleData.a;                    // County

                        // Border exists where neighbor differs from selected region
                        float isDifferent = abs(sampleValue - selectedId) > 0.00001 ? 1.0 : 0.0;

                        borderWeight += isDifferent * weight;
                        totalWeight += weight;
                    }
                }

                float coverage = borderWeight / max(totalWeight, 0.001);
                return smoothstep(0.0, 0.5, coverage);
            }

            // Check if current pixel is inside the selected region (for fill tint)
            bool IsInSelectedRegion(float centerValue, float selectedId)
            {
                return abs(centerValue - selectedId) < 0.00001;
            }

            // Calculate anti-aliased selection border for market zones (requires cell-to-market lookup)
            // centerIsWater: true if the current pixel is water (no selection on water)
            float CalculateMarketSelectionBorderAA(float2 uv, float centerMarketId, float selectedMarketId, bool centerIsWater, float borderWidth, float uvPerPixel)
            {
                // No market selection on water
                if (centerIsWater) return 0;

                // Only calculate if this pixel is in the selected market
                if (abs(centerMarketId - selectedMarketId) > 0.00001) return 0;

                float innerRadius = max(0.0, borderWidth - 1.0);
                float outerRadius = borderWidth + 0.5;

                float totalWeight = 0;
                float borderWeight = 0;

                float radii[3] = { innerRadius, borderWidth, outerRadius };
                float weights[3] = { 0.5, 1.0, 0.25 };

                for (int r = 0; r < 3; r++)
                {
                    float radius = radii[r];
                    float weight = weights[r];

                    if (radius < 0.1) continue;

                    for (int i = 0; i < 16; i++)
                    {
                        float2 sampleUV = uv + sampleDirs[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        // Check if sampled cell is water - treat as border (edge of market zone)
                        float samplePackedBiome = sampleData.b * 65535.0;
                        bool sampleIsWater = samplePackedBiome >= 32000.0;

                        float isDifferent;
                        if (sampleIsWater)
                        {
                            // Water is always "different" - creates border at coastline
                            isDifferent = 1.0;
                        }
                        else
                        {
                            float sampleMarketId = GetMarketIdForCell(sampleData.a);
                            isDifferent = abs(sampleMarketId - selectedMarketId) > 0.00001 ? 1.0 : 0.0;
                        }

                        borderWeight += isDifferent * weight;
                        totalWeight += weight;
                    }
                }

                float coverage = borderWeight / max(totalWeight, 0.001);
                return smoothstep(0.0, 0.5, coverage);
            }

            // Calculate hover outline coverage with proper anti-aliasing
            // Samples at multiple radii and counts hits for smooth coverage
            // channel: 0=state (R), 1=province (G), 2=county (A)
            // NOTE: Outline CAN be drawn on water pixels if they're near a hovered land region
            float CalculateHoverOutlineAA(float2 uv, float centerValue, float hoveredId, bool centerIsWater, int channel, float offset, float width, float uvPerPixel)
            {
                if (hoveredId < 0) return 0;

                // If we're INSIDE the hovered region (and on land), no outline here
                if (!centerIsWater && abs(centerValue - hoveredId) < 0.00001) return 0;

                float halfWidth = width * 0.5;
                float innerEdge = offset - halfWidth;
                float outerEdge = offset + halfWidth;

                // Sample at 3 radii across the outline width
                float radii[3] = { innerEdge, offset, outerEdge };
                float weights[3] = { 0.25, 0.5, 0.25 };

                float totalHits = 0;
                float totalWeight = 0;

                for (int r = 0; r < 3; r++)
                {
                    float radius = radii[r];
                    if (radius < 0.5) continue;

                    float weight = weights[r];

                    // Sample in 16 directions at this radius
                    for (int i = 0; i < 16; i++)
                    {
                        float2 sampleUV = uv + sampleDirs[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        // Skip water samples - we're looking for land in the hovered region
                        float samplePackedBiome = sampleData.b * 65535.0;
                        if (samplePackedBiome >= 32000.0) continue;

                        float sampleValue;
                        if (channel == 0) sampleValue = sampleData.r;
                        else if (channel == 1) sampleValue = sampleData.g;
                        else sampleValue = sampleData.a;

                        bool isHovered = abs(sampleValue - hoveredId) < 0.00001;
                        totalHits += isHovered ? weight : 0;
                        totalWeight += weight;
                    }
                }

                if (totalWeight < 0.001) return 0;

                // Coverage based on ratio of hits - pixels at the boundary get ~50% hits
                float hitRatio = totalHits / totalWeight;

                // We want outline where we're NEAR but OUTSIDE the region
                // hitRatio near 0.5 = right at the edge = maximum outline
                // hitRatio near 0 = far outside = no outline
                // hitRatio near 1 = mostly inside = no outline (shouldn't happen since we check above)

                // Transform: peak at ~0.3-0.5 hit ratio, falloff to 0 at edges
                float coverage = smoothstep(0.0, 0.3, hitRatio) * smoothstep(0.8, 0.4, hitRatio);

                return coverage;
            }

            // Calculate hover outline for market zones with proper anti-aliasing
            // NOTE: Outline CAN be drawn on water pixels if they're near a hovered land market
            float CalculateMarketHoverOutlineAA(float2 uv, float centerMarketId, float hoveredMarketId, bool centerIsWater, float offset, float width, float uvPerPixel)
            {
                if (hoveredMarketId < 0) return 0;

                // If we're INSIDE the hovered market (and on land), no outline here
                if (!centerIsWater && abs(centerMarketId - hoveredMarketId) < 0.00001) return 0;

                float halfWidth = width * 0.5;
                float innerEdge = offset - halfWidth;
                float outerEdge = offset + halfWidth;

                float radii[3] = { innerEdge, offset, outerEdge };
                float weights[3] = { 0.25, 0.5, 0.25 };

                float totalHits = 0;
                float totalWeight = 0;

                for (int r = 0; r < 3; r++)
                {
                    float radius = radii[r];
                    if (radius < 0.5) continue;

                    float weight = weights[r];

                    for (int i = 0; i < 16; i++)
                    {
                        float2 sampleUV = uv + sampleDirs[i] * uvPerPixel * radius;
                        float4 sampleData = SampleCellData(sampleUV);

                        float samplePackedBiome = sampleData.b * 65535.0;
                        if (samplePackedBiome >= 32000.0) continue;

                        float sampleMarketId = GetMarketIdForCell(sampleData.a);
                        bool isHovered = abs(sampleMarketId - hoveredMarketId) < 0.00001;
                        totalHits += isHovered ? weight : 0;
                        totalWeight += weight;
                    }
                }

                if (totalWeight < 0.001) return 0;

                float hitRatio = totalHits / totalWeight;
                float coverage = smoothstep(0.0, 0.3, hitRatio) * smoothstep(0.8, 0.4, hitRatio);

                return coverage;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.dataUV;

                // Sample center cell data
                float4 centerData = SampleCellData(uv);
                float stateId = centerData.r;
                float provinceId = centerData.g;
                float countyId = centerData.a;

                // Look up market ID from cell-to-market texture (uses cell ID for lookup, but texture stores county mapping)
                float countyIdRaw = countyId * 65535.0;
                float marketU = (clamp(round(countyIdRaw), 0, 16383) + 0.5) / 16384.0;
                float marketId = tex2D(_CellToMarketTex, float2(marketU, 0.5)).r;

                // Unpack biome ID and water flag from B channel
                // Format: biomeId + (isWater ? 32768 : 0), normalized by 65535
                float packedBiome = centerData.b * 65535.0;
                bool isCellWater = packedBiome >= 32000.0;  // Water flag is 32768, biomes are < 100
                float biomeId = (packedBiome - (isCellWater ? 32768.0 : 0.0)) / 65535.0;

                // Sample river mask (uses same UV as heightmap - Unity coordinates)
                float riverMask = tex2D(_RiverMaskTex, IN.heightUV).r;
                bool isRiver = riverMask > 0.5;

                // Combine water sources: ocean/lake cells OR rivers
                bool isWater = isCellWater || isRiver;

                // Sample height for height-based coloring
                float height = tex2D(_HeightmapTex, IN.heightUV).r;

                // Calculate UV change per pixel (needed for gradient calculation)
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                float uvPerPixel = length(float2(length(dx), length(dy)));

                // Always calculate terrain color (used as base for political gradient blending)
                fixed3 terrainColor;
                if (isWater)
                {
                    // Water gradient: deep to shallow
                    float waterT = height / _SeaLevel;
                    terrainColor = lerp(
                        fixed3(0.08, 0.2, 0.4),   // Deep water
                        fixed3(0.2, 0.4, 0.6),    // Shallow water
                        waterT
                    );
                }
                else
                {
                    // Sample biome-elevation matrix for terrain
                    float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
                    float biomeRaw = clamp(biomeId * 65535.0, 0, 63);
                    float biomeU = (biomeRaw + 0.5) / 64.0;
                    terrainColor = tex2D(_BiomeMatrixTex, float2(biomeU, landHeight)).rgb;
                }

                // Base color depends on map mode
                fixed3 baseColor;

                if (_MapMode == 0)
                {
                    // Height mode - procedural gradient (handles both land AND water)
                    if (isWater)
                    {
                        baseColor = terrainColor;
                    }
                    else
                    {
                        // Land gradient: green -> brown -> white
                        float landT = (height - _SeaLevel) / (1.0 - _SeaLevel);
                        if (landT < 0.3)
                        {
                            float t = landT / 0.3;
                            baseColor = lerp(
                                fixed3(0.31, 0.63, 0.31),  // Coastal green
                                fixed3(0.47, 0.71, 0.31),  // Grassland
                                t
                            );
                        }
                        else if (landT < 0.6)
                        {
                            float t = (landT - 0.3) / 0.3;
                            baseColor = lerp(
                                fixed3(0.47, 0.71, 0.31),  // Grassland
                                fixed3(0.55, 0.47, 0.4),   // Brown hills
                                t
                            );
                        }
                        else
                        {
                            float t = (landT - 0.6) / 0.4;
                            baseColor = lerp(
                                fixed3(0.55, 0.47, 0.4),   // Brown hills
                                fixed3(0.94, 0.94, 0.98),  // Snow caps
                                t
                            );
                        }
                    }
                }
                else if (isWater)
                {
                    // All other modes - flat water color
                    baseColor = WATER_COLOR;
                }
                else if (_MapMode == 1 || _MapMode == 2 || _MapMode == 3)
                {
                    // Political modes - gradient fill with darkening near water/rivers/borders

                    // Use pure heightmap for grayscale terrain (normalized land height)
                    float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
                    fixed3 grayTerrain = fixed3(landHeight, landHeight, landHeight);

                    // Get political color
                    fixed3 politicalColor = LookupPaletteColor(_StatePaletteTex, stateId);

                    // Calculate edge proximity for gradient (state boundaries, water, rivers)
                    float edgeProximity = CalculatePoliticalEdgeProximity(uv, IN.heightUV, stateId, isWater, _GradientRadius, uvPerPixel);

                    // Multiply blend: terrain * political color (like Photoshop multiply layer)
                    fixed3 multiplied = grayTerrain * politicalColor;

                    // Edge color: blend from political to multiplied based on darkening setting
                    fixed3 edgeColor = lerp(politicalColor, multiplied, _GradientEdgeDarkening);

                    // Center color: blend from terrain to political based on center opacity
                    fixed3 centerColor = lerp(grayTerrain, politicalColor, _GradientCenterOpacity);

                    // Gradient from edge (multiplied/dark) to center (political/light)
                    baseColor = lerp(edgeColor, centerColor, edgeProximity);

                    // State border band overlay
                    float borderWidthUV = _StateBorderWidth * _CellDataTex_TexelSize.x;
                    float stateBorderProximity = CalculateStateBorderProximity(uv, stateId, isWater, borderWidthUV);
                    if (stateBorderProximity < 0.5)
                    {
                        float3 hsv = rgb2hsv(politicalColor);
                        hsv.z = max(hsv.z * 0.65, 0.35);
                        fixed3 borderTint = hsv2rgb(hsv);
                        fixed3 borderColor = grayTerrain * borderTint;
                        baseColor = lerp(baseColor, borderColor, _StateBorderOpacity);
                    }
                }
                else if (_MapMode == 4)
                {
                    // Market mode - same gradient style as political modes

                    // Use pure heightmap for grayscale terrain (normalized land height)
                    float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
                    fixed3 grayTerrain = fixed3(landHeight, landHeight, landHeight);

                    // Get market color and calculate gradient
                    fixed3 marketColor = LookupPaletteColor(_MarketPaletteTex, marketId);

                    // Calculate edge proximity for gradient (based on market boundaries and rivers)
                    float edgeProximity = CalculateMarketEdgeProximity(uv, IN.heightUV, marketId, isWater, _GradientRadius, uvPerPixel);

                    // Multiply blend: terrain * market color (like Photoshop multiply layer)
                    fixed3 multiplied = grayTerrain * marketColor;

                    // Edge color: blend from market to multiplied based on darkening setting
                    fixed3 edgeColor = lerp(marketColor, multiplied, _GradientEdgeDarkening);

                    // Center color: blend from terrain to market based on center opacity
                    fixed3 centerColor = lerp(grayTerrain, marketColor, _GradientCenterOpacity);

                    // Gradient from edge (multiplied/dark) to center (market/light)
                    baseColor = lerp(edgeColor, centerColor, edgeProximity);
                }
                else if (_MapMode == 5)
                {
                    // Terrain/biome mode - use pre-calculated terrain color
                    baseColor = terrainColor;
                }
                else
                {
                    // Fallback - shouldn't happen but defensive
                    baseColor = fixed3(0.5, 0.5, 0.5);
                }

                // Selection highlight (check state, province, market, cell in priority order)
                float selectionBorderAA = 0;
                bool isInSelection = false;
                if (_SelectedStateId >= 0)
                {
                    selectionBorderAA = CalculateSelectionBorderAA(uv, stateId, _SelectedStateId, isWater, 0, _SelectionBorderWidth, uvPerPixel);
                    isInSelection = !isWater && IsInSelectedRegion(stateId, _SelectedStateId);
                }
                else if (_SelectedProvinceId >= 0)
                {
                    selectionBorderAA = CalculateSelectionBorderAA(uv, provinceId, _SelectedProvinceId, isWater, 1, _SelectionBorderWidth, uvPerPixel);
                    isInSelection = !isWater && IsInSelectedRegion(provinceId, _SelectedProvinceId);
                }
                else if (_SelectedMarketId >= 0)
                {
                    selectionBorderAA = CalculateMarketSelectionBorderAA(uv, marketId, _SelectedMarketId, isWater, _SelectionBorderWidth, uvPerPixel);
                    isInSelection = !isWater && IsInSelectedRegion(marketId, _SelectedMarketId);
                }
                else if (_SelectedCountyId >= 0)
                {
                    selectionBorderAA = CalculateSelectionBorderAA(uv, countyId, _SelectedCountyId, isWater, 2, _SelectionBorderWidth, uvPerPixel);
                    isInSelection = !isWater && IsInSelectedRegion(countyId, _SelectedCountyId);
                }

                // Composite remaining borders (for non-political modes)
                fixed3 finalColor = baseColor;

                // Hover effect - animated saturation boost
                bool isHovered = (!isWater) && (
                    (_HoveredStateId >= 0 && abs(stateId - _HoveredStateId) < 0.00001) ||
                    (_HoveredProvinceId >= 0 && abs(provinceId - _HoveredProvinceId) < 0.00001) ||
                    (_HoveredCountyId >= 0 && abs(countyId - _HoveredCountyId) < 0.00001) ||
                    (_HoveredMarketId >= 0 && abs(marketId - _HoveredMarketId) < 0.00001));
                if (isHovered && _HoverIntensity > 0)
                {
                    float3 hsv = rgb2hsv(finalColor);
                    hsv.y = saturate(hsv.y * (1.0 + 0.15 * _HoverIntensity));  // up to +15% saturation
                    hsv.z = saturate(hsv.z * (1.0 + 0.25 * _HoverIntensity));  // up to +25% brightness
                    finalColor = hsv2rgb(hsv);
                }

                // Selection dimming - darken and desaturate everything EXCEPT the selected zone
                bool hasSelection = _SelectedStateId >= 0 || _SelectedProvinceId >= 0 || _SelectedMarketId >= 0 || _SelectedCountyId >= 0;
                if (hasSelection && !isInSelection && !isWater)
                {
                    // Desaturate then darken non-selected land areas (both animated)
                    fixed3 gray = ToGrayscale(finalColor);
                    finalColor = lerp(finalColor, gray, _SelectionDesaturation);
                    finalColor *= _SelectionDimming;
                }

                // Selection border (topmost layer - always visible)
                if (selectionBorderAA > 0)
                {
                    float alpha = selectionBorderAA * _SelectionBorderColor.a;
                    finalColor = lerp(finalColor, _SelectionBorderColor.rgb, alpha);
                }

                return fixed4(finalColor, 1);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
