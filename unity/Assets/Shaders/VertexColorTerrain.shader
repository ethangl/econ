Shader "EconSim/VertexColorTerrain"
{
    Properties
    {
        _Glossiness ("Smoothness", Range(0,1)) = 0.2
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _FORWARD_PLUS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                half fogFactor : TEXCOORD2;
            };

            half _Glossiness;
            half _Metallic;

            Varyings vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.fogFactor = ComputeFogFactor(output.positionHCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = SafeNormalize(input.normalWS);
                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 albedo = input.color.rgb;
                half smoothness = saturate(_Glossiness);
                half metallic = saturate(_Metallic);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half3 diffuse = albedo * (SampleSH(normalWS) + mainLight.color * (NdotL * mainLight.shadowAttenuation));

                half3 halfDir = SafeNormalize(mainLight.direction + viewDirWS);
                half specPow = exp2(1.0h + smoothness * 10.0h);
                half spec = pow(saturate(dot(normalWS, halfDir)), specPow) * NdotL;
                half3 specColor = lerp(half3(0.04, 0.04, 0.04), albedo, metallic);
                half3 color = diffuse + specColor * spec * mainLight.color * mainLight.shadowAttenuation;

                #ifdef _ADDITIONAL_LIGHTS
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0; lightIndex < lightCount; lightIndex++)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    half addNdotL = saturate(dot(normalWS, light.direction));
                    half3 addHalfDir = SafeNormalize(light.direction + viewDirWS);
                    half addSpec = pow(saturate(dot(normalWS, addHalfDir)), specPow) * addNdotL;
                    half attenuation = light.distanceAttenuation * light.shadowAttenuation;

                    color += albedo * light.color * addNdotL * attenuation;
                    color += specColor * addSpec * light.color * attenuation;
                }
                #endif

                color = MixFog(color, input.fogFactor);
                return half4(color, input.color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
