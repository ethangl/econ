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
                float2 uv1 : TEXCOORD1;  // x = border's realm ID (normalized)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 dataUV : TEXCOORD0;
                float borderRealmId : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.dataUV = v.uv0;
                o.borderRealmId = v.uv1.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Sample the cell data texture to get the realm ID at this pixel
                float4 cellData = tex2D(_CellDataTex, i.dataUV);
                float pixelRealmId = cellData.r;  // R channel = RealmId / 65535

                // Compare with the border's realm ID
                // Use a small tolerance for floating point comparison
                float diff = abs(pixelRealmId - i.borderRealmId);
                if (diff > 0.00002)  // ~1.3 realm IDs tolerance
                    discard;

                return i.color;
            }
            ENDCG
        }
    }
}
