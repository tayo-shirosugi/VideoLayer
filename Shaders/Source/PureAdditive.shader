Shader "VideoLayer/PureAdditive"
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
        // Add video color while keeping the existing scene Bloom mask.
        Blend SrcAlpha One, Zero One
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma only_renderers d3d11
            #pragma vertex VideoVert
            #pragma fragment VideoFrag
            #define VIDEO_MODE 3
            #include "VideoLayer.cginc"
            ENDCG
        }
    }
    Fallback Off
}
