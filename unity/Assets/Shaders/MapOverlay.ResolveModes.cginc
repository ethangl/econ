#ifndef MAP_OVERLAY_RESOLVE_MODES_INCLUDED
#define MAP_OVERLAY_RESOLVE_MODES_INCLUDED

float3 ApplyPoliticalModeStyle(float2 uv, float3 politicalColor, float displayLevel)
{
    float realmDist = tex2D(_RealmBorderDistTex, uv).r * 255.0;
    float3 modeColor = politicalColor;

    // Flat edge band along realm boundaries (including coasts).
    float edgeAA = fwidth(realmDist);
    float edgeFactor = 1.0 - smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, realmDist);
    if (edgeFactor > 0.001)
    {
        float3 edgeColor = politicalColor * (1.0 - saturate(_EdgeDarkening));
        modeColor = lerp(modeColor, edgeColor, edgeFactor);
    }

    // County border band overlay (thinnest, lightest — drawn first).
    // Only where display level indicates county drill-down (level >= 3).
    if (displayLevel >= 2.5)
    {
        float countyBorderDist = tex2D(_CountyBorderDistTex, uv).r * 255.0;
        float countyBorderAA = fwidth(countyBorderDist);
        float countyBorderFactor = 1.0 - smoothstep(_CountyBorderWidth - countyBorderAA, _CountyBorderWidth + countyBorderAA, countyBorderDist);
        if (countyBorderFactor > 0.001)
        {
            float3 countyBorderColor = politicalColor * (1.0 - _CountyBorderDarkening);
            modeColor = lerp(modeColor, countyBorderColor, countyBorderFactor);
        }
    }

    // Province border band overlay — only where display level >= 2.
    if (displayLevel >= 1.5)
    {
        float provinceBorderDist = tex2D(_ProvinceBorderDistTex, uv).r * 255.0;
        float provinceBorderAA = fwidth(provinceBorderDist);
        float provinceBorderFactor = 1.0 - smoothstep(_ProvinceBorderWidth - provinceBorderAA, _ProvinceBorderWidth + provinceBorderAA, provinceBorderDist);
        if (provinceBorderFactor > 0.001)
        {
            float3 provinceBorderColor = politicalColor * (1.0 - _ProvinceBorderDarkening);
            modeColor = lerp(modeColor, provinceBorderColor, provinceBorderFactor);
        }
    }

    // Realm border band overlay (always shown).
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

float3 ApplyMarketModeStyle(float2 uv, float3 marketColor)
{
    float marketDist = tex2D(_MarketBorderDistTex, uv).r * 255.0;
    float3 modeColor = marketColor;

    // Flat edge band along market boundaries (including coasts).
    float edgeAA = fwidth(marketDist);
    float edgeFactor = 1.0 - smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, marketDist);
    if (edgeFactor > 0.001)
    {
        float3 edgeColor = marketColor * (1.0 - saturate(_EdgeDarkening));
        modeColor = lerp(modeColor, edgeColor, edgeFactor);
    }

    // Path overlay: black routes blended over market color.
    // Mask is direct coverage (0=no path, 1=path), bilinear-filtered for AA.
    float roadMask = tex2D(_RoadMaskTex, uv).r;
    if (roadMask > 0.01)
    {
        modeColor = lerp(modeColor, float3(0.0, 0.0, 0.0), roadMask * _PathOpacity);
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

float3 ApplyReligionModeStyle(float2 uv, float3 territoryColor, float displayLevel)
{
    float archDist = tex2D(_ArchdioceseBorderDistTex, uv).r * 255.0;
    float3 modeColor = territoryColor;

    // Flat edge band along archdiocese boundaries (including coasts).
    float edgeAA = fwidth(archDist);
    float edgeFactor = 1.0 - smoothstep(_EdgeWidth - edgeAA, _EdgeWidth + edgeAA, archDist);
    if (edgeFactor > 0.001)
    {
        float3 edgeColor = territoryColor * (1.0 - saturate(_EdgeDarkening));
        modeColor = lerp(modeColor, edgeColor, edgeFactor);
    }

    // Parish border band overlay (thinnest — only where display level >= 3)
    if (displayLevel >= 2.5)
    {
        float parishBorderDist = tex2D(_ParishBorderDistTex, uv).r * 255.0;
        float parishBorderAA = fwidth(parishBorderDist);
        float parishBorderFactor = 1.0 - smoothstep(_CountyBorderWidth - parishBorderAA, _CountyBorderWidth + parishBorderAA, parishBorderDist);
        if (parishBorderFactor > 0.001)
        {
            float3 parishBorderColor = territoryColor * (1.0 - _CountyBorderDarkening);
            modeColor = lerp(modeColor, parishBorderColor, parishBorderFactor);
        }
    }

    // Diocese border band overlay — only where display level >= 2
    if (displayLevel >= 1.5)
    {
        float dioceseBorderDist = tex2D(_DioceseBorderDistTex, uv).r * 255.0;
        float dioceseBorderAA = fwidth(dioceseBorderDist);
        float dioceseBorderFactor = 1.0 - smoothstep(_ProvinceBorderWidth - dioceseBorderAA, _ProvinceBorderWidth + dioceseBorderAA, dioceseBorderDist);
        if (dioceseBorderFactor > 0.001)
        {
            float3 dioceseBorderColor = territoryColor * (1.0 - _ProvinceBorderDarkening);
            modeColor = lerp(modeColor, dioceseBorderColor, dioceseBorderFactor);
        }
    }

    // Archdiocese border band overlay (always shown in religion modes)
    float archBorderAA = fwidth(archDist);
    float archBorderFactor = 1.0 - smoothstep(_RealmBorderWidth - archBorderAA, _RealmBorderWidth + archBorderAA, archDist);
    if (archBorderFactor > 0.001)
    {
        float3 archBorderColor = territoryColor * (1.0 - _RealmBorderDarkening);
        modeColor = lerp(modeColor, archBorderColor, archBorderFactor);
    }

    return modeColor;
}

float4 ComputeMapMode(float2 uv, bool isCellWater, bool isRiver, float height, float realmId, float provinceId, float countyId, float marketId)
{
    // No map mode overlay on water, rivers, height mode (0), or biomes mode (6).
    if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 6)
        return float4(0, 0, 0, 0);

    float3 modeColor;

    if (_MapMode >= 1 && _MapMode <= 3)
    {
        // Political modes — non-resolved path always shows realm level (displayLevel=1)
        float3 politicalColor = LookupPaletteColor(_RealmPaletteTex, sampler_RealmPaletteTex, realmId);
        modeColor = ApplyPoliticalModeStyle(uv, politicalColor, 1.0);
    }
    else if (_MapMode == 4)
    {
        // Market mode
        float3 marketColor = LookupPaletteColor(_MarketPaletteTex, sampler_MarketPaletteTex, marketId);
        modeColor = ApplyMarketModeStyle(uv, marketColor);
    }
    else
    {
        return float4(0, 0, 0, 0);
    }

    return float4(modeColor, 1.0);
}

float4 ComputeMapModeFromResolvedBase(float2 uv, bool isCellWater, bool isRiver, float height, float3 resolvedBaseColor, float resolvedAlpha)
{
    // No map mode overlay on water, rivers, height mode (0), or biomes mode (6).
    if (isCellWater || isRiver || _MapMode == 0 || _MapMode == 6)
        return float4(0, 0, 0, 0);

    float3 modeColor;

    if (_MapMode >= 1 && _MapMode <= 3)
    {
        // Display level encoded in alpha: 1/255=realm, 2/255=province, 3/255=county
        float displayLevel = resolvedAlpha * 255.0;
        modeColor = ApplyPoliticalModeStyle(uv, resolvedBaseColor, displayLevel);
    }
    else if (_MapMode == 4)
    {
        modeColor = ApplyMarketModeStyle(uv, resolvedBaseColor);
    }
    else if (_MapMode == 8 || _MapMode == 9)
    {
        // Transport heatmaps are already fully resolved in C#.
        modeColor = resolvedBaseColor;
    }
    else if (_MapMode >= 10 && _MapMode <= 12)
    {
        // Religion: display level in top 2 bits of alpha
        float packedAlpha = resolvedAlpha * 255.0;
        float displayLevel = floor(packedAlpha / 64.0);
        modeColor = ApplyReligionModeStyle(uv, resolvedBaseColor, displayLevel);
    }
    else
    {
        return float4(0, 0, 0, 0);
    }

    return float4(modeColor, 1.0);
}

#endif
