#ifndef MAP_OVERLAY_COMMON_INCLUDED
#define MAP_OVERLAY_COMMON_INCLUDED

// Sample split core textures at a UV position with point filtering.
float4 SamplePoliticalIds(float2 uv)
{
    uv = saturate(uv);
    return tex2D(_PoliticalIdsTex, uv);
}

float4 SampleGeographyBase(float2 uv)
{
    uv = saturate(uv);
    return tex2D(_GeographyBaseTex, uv);
}

// Look up color from palette texture (256 entries).
// normalizedId is id / 65535.
float3 LookupPaletteColor(sampler2D palette, float normalizedId)
{
    float id = normalizedId * 65535.0;
    float paletteU = (clamp(round(id), 0, 255) + 0.5) / 256.0;
    return tex2D(palette, float2(paletteU, 0.5)).rgb;
}

float NormalizeLandHeight(float height)
{
    return saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
}

float LookupMarketIdFromCounty(float countyId)
{
    float countyIdRaw = countyId * 65535.0;
    float marketU = (clamp(round(countyIdRaw), 0, 16383) + 0.5) / 16384.0;
    return tex2D(_CellToMarketTex, float2(marketU, 0.5)).r;
}

bool IdEquals(float lhs, float rhs)
{
    return abs(lhs - rhs) < 0.00001;
}

// Integer hash function for deterministic variance (returns 0-1).
// Uses multiplicative hashing for better distribution with sequential inputs.
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

// 2D hash for noise functions (different from integer hash above).
float hash2d(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// Value noise for water shimmer.
float valueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);

    // Cubic interpolation for smoother result.
    float2 u = f * f * (3.0 - 2.0 * f);

    float a = hash2d(i);
    float b = hash2d(i + float2(1.0, 0.0));
    float c = hash2d(i + float2(0.0, 1.0));
    float d = hash2d(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 2-octave FBM for water shimmer.
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

// Convert color to grayscale using perceptual luminance weights.
float3 ToGrayscale(float3 color)
{
    float luma = dot(color, float3(0.299, 0.587, 0.114));
    return float3(luma, luma, luma);
}

#endif
