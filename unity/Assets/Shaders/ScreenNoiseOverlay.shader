Shader "EconSim/ScreenNoiseOverlay"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _NoiseIntensity;

            // Hash function for procedural noise
            float hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample source texture
                fixed4 col = tex2D(_MainTex, i.uv);

                // Generate per-pixel noise (each screen pixel gets independent random value)
                float2 pixelCoord = i.uv * _ScreenParams.xy;
                float noise = hash(pixelCoord) - 0.5;  // -0.5 to 0.5

                // Add noise to image
                col.rgb += noise * _NoiseIntensity;

                return col;
            }
            ENDCG
        }
    }
}
