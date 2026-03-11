Shader "EconSim/Mapgen4/BorderPass"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off

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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _TopdownMatrix;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = mul(_TopdownMatrix, float4(input.positionOS.xy, 0, 1));
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float centerDistance = abs(input.uv.x - 0.5);
                float halo = 1.0 - smoothstep(0.24, 0.5, centerDistance);
                float core = 1.0 - smoothstep(0.03, 0.10, centerDistance);
                return half4(core, halo, 0.0, halo);
            }
            ENDHLSL
        }
    }
}
