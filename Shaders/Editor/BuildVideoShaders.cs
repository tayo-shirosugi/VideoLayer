using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class BuildVideoShaders
{
    private static readonly string[] Modes = { "ChromaKey", "GreenKey", "Opaque", "PureAdditive" };

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
                if (mode == "ChromaKey")
                {
                    Check(material, Color.black, Color.white, Color.white, 1);
                    Check(material, Color.red, Color.white, Color.red, 0);
                }
                else if (mode == "GreenKey")
                {
                    CheckGreen(material, new Color(0.2f, 0.4f, 0.6f, 0.4f));
                }
                UnityEngine.Object.DestroyImmediate(material);
            }
            var bundle = (AssetBundle)loader.GetField("_bundle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            if (bundle == null || bundle.GetAllAssetNames().Length != Modes.Length)
                throw new Exception("Embedded plugin shader bundle contains unexpected assets.");
            Debug.Log("VideoLayer compiled DLL verification passed: actual plugin loader; black/green keying, text edges and Bloom masks verified.");
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
                material.SetFloat("_Threshold", 0.05f);
                material.SetFloat("_Smoothness", 0.05f);
                foreach (var background in new[] { Color.white, new Color(0, 0, 0, 0), new Color(0.2f, 0.4f, 0.6f, 0.4f) })
                {
                    if (mode == "ChromaKey")
                    {
                        Check(material, Color.black, background, background, background.a);
                        Check(material, new Color(0.025f, 0.025f, 0.025f, 1), background, background, background.a);
                        Check(material, Color.red, background, Color.red, 0);
                        Check(material, new Color(0.075f, 0.075f, 0.075f, 1), background,
                            new Color(0.075f, 0.075f, 0.075f, 1) * 0.5f + background * 0.5f, background.a * 0.5f);
                    }
                    else if (mode == "GreenKey")
                        CheckGreen(material, background);
                    else if (mode == "Opaque")
                        Check(material, new Color(1, 0, 0, 0), background, Color.red, 0);
                    else
                        Check(material, new Color(0.2f, 0.1f, 0, 0.5f), background,
                            new Color(background.r + 0.1f, background.g + 0.05f, background.b, 1), background.a);
                }
                UnityEngine.Object.DestroyImmediate(material);
            }
            bundle.Unload(true);
            Debug.Log("VideoLayer shader GPU verification passed: 57 color and Bloom mask cases; all four embedded shaders loaded.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void CheckGreen(Material material, Color background)
    {
        Check(material, Color.green, background, background, background.a);
        Check(material, new Color(0.01f, 0.98f, 0.01f, 1), background, background, background.a);
        foreach (var text in new[] { Color.black, Color.white, Color.red, Color.blue, Color.magenta, new Color(1, 1, 0, 1), Color.cyan, new Color(0.1f, 0.1f, 0.1f, 1) })
            Check(material, text, background, text, 0);
        // Source pixels are a 50% text edge composited onto pure green.
        float coverage = (0.5f - 0.05f) / 0.95f;
        Check(material, new Color(0.5f, 1, 0.5f, 1), background,
            Color.white * coverage + background * (1 - coverage), background.a * (1 - coverage));
        Check(material, new Color(0, 0.5f, 0, 1), background,
            Color.black * coverage + background * (1 - coverage), background.a * (1 - coverage));
        Check(material, new Color(1, 1, 1, 0.5f), background,
            Color.white * 0.5f + background * 0.5f, background.a * 0.5f);
    }

    private static void Check(Material material, Color source, Color background, Color expected, float expectedBloomMask)
    {
        var input = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        input.SetPixel(0, 0, source);
        input.Apply();
        var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        target.Create();
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        GL.Clear(true, true, background);
        Graphics.Blit(input, target, material);
        var output = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
        output.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
        output.Apply();
        var actual = output.GetPixel(4, 4);
        RenderTexture.active = previous;
        target.Release();
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(input);
        UnityEngine.Object.DestroyImmediate(output);
        if (Mathf.Abs(actual.r - expected.r) > 0.015f || Mathf.Abs(actual.g - expected.g) > 0.015f || Mathf.Abs(actual.b - expected.b) > 0.015f || Mathf.Abs(actual.a - expectedBloomMask) > 0.015f)
            throw new Exception(material.shader.name + " produced " + actual + "; expected RGB " + expected + " and Bloom mask " + expectedBloomMask + " on " + background);
    }
}
