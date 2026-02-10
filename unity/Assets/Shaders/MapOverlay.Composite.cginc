#ifndef MAP_OVERLAY_COMPOSITE_INCLUDED
#define MAP_OVERLAY_COMPOSITE_INCLUDED

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
        float landHeight = NormalizeLandHeight(height);
        float biomeRaw = clamp(biomeId * 65535.0, 0, 63);
        float biomeU = (biomeRaw + 0.5) / 64.0;
        terrain = tex2D(_BiomeMatrixTex, float2(biomeU, landHeight)).rgb;

        // River darkening: wet soil effect under rivers
        terrain *= lerp(1.0, 1.0 - _RiverDarken, riverMask);
    }

    return terrain;
}

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

float3 ComputeChannelInspector(float2 uv, float4 politicalIds, float4 geographyBase)
{
    float sampleValue = 0.0;

    if (_DebugView == 0) sampleValue = politicalIds.r;
    else if (_DebugView == 1) sampleValue = politicalIds.g;
    else if (_DebugView == 2) sampleValue = politicalIds.b;
    else if (_DebugView == 3) sampleValue = politicalIds.a;
    else if (_DebugView == 4) sampleValue = geographyBase.r;
    else if (_DebugView == 5) sampleValue = geographyBase.g;
    else if (_DebugView == 6) sampleValue = geographyBase.b;
    else if (_DebugView == 7) sampleValue = geographyBase.a;
    else if (_DebugView == 8) sampleValue = tex2D(_RealmBorderDistTex, uv).r;
    else if (_DebugView == 9) sampleValue = tex2D(_ProvinceBorderDistTex, uv).r;
    else if (_DebugView == 10) sampleValue = tex2D(_CountyBorderDistTex, uv).r;
    else if (_DebugView == 11) sampleValue = tex2D(_MarketBorderDistTex, uv).r;
    else if (_DebugView == 12) sampleValue = tex2D(_RiverMaskTex, uv).r;
    else if (_DebugView == 13) sampleValue = tex2D(_HeightmapTex, uv).r;
    else if (_DebugView == 14) sampleValue = tex2D(_RoadMaskTex, uv).r;
    else if (_DebugView == 15)
    {
        float3 mode = tex2D(_ModeColorResolve, uv).rgb;
        sampleValue = dot(mode, float3(0.299, 0.587, 0.114));
    }

    return float3(sampleValue, sampleValue, sampleValue);
}

#endif
