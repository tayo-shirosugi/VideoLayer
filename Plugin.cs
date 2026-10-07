using System.Reflection;
using HarmonyLib;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using UnityEngine;
using IPALogger = IPA.Logging.Logger;

namespace VideoLayer
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        internal static PluginConfig Config { get; private set; }
        internal static IPALogger Log { get; private set; }
        internal static VideoLayerController Controller { get; private set; }

        private Harmony _harmony;
        private static Config _rawConfig;

        [Init]
        public void Init(Config config, IPALogger logger)
        {
            Log = logger;
            _rawConfig = config;
            Config = config.Generated<PluginConfig>();
        }

        internal static void ReloadConfig()
        {
            try
            {
                _rawConfig?.LoadSync();
                Log.Info($"Config loaded: BackOffset={Config.BackOffsetMeters:F2}m, FrontDistance={Config.FrontDistanceMeters:F2}m");
            }
            catch (System.Exception ex)
            {
                Log.Warn($"Failed to reload config: {ex.Message}");
            }
        }

        internal static void SaveConfig()
        {
            try
            {
                Config?.Changed();
            }
            catch (System.Exception ex)
            {
                Log.Warn($"Failed to save config: {ex.Message}");
            }
        }

        [OnStart]
        public void OnApplicationStart()
        {
            _harmony = new Harmony("com.github.tayo-shirosugi.videolayer");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());

            var go = new GameObject("VideoLayerController");
            Object.DontDestroyOnLoad(go);
            Controller = go.AddComponent<VideoLayerController>();
            Log.Info("VideoLayer 0.1.1 started");

            UI.SettingsController.Initialize();
        }

        [OnExit]
        public void OnApplicationQuit()
        {
            UI.SettingsController.Deinitialize();

            if (Controller)
                Object.Destroy(Controller.gameObject);
            Controller = null;
            _harmony?.UnpatchSelf();
        }
    }
}
