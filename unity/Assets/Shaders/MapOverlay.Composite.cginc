#ifndef MAP_OVERLAY_COMPOSITE_INCLUDED
#define MAP_OVERLAY_COMPOSITE_INCLUDED

float3 ComputeTerrain(float2 uv, bool isCellWater, float biomeId, float height)
{
    float3 terrain;

    if (isCellWater)
    {
        // Seabed: sand hue darkening with depth (sea-level-relative depth, no user range control).
        float depthT = saturate((_SeaLevel - height) / max(_SeaLevel, 0.001));
        depthT = sqrt(depthT);  // Stretch — actual ocean depths cluster in low range
        float3 sandHue = float3(0.70, 0.64, 0.46);
        terrain = sandHue * lerp(0.22, 0.04, depthT);
    }
    else
    {
        // Land: biome palette with elevation-based shading.
        float landHeight = NormalizeLandHeight(height);
        float3 biomeColor = LookupPaletteColor(_BiomePaletteTex, biomeId);

        if (landHeight < 0.85)
        {
            // Continuous brightness gradient: darker at low elevation, brighter at high.
            float brightness = 0.4 + landHeight * 0.7;
            terrain = saturate(biomeColor * brightness);
        }
        else
        {
            // Snow zone: blend biome color toward white at high elevation.
            float t = saturate((landHeight - 0.85) / 0.15);
            float3 snow = float3(0.95, 0.95, 0.98);
            terrain = lerp(biomeColor, snow, t);
        }
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

float3 ComputeWater(bool isCellWater, float height, float riverMask, float2 uv, float2 worldUV, float3 underlyingColor)
{
    // No water layer if not ocean and not river
    if (!isCellWater && riverMask < 0.01)
        return underlyingColor;

    if (isCellWater)
    {
        // Ocean/lake volume attenuation:
        // Transmittance decays with depth (Beer-Lambert style), so deeper water hides seabed.
        float depth01 = saturate((_SeaLevel - height) / max(_SeaLevel, 0.001));
        float curvedDepth = pow(depth01, max(_WaterDepthExponent, 0.001));
        float opticalDepth = curvedDepth * max(_WaterOpticalDepth, 0.001);
        float3 absorption = max(_WaterAbsorption.rgb, float3(0.001, 0.001, 0.001));
        float3 transmittance = exp(-absorption * opticalDepth);

        float3 waterTint = lerp(_WaterShallowColor.rgb, _WaterDeepColor.rgb, depth01);

        // Animated shimmer rides on in-scattering tint (more visible in shallower water).
        float time = _Time.y * _ShimmerSpeed;
        float2 uv1 = worldUV + float2(time, time * 0.7);
        float2 uv2 = worldUV * 1.3 + float2(-time * 0.8, time * 0.5);
        float shimmer = (fbm2(uv1) + fbm2(uv2)) * 0.5;
        float shimmerStrength = _ShimmerIntensity * lerp(1.0, 0.45, depth01);
        float brightness = 1.0 + (shimmer - 0.5) * 2.0 * shimmerStrength;
        waterTint *= brightness;

        // Animated seabed refraction (screen-space approximation using UV offset).
        // Uses independent scale/speed controls so motion is decoupled from shimmer.
        float refractionStrength = _WaterRefractionStrength * lerp(1.0, 0.35, depth01);
        float refractionScaleRatio = _WaterRefractionScale / max(_ShimmerScale, 0.0001);
        float2 refractionBaseUV = worldUV * refractionScaleRatio;
        float refractionTime = _Time.y * _WaterRefractionSpeed;
        float2 refractNoiseUV1 = refractionBaseUV + float2(refractionTime * 0.9, -refractionTime * 0.6);
        float2 refractNoiseUV2 = refractionBaseUV * 1.7 + float2(-refractionTime * 0.7, refractionTime * 1.1);
        float2 refractionNoise = float2(
            fbm2(refractNoiseUV1 + float2(11.3, 3.7)),
            fbm2(refractNoiseUV2 + float2(5.9, 17.1))
        ) * 2.0 - 1.0;
        float2 refractUV = saturate(uv + refractionNoise * refractionStrength);

        float3 refractedUnderlying = underlyingColor;
        float4 refractedGeo = SampleGeographyBase(refractUV);
        bool refractedIsWater = refractedGeo.a >= 0.5;
        if (refractedIsWater)
        {
            float refractedHeight = tex2D(_HeightmapTex, refractUV).r;
            refractedUnderlying = ComputeTerrain(refractUV, true, refractedGeo.r, refractedHeight);
        }

        float3 attenuatedUnderlying = refractedUnderlying * transmittance;
        float3 inScattering = waterTint * (1.0 - transmittance);
        float3 volumetricColor = attenuatedUnderlying + inScattering;
        return volumetricColor;
    }

    // River on land: preserve current shallow overlay behavior.
    float riverAlpha = _WaterShallowAlpha * riverMask;
    return lerp(underlyingColor, _WaterShallowColor.rgb, riverAlpha);
}

float3 ApplyReliefShading(float3 baseColor, float2 uv, bool isWater)
{
    if (isWater || _ReliefShadeStrength <= 0.001)
        return baseColor;

    float3 encodedNormal = tex2D(_ReliefNormalTex, uv).rgb;
    float3 reliefNormal = normalize(encodedNormal * 2.0 - 1.0);
    reliefNormal = normalize(lerp(float3(0.0, 1.0, 0.0), reliefNormal, saturate(_ReliefNormalStrength)));

    float3 lightDir = normalize(_ReliefLightDir.xyz);
    float ndotl = saturate(dot(reliefNormal, lightDir));
    float shade = lerp(_ReliefAmbient, 1.0, ndotl);
    float shadeMix = lerp(1.0, shade, _ReliefShadeStrength);
    return baseColor * shadeMix;
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
    else if (_DebugView == 16)
    {
        return tex2D(_ReliefNormalTex, uv).rgb;
    }
    else if (_DebugView == 17)
    {
        sampleValue = saturate((tex2D(_VegetationTex, uv).r * 65535.0) / 6.0);
    }
    else if (_DebugView == 18)
    {
        sampleValue = tex2D(_VegetationTex, uv).g;
    }

    return float3(sampleValue, sampleValue, sampleValue);
}

#endif
