Shader "EconSim/MapOverlay"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0.0

        // Data texture: R=StateId, G=ProvinceId, B=CellId, A=MarketId (normalized to 0-1)
        _CellDataTex ("Cell Data", 2D) = "black" {}

        // Color palettes (256 entries each, stacked vertically)
        _StatePaletteTex ("State Palette", 2D) = "white" {}
        _ProvincePaletteTex ("Province Palette", 2D) = "white" {}
        _MarketPaletteTex ("Market Palette", 2D) = "white" {}

        // Border settings
        _StateBorderColor ("State Border Color", Color) = (0.1, 0.1, 0.1, 1)
        _ProvinceBorderColor ("Province Border Color", Color) = (0.3, 0.3, 0.3, 0.8)
        _MarketBorderColor ("Market Border Color", Color) = (0.5, 0.3, 0.1, 1)
        _StateBorderWidth ("State Border Width (pixels)", Range(0.5, 5)) = 2.0
        _ProvinceBorderWidth ("Province Border Width (pixels)", Range(0.5, 3)) = 1.0
        _MarketBorderWidth ("Market Border Width (pixels)", Range(0.5, 3)) = 1.5

        // Map mode: 0=vertex color (terrain/height), 1=political, 2=province, 3=county, 4=market
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
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5

        struct Input
        {
            float4 vertexColor;
            float2 dataUV;  // UV for sampling data texture (Azgaar coordinates)
        };

        sampler2D _CellDataTex;
        float4 _CellDataTex_TexelSize;  // (1/width, 1/height, width, height)

        sampler2D _StatePaletteTex;
        sampler2D _ProvincePaletteTex;
        sampler2D _MarketPaletteTex;

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
            // Use UV1 for data texture coordinates (set by MapOverlayManager)
            o.dataUV = v.texcoord1.xy;
        }

        // Sample cell data at a UV position with point filtering
        float4 SampleCellData(float2 uv)
        {
            // Clamp to valid range
            uv = saturate(uv);
            return tex2D(_CellDataTex, uv);
        }

        // Look up color from palette texture
        // Palette is 256x1, index is 0-1 normalized
        fixed3 LookupPaletteColor(sampler2D palette, float index)
        {
            // Index is already normalized (id / 65535), scale to 0-255 range for palette
            float paletteU = index * 65535.0 / 256.0;
            paletteU = frac(paletteU);  // Wrap around for safety
            return tex2D(palette, float2(paletteU, 0.5)).rgb;
        }

        // Calculate anti-aliased border coverage for a given channel
        // Returns 0-1 where 1 = fully on border, 0 = fully off border
        float CalculateBorderAA(float2 uv, float centerValue, int channel, float borderWidth, float uvPerPixel)
        {
            // Sample at multiple radii for smooth falloff
            // Inner radius (border starts), mid radius, outer radius (border ends)
            float innerRadius = max(0.0, borderWidth - 1.0);
            float outerRadius = borderWidth + 0.5;

            float totalWeight = 0;
            float borderWeight = 0;

            // Sample at 3 radii with 16 directions each for smooth AA
            float radii[3] = { innerRadius, borderWidth, outerRadius };
            float weights[3] = { 0.5, 1.0, 0.25 };  // Weight inner samples less, outer samples even less

            for (int r = 0; r < 3; r++)
            {
                float radius = radii[r];
                float weight = weights[r];

                if (radius < 0.1) continue;  // Skip tiny radii

                for (int i = 0; i < 16; i++)
                {
                    float2 sampleUV = uv + sampleDirs[i] * uvPerPixel * radius;
                    float4 sampleData = SampleCellData(sampleUV);

                    float sampleValue;
                    if (channel == 0) sampleValue = sampleData.r;      // State
                    else if (channel == 1) sampleValue = sampleData.g; // Province
                    else sampleValue = sampleData.a;                   // Market

                    // Check if this sample is in a different region
                    float isDifferent = abs(centerValue - sampleValue) > 0.00001 ? 1.0 : 0.0;

                    borderWeight += isDifferent * weight;
                    totalWeight += weight;
                }
            }

            // Normalize and apply smoothstep for extra smoothness
            float coverage = borderWeight / max(totalWeight, 0.001);

            // Apply smoothstep to soften the transition
            return smoothstep(0.0, 0.5, coverage);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.dataUV;

            // Sample center cell data
            float4 centerData = SampleCellData(uv);
            float stateId = centerData.r;
            float provinceId = centerData.g;
            float cellId = centerData.b;
            float marketId = centerData.a;

            // Base color depends on map mode
            // Water cells (stateId=0) always use vertex colors
            fixed3 baseColor;
            bool isWater = stateId < 0.00001;

            if (_MapMode == 0 || isWater)
            {
                // Vertex color mode (terrain/height) or water - pass through
                baseColor = IN.vertexColor.rgb;
            }
            else if (_MapMode == 1)
            {
                // Political mode - color by state
                baseColor = LookupPaletteColor(_StatePaletteTex, stateId);
            }
            else if (_MapMode == 2)
            {
                // Province mode - color by province
                // Fall back to vertex color if no province
                if (provinceId < 0.00001)
                    baseColor = IN.vertexColor.rgb;
                else
                    baseColor = LookupPaletteColor(_ProvincePaletteTex, provinceId);
            }
            else if (_MapMode == 3)
            {
                // County mode - color by cell (with province-based variation)
                // Fall back to vertex color if no province
                if (provinceId < 0.00001)
                {
                    baseColor = IN.vertexColor.rgb;
                }
                else
                {
                    baseColor = LookupPaletteColor(_ProvincePaletteTex, provinceId);
                    // Add per-cell variation using cell ID hash
                    float hash = frac(sin(cellId * 65535.0 * 78.233) * 43758.5453);
                    baseColor = baseColor * (0.85 + hash * 0.3);
                }
            }
            else if (_MapMode == 4)
            {
                // Market mode - color by market zone
                // Fall back to vertex color if no market
                if (marketId < 0.00001)
                    baseColor = IN.vertexColor.rgb;
                else
                    baseColor = LookupPaletteColor(_MarketPaletteTex, marketId);
            }
            else
            {
                baseColor = IN.vertexColor.rgb;
            }

            // Calculate UV change per pixel for consistent border width
            float2 dx = ddx(uv);
            float2 dy = ddy(uv);
            float uvPerPixel = length(float2(length(dx), length(dy)));

            // Calculate anti-aliased border coverage for each border type
            float stateBorderAA = 0;
            float provinceBorderAA = 0;
            float marketBorderAA = 0;

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
                marketBorderAA = CalculateBorderAA(uv, marketId, 2, _MarketBorderWidth, uvPerPixel);
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

            o.Albedo = finalColor;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = 1;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
