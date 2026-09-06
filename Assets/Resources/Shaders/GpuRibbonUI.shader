Shader "Gugarhythm/GPU Ribbon UI"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
        _ApproachDuration ("Approach Duration", Float) = 2
        _CanvasHeight ("Canvas Height", Float) = 1080
        _NearTrackProgress ("Near Track Progress", Float) = 1.5
        _UvInset ("Horizontal UV Inset", Float) = 0
        _IsHold ("Hold Style", Float) = 0
        _RibbonOpacity ("Ribbon Opacity", Float) = 1
        _GroupCount ("Time Scale Group Count", Float) = 1
        _HoldStateCount ("Hold State Count", Float) = 1
        _HoldStateTex ("Hold State", 2D) = "black" {}
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 texcoord : TEXCOORD0;
                // (center constant, center slope, width constant, width slope):
                // GpuRibbonProjection.Vertex bakes LaneX's per-lane algebra
                // into this affine form at chart-load time (lane and size are
                // fixed per vertex), so the vertex stage below needs two
                // multiply-adds instead of the Intercepts/Slopes lookups
                // LaneX itself performs.
                float4 laneProjection : TEXCOORD1;
                // (time-scale group index, auxiliary index): a Canvas streams
                // TEXCOORD0 with two components only, so these cannot ride in
                // texcoord.zw -- they would arrive as zeroes, pinning every
                // ribbon to group 0 and every Guide to colour 0.
                float4 ribbonIndices : TEXCOORD2;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                float3 clipState : TEXCOORD2;
            };

            sampler2D _MainTex;
            sampler2D _HoldStateTex;
            float4 _ClipRect;
            float _ApproachDuration;
            float _CanvasHeight;
            float _NearTrackProgress;
            float _UvInset;
            float _IsHold;
            float _RibbonOpacity;
            float _GroupCount;
            float _HoldStateCount;
            float _GroupPositions[256];

            float Perspective(float approach)
            {
                if (approach <= 0) return approach / 3.2;
                if (approach >= 1) return 1 + (approach - 1) * 3.2;
                return approach / (3.2 - 2.2 * approach);
            }

            float ScreenY(float progress)
            {
                float top = _CanvasHeight * 0.5;
                float hit = top - 500.0 / 732.0 * _CanvasHeight;
                return lerp(top, hit, progress);
            }

            fixed4 GuideColor(float index)
            {
                if (index > 5.5) return fixed4(28.0 / 255.0, 34.0 / 255.0, 48.0 / 255.0, 1);
                if (index > 4.5) return fixed4(115.0 / 255.0, 214.0 / 255.0, 205.0 / 255.0, 1);
                if (index > 3.5) return fixed4(214.0 / 255.0, 179.0 / 255.0, 98.0 / 255.0, 1);
                if (index > 2.5) return fixed4(214.0 / 255.0, 115.0 / 255.0, 123.0 / 255.0, 1);
                if (index > 1.5) return fixed4(115.0 / 255.0, 165.0 / 255.0, 214.0 / 255.0, 1);
                if (index > .5) return fixed4(214.0 / 255.0, 115.0 / 255.0, 205.0 / 255.0, 1);
                return fixed4(115.0 / 255.0, 214.0 / 255.0, 157.0 / 255.0, 1);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                float targetPosition = input.vertex.z;
                float side = input.texcoord.x * 2.0 - 1.0;
                float groupIndex = round(input.ribbonIndices.x);
                float auxiliaryIndex = round(input.ribbonIndices.y);
                int currentGroup = (int)clamp(groupIndex, 0.0, min(255.0, _GroupCount - 1.0));
                float currentPosition = _GroupPositions[currentGroup];
                float approach = 1.0 - (targetPosition - currentPosition) / max(0.0001, _ApproachDuration);
                float progress = clamp(Perspective(approach), 0.0, _NearTrackProgress);
                float sourceY = (_CanvasHeight * 0.5 - ScreenY(progress)) * 732.0 / _CanvasHeight;
                float centerX = input.laneProjection.x + input.laneProjection.y * sourceY;
                float width = max(12.0, input.laneProjection.z + input.laneProjection.w * sourceY);
                input.vertex = float4(centerX + side * width * 0.5, ScreenY(progress), 0, 1);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = float2(lerp(_UvInset, 1.0 - _UvInset, input.texcoord.x), input.texcoord.y);
                output.color = _IsHold > .5
                    ? fixed4(1, 1, 1, _RibbonOpacity * input.color.a)
                    : GuideColor(auxiliaryIndex) * fixed4(1, 1, 1, input.color.a);
                output.clipState = float3(approach, 0, 1);
                output.worldPosition.w = _IsHold > .5 && auxiliaryIndex < _HoldStateCount
                    ? (auxiliaryIndex + .5) / max(1.0, _HoldStateCount)
                    : -1;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                clip(input.clipState.x - input.clipState.y);
                // Holds and Guides alike stop at the judgment line: the span
                // a Hold has already been held through is consumed, and its
                // remaining body is anchored there by the persistent head.
                // (An earlier attempt to keep Hold bodies past approach > 1
                // was chasing a different failure -- the group index never
                // reaching the shader -- and only made passed Holds sweep on
                // past the judgment line instead of being destroyed at it.)
                clip(input.clipState.z - input.clipState.x);
                fixed4 color = tex2D(_MainTex, input.texcoord) * input.color;
                float stateU = input.worldPosition.w;
                if (_IsHold > 0.5 && stateU >= 0.0 && tex2D(_HoldStateTex, float2(stateU, 0.5)).r > 0.5)
                {
                    fixed luminance = dot(color.rgb, fixed3(0.2126, 0.7152, 0.0722));
                    color.rgb = lerp(luminance.xxx, color.rgb, 0.2);
                }
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
