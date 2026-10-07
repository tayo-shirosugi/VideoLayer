using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BuildVideoShaders
{
    private static readonly string[] Modes = { "ChromaKey", "GreenKey", "Opaque", "PureAdditive" };
    private static int _checks;

    public static void VerifyEmbeddedPlugin()
    {
        try
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, "-videoLayerPluginPath");
            if (index < 0 || index + 1 >= arguments.Length) throw new Exception("Plugin DLL path is missing.");
            var assembly = Assembly.Load(File.ReadAllBytes(arguments[index + 1]));
            var loader = assembly.GetType("VideoLayer.VideoShaders", true);
            var find = loader.GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (var mode in Modes)
            {
                var shader = (Shader)find.Invoke(null, new object[] { "VideoLayer/" + mode });
                if (shader.name != "VideoLayer/" + mode || !shader.isSupported)
                    throw new Exception("Embedded plugin shader unavailable: " + mode);
                var material = new Material(shader);
                VerifyMaterial(material, mode);
                UnityEngine.Object.DestroyImmediate(material);
            }
            var bundle = (AssetBundle)loader.GetField("_bundle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (bundle == null || bundle.GetAllAssetNames().Length != Modes.Length)
                throw new Exception("Embedded plugin shader bundle contains unexpected assets.");
            Debug.Log("VideoLayer compiled DLL verification passed: " + _checks + " GPU pixel checks through the actual plugin loader.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void BuildAndVerify()
    {
        try
        {
            AssetDatabase.Refresh();
            foreach (var mode in Modes)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/" + mode + ".shader");
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    throw new Exception("Shader compilation failed: " + mode);
            }
            Directory.CreateDirectory("Output");
            var build = new AssetBundleBuild
            {
                assetBundleName = "videolayer.shaders",
                assetNames = Modes.Select(m => "Assets/Shaders/" + m + ".shader").ToArray(),
                addressableNames = Modes.Select(m => "VideoLayer/" + m).ToArray()
            };
            if (BuildPipeline.BuildAssetBundles("Output", new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                BuildTarget.StandaloneWindows64) == null)
                throw new Exception("Shader bundle build failed.");

            var bundle = AssetBundle.LoadFromMemory(File.ReadAllBytes("Output/videolayer.shaders"));
            if (bundle == null) throw new Exception("Shader bundle failed to load.");
            if (bundle.GetAllAssetNames().Length != Modes.Length)
                throw new Exception("Shader bundle contains unexpected assets.");
            foreach (var mode in Modes)
            {
                var shader = bundle.LoadAsset<Shader>("VideoLayer/" + mode);
                if (shader == null || !shader.isSupported)
                    throw new Exception("Bundled shader unavailable: " + mode);
                var material = new Material(shader);
                VerifyMaterial(material, mode);
                UnityEngine.Object.DestroyImmediate(material);
            }
            bundle.Unload(true);
            Debug.Log("VideoLayer shader GPU verification passed: " + _checks + " color, edge interpolation and Bloom mask checks; display and linear inputs; all four embedded shaders loaded.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void VerifyMaterial(Material material, string mode)
    {
        foreach (var background in new[] { Color.white, Color.clear, new Color(0.2f, 0.4f, 0.6f, 0.4f) })
        {
            if (mode == "Opaque")
                Check(material, new Color(1, 0, 0, 0), background, Color.red, 0);
            else if (mode == "PureAdditive")
                Check(material, new Color(0.2f, 0.1f, 0, 0.5f), background,
                    new Color(background.r + 0.1f, background.g + 0.05f, background.b, 1), background.a);
            else
            {
                foreach (bool linear in new[] { false, true })
                {
                    material.SetFloat("_KeyInLinearSpace", linear ? 1 : 0);
                    if (mode == "ChromaKey") VerifyBlack(material, background, linear);
                    else VerifyGreen(material, background, linear);
                    CheckSpatial(material, background, linear, mode == "GreenKey");
                }
            }
        }
    }

    private static void VerifyBlack(Material material, Color background, bool linear)
    {
        var colors = new[] { Color.white, Color.red, Color.blue, Color.green, Color.magenta, Color.yellow, Color.cyan };
        foreach (float level in new[] { 1f, 0.6f })
        {
            material.SetFloat("_WhiteLevel", level);
            foreach (var foreground in colors)
            foreach (float alpha in new[] { 0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.075f, 0.1f, 0.25f, 0.5f, 0.75f, 1f })
            {
                var source = foreground * (alpha * level);
                source.a = 1;
                float opacity = alpha * NoiseWeight(alpha * level, 0.02f, 0.02f);
                CheckComposite(material, source, foreground * level, opacity, background, linear);
            }
        }
        material.SetFloat("_WhiteLevel", 1);
        CheckComposite(material, new Color(0.5f, 0.5f, 0.5f, 0.4f), Color.white, 0.2f, background, linear);
        CheckComposite(material, new Color(1, 0, 0, 0), Color.red, 0, background, linear);
        // An elevated compression floor can be removed without changing mid-edges.
        material.SetFloat("_Threshold", 0.08f);
        material.SetFloat("_Smoothness", 0.04f);
        CheckComposite(material, new Color(0.07f, 0.07f, 0.07f, 1), Color.white, 0, background, linear);
        CheckComposite(material, new Color(0.5f, 0.5f, 0.5f, 1), Color.white, 0.5f, background, linear);
        material.SetFloat("_Threshold", 0.02f);
        material.SetFloat("_Smoothness", 0.02f);
    }

    private static void VerifyGreen(Material material, Color background, bool linear)
    {
        var colors = new[] { Color.black, Color.white, Color.red, Color.blue, Color.magenta, Color.yellow, Color.cyan,
            new Color(0.1f, 0.1f, 0.1f, 1), new Color(0.5f, 0.5f, 0.5f, 1) };
        foreach (var screen in new[] { Color.green, new Color(0.03f, 0.9f, 0.015f, 1) })
        {
            material.SetVector("_GreenScreenColor", new Vector4(screen.r, screen.g, screen.b, 0));
            foreach (var foreground in new[] { Color.black, Color.white, new Color(0.1f, 0.1f, 0.1f, 1) })
            foreach (float alpha in new[] { 0f, 0.01f, 0.02f, 0.03f, 0.04f, 0.06f, 0.1f, 0.25f, 0.5f, 0.75f, 1f })
            {
                // Independent oracle: flatten a known foreground/coverage onto
                // the known screen, then require that foreground/coverage back.
                var source = Color.Lerp(screen, foreground, alpha);
                CheckComposite(material, source, foreground,
                    alpha * NoiseWeight(alpha, 0.03f, 0.03f), background, linear);
            }
            foreach (var foreground in colors)
            {
                CheckComposite(material, foreground, foreground, 1, background, linear);
                foreach (float alpha in new[] { 0.1f, 0.25f, 0.5f, 0.75f })
                    CheckColoredEdge(material, foreground, screen, alpha, background, linear);
            }
        }
        material.SetVector("_GreenScreenColor", new Vector4(0, 1, 0, 0));
        CheckComposite(material, new Color(0.01f, 0.98f, 0.01f, 1), Color.black, 0, background, linear);
        CheckComposite(material, new Color(1, 1, 1, 0.4f), Color.white, 0.4f, background, linear);
        CheckComposite(material, new Color(0.5f, 1, 0.5f, 0.4f), Color.white, 0.2f, background, linear);
        CheckComposite(material, new Color(1, 0, 0, 0), Color.red, 0, background, linear);
        material.SetFloat("_GreenTolerance", 0.08f);
        material.SetFloat("_GreenSoftness", 0.04f);
        CheckComposite(material, new Color(0.07f, 1, 0.07f, 1), Color.white, 0, background, linear);
        CheckComposite(material, new Color(0.5f, 1, 0.5f, 1), Color.white, 0.5f, background, linear);
        material.SetFloat("_GreenTolerance", 0.03f);
        material.SetFloat("_GreenSoftness", 0.03f);
        CheckCodecEdge(material, Color.red, new Color(0.52f, 0.52f, 0, 1), background, linear);
        CheckCodecEdge(material, Color.magenta, new Color(0.52f, 0.54f, 0.52f, 1), background, linear);
        material.SetFloat("_GreenEdgeRecovery", 0);
        CheckComposite(material, new Color(0.5f, 0.5f, 0, 1), new Color(0.5f, 0.5f, 0, 1), 1, background, linear);
        material.SetFloat("_GreenEdgeRecovery", 1);
    }

    private static float NoiseWeight(float value, float floor, float transition)
    {
        float t = Mathf.Clamp01((value - floor) / transition);
        return t * t * (3 - 2 * t);
    }

    private static void CheckColoredEdge(Material material, Color foreground, Color screen, float alpha, Color background, bool linear)
    {
        var input = new Texture2D(3, 3, TextureFormat.RGBAFloat, false, true);
        input.SetPixels(Enumerable.Repeat(RendererColor(foreground, linear), 9).ToArray());
        input.SetPixel(1, 1, RendererColor(Color.Lerp(screen, foreground, alpha), linear));
        input.Apply();
        var output = Render(material, input, background, 3, 3);
        AssertPixel(material, output.GetPixel(1, 1), RendererColor(foreground, linear) * alpha + background * (1 - alpha),
            background.a * (1 - alpha), background);
        UnityEngine.Object.DestroyImmediate(output);
        UnityEngine.Object.DestroyImmediate(input);
    }

    private static void CheckCodecEdge(Material material, Color foreground, Color encoded, Color background, bool linear)
    {
        var input = new Texture2D(3, 3, TextureFormat.RGBAFloat, false, true);
        input.SetPixels(Enumerable.Repeat(RendererColor(foreground, linear), 9).ToArray());
        input.SetPixel(1, 1, RendererColor(encoded, linear));
        input.Apply();
        var output = Render(material, input, background, 3, 3);
        AssertPixel(material, output.GetPixel(1, 1), RendererColor(foreground, linear) * 0.5f + background * 0.5f,
            background.a * 0.5f, background);
        UnityEngine.Object.DestroyImmediate(output);
        UnityEngine.Object.DestroyImmediate(input);
    }

    private static Color RendererColor(Color display, bool linear)
    {
        return linear ? display.linear : display;
    }

    private static void CheckComposite(Material material, Color source, Color foreground, float opacity, Color background, bool linear)
    {
        Check(material, RendererColor(source, linear), background,
            RendererColor(foreground, linear) * opacity + background * (1 - opacity), background.a * (1 - opacity));
    }

    private static void CheckSpatial(Material material, Color background, bool linear, bool green)
    {
        // Test color transitions, subpixel strokes, source alpha, and image borders.
        // Adjacent opaque yellow/cyan must NEVER turn into transparent white.
        var foregrounds = green
            ? new[] { Color.yellow, Color.cyan, Color.white, Color.black }
            : new[] { Color.red, Color.blue, Color.white, Color.green };
        var opacities = new[] { 1f, 1f, 0.25f, 0f };
        var pixels = new Color[4];
        var premultiplied = new Color[4];
        for (int i = 0; i < 4; i++)
        {
            pixels[i] = RendererColor(Color.Lerp(green ? Color.green : Color.black, foregrounds[i], opacities[i]), linear);
            premultiplied[i] = RendererColor(foregrounds[i], linear) * opacities[i];
            premultiplied[i].a = opacities[i];
        }
        var input = new Texture2D(2, 2, TextureFormat.RGBAFloat, false, true);
        input.SetPixels(pixels);
        input.Apply();
        var output = Render(material, input, background, 8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
        {
            float u = Mathf.Clamp((x + 0.5f) / 8, 0.25f, 0.75f) * 2 - 0.5f;
            float v = Mathf.Clamp((y + 0.5f) / 8, 0.25f, 0.75f) * 2 - 0.5f;
            var sample = Color.Lerp(Color.Lerp(premultiplied[0], premultiplied[1], u),
                Color.Lerp(premultiplied[2], premultiplied[3], u), v);
            AssertPixel(material, output.GetPixel(x, y), sample + background * (1 - sample.a), background.a * (1 - sample.a), background);
        }
        UnityEngine.Object.DestroyImmediate(output);
        UnityEngine.Object.DestroyImmediate(input);
    }

    private static void Check(Material material, Color source, Color background, Color expected, float expectedBloomMask)
    {
        var input = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        input.SetPixel(0, 0, source);
        input.Apply();
        var output = Render(material, input, background, 8, 8);
        AssertPixel(material, output.GetPixel(4, 4), expected, expectedBloomMask, background);
        UnityEngine.Object.DestroyImmediate(input);
        UnityEngine.Object.DestroyImmediate(output);
    }

    private static Texture2D Render(Material material, Texture input, Color background, int width, int height)
    {
        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true, true, background);
        Graphics.Blit(input, target, material);
        var output = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
        output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        output.Apply();
        RenderTexture.active = previous;
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        return output;
    }

    private static void AssertPixel(Material material, Color actual, Color expected, float expectedBloomMask, Color background)
    {
        _checks++;
        if (Mathf.Abs(actual.r - expected.r) > 0.005f || Mathf.Abs(actual.g - expected.g) > 0.005f || Mathf.Abs(actual.b - expected.b) > 0.005f || Mathf.Abs(actual.a - expectedBloomMask) > 0.005f)
            throw new Exception(material.shader.name + " produced " + actual + "; expected RGB " + expected + " and Bloom mask " + expectedBloomMask + " on " + background);
    }
}
