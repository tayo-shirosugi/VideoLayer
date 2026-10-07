// Original VideoLayer shader implementation. Licensed under the repository MIT license.
#include "UnityCG.cginc"

sampler2D _MainTex;
float4 _MainTex_ST;
float4 _MainTex_TexelSize;
float _Threshold;
float _Smoothness;
float _WhiteLevel;
float _GreenTolerance;
float _GreenSoftness;
float4 _GreenScreenColor;
float _GreenEdgeRecovery;
float _KeyInLinearSpace;

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

#if VIDEO_MODE == 0 || VIDEO_MODE == 4
float3 VideoDisplayRGB(float3 rgb)
{
    rgb = saturate(rgb);
    if (_KeyInLinearSpace > 0.5)
        return lerp(rgb * 12.92, 1.055 * pow(rgb, 1.0 / 2.4) - 0.055, step(0.0031308, rgb));
    return rgb;
}

float3 VideoRendererRGB(float3 rgb)
{
    if (_KeyInLinearSpace > 0.5)
        return lerp(rgb / 12.92, pow((rgb + 0.055) / 1.055, 2.4), step(0.04045, rgb));
    return rgb;
}

#if VIDEO_MODE == 4
float RecoverGreenCoverage(inout float3 rgb, float3 screen, float coverage, float2 uv)
{
    // A single flattened pixel cannot distinguish a red edge from opaque yellow
    // or a magenta edge from opaque gray. Neighboring foreground texels supply
    // a local color reference, only when the pixel fits that screen/color mix.
    float3 lower = (screen - rgb) / max(screen, float3(0.0001, 0.0001, 0.0001));
    float3 upper = (rgb - screen) / max(1.0 - screen, float3(0.0001, 0.0001, 0.0001));
    float3 bounds = max(lower, upper);
    float minimum = saturate(max(bounds.r, max(bounds.g, bounds.b)));
    coverage = max(coverage, minimum);
    if (_GreenEdgeRecovery < 0.5 || coverage <= _GreenTolerance || coverage - minimum < 0.0001)
        return coverage;

    float2 texel = 1.0 / max(abs(_MainTex_TexelSize.zw), float2(1.0, 1.0));
    float3 fromScreen = rgb - screen;
    float3 recovered = rgb;
    [unroll]
    for (int y = -1; y <= 1; y++)
    {
        [unroll]
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0) continue;
            float2 sampleUV = clamp(uv + float2(x, y) * texel, texel * 0.5, 1.0 - texel * 0.5);
            float4 neighbor = tex2Dlod(_MainTex, float4(sampleUV, 0, 0));
            float3 neighborRGB = VideoDisplayRGB(neighbor.rgb);
            float3 reference = neighborRGB - screen;
            float distanceSquared = dot(reference, reference);
            float candidate = saturate(dot(fromScreen, reference) / max(distanceSquared, 0.0001));
            float3 residual = fromScreen - candidate * reference;
            // Chroma subsampling can displace an edge's color from its ideal
            // screen/foreground line. A strongly colored neighbor permits more
            // chroma repair; neutral neighbors retain a tight fit to protect gray.
            float colorRange = max(neighborRGB.r, max(neighborRGB.g, neighborRGB.b))
                - min(neighborRGB.r, min(neighborRGB.g, neighborRGB.b));
            float fitTolerance = colorRange > 0.5 ? 0.0064 : 0.0004;
            if (neighbor.a > 0 && distanceSquared > 0.0001 && candidate < coverage
                && dot(residual, residual) <= fitTolerance)
            {
                coverage = candidate;
                recovered = screen + candidate * reference;
            }
        }
    }
    rgb = recovered;
    return coverage;
}
#endif

// Recover coverage in the video's display RGB space, then return premultiplied
// color in the renderer's space. The uniform avoids baking the editor's color
// space into a bundle used by a game with a different color space.
float4 KeyVideoPixel(float4 source, float2 uv)
{
    float3 rgb = VideoDisplayRGB(source.rgb);
    float originalCoverage;
    float coverage;
#if VIDEO_MODE == 0
    // A black backing has already multiplied the foreground by its coverage.
    // The peak channel recovers white/saturated artwork without changing hue.
    float brightness = max(rgb.r, max(rgb.g, rgb.b));
    originalCoverage = saturate(brightness / max(_WhiteLevel, _Threshold + 0.0001));
    coverage = originalCoverage * smoothstep(_Threshold,
        _Threshold + max(_Smoothness, 0.0001), brightness);
    rgb = saturate(rgb / max(originalCoverage, 0.0001));
#elif VIDEO_MODE == 4
    // Screen contribution is measured against BOTH non-green channels. This
    // preserves opaque yellows/cyans and supports an imperfect green backing.
    float3 screen = saturate(_GreenScreenColor.rgb);
    float2 excess = (rgb.gg - rgb.rb) / max(screen.gg - screen.rb, float2(0.0001, 0.0001));
    float backing = saturate(min(excess.x, excess.y));
    originalCoverage = RecoverGreenCoverage(rgb, screen, 1.0 - backing, uv);
    backing = 1.0 - originalCoverage;
    rgb = saturate((rgb - backing * screen) / max(originalCoverage, 0.0001));
    // Suppress only the noise floor: do not shrink all text edges or fades.
    coverage = originalCoverage * smoothstep(_GreenTolerance,
        _GreenTolerance + max(_GreenSoftness, 0.0001), originalCoverage);
#endif
    rgb = VideoRendererRGB(rgb);
    float alpha = saturate(source.a) * coverage;
    return float4(rgb * alpha, alpha);
}

float4 SampleKeyedVideo(float2 uv)
{
    // Key texels BEFORE interpolation. Interpolating raw yellow and cyan, for
    // example, produces pale green that a keyer would mistake for backing.
    // Premultiplied interpolation also prevents colored/dark transparent halos.
    float2 size = abs(_MainTex_TexelSize.zw);
    float2 texel = 1.0 / max(size, float2(1.0, 1.0));
    float2 position = uv * size - 0.5;
    float2 baseUV = (floor(position) + 0.5) * texel;
    float2 weight = frac(position);
    float2 low = texel * 0.5;
    float2 high = 1.0 - low;
    float2 uvA = clamp(baseUV, low, high);
    float2 uvB = clamp(baseUV + float2(texel.x, 0), low, high);
    float2 uvC = clamp(baseUV + float2(0, texel.y), low, high);
    float2 uvD = clamp(baseUV + texel, low, high);
    float4 a = KeyVideoPixel(tex2Dlod(_MainTex, float4(uvA, 0, 0)), uvA);
    float4 b = KeyVideoPixel(tex2Dlod(_MainTex, float4(uvB, 0, 0)), uvB);
    float4 c = KeyVideoPixel(tex2Dlod(_MainTex, float4(uvC, 0, 0)), uvC);
    float4 d = KeyVideoPixel(tex2Dlod(_MainTex, float4(uvD, 0, 0)), uvD);
    return lerp(lerp(a, b, weight.x), lerp(c, d, weight.x), weight.y);
}
#endif

float4 VideoFrag(VideoFragment input) : SV_Target
{
#if VIDEO_MODE == 0 || VIDEO_MODE == 4
    return SampleKeyedVideo(input.uv);
#else
    float4 color = tex2D(_MainTex, input.uv);
#if VIDEO_MODE == 2
    color.a = 1.0;
#endif
    return color;
#endif
}
