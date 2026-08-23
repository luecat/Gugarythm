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
        _GroupPositionTex ("Time Scale Group Positions", 2D) = "black" {}
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
                float2 texcoord : TEXCOORD0;
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
            sampler2D _GroupPositionTex;
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

            static const float Intercepts[13] =
            {
                616.0356, 620.9612, 624.5489, 628.4903, 631.5389, 635.4715, 638.8049,
                642.5187, 646.0649, 649.5068, 653.0450, 656.5548, 660.2418
            };
            static const float Slopes[13] =
            {
                -0.8379661, -0.7036342, -0.5590519, -0.4198532, -0.2774788, -0.1406074, 0.0000444,
                0.1412126, 0.2827021, 0.4205463, 0.5611308, 0.7017399, 0.8439814
            };

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

            float LaneX(float lane, float progress)
            {
                float sourceY = (_CanvasHeight * 0.5 - ScreenY(progress)) * 732.0 / _CanvasHeight;
                int guide = (int)clamp(floor(lane + 6.0), 0.0, 11.0);
                float guideLane = -6.0 + guide;
                float t = lane - guideLane;
                float left = Intercepts[guide] + Slopes[guide] * sourceY;
                float right = Intercepts[guide + 1] + Slopes[guide + 1] * sourceY;
                float sourceX = lerp(left, right, t);
                float sourceCenter = Intercepts[6] + Slopes[6] * sourceY;
                return (sourceX - sourceCenter) / 1280.0 * 1920.0;
            }

            fixed4 GuideColor(float index)
            {
                if (index > 5.5) return fixed4(28.0 / 255.0, 34.0 / 255.0, 48.0 / 255.0, .32);
                if (index > 4.5) return fixed4(115.0 / 255.0, 214.0 / 255.0, 205.0 / 255.0, .32);
                if (index > 3.5) return fixed4(214.0 / 255.0, 179.0 / 255.0, 98.0 / 255.0, .32);
                if (index > 2.5) return fixed4(214.0 / 255.0, 115.0 / 255.0, 123.0 / 255.0, .32);
                if (index > 1.5) return fixed4(115.0 / 255.0, 165.0 / 255.0, 214.0 / 255.0, .32);
                if (index > .5) return fixed4(214.0 / 255.0, 115.0 / 255.0, 205.0 / 255.0, .32);
                return fixed4(115.0 / 255.0, 214.0 / 255.0, 157.0 / 255.0, .32);
            }

            v2f vert(appdata_t input)
            {
                v2f output;
                float lane = input.vertex.x;
                float size = input.vertex.y;
                float targetPosition = input.vertex.z;
                float side = input.texcoord.x * 2.0 - 1.0;
                float groupIndex = round(input.color.r * 255.0) + round(input.color.g * 255.0) * 256.0;
                float groupU = (groupIndex + .5) / max(1.0, _GroupCount);
                float currentPosition = tex2Dlod(_GroupPositionTex, float4(groupU, .5, 0, 0)).r;
                float approach = 1.0 - (targetPosition - currentPosition) / max(0.0001, _ApproachDuration);
                float progress = clamp(Perspective(approach), 0.0, _NearTrackProgress);
                float centerX = LaneX(lane, progress);
                float width = max(12.0, LaneX(lane + size, progress) - LaneX(lane - size, progress));
                input.vertex = float4(centerX + side * width * 0.5, ScreenY(progress), 0, 1);
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = float2(lerp(_UvInset, 1.0 - _UvInset, input.texcoord.x), input.texcoord.y);
                float auxiliaryLow = round(input.color.b * 255.0);
                float auxiliaryHigh = round(input.color.a * 255.0);
                output.color = _IsHold > .5
                    ? fixed4(1, 1, 1, _RibbonOpacity)
                    : GuideColor(auxiliaryLow) * fixed4(1, 1, 1, input.color.a);
                output.clipState = float3(approach, 0, 1);
                float stateIndex = auxiliaryLow + auxiliaryHigh * 256.0;
                output.worldPosition.w = _IsHold > .5 && stateIndex < _HoldStateCount
                    ? (stateIndex + .5) / max(1.0, _HoldStateCount)
                    : -1;
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                clip(input.clipState.x - input.clipState.y);
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
