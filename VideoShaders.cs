using System;
using System.Reflection;
using UnityEngine;

namespace VideoLayer
{
    internal static class VideoShaders
    {
        private static AssetBundle _bundle;

        internal static Shader Find(string name)
        {
            if (_bundle == null)
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("VideoLayer.Shaders"))
                {
                    if (stream == null)
                        throw new InvalidOperationException("Embedded VideoLayer shader bundle is missing.");
                    var bytes = new byte[checked((int)stream.Length)];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read == 0) throw new InvalidOperationException("Incomplete VideoLayer shader bundle.");
                        offset += read;
                    }
                    _bundle = AssetBundle.LoadFromMemory(bytes);
                }
                if (_bundle == null)
                    throw new InvalidOperationException("Could not load the embedded VideoLayer shader bundle.");
            }

            var shader = _bundle.LoadAsset<Shader>(name);
            if (shader == null || !shader.isSupported)
                throw new InvalidOperationException("VideoLayer shader is missing or unsupported: " + name);
            return shader;
        }
    }
}
