Shader "EconSim/Mapgen4/DepthPass"
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

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 em : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float z : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ProjectionMatrix;
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
                output.z = input.em.x;
                float4 pos = mul(_ProjectionMatrix, float4(input.positionOS.xy, max(0.0, input.em.x), 1.0));
                output.positionHCS = ConvertClipDepth(pos);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(input.z, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
