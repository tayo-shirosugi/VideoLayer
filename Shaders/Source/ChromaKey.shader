Shader "VideoLayer/ChromaKey"
{
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        _Threshold ("Black threshold", Range(0, 1)) = 0.05
        _Smoothness ("Black edge softness", Range(0, 1)) = 0.05
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        // Screen alpha is a Bloom mask in Beat Saber, not video opacity.
        // Transparent pixels preserve the scene mask; opaque video clears it.
        Blend SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha
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
