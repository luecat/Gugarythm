Shader "Gugarhythm/Desaturate UI"
{
    Properties { [PerRendererData] _MainTex ("Texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
    SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha
        Pass { CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert
        #pragma fragment frag
        #include "UnityCG.cginc"
        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
        struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
        sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
        v2f vert(appdata i) { v2f o; o.vertex = UnityObjectToClipPos(i.vertex); o.uv = TRANSFORM_TEX(i.uv, _MainTex); o.color = i.color * _Color; return o; }
        fixed4 frag(v2f i) : SV_Target { fixed4 c = tex2D(_MainTex, i.uv) * i.color; fixed l = dot(c.rgb, fixed3(.2126, .7152, .0722)); c.rgb = lerp(l.xxx, c.rgb, .2); float edgeDistance = min(i.uv.x, 1 - i.uv.x); float edgeWidth = max(fwidth(i.uv.x), 1e-5); c.a *= smoothstep(0, edgeWidth, edgeDistance); return c; }
        ENDCG }
    }
}
