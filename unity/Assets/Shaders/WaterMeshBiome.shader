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
        _FresnelIntensity ("Fresnel Intensity", Range(0, 1)) = 0.4
        _WaveScale ("Wave Scale", Range(1, 200)) = 40
        _WaveStrength ("Wave Strength", Range(0, 1)) = 0.3
        _WaveSpeed ("Wave Speed", Range(0, 2)) = 0.4
    }

    SubShader
    {
        Tags { "Queue"="Geometry+1" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 worldPos : TEXCOORD3;
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
                float _FresnelIntensity;
                float _WaveScale;
                float _WaveStrength;
                float _WaveSpeed;
            CBUFFER_END

            static const float MESH_Y_OFFSET = 0.002;

            // ---- Procedural noise for wave normals ----

            float _hash2d(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float _valueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = _hash2d(i);
                float b = _hash2d(i + float2(1, 0));
                float c = _hash2d(i + float2(0, 1));
                float d = _hash2d(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 3-octave FBM for richer wave detail.
            float _fbm3(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                float2x2 rot = float2x2(0.8, 0.6, -0.6, 0.8);
                for (int i = 0; i < 3; i++)
                {
                    v += _valueNoise(p) * a;
                    p = mul(rot, p) * 2.0;
                    a *= 0.5;
                }
                return v;
            }

            // Compute wave-perturbed normal via finite differences on FBM heightfield.
            half3 WaveNormal(float2 worldXZ, float strength)
            {
                float t = _Time.y * _WaveSpeed;
                float2 uv = worldXZ * _WaveScale;

                // Two overlapping FBM layers moving in different directions.
                float2 uv1 = uv + float2(t * 0.7, t * 0.3);
                float2 uv2 = uv * 0.7 + float2(-t * 0.4, t * 0.6);
                float eps = 0.5;

                // Sample center and neighbors for gradient.
                float hC = _fbm3(uv1) + _fbm3(uv2);
                float hR = _fbm3(uv1 + float2(eps, 0)) + _fbm3(uv2 + float2(eps, 0));
                float hU = _fbm3(uv1 + float2(0, eps)) + _fbm3(uv2 + float2(0, eps));

                float dX = (hR - hC) * strength;
                float dZ = (hU - hC) * strength;

                return normalize(half3(-dX, 1.0, -dZ));
            }

            // ---- Vertex / Fragment ----

            v2f vert(appdata v)
            {
                v2f o;
                float4 vertex = v.vertex;
                float height01 = v.uv.x;
                vertex.y = (height01 - _SeaLevel) * _HeightScale + MESH_Y_OFFSET;
                o.pos = TransformObjectToHClip(vertex.xyz);
                o.uv = v.uv;
                o.vcolor = v.color;
                o.dataUV = vertex.xz / max(_MapWorldSize, float2(1, 1));
                o.worldPos = TransformObjectToWorld(vertex.xyz);
                return o;
            }

            half4 frag(v2f IN) : SV_Target
            {
                float typeR = IN.vcolor.r;

                // River: edge softness + waves/fresnel.
                if (typeR < 0.25)
                {
                    float distFromCenter = abs(IN.uv.y - 0.5) * 2.0;
                    float edgeAlpha = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, distFromCenter);

                    half3 N = WaveNormal(IN.worldPos.xz, _WaveStrength);
                    half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.worldPos);
                    half cosTheta = saturate(dot(N, viewDir));
                    half fresnel = 0.02 + 0.98 * pow(1.0 - cosTheta, 5.0);

                    Light mainLight = GetMainLight();
                    half3 color = _RiverColor.rgb + mainLight.color * fresnel * _FresnelIntensity;
                    half alpha = _RiverColor.a * edgeAlpha * IN.vcolor.a;
                    alpha = saturate(alpha + fresnel * _FresnelIntensity * edgeAlpha);

                    return half4(color, alpha);
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

                // Wave-perturbed normal for Fresnel (stronger in deep water).
                half3 N = WaveNormal(IN.worldPos.xz, _WaveStrength * saturate(depth01 * 4.0));

                // Fresnel reflection with perturbed normal.
                half3 viewDir = normalize(_WorldSpaceCameraPos.xyz - IN.worldPos);
                half cosTheta = saturate(dot(N, viewDir));
                half fresnel = 0.02 + 0.98 * pow(1.0 - cosTheta, 5.0);

                Light mainLight = GetMainLight();
                color += mainLight.color * fresnel * _FresnelIntensity;
                alpha = saturate(alpha + fresnel * _FresnelIntensity);

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
