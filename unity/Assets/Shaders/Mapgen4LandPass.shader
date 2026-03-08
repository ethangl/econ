Shader "EconSim/Mapgen4/LandPass"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_WaterTex);
            SAMPLER(sampler_WaterTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 em : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float e : TEXCOORD0;
                float2 xy : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _TopdownMatrix;
                float _OutlineWater;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 pos = mul(_TopdownMatrix, float4(input.positionOS.xy, 0, 1));
                output.positionHCS = pos;
                output.xy = (1.0 + pos.xy) * 0.5;
                output.e = input.em.x;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float e = 0.5 * (1.0 + input.e);
                float river = SAMPLE_TEXTURE2D(_WaterTex, sampler_WaterTex, input.xy).a;
                if (e >= 0.5)
                {
                    float bump = _OutlineWater / 256.0;
                    float l1 = e + bump;
                    float l2 = (e - 0.5) * (bump * 100.0) + 0.5;
                    e = min(l1, lerp(l1, l2, river));
                }
                return half4(e, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
