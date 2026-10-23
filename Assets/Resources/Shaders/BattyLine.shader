Shader "GoingBatty/Line"
{
    // Outline lines drawn on top of the black world boxes. Pulled toward the camera
    // with a depth offset so they never z-fight with the faces they sit on.
    Properties
    {
        _Color ("Color", Color) = (0.35,0.42,0.55,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Cull Off
            ZWrite On
            Offset -1, -8
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; };
            v2f vert (appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; return o; }
            fixed4 frag (v2f i) : SV_Target { return i.color * _Color; }
            ENDCG
        }
    }
}
