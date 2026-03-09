Shader "EconSim/Mapgen4/Display"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_ColorMap);
            SAMPLER(sampler_ColorMap);
            TEXTURE2D(_ElevationTex);
            SAMPLER(sampler_ElevationTex);
            TEXTURE2D(_WaterTex);
            SAMPLER(sampler_WaterTex);
            TEXTURE2D(_DepthTex);
            SAMPLER(sampler_DepthTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 em : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 xy : TEXCOORD1;
                float2 em : TEXCOORD2;
                float z : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ProjectionMatrix;
                float2 _LightAngle;
                float2 _InverseTextureSize;
                float _Slope;
                float _Flat;
                float _Ambient;
                float _Overhead;
                float _OutlineStrength;
                float _OutlineCoast;
                float _OutlineWater;
                float _OutlineDepth;
                float _OutlineThreshold;
                float _BiomeColors;
            CBUFFER_END

            float4 ConvertClipDepth(float4 clipPos)
            {
                #if UNITY_UV_STARTS_AT_TOP
                float ndcZ = clipPos.z / max(clipPos.w, 1e-6);
                    #if UNITY_REVERSED_Z
                    clipPos.z = saturate(0.5 * (1.0 - ndcZ)) * clipPos.w;
                    #else
                    clipPos.z = saturate(0.5 * (ndcZ + 1.0)) * clipPos.w;
                    #endif
                #endif
                return clipPos;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.em = input.em;
                output.z = max(0.0, input.em.x);
                float4 pos = mul(_ProjectionMatrix, float4(input.positionOS.xy, output.z, 1.0));
                pos = ConvertClipDepth(pos);
                output.positionHCS = pos;
                output.uv = input.positionOS.xy / 1000.0;
                output.xy = (1.0 + pos.xy) * 0.5;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 sampleOffset = 0.5 * _InverseTextureSize;
                float2 pos = input.uv + sampleOffset;
                float2 rtPos = pos;
                float2 rtScreen = input.xy;
                #if UNITY_UV_STARTS_AT_TOP
                rtPos.y = 1.0 - rtPos.y;
                rtScreen.y = 1.0 - rtScreen.y;
                #endif
                float2 dx = float2(_InverseTextureSize.x, 0);
                float2 dy = float2(0, _InverseTextureSize.y);
                float2 dxUv = float2(dx.x, 0.0);
                float2 dyUv = float2(0.0, dy.y);
                #if UNITY_UV_STARTS_AT_TOP
                dyUv.y = -dyUv.y;
                #endif

                float z = SAMPLE_TEXTURE2D(_ElevationTex, sampler_ElevationTex, rtPos).x;
                float zE = SAMPLE_TEXTURE2D(_ElevationTex, sampler_ElevationTex, rtPos + dxUv).x;
                float zN = SAMPLE_TEXTURE2D(_ElevationTex, sampler_ElevationTex, rtPos - dyUv).x;
                float zW = SAMPLE_TEXTURE2D(_ElevationTex, sampler_ElevationTex, rtPos - dxUv).x;
                float zS = SAMPLE_TEXTURE2D(_ElevationTex, sampler_ElevationTex, rtPos + dyUv).x;
                float3 slopeVector = normalize(float3(zS - zN, zE - zW, _Overhead * (_InverseTextureSize.x + _InverseTextureSize.y)));
                float3 lightVector = normalize(float3(_LightAngle.xy, lerp(_Slope, _Flat, slopeVector.z)));
                float light = _Ambient + max(0.0, dot(lightVector, slopeVector));
                float3 neutralLandBiome = SRGBToLinear(float3(0.9, 0.8, 0.7));
                float3 neutralWaterBiome = 0.8 * neutralLandBiome;
                float3 neutralBiome = neutralLandBiome;
                float4 waterColor = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, rtPos);
                if (z >= 0.5 && input.z >= 0.0)
                {
                    z -= _OutlineWater / 256.0 * (1.0 - waterColor.a);
                }
                else
                {
                    waterColor.a = 0.0;
                    neutralBiome = neutralWaterBiome;
                }

                float3 biomeColor = SAMPLE_TEXTURE2D(_ColorMap, sampler_ColorMap, float2(z, input.em.y)).rgb;
                float3 neutralRiverBiome = neutralWaterBiome;
                float3 neutralWaterColor = lerp(neutralWaterBiome, neutralRiverBiome, waterColor.a);
                waterColor = lerp(float4(neutralWaterColor, waterColor.a), waterColor, _BiomeColors);
                biomeColor = lerp(neutralBiome, biomeColor, _BiomeColors);

                float depth0 = SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen).x;
                float depth1 = max(max(SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen + _OutlineDepth * (-dyUv - dxUv)).x,
                                       SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen + _OutlineDepth * (-dyUv + dxUv)).x),
                                   SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen + _OutlineDepth * (-dyUv)).x);
                float depth2 = max(max(SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen + _OutlineDepth * (dyUv - dxUv)).x,
                                       SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen + _OutlineDepth * (dyUv + dxUv)).x),
                                   SAMPLE_TEXTURE2D(_DepthTex, sampler_DepthTex, rtScreen + _OutlineDepth * (dyUv)).x);
                float outline = 1.0 + _OutlineStrength * (max(_OutlineThreshold, depth1 - depth0) - _OutlineThreshold);

                float neighboringRiver = max(
                    max(SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, rtPos + _OutlineDepth * dxUv).a,
                        SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, rtPos - _OutlineDepth * dxUv).a),
                    max(SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, rtPos + _OutlineDepth * dyUv).a,
                        SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, rtPos - _OutlineDepth * dyUv).a)
                );
                if (z <= 0.5 && max(depth1, depth2) > 1.0 / 256.0 && neighboringRiver <= 0.2)
                {
                    outline += _OutlineCoast * 256.0 * (max(depth1, depth2) - 2.0 * (z - 0.5));
                }

                float3 color = lerp(biomeColor, waterColor.rgb, waterColor.a) * light / outline;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
