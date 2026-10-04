Shader "VideoLayer/Opaque"
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
        // The video replaces the scene color without adding to its Bloom mask.
        Blend One Zero, Zero Zero
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma vertex VideoVert
            #pragma fragment VideoFrag
            #define VIDEO_MODE 2
            #include "VideoLayer.cginc"
            ENDCG
        }
    }
    Fallback Off
}
