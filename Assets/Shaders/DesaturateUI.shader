Shader "Gugarhythm/Desaturate UI"
{
    Properties { [PerRendererData] _MainTex ("Texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) }
    SubShader { Tags { "Queue"="Transparent" "RenderType"="Transparent" } Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha
        Pass { CGPROGRAM
        #pragma target 3.0
        #pragma vertex vert
        #pragma fragment frag
        #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
        #include "UnityCG.cginc"
        #include "UnityUI.cginc"
        struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
        struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; float2 worldPosition : TEXCOORD1; };
        sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color; float4 _ClipRect;
        v2f vert(appdata i) { v2f o; o.worldPosition = i.vertex.xy; o.vertex = UnityObjectToClipPos(i.vertex); o.uv = TRANSFORM_TEX(i.uv, _MainTex); o.color = i.color * _Color; return o; }
        fixed4 frag(v2f i) : SV_Target
        {
            fixed4 c = tex2D(_MainTex, i.uv) * i.color;
            fixed l = dot(c.rgb, fixed3(.2126, .7152, .0722));
            c.rgb = lerp(l.xxx, c.rgb, .2);
            float edgeDistance = min(i.uv.x, 1 - i.uv.x);
            // fwidth(uv.x) is the screen-space derivative of the ribbon's
            // across-width UV. Where adjacent quads meet at a sharp angle, or
            // under an extreme local TimeScale, that derivative can spike past
            // the 0-0.5 range edgeDistance ever reaches, which would fade a
            // whole span toward alpha 0 instead of just softening its edge.
            // Capping it keeps the intended anti-aliasing on ordinary geometry
            // and bounds the worst case.
            float edgeWidth = clamp(fwidth(i.uv.x), 1e-5, .1);
            c.a *= smoothstep(0, edgeWidth, edgeDistance);
            #ifdef UNITY_UI_CLIP_RECT
            c.a *= UnityGet2DClipping(i.worldPosition, _ClipRect);
            #endif
            return c;
        }
        ENDCG }
    }
}
