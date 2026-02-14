Shader "EconSim/Water"
{
    Properties
    {
        // Heightmap for depth calculation (same as land shader)
        _HeightmapTex ("Heightmap", 2D) = "gray" {}
        _SeaLevel ("Sea Level", Float) = 0.2

        // Water colors
        _ShallowColor ("Shallow Color", Color) = (0.25, 0.55, 0.65, 1)
        _DeepColor ("Deep Color", Color) = (0.05, 0.15, 0.35, 1)

        // Depth control
        _DepthRange ("Depth Range", Float) = 0.2  // How much height below sea level = max depth

        // Animation
        _ShimmerScale ("Shimmer Scale", Float) = 0.02  // UV scale for noise
        _ShimmerSpeed ("Shimmer Speed", Float) = 0.03  // Animation speed
        _ShimmerIntensity ("Shimmer Intensity", Range(0, 0.2)) = 0.08  // Brightness variation
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-1" "RenderPipeline"="UniversalPipeline" }  // Render before land
        LOD 100

        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 worldUV : TEXCOORD1;  // For consistent shimmer across the map
            };

            sampler2D _HeightmapTex;
            float _SeaLevel;

            fixed4 _ShallowColor;
            fixed4 _DeepColor;
            float _DepthRange;

            float _ShimmerScale;
            float _ShimmerSpeed;
            float _ShimmerIntensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;

                // World UV for shimmer (consistent scale regardless of mesh UVs)
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldUV = worldPos.xz * _ShimmerScale;

                return o;
            }

            // Simple 2D noise function (value noise)
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                // Cubic interpolation for smoother result
                float2 u = f * f * (3.0 - 2.0 * f);

                float a = hash(i);
                float b = hash(i + float2(1.0, 0.0));
                float c = hash(i + float2(0.0, 1.0));
                float d = hash(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Fractal noise (2 octaves for subtle variation)
            float fbm(float2 p)
            {
                float value = 0.0;
                float amplitude = 0.5;

                value += noise(p) * amplitude;
                p *= 2.0;
                amplitude *= 0.5;
                value += noise(p) * amplitude;

                return value;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // DEBUG: Output solid magenta to verify shader is rendering
                return fixed4(1.0, 0.0, 1.0, 1.0);

                /*
                // Sample heightmap to get depth
                float height = tex2D(_HeightmapTex, IN.uv).r;

                // Calculate depth (0 = at sea level, 1 = max depth)
                // Height below sea level = deeper water
                float depth = saturate((_SeaLevel - height) / _DepthRange);

                // Base color from depth gradient
                fixed3 baseColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth);

                // Animated shimmer - two layers moving in different directions
                float time = _Time.y * _ShimmerSpeed;

                float2 uv1 = IN.worldUV + float2(time, time * 0.7);
                float2 uv2 = IN.worldUV * 1.3 + float2(-time * 0.8, time * 0.5);

                float shimmer1 = fbm(uv1);
                float shimmer2 = fbm(uv2);

                // Combine shimmer layers
                float shimmer = (shimmer1 + shimmer2) * 0.5;

                // Shimmer is stronger in shallow water (more visible light play)
                float shimmerStrength = _ShimmerIntensity * (1.0 - depth * 0.5);

                // Apply shimmer as brightness variation (centered around 1.0)
                float brightness = 1.0 + (shimmer - 0.5) * 2.0 * shimmerStrength;

                fixed3 finalColor = baseColor * brightness;

                return fixed4(finalColor, 1.0);
                */
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
