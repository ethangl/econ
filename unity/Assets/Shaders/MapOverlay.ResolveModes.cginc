#ifndef MAP_OVERLAY_RESOLVE_MODES_INCLUDED
#define MAP_OVERLAY_RESOLVE_MODES_INCLUDED

float3 ApplyPoliticalBorders(float2 uv, float3 color, float displayLevel)
{
    // Edge band (multiply).
    float realmDist = tex2D(_RealmBorderDistTex, uv).r * 255.0;
    float edgeAA = fwidth(realmDist);
    float edgeFactor = 1.0 - smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, realmDist);
    color *= 1.0 - edgeFactor * saturate(_EdgeDarkening);

    // County border (multiply) — only at display level >= 3.
    if (displayLevel >= 2.5)
    {
        float countyBorderDist = tex2D(_CountyBorderDistTex, uv).r * 255.0;
        float countyBorderAA = fwidth(countyBorderDist);
        float countyBorderFactor = 1.0 - smoothstep(_CountyBorderWidth - countyBorderAA, _CountyBorderWidth + countyBorderAA, countyBorderDist);
        color *= 1.0 - countyBorderFactor * _CountyBorderDarkening;
    }

    // Province border (multiply) — only at display level >= 2.
    if (displayLevel >= 1.5)
    {
        float provinceBorderDist = tex2D(_ProvinceBorderDistTex, uv).r * 255.0;
        float provinceBorderAA = fwidth(provinceBorderDist);
        float provinceBorderFactor = 1.0 - smoothstep(_ProvinceBorderWidth - provinceBorderAA, _ProvinceBorderWidth + provinceBorderAA, provinceBorderDist);
        color *= 1.0 - provinceBorderFactor * _ProvinceBorderDarkening;
    }

    // Realm border (multiply, always shown).
    float borderAA = fwidth(realmDist);
    float borderFactor = 1.0 - smoothstep(_RealmBorderWidth - borderAA, _RealmBorderWidth + borderAA, realmDist);
    color *= 1.0 - borderFactor * _RealmBorderDarkening;

    return color;
}

float3 ApplyMarketBorders(float2 uv, float3 color)
{
    // Edge band (multiply).
    float marketDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
    float edgeAA = fwidth(marketDist);
    float edgeFactor = 1.0 - smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, marketDist);
    color *= 1.0 - edgeFactor * saturate(_EdgeDarkening);

    // Path overlay: black routes blended over market color.
    float roadMask = tex2D(_RoadMaskTex, uv).r;
    if (roadMask > 0.01)
    {
        color = lerp(color, float3(0.0, 0.0, 0.0), roadMask * _PathOpacity);
    }

    // Market zone border (multiply).
    float marketBorderDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
    float marketBorderAA = fwidth(marketBorderDist);
    float marketBorderFactor = 1.0 - smoothstep(_MarketBorderWidth - marketBorderAA, _MarketBorderWidth + marketBorderAA, marketBorderDist);
    color *= 1.0 - marketBorderFactor * _MarketBorderDarkening;

    return color;
}

float3 ApplyReligionBorders(float2 uv, float3 color, float displayLevel)
{
    // Edge band (multiply).
    float archDist = tex2D(_ArchdioceseBorderDistTex, uv).r * 255.0;
    float edgeAA = fwidth(archDist);
    float edgeFactor = 1.0 - smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, archDist);
    color *= 1.0 - edgeFactor * saturate(_EdgeDarkening);

    // Parish border (multiply) — only at display level >= 3.
    if (displayLevel >= 2.5)
    {
        float parishBorderDist = tex2D(_ParishBorderDistTex, uv).r * 255.0;
        float parishBorderAA = fwidth(parishBorderDist);
        float parishBorderFactor = 1.0 - smoothstep(_CountyBorderWidth - parishBorderAA, _CountyBorderWidth + parishBorderAA, parishBorderDist);
        color *= 1.0 - parishBorderFactor * _CountyBorderDarkening;
    }

    // Diocese border (multiply) — only at display level >= 2.
    if (displayLevel >= 1.5)
    {
        float dioceseBorderDist = tex2D(_DioceseBorderDistTex, uv).r * 255.0;
        float dioceseBorderAA = fwidth(dioceseBorderDist);
        float dioceseBorderFactor = 1.0 - smoothstep(_ProvinceBorderWidth - dioceseBorderAA, _ProvinceBorderWidth + dioceseBorderAA, dioceseBorderDist);
        color *= 1.0 - dioceseBorderFactor * _ProvinceBorderDarkening;
    }

    // Archdiocese border (multiply, always shown).
    float archBorderAA = fwidth(archDist);
    float archBorderFactor = 1.0 - smoothstep(_RealmBorderWidth - archBorderAA, _RealmBorderWidth + archBorderAA, archDist);
    color *= 1.0 - archBorderFactor * _RealmBorderDarkening;

    return color;
}

float4 ComputeMapMode(float2 uv, bool isCellWater, bool isRiver, float height, float realmId, float provinceId, float countyId, float marketId)
{
    if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 6)
        return float4(0, 0, 0, 0);

    float3 color;

    if (_MapMode >= 1 && _MapMode <= 3)
    {
        color = LookupPaletteColor(_RealmPaletteTex, sampler_RealmPaletteTex, realmId);
        color = ApplyPoliticalBorders(uv, color, 1.0);
    }
    else if (_MapMode == 4)
    {
        color = LookupPaletteColor(_MarketPaletteTex, sampler_MarketPaletteTex, marketId);
        color = ApplyMarketBorders(uv, color);
    }
    else
    {
        return float4(0, 0, 0, 0);
    }

    return float4(color, 1.0);
}

float4 ComputeMapModeFromResolvedBase(float2 uv, bool isCellWater, bool isRiver, float height, float3 resolvedBaseColor, float resolvedAlpha)
{
    if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 6)
        return float4(0, 0, 0, 0);

    float3 color;

    if (_MapMode >= 1 && _MapMode <= 3)
    {
        float displayLevel = resolvedAlpha * 255.0;
        color = ApplyPoliticalBorders(uv, resolvedBaseColor, displayLevel);
    }
    else if (_MapMode == 4)
    {
        color = ApplyMarketBorders(uv, resolvedBaseColor);
    }
    else if (_MapMode == 8 || _MapMode == 9)
    {
        color = resolvedBaseColor;
    }
    else if (_MapMode >= 10 && _MapMode <= 12)
    {
        float packedAlpha = resolvedAlpha * 255.0;
        float displayLevel = floor(packedAlpha / 64.0);
        color = ApplyReligionBorders(uv, resolvedBaseColor, displayLevel);
    }
    else
    {
        return float4(0, 0, 0, 0);
    }

    return float4(color, 1.0);
}

#endif
