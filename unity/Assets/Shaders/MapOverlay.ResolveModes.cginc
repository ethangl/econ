#ifndef MAP_OVERLAY_RESOLVE_MODES_INCLUDED
#define MAP_OVERLAY_RESOLVE_MODES_INCLUDED

float3 ApplyPoliticalModeStyle(float2 uv, float3 grayTerrain, float3 politicalColor)
{
    float realmDist = tex2D(_RealmBorderDistTex, uv).r * 255.0;
    float edgeProximity = saturate(realmDist / _GradientRadius);

    float3 multiplied = grayTerrain * politicalColor;
    float3 edgeColor = lerp(politicalColor, multiplied, _GradientEdgeDarkening);
    float3 centerColor = lerp(grayTerrain, politicalColor, _GradientCenterOpacity);
    float3 modeColor = lerp(edgeColor, centerColor, edgeProximity);

    // County border band overlay (thinnest, lightest — drawn first).
    float countyBorderDist = tex2D(_CountyBorderDistTex, uv).r * 255.0;
    float countyBorderAA = fwidth(countyBorderDist);
    float countyBorderFactor = 1.0 - smoothstep(_CountyBorderWidth - countyBorderAA, _CountyBorderWidth + countyBorderAA, countyBorderDist);
    if (countyBorderFactor > 0.001)
    {
        float3 countyBorderColor = politicalColor * (1.0 - _CountyBorderDarkening);
        modeColor = lerp(modeColor, countyBorderColor, countyBorderFactor);
    }

    // Province border band overlay (thinner, lighter — drawn on top of county borders).
    float provinceBorderDist = tex2D(_ProvinceBorderDistTex, uv).r * 255.0;
    float provinceBorderAA = fwidth(provinceBorderDist);
    float provinceBorderFactor = 1.0 - smoothstep(_ProvinceBorderWidth - provinceBorderAA, _ProvinceBorderWidth + provinceBorderAA, provinceBorderDist);
    if (provinceBorderFactor > 0.001)
    {
        float3 provinceBorderColor = politicalColor * (1.0 - _ProvinceBorderDarkening);
        modeColor = lerp(modeColor, provinceBorderColor, provinceBorderFactor);
    }

    // Realm border band overlay (distance texture + smoothstep AA, on top of province borders).
    float realmBorderDist = tex2D(_RealmBorderDistTex, uv).r * 255.0;
    float borderAA = fwidth(realmBorderDist);
    float borderFactor = 1.0 - smoothstep(_RealmBorderWidth - borderAA, _RealmBorderWidth + borderAA, realmBorderDist);
    if (borderFactor > 0.001)
    {
        float3 borderColor = politicalColor * (1.0 - _RealmBorderDarkening);
        modeColor = lerp(modeColor, borderColor, borderFactor);
    }

    return modeColor;
}

float3 ApplyMarketModeStyle(float2 uv, float3 grayTerrain, float3 marketColor)
{
    float marketDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
    float edgeProximity = saturate(marketDist / _GradientRadius);

    float3 multiplied = grayTerrain * marketColor;
    float3 edgeColor = lerp(marketColor, multiplied, _GradientEdgeDarkening);
    float3 centerColor = lerp(grayTerrain, marketColor, _GradientCenterOpacity);
    float3 modeColor = lerp(edgeColor, centerColor, edgeProximity);

    // Path overlay: white dotted routes blended over market color.
    // Mask is direct coverage (0=no path, 1=path), bilinear-filtered for AA.
    float roadMask = tex2D(_RoadMaskTex, uv).r;
    if (roadMask > 0.01)
    {
        modeColor = lerp(modeColor, float3(1.0, 1.0, 1.0), roadMask * _PathOpacity);
    }

    // Market zone border band overlay (distance texture + smoothstep AA).
    float marketBorderDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
    float marketBorderAA = fwidth(marketBorderDist);
    float marketBorderFactor = 1.0 - smoothstep(_MarketBorderWidth - marketBorderAA, _MarketBorderWidth + marketBorderAA, marketBorderDist);
    if (marketBorderFactor > 0.001)
    {
        float3 marketBorderColor = marketColor * (1.0 - _MarketBorderDarkening);
        modeColor = lerp(modeColor, marketBorderColor, marketBorderFactor);
    }

    return modeColor;
}

float4 ComputeMapMode(float2 uv, bool isCellWater, bool isRiver, float height, float realmId, float provinceId, float countyId, float marketId)
{
    // No map mode overlay on water, rivers, height mode (0), or biomes mode (6).
    if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 6)
        return float4(0, 0, 0, 0);

    // Grayscale terrain for multiply blending
    float landHeight = NormalizeLandHeight(height);
    float3 grayTerrain = float3(landHeight, landHeight, landHeight);

    float3 modeColor;

    if (_MapMode >= 1 && _MapMode <= 3)
    {
        // Political modes (1=realm, 2=province, 3=county)
        float3 politicalColor = LookupPaletteColor(_RealmPaletteTex, sampler_RealmPaletteTex, realmId);
        modeColor = ApplyPoliticalModeStyle(uv, grayTerrain, politicalColor);
    }
    else if (_MapMode == 4)
    {
        // Market mode
        float3 marketColor = LookupPaletteColor(_MarketPaletteTex, sampler_MarketPaletteTex, marketId);
        modeColor = ApplyMarketModeStyle(uv, grayTerrain, marketColor);
    }
    else
    {
        return float4(0, 0, 0, 0);
    }

    return float4(modeColor, 1.0);
}

float4 ComputeMapModeFromResolvedBase(float2 uv, bool isCellWater, bool isRiver, float height, float3 resolvedBaseColor)
{
    // No map mode overlay on water, rivers, height mode (0), or biomes mode (6).
    if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 6)
        return float4(0, 0, 0, 0);

    // Grayscale terrain for multiply blending
    float landHeight = NormalizeLandHeight(height);
    float3 grayTerrain = float3(landHeight, landHeight, landHeight);
    float3 modeColor;

    if (_MapMode >= 1 && _MapMode <= 3)
    {
        modeColor = ApplyPoliticalModeStyle(uv, grayTerrain, resolvedBaseColor);
    }
    else if (_MapMode == 4)
    {
        modeColor = ApplyMarketModeStyle(uv, grayTerrain, resolvedBaseColor);
    }
    else if (_MapMode == 8 || _MapMode == 9)
    {
        // Transport heatmaps are already fully resolved in C#.
        modeColor = resolvedBaseColor;
    }
    else
    {
        return float4(0, 0, 0, 0);
    }

    return float4(modeColor, 1.0);
}

#endif
