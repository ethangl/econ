Shader "EconSim/Mapgen4/RiverPass"
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
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 widths : TEXCOORD0;
                float2 barycentricXY : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 widths : TEXCOORD0;
                float3 barycentric : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4x4 _TopdownMatrix;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = mul(_TopdownMatrix, float4(input.positionOS.xy, 0, 1));
                output.widths = input.widths;
                output.barycentric = float3(input.barycentricXY.x, input.barycentricXY.y, 1.0 - input.barycentricXY.x - input.barycentricXY.y);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float xt = input.barycentric.x / max(1e-5, input.barycentric.z + input.barycentric.x);
                float dist = sqrt(input.barycentric.z * input.barycentric.z + input.barycentric.x * input.barycentric.x + input.barycentric.z * input.barycentric.x);
                float width = 0.35 * lerp(input.widths.x, input.widths.y, xt);
                float inRiver = smoothstep(width + 0.025, max(0.0, width - 0.05), abs(dist - 0.5));
                float3 riverColor = SRGBToLinear(float3(0.2, 0.5, 0.7));
                return half4(riverColor * inRiver, inRiver);
            }
            ENDHLSL
        }
    }
}
