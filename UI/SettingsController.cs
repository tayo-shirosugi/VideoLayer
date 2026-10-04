using System;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.MenuButtons;
using BeatSaberMarkupLanguage.Util;

namespace VideoLayer.UI
{
    public static class SettingsController
    {
        private static MenuButton _menuButton;
        private static VideoLayerFlowCoordinator _flowCoordinator;
        private static bool _registered = false;

        public static void Initialize()
        {
            try
            {
                MainMenuAwaiter.MainMenuInitializing += OnMainMenuInitializing;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Failed to hook MainMenuInitializing: {ex.Message}");
                TryRegisterMenu();
            }
        }

        private static void OnMainMenuInitializing()
        {
            TryRegisterMenu();
        }

        private static void TryRegisterMenu()
        {
            if (_registered) return;

            try
            {
                _menuButton = new MenuButton("VideoLayer", "Configure VideoLayer (Front/Back MP4)", OnMenuButtonClicked);
                MenuButtons.Instance?.RegisterButton(_menuButton);
                _registered = true;
                Plugin.Log.Info("Registered VideoLayer button into Main Menu 'MODS' panel.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Failed to register VideoLayer menu button: {ex.Message}");
            }
        }

        private static void OnMenuButtonClicked()
        {
            try
            {
                if (_flowCoordinator == null)
                {
                    _flowCoordinator = BeatSaberUI.CreateFlowCoordinator<VideoLayerFlowCoordinator>();
                }
                BeatSaberUI.MainFlowCoordinator.PresentFlowCoordinator(_flowCoordinator);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Failed to present VideoLayerFlowCoordinator: {ex.Message}");
            }
        }

        public static void Deinitialize()
        {
            try
            {
                MainMenuAwaiter.MainMenuInitializing -= OnMainMenuInitializing;

                if (_menuButton != null)
                {
                    MenuButtons.Instance?.UnregisterButton(_menuButton);
                    _menuButton = null;
                }
                _registered = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Failed to unregister VideoLayer menu button: {ex.Message}");
            }
        }
    }
}
