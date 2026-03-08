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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.z = input.em.x;
                output.positionHCS = mul(_ProjectionMatrix, float4(input.positionOS.xy, max(0.0, input.em.x), 1.0));
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
