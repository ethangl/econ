Shader "EconSim/BorderLine"
{
    Properties
    {
        _Color ("Color", Color) = (0.1, 0.1, 0.1, 1)
        _PatternMode ("Pattern Mode (0=solid, 1=dashed, 2=dotted)", Int) = 0
        _PatternScale ("Pattern Scale (world units per cycle)", Float) = 0.1
        _DashRatio ("Dash Ratio (0-1, portion visible)", Range(0, 1)) = 0.6
        _AnimOffset ("Animation Offset (cycles)", Float) = 0
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.1
        _DepthOffset ("Depth Offset (negative = closer)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" }
        LOD 100

        // Enable alpha blending for smooth edges
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off  // Draw both sides for robustness

        Pass
        {
            // Depth bias to render on top of terrain without geometry offset
            Offset [_DepthOffset], [_DepthOffset]

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;  // x = distance along line (world units), y = perpendicular (0-1)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            fixed4 _Color;
            int _PatternMode;
            float _PatternScale;
            float _DashRatio;
            float _AnimOffset;
            float _EdgeSoftness;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // Start with vertex color multiplied by tint
                fixed4 col = IN.color * _Color;

                // Pattern visibility (1 = visible, 0 = gap)
                float patternAlpha = 1.0;

                if (_PatternMode > 0 && _PatternScale > 0.001)
                {
                    // Calculate phase in pattern cycle
                    float phase = (IN.uv.x / _PatternScale) + _AnimOffset;
                    float frac_phase = frac(phase);

                    if (_PatternMode == 1)
                    {
                        // Dashed: visible when frac < ratio
                        // Add slight softness at dash edges for anti-aliasing
                        float dashEdge = 0.02;
                        patternAlpha = smoothstep(0, dashEdge, frac_phase) *
                                       smoothstep(_DashRatio + dashEdge, _DashRatio, frac_phase);
                    }
                    else if (_PatternMode == 2)
                    {
                        // Dotted: shorter dashes (ratio reduced)
                        float dotRatio = _DashRatio * 0.5;
                        float dotEdge = 0.05;
                        patternAlpha = smoothstep(0, dotEdge, frac_phase) *
                                       smoothstep(dotRatio + dotEdge, dotRatio, frac_phase);
                    }
                }

                // Edge softness: fade out at line edges (uv.y near 0 or 1)
                // uv.y = 0 at left edge, 1 at right edge
                float edgeDist = min(IN.uv.y, 1.0 - IN.uv.y);  // Distance from nearest edge (0-0.5)
                float edgeAlpha = smoothstep(0, _EdgeSoftness, edgeDist);

                // Combine alphas
                col.a *= patternAlpha * edgeAlpha;

                // Discard fully transparent pixels
                if (col.a < 0.001)
                    discard;

                return col;
            }
            ENDCG
        }
    }
    FallBack "Transparent/Diffuse"
}
