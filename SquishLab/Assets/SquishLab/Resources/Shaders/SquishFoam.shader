// SQUISH LAB — スクイーズのフォーム素材。
// 上の SubShader が URP 用、下が Built-in 用。中身は SquishFoamCommon.hlsl で共通。
Shader "SquishLab/Foam"
{
    Properties
    {
        _CrumbColor ("Crumb Color", Color) = (0.99, 0.965, 0.9, 1)
        _CrustColor ("Crust Color", Color) = (0.89, 0.67, 0.4, 1)
        _SubsurfaceColor ("Subsurface Color", Color) = (1, 0.78, 0.55, 1)
        _PoreScale ("Pore Scale", Range(5, 80)) = 52
        _PoreStrength ("Pore Strength", Range(0, 1)) = 0.45
        _WrinkleStrength ("Wrinkle Strength", Range(0, 1)) = 0.5
        _Sheen ("Sheen", Range(0, 1)) = 0.35

        // 照明は「強さ」なので Color ではなく Vector で持つ（リニア色空間で勝手に変換されないように）
        [Header(Studio Light linear intensity)]
        _KeyDir ("Key Light Dir (world, to light)", Vector) = (-0.45, 0.82, -0.38, 0)
        _KeyColor ("Key Light (rgb)", Vector) = (0.64, 0.62, 0.58, 1)
        _FillDir ("Fill Light Dir", Vector) = (0.7, 0.35, -0.3, 0)
        _FillColor ("Fill Light (rgb)", Vector) = (0.42, 0.42, 0.43, 1)
        _SkyColor ("Ambient Sky (rgb)", Vector) = (0.3, 0.305, 0.31, 1)
        _GroundColor ("Ambient Ground (rgb)", Vector) = (0.22, 0.2, 0.18, 1)
        _Wrap ("Diffuse Wrap", Range(0, 1)) = 0.55
        _SpecStrength ("Specular", Range(0, 0.5)) = 0.07
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex SqVert
            #pragma fragment SqFrag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #define SQ_OBJECT_TO_CLIP(p) TransformObjectToHClip(p)
            #define SQ_OBJECT_TO_WORLD(p) TransformObjectToWorld(p)
            #define SQ_NORMAL_TO_WORLD(n) TransformObjectToWorldNormal(n)
            #define SQ_CAMERA_POS _WorldSpaceCameraPos
            #include "SquishFoamCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS.xyz); }
            half4 DepthFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            Cull Back
            ZWrite On

            CGPROGRAM
            #pragma vertex SqVert
            #pragma fragment SqFrag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #define SQ_OBJECT_TO_CLIP(p) UnityObjectToClipPos(p)
            #define SQ_OBJECT_TO_WORLD(p) mul(unity_ObjectToWorld, float4(p, 1.0)).xyz
            #define SQ_NORMAL_TO_WORLD(n) UnityObjectToWorldNormal(n)
            #define SQ_CAMERA_POS _WorldSpaceCameraPos
            #include "SquishFoamCommon.hlsl"
            ENDCG
        }
    }

    Fallback Off
}
