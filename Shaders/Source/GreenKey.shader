Shader "VideoLayer/GreenKey"
{
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        _GreenTolerance ("Green noise floor", Range(0, 0.5)) = 0.03
        _GreenSoftness ("Green noise transition", Range(0, 0.5)) = 0.03
        _GreenScreenColor ("Green screen RGB (sRGB)", Vector) = (0, 1, 0, 0)
        [Toggle] _GreenEdgeRecovery ("Recover colored text edges", Float) = 1
        [HideInInspector] _KeyInLinearSpace ("Linear input", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        // Video opacity blends color but does not create a screen Bloom mask.
        Blend One OneMinusSrcAlpha, Zero OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma vertex VideoVert
            #pragma fragment VideoFrag
            #define VIDEO_MODE 4
            #include "VideoLayer.cginc"
            ENDCG
        }
    }
    Fallback Off
}
