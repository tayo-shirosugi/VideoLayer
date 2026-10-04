Shader "VideoLayer/GreenKey"
{
    Properties
    {
        _MainTex ("Video", 2D) = "white" {}
        _GreenTolerance ("Green background tolerance", Range(0, 0.5)) = 0.05
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        // Video opacity blends color but does not create a screen Bloom mask.
        Blend SrcAlpha OneMinusSrcAlpha, Zero OneMinusSrcAlpha
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
