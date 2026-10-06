// SQUISH LAB — 撮影ブースの床（白〜薄いグレーのグラデーション）と、スクイーズの柔らかい接地影。
// 影はシャドウマップを使わず、底面の角丸矩形の距離場から解析的に描く（軽くてボケが綺麗）。
Shader "SquishLab/Floor"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.95, 0.95, 0.95, 1)
        _EdgeColor ("Edge Color", Color) = (0.8, 0.8, 0.81, 1)
        _FadeRadius ("Fade Radius", Float) = 4.5
        _Center ("Center (xz)", Vector) = (0, 0, 0, 0)

        _ShadowCenter ("Shadow Center (xz)", Vector) = (0, 0, 0, 0)
        _ShadowHalfSize ("Shadow Half Size (xz)", Vector) = (1, 1, 0, 0)
        _ShadowCorner ("Shadow Corner Radius", Float) = 0.35
        _ShadowOffset ("Shadow Offset (xz)", Vector) = (0.12, 0.1, 0, 0)
        _ShadowSoftness ("Shadow Softness", Float) = 0.55
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 0.28
        _ContactStrength ("Contact Strength", Range(0, 1)) = 0.45
    }


    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SquishFloorCommon.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            V Vert(A a)
            {
                V o;
                o.positionWS = TransformObjectToWorld(a.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }
            float4 Frag(V i) : SV_Target { return FloorColor(i.positionWS); }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-10" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "SquishFloorCommon.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            V Vert(A a)
            {
                V o;
                o.positionWS = mul(unity_ObjectToWorld, float4(a.positionOS.xyz, 1.0)).xyz;
                o.positionCS = UnityObjectToClipPos(a.positionOS.xyz);
                return o;
            }
            float4 Frag(V i) : SV_Target { return FloorColor(i.positionWS); }
            ENDCG
        }
    }

    Fallback Off
}
