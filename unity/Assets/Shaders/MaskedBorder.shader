Shader "EconSim/MaskedBorder"
{
    Properties
    {
        _PoliticalIdsTex ("Political IDs", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PoliticalIdsTex);
            SAMPLER(sampler_PoliticalIdsTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv0 : TEXCOORD0;  // Data texture coordinates
                float2 uv1 : TEXCOORD1;  // x = border's realm ID (normalized)
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color : COLOR;
                float2 dataUV : TEXCOORD0;
                float borderRealmId : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.dataUV = input.uv0;
                output.borderRealmId = input.uv1.x;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample the cell data texture to get the realm ID at this pixel
                float4 cellData = SAMPLE_TEXTURE2D(_PoliticalIdsTex, sampler_PoliticalIdsTex, input.dataUV);
                float pixelRealmId = cellData.r;  // R channel = RealmId / 65535

                // Compare with the border's realm ID
                // Use a small tolerance for floating point comparison
                float diff = abs(pixelRealmId - input.borderRealmId);
                if (diff > 0.00002)  // ~1.3 realm IDs tolerance
                    discard;

                return input.color;
            }
            ENDHLSL
        }
    }
}
