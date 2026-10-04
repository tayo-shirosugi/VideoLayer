// Original VideoLayer shader implementation. Licensed under the repository MIT license.
#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_ST;
float _Threshold;
float _Smoothness;
float _GreenTolerance;

struct VideoVertex
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct VideoFragment
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VideoFragment VideoVert(VideoVertex input)
{
    VideoFragment output;
    output.position = UnityObjectToClipPos(input.vertex);
    output.uv = TRANSFORM_TEX(input.uv, _MainTex);
    return output;
}

float4 VideoFrag(VideoFragment input) : SV_Target
{
    float4 color = tex2D(_MainTex, input.uv);
#if VIDEO_MODE == 0
    // Black-key coverage is independent of the destination/background color.
    float brightness = max(color.r, max(color.g, color.b));
    float coverage = smoothstep(_Threshold, _Threshold + max(_Smoothness, 0.0001), brightness);
    color.a *= coverage;
#elif VIDEO_MODE == 2
    color.a = 1.0;
#elif VIDEO_MODE == 4
    // A pure-green backing contributes green beyond the other channels.
    // Recover black/white text edges before applying the matte tolerance.
    float greenBacking = saturate(color.g - max(color.r, color.b));
    float originalCoverage = 1.0 - greenBacking;
    color.g -= greenBacking;
    color.rgb = saturate(color.rgb / max(originalCoverage, 0.0001));
    float coverage = saturate((originalCoverage - _GreenTolerance) / max(1.0 - _GreenTolerance, 0.0001));
    color.a *= coverage;
#endif
    return color;
}
