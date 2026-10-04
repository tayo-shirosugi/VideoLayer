using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo(IPA.Config.Stores.GeneratedStore.AssemblyVisibilityTarget)]

namespace VideoLayer
{
    public class PluginConfig
    {
        public virtual bool Enabled { get; set; } = true;

        // Current CameraPlus camera -> avatar/head depth + this value.
        public virtual float BackOffsetMeters { get; set; } = 2.5f;

        // Front quad distance from the CameraPlus camera.
        public virtual float FrontDistanceMeters { get; set; } = 0.20f;

        // Slight overscan avoids a 1px edge at extreme FOV/aspect changes.
        public virtual float Overscan { get; set; } = 1.02f;

        // Hard seek when video drifts farther than this from Beat Saber songTime.
        public virtual float SyncThresholdSeconds { get; set; } = 0.10f;

        // Treat songTime as paused after it has not advanced for this long.
        public virtual float PauseDetectionSeconds { get; set; } = 0.12f;

        // Black-key shader is embedded in VideoLayer; no external shader MOD is required.
        public virtual string FrontBlendMode { get; set; } = "ChromaKey";
        public virtual string BackBlendMode { get; set; } = "ChromaKey";

        // Back is intentionally before normal Transparent(3000), Front is near-overlay.
        public virtual int FrontRenderQueue { get; set; } = 3999;
        public virtual int BackRenderQueue { get; set; } = 2499;

        // Decode target. 1920x1080 is the intended first target.
        public virtual int RenderWidth { get; set; } = 1920;
        public virtual int RenderHeight { get; set; } = 1080;

        // VRAM safety limits: prevents extreme VRAM consumption when playing 4K/high-res videos
        public virtual int MaxRenderWidth { get; set; } = 1920;
        public virtual int MaxRenderHeight { get; set; } = 1080;

        // Layer dedicated for VideoLayer quads.
        // Uses Layer 27 (an unused user layer in Beat Saber) to prevent interfering with avatar meshes
        // (which use Layer 3 'OnlyInThirdPerson' and Layer 10 'Avatar'). This ensures mods like
        // AvatarAfterImage can isolate the avatar silhouette without capturing the video layers.
        public virtual int CameraOnlyLayer { get; set; } = 27;

        public virtual bool DebugLog { get; set; } = false;

        public virtual void Changed() { }
        public virtual void OnReload() { }
    }
}
