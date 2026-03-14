Shader "EconSim/WaterMeshBiome"
{
    Properties
    {
        _HeightScale ("Height Scale", Float) = 0.0
        _SeaLevel ("Sea Level", Float) = 0.2
        _RiverColor ("River Color", Color) = (0.18, 0.42, 0.68, 0.75)
        _LakeColor ("Lake Color", Color) = (0.15, 0.35, 0.55, 0.60)
        _OceanColor ("Ocean Color", Color) = (0.10, 0.25, 0.45, 0.65)
        _EdgeSoftness ("River Edge Softness", Range(0.01, 0.25)) = 0.08
        _DepthAbsorption ("Depth Absorption", Range(0.5, 20)) = 6.0
        _ShallowTint ("Shallow Tint", Color) = (0.30, 0.60, 0.55, 1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry+1" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 vcolor : TEXCOORD1;
                float2 dataUV : TEXCOORD2;
            };

            TEXTURE2D(_HeightmapTex);
            SAMPLER(sampler_HeightmapTex);

            CBUFFER_START(UnityPerMaterial)
                float _HeightScale;
                float _SeaLevel;
                half4 _RiverColor;
                half4 _LakeColor;
                half4 _OceanColor;
                float _EdgeSoftness;
                float _DepthAbsorption;
                half4 _ShallowTint;
                float2 _MapWorldSize;
            CBUFFER_END

            static const float MESH_Y_OFFSET = 0.002;

            v2f vert(appdata v)
            {
                v2f o;
                float4 vertex = v.vertex;
                float height01 = v.uv.x;
                vertex.y = (height01 - _SeaLevel) * _HeightScale + MESH_Y_OFFSET;
                o.pos = TransformObjectToHClip(vertex.xyz);
                o.uv = v.uv;
                o.vcolor = v.color;
                // Compute data UV from object-space XZ position and map dimensions.
                o.dataUV = vertex.xz / max(_MapWorldSize, float2(1, 1));
                return o;
            }

            half4 frag(v2f IN) : SV_Target
            {
                float typeR = IN.vcolor.r;

                // River: flat color with edge softness, no depth effect.
                if (typeR < 0.25)
                {
                    float distFromCenter = abs(IN.uv.y - 0.5) * 2.0;
                    float edgeAlpha = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, distFromCenter);
                    return half4(_RiverColor.rgb, _RiverColor.a * edgeAlpha * IN.vcolor.a);
                }

                // Lake / ocean: depth-based light absorption.
                half4 baseColor = (typeR < 0.75) ? _LakeColor : _OceanColor;

                // Water surface level is in UV.x; sample terrain height underneath.
                float waterLevel = IN.uv.x;
                float terrainHeight = SAMPLE_TEXTURE2D(_HeightmapTex, sampler_HeightmapTex, IN.dataUV).r;
                float depth01 = saturate(waterLevel - terrainHeight);

                // Beer-Lambert style absorption: transmittance decays exponentially with depth.
                float transmittance = exp(-depth01 * _DepthAbsorption);

                // Shallow water: tinted, more transparent (terrain shows through).
                // Deep water: base color, full opacity.
                half3 color = lerp(baseColor.rgb, _ShallowTint.rgb, transmittance);
                half alpha = baseColor.a * (1.0 - transmittance * (1.0 - 0.15));

                return half4(color, alpha);
            }
        ENDHLSL

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            // Prevent double-blending at cell boundaries:
            // first fragment to hit a pixel writes stencil, subsequent fragments are skipped.
            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            ENDHLSL
        }
    }

    FallBack Off
}
