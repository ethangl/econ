Shader "EconSim/MapOverlay"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0.0

        // Heightmap for terrain (Phase 6)
        _HeightmapTex ("Heightmap", 2D) = "gray" {}
        _HeightScale ("Height Scale", Float) = 3
        _SeaLevel ("Sea Level", Float) = 0.2
        _UseHeightDisplacement ("Use Height Displacement", Int) = 0

        // Data texture: R=StateId, G=ProvinceId, B=BiomeId+WaterFlag, A=CellId (normalized to 0-1)
        _CellDataTex ("Cell Data", 2D) = "black" {}

        // Cell to market mapping (dynamic, updated when economy changes)
        _CellToMarketTex ("Cell To Market", 2D) = "black" {}

        // Color palettes (256 entries each)
        _StatePaletteTex ("State Palette", 2D) = "white" {}
        _MarketPaletteTex ("Market Palette", 2D) = "white" {}
        _BiomePaletteTex ("Biome Palette", 2D) = "white" {}

        // Biome-elevation matrix (64x64: biome × elevation)
        _BiomeMatrixTex ("Biome Elevation Matrix", 2D) = "white" {}

        // Border settings
        _StateBorderColor ("State Border Color", Color) = (0.1, 0.1, 0.1, 1)
        _ProvinceBorderColor ("Province Border Color", Color) = (0.3, 0.3, 0.3, 0.8)
        _MarketBorderColor ("Market Border Color", Color) = (0.5, 0.3, 0.1, 1)
        _StateBorderWidth ("State Border Width (pixels)", Range(0.5, 5)) = 2.0
        _ProvinceBorderWidth ("Province Border Width (pixels)", Range(0.5, 3)) = 1.0
        _MarketBorderWidth ("Market Border Width (pixels)", Range(0.5, 3)) = 1.5

        // Selection highlight (only one should be >= 0 at a time)
        _SelectedStateId ("Selected State ID (normalized)", Float) = -1
        _SelectedProvinceId ("Selected Province ID (normalized)", Float) = -1
        _SelectedCellId ("Selected Cell ID (normalized)", Float) = -1
        _SelectedMarketId ("Selected Market ID (normalized)", Float) = -1
        _SelectionBorderColor ("Selection Border Color", Color) = (1, 0.9, 0.2, 1)
        _SelectionBorderWidth ("Selection Border Width (pixels)", Range(1, 6)) = 3.0
        _SelectionFillAlpha ("Selection Fill Alpha", Range(0, 0.5)) = 0.15

        // Map mode: 0=height gradient, 1=political, 2=province, 3=county, 4=market, 5=terrain/biome
        _MapMode ("Map Mode", Int) = 0

        // Border visibility flags
        _ShowStateBorders ("Show State Borders", Int) = 1
        _ShowProvinceBorders ("Show Province Borders", Int) = 0
        _ShowMarketBorders ("Show Market Borders", Int) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        // Use custom unlit lighting model - no directional lights, just flat color
        #pragma surface surf Unlit vertex:vert noforwardadd
        #pragma target 3.5

        // Custom unlit lighting model - returns albedo directly without lighting
        half4 LightingUnlit(SurfaceOutput s, half3 lightDir, half atten)
        {
            return half4(s.Albedo, s.Alpha);
        }

        struct Input
        {
            float4 vertexColor;
            float2 dataUV;    // UV for sampling data texture (Azgaar coordinates)
            float2 heightUV;  // UV for sampling heightmap (Unity coordinates, Y-flipped)
        };

        sampler2D _HeightmapTex;
        float4 _HeightmapTex_TexelSize;  // (1/width, 1/height, width, height)
        float _HeightScale;
        float _SeaLevel;
        int _UseHeightDisplacement;

        sampler2D _CellDataTex;
        float4 _CellDataTex_TexelSize;  // (1/width, 1/height, width, height)

        sampler2D _CellToMarketTex;  // 16384x1 texture mapping cellId -> marketId

        sampler2D _StatePaletteTex;
        sampler2D _MarketPaletteTex;
        sampler2D _BiomePaletteTex;
        sampler2D _BiomeMatrixTex;

        // Water color constant
        static const fixed3 WATER_COLOR = fixed3(0.12, 0.2, 0.35);

        half _Glossiness;
        half _Metallic;

        fixed4 _StateBorderColor;
        fixed4 _ProvinceBorderColor;
        fixed4 _MarketBorderColor;
        float _StateBorderWidth;
        float _ProvinceBorderWidth;
        float _MarketBorderWidth;

        int _MapMode;
        int _ShowStateBorders;
        int _ShowProvinceBorders;
        int _ShowMarketBorders;

        float _SelectedStateId;
        float _SelectedProvinceId;
        float _SelectedCellId;
        float _SelectedMarketId;
        fixed4 _SelectionBorderColor;
        float _SelectionBorderWidth;
        float _SelectionFillAlpha;

        // 16 sample directions for smoother AA (8 cardinal + 8 at 22.5° offsets)
        static const float2 sampleDirs[16] = {
            float2(1, 0), float2(0.924, 0.383), float2(0.707, 0.707), float2(0.383, 0.924),
            float2(0, 1), float2(-0.383, 0.924), float2(-0.707, 0.707), float2(-0.924, 0.383),
            float2(-1, 0), float2(-0.924, -0.383), float2(-0.707, -0.707), float2(-0.383, -0.924),
            float2(0, -1), float2(0.383, -0.924), float2(0.707, -0.707), float2(0.924, -0.383)
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertexColor = v.color;
            // UV0 for heightmap (Unity coordinates, Y-flipped)
            o.heightUV = v.texcoord.xy;
            // UV1 for data texture (Azgaar coordinates)
            o.dataUV = v.texcoord1.xy;

            // Vertex displacement from heightmap
            if (_UseHeightDisplacement > 0)
            {
                float height = tex2Dlod(_HeightmapTex, float4(v.texcoord.xy, 0, 0)).r;
                v.vertex.y = (height - _SeaLevel) * _HeightScale;
            }
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

        // Look up market ID for a cell
        float GetMarketIdForCell(float cellIdNorm)
        {
            float cellIdRaw = cellIdNorm * 65535.0;
            float marketU = (clamp(round(cellIdRaw), 0, 16383) + 0.5) / 16384.0;
            return tex2D(_CellToMarketTex, float2(marketU, 0.5)).r;
        }

        // Calculate anti-aliased border coverage for state/province (direct from data texture)
        float CalculateBorderAA(float2 uv, float centerValue, int channel, float borderWidth, float uvPerPixel)
        {
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

                    float sampleValue;
                    if (channel == 0) sampleValue = sampleData.r;      // State
                    else sampleValue = sampleData.g;                   // Province

                    float isDifferent = abs(centerValue - sampleValue) > 0.00001 ? 1.0 : 0.0;

                    borderWeight += isDifferent * weight;
                    totalWeight += weight;
                }
            }

            float coverage = borderWeight / max(totalWeight, 0.001);
            return smoothstep(0.0, 0.5, coverage);
        }

        // Calculate anti-aliased border coverage for markets (requires cell-to-market lookup)
        // Ignores water cells - only draws borders between land cells with different markets
        float CalculateMarketBorderAA(float2 uv, float centerMarketId, bool centerIsWater, float borderWidth, float uvPerPixel)
        {
            // No market borders on water
            if (centerIsWater) return 0;

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

                    // Check if sampled cell is water - skip water cells for market borders
                    float samplePackedBiome = sampleData.b * 65535.0;
                    bool sampleIsWater = samplePackedBiome >= 32000.0;

                    if (sampleIsWater)
                    {
                        // Don't count water as a border difference
                        continue;
                    }

                    float sampleMarketId = GetMarketIdForCell(sampleData.a);
                    float isDifferent = abs(centerMarketId - sampleMarketId) > 0.00001 ? 1.0 : 0.0;

                    borderWeight += isDifferent * weight;
                    totalWeight += weight;
                }
            }

            float coverage = borderWeight / max(totalWeight, 0.001);
            return smoothstep(0.0, 0.5, coverage);
        }

        // Calculate anti-aliased selection border for state/province/cell
        // channel: 0=state (R), 1=province (G), 2=cell (A)
        // Returns border coverage (0-1) for the current pixel
        float CalculateSelectionBorderAA(float2 uv, float centerValue, float selectedId, int channel, float borderWidth, float uvPerPixel)
        {
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

                    float sampleValue;
                    if (channel == 0) sampleValue = sampleData.r;       // State
                    else if (channel == 1) sampleValue = sampleData.g;  // Province
                    else sampleValue = sampleData.a;                    // Cell

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

        void surf(Input IN, inout SurfaceOutput o)
        {
            float2 uv = IN.dataUV;

            // Calculate normal from heightmap gradient (for lighting on displaced terrain)
            if (_UseHeightDisplacement > 0)
            {
                float2 texelSize = _HeightmapTex_TexelSize.xy;
                float hL = tex2D(_HeightmapTex, IN.heightUV - float2(texelSize.x, 0)).r;
                float hR = tex2D(_HeightmapTex, IN.heightUV + float2(texelSize.x, 0)).r;
                float hD = tex2D(_HeightmapTex, IN.heightUV - float2(0, texelSize.y)).r;
                float hU = tex2D(_HeightmapTex, IN.heightUV + float2(0, texelSize.y)).r;

                // Normal from height gradient (scale affects steepness)
                float3 normal = normalize(float3(
                    (hL - hR) * _HeightScale * 0.5,
                    1.0,
                    (hD - hU) * _HeightScale * 0.5
                ));
                o.Normal = normal;
            }

            // Sample center cell data
            float4 centerData = SampleCellData(uv);
            float stateId = centerData.r;
            float provinceId = centerData.g;
            float cellId = centerData.a;

            // Look up market ID from cell-to-market texture
            float cellIdRaw = cellId * 65535.0;
            float marketU = (clamp(round(cellIdRaw), 0, 16383) + 0.5) / 16384.0;
            float marketId = tex2D(_CellToMarketTex, float2(marketU, 0.5)).r;

            // Unpack biome ID and water flag from B channel
            // Format: biomeId + (isWater ? 32768 : 0), normalized by 65535
            float packedBiome = centerData.b * 65535.0;
            bool isWater = packedBiome >= 32000.0;  // Water flag is 32768, biomes are < 100
            float biomeId = (packedBiome - (isWater ? 32768.0 : 0.0)) / 65535.0;

            // Sample height for height-based coloring (only used in height mode)
            float height = tex2D(_HeightmapTex, IN.heightUV).r;

            // Base color depends on map mode
            fixed3 baseColor;

            if (_MapMode == 0)
            {
                // Height mode - procedural gradient (handles both land AND water)
                if (isWater)
                {
                    // Water gradient: deep to shallow
                    float waterT = height / _SeaLevel;
                    baseColor = lerp(
                        fixed3(0.08, 0.2, 0.4),   // Deep water
                        fixed3(0.2, 0.4, 0.6),    // Shallow water
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
            else if (_MapMode == 1)
            {
                // Political mode - state palette
                baseColor = LookupPaletteColor(_StatePaletteTex, stateId);
            }
            else if (_MapMode == 2)
            {
                // Province mode - derived from state color
                fixed3 stateColor = LookupPaletteColor(_StatePaletteTex, stateId);
                float provId = provinceId * 65535.0;
                baseColor = DeriveProvinceColor(stateColor, provId);
            }
            else if (_MapMode == 3)
            {
                // County mode - derived from province color with per-cell variation
                fixed3 stateColor = LookupPaletteColor(_StatePaletteTex, stateId);
                float provId = provinceId * 65535.0;
                fixed3 provinceColor = DeriveProvinceColor(stateColor, provId);
                baseColor = DeriveCountyColor(provinceColor, cellIdRaw);
            }
            else if (_MapMode == 4)
            {
                // Market mode - market palette
                baseColor = LookupPaletteColor(_MarketPaletteTex, marketId);
            }
            else if (_MapMode == 5)
            {
                // Terrain/biome mode - sample biome-elevation matrix
                float landHeight = saturate((height - _SeaLevel) / (1.0 - _SeaLevel));

                // Convert normalized biomeId to matrix U coordinate
                float biomeRaw = clamp(biomeId * 65535.0, 0, 63);
                float biomeU = (biomeRaw + 0.5) / 64.0;

                // Sample the 2D matrix: U = biome, V = elevation
                baseColor = tex2D(_BiomeMatrixTex, float2(biomeU, landHeight)).rgb;
            }
            else
            {
                // Fallback - shouldn't happen but defensive
                baseColor = fixed3(0.5, 0.5, 0.5);
            }

            // Calculate UV change per pixel for consistent border width
            float2 dx = ddx(uv);
            float2 dy = ddy(uv);
            float uvPerPixel = length(float2(length(dx), length(dy)));

            // Calculate anti-aliased border coverage for each border type
            float stateBorderAA = 0;
            float provinceBorderAA = 0;
            float marketBorderAA = 0;
            float selectionBorderAA = 0;

            if (_ShowStateBorders > 0)
            {
                stateBorderAA = CalculateBorderAA(uv, stateId, 0, _StateBorderWidth, uvPerPixel);
            }

            if (_ShowProvinceBorders > 0)
            {
                provinceBorderAA = CalculateBorderAA(uv, provinceId, 1, _ProvinceBorderWidth, uvPerPixel);
            }

            if (_ShowMarketBorders > 0)
            {
                marketBorderAA = CalculateMarketBorderAA(uv, marketId, isWater, _MarketBorderWidth, uvPerPixel);
            }

            // Selection highlight (check state, province, market, cell in priority order)
            bool isInSelection = false;
            if (_SelectedStateId >= 0)
            {
                selectionBorderAA = CalculateSelectionBorderAA(uv, stateId, _SelectedStateId, 0, _SelectionBorderWidth, uvPerPixel);
                isInSelection = IsInSelectedRegion(stateId, _SelectedStateId);
            }
            else if (_SelectedProvinceId >= 0)
            {
                selectionBorderAA = CalculateSelectionBorderAA(uv, provinceId, _SelectedProvinceId, 1, _SelectionBorderWidth, uvPerPixel);
                isInSelection = IsInSelectedRegion(provinceId, _SelectedProvinceId);
            }
            else if (_SelectedMarketId >= 0)
            {
                selectionBorderAA = CalculateMarketSelectionBorderAA(uv, marketId, _SelectedMarketId, isWater, _SelectionBorderWidth, uvPerPixel);
                isInSelection = !isWater && IsInSelectedRegion(marketId, _SelectedMarketId);
            }
            else if (_SelectedCellId >= 0)
            {
                selectionBorderAA = CalculateSelectionBorderAA(uv, cellId, _SelectedCellId, 2, _SelectionBorderWidth, uvPerPixel);
                isInSelection = IsInSelectedRegion(cellId, _SelectedCellId);
            }

            // Composite borders with anti-aliased blending (state on top, then province, then market)
            fixed3 finalColor = baseColor;

            // Market borders (bottom layer)
            if (marketBorderAA > 0)
            {
                float alpha = marketBorderAA * _MarketBorderColor.a;
                finalColor = lerp(finalColor, _MarketBorderColor.rgb, alpha);
            }

            // Province borders (middle layer)
            if (provinceBorderAA > 0)
            {
                float alpha = provinceBorderAA * _ProvinceBorderColor.a;
                finalColor = lerp(finalColor, _ProvinceBorderColor.rgb, alpha);
            }

            // State borders (top layer)
            if (stateBorderAA > 0)
            {
                float alpha = stateBorderAA * _StateBorderColor.a;
                finalColor = lerp(finalColor, _StateBorderColor.rgb, alpha);
            }

            // Selection fill tint (subtle highlight for selected region interior)
            if (isInSelection && _SelectionFillAlpha > 0)
            {
                finalColor = lerp(finalColor, _SelectionBorderColor.rgb, _SelectionFillAlpha);
            }

            // Selection border (topmost layer - always visible)
            if (selectionBorderAA > 0)
            {
                float alpha = selectionBorderAA * _SelectionBorderColor.a;
                finalColor = lerp(finalColor, _SelectionBorderColor.rgb, alpha);
            }

            o.Albedo = finalColor;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
