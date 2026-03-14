Shader "EconSim/Parchment"
{
    Properties
    {
        _Color ("Color", Color) = (0.85, 0.78, 0.65, 1.0)
    }

    SubShader
    {
        Tags { "Queue"="Geometry-2" "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float4 _Color;

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }

            fixed4 frag() : SV_Target
            {
                return _Color;
            }
            ENDCG
        }
    }
}
