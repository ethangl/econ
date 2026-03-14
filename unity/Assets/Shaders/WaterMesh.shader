Shader "EconSim/WaterMesh"
{
    Properties
    {
        _RiverColor ("River Color", Color) = (0.18, 0.42, 0.68, 0.75)
        _LakeColor ("Lake Color", Color) = (0.15, 0.35, 0.55, 0.80)
        _OceanColor ("Ocean Color", Color) = (0.10, 0.25, 0.45, 0.85)
        _EdgeSoftness ("River Edge Softness", Range(0.01, 0.25)) = 0.08
        _HeightScale ("Height Scale", Float) = 0.0
        _SeaLevel ("Sea Level", Float) = 0.2
    }

    SubShader
    {
        Tags { "Queue"="Geometry-1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        // Pass 0: Early stencil write — marks water pixels so the terrain shader can discard them.
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            Cull Off
            Stencil
            {
                Ref 2
                WriteMask 2
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex vert_stencil
            #pragma fragment frag_stencil

            #include "UnityCG.cginc"

            float _HeightScale;
            float _SeaLevel;
            static const float MESH_Y_OFFSET = 0.002;

            float4 vert_stencil(float4 vertex : POSITION, float2 uv : TEXCOORD0) : SV_POSITION
            {
                float height01 = uv.x;
                vertex.y = (height01 - _SeaLevel) * _HeightScale + MESH_Y_OFFSET;
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag_stencil() : SV_Target
            {
                return 0;
            }
            ENDCG
        }

        // Pass 1: Color rendering
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
            float4 _LakeColor;
            float4 _OceanColor;
            float _EdgeSoftness;
            float _HeightScale;
            float _SeaLevel;

            static const float MESH_Y_OFFSET = 0.002;

            v2f vert(appdata v)
            {
                v2f o;

                float4 vertex = v.vertex;

                // UV.x = raw 0-1 height. Same displacement as terrain shader.
                float height01 = v.uv.x;
                vertex.y = (height01 - _SeaLevel) * _HeightScale + MESH_Y_OFFSET;

                o.pos = UnityObjectToClipPos(vertex);
                o.uv = v.uv;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Vertex color R encodes type: 0 = river, 0.5 = lake, 1.0 = ocean
                float typeR = i.color.r;

                float4 baseColor;
                float alpha;

                if (typeR < 0.25)
                {
                    // River: edge AA from UV.y (0 at left edge, 1 at right edge)
                    baseColor = _RiverColor;
                    float distFromCenter = abs(i.uv.y - 0.5) * 2.0;
                    float edgeAlpha = 1.0 - smoothstep(1.0 - _EdgeSoftness * 2.0, 1.0, distFromCenter);
                    alpha = baseColor.a * edgeAlpha * i.color.a;
                }
                else if (typeR < 0.75)
                {
                    // Lake: solid fill
                    baseColor = _LakeColor;
                    alpha = baseColor.a;
                }
                else
                {
                    // Ocean: solid fill
                    baseColor = _OceanColor;
                    alpha = baseColor.a;
                }

                fixed4 col = fixed4(baseColor.rgb, alpha);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }

    FallBack Off
}
