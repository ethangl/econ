Shader "EconSim/MaskedBorder"
{
    Properties
    {
        _CellDataTex ("Cell Data", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Offset -1, -1

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            sampler2D _CellDataTex;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv0 : TEXCOORD0;  // Data texture coordinates
                float2 uv1 : TEXCOORD1;  // x = border's state ID (normalized)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 dataUV : TEXCOORD0;
                float borderStateId : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.dataUV = v.uv0;
                o.borderStateId = v.uv1.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample the cell data texture to get the state ID at this pixel
                float4 cellData = tex2D(_CellDataTex, i.dataUV);
                float pixelStateId = cellData.r;  // R channel = StateId / 65535

                // Compare with the border's state ID
                // Use a small tolerance for floating point comparison
                float diff = abs(pixelStateId - i.borderStateId);
                if (diff > 0.00002)  // ~1.3 state IDs tolerance
                    discard;

                return i.color;
            }
            ENDCG
        }
    }
}
