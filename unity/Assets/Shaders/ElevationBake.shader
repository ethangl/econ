Shader "EconSim/ElevationBake"
{
    // Renders interpolated vertex elevation to a RenderTexture.
    // Expects elevation in UV1.x (0-1 normalized).
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 elevation : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float elevation : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.elevation = input.elevation.x;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(input.elevation, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
