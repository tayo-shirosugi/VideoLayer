Shader "VideoLayer/ChromaKey"
{
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        _Threshold ("Black noise floor", Range(0, 1)) = 0.02
        _Smoothness ("Black noise transition", Range(0, 1)) = 0.02
        _WhiteLevel ("Opaque foreground level", Range(0.01, 1)) = 1
        [HideInInspector] _KeyInLinearSpace ("Linear input", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        // Screen alpha is a Bloom mask in Beat Saber, not video opacity.
        // Transparent pixels preserve the scene mask; opaque video clears it.
        Blend One OneMinusSrcAlpha, Zero OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma vertex VideoVert
            #pragma fragment VideoFrag
            #define VIDEO_MODE 0
            #include "VideoLayer.cginc"
            ENDCG
        }
    }
    Fallback Off
}
