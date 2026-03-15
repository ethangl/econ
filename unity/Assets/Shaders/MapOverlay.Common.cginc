#ifndef MAP_OVERLAY_COMMON_INCLUDED
#define MAP_OVERLAY_COMMON_INCLUDED

// Combined palette texture (256×4): realm=row0, biome=row1, market=row2, spare=row3.
TEXTURE2D(_PaletteTex);
SAMPLER(sampler_PaletteTex);

#define PALETTE_ROW_REALM  0
#define PALETTE_ROW_BIOME  1
#define PALETTE_ROW_MARKET 2

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

// Look up color from combined palette texture.
// paletteRow: PALETTE_ROW_REALM, PALETTE_ROW_BIOME, or PALETTE_ROW_MARKET.
// normalizedId: id / 65535.
float3 LookupPaletteColor(int paletteRow, float normalizedId)
{
    float id = normalizedId * 65535.0;
    float paletteU = (clamp(round(id), 0, 255) + 0.5) / 256.0;
    float paletteV = (float(paletteRow) + 0.5) / 4.0;
    return SAMPLE_TEXTURE2D(_PaletteTex, sampler_PaletteTex, float2(paletteU, paletteV)).rgb;
}

float NormalizeLandHeight(float height)
{
    return saturate((height - _SeaLevel) / (1.0 - _SeaLevel));
}

float LookupMarketIdFromCounty(float countyId)
{
    return 0.0;
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

// Convert color to grayscale using perceptual luminance weights.
float3 ToGrayscale(float3 color)
{
    float luma = dot(color, float3(0.299, 0.587, 0.114));
    return float3(luma, luma, luma);
}

#endif
