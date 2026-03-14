Shader "EconSim/WaterMesh"
{
    Properties
    {
        _RiverColor ("River Color", Color) = (0.18, 0.42, 0.68, 0.75)
        _CoastColor ("Coast Color", Color) = (0.55, 0.50, 0.40, 0.60)
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.25)) = 0.08
    }

    SubShader
    {
        Tags { "Queue"="Transparent-50" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

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
                float4 color : TEXCOORD1;
                UNITY_FOG_COORDS(2)
            };

            float4 _RiverColor;
            float4 _CoastColor;
            float _EdgeSoftness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Select river vs coast by vertex color R channel
                float isCoast = i.color.r;
                float4 baseColor = lerp(_RiverColor, _CoastColor, isCoast);

                // Soft-edge AA: UV.y goes 0 at left edge, 1 at right edge.
                // Distance from center = abs(uv.y - 0.5) * 2, range [0, 1]
                float distFromCenter = abs(i.uv.y - 0.5) * 2.0;

                // smoothstep fade at edges
                float edgeAlpha = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, distFromCenter);

                // Vertex alpha for endpoint fading
                float alpha = baseColor.a * edgeAlpha * i.color.a;

                fixed4 col = fixed4(baseColor.rgb, alpha);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
