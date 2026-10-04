using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.ViewControllers;
using UnityEngine;

namespace VideoLayer.UI
{
    public class VideoLayerViewController : BSMLResourceViewController
    {
        public override string ResourceName => "VideoLayer.UI.settings.bsml";

        [UIValue("enableButtonText")]
        public string EnableButtonText { get; set; } = "ON";

        [UIValue("backDistanceText")]
        public string BackDistanceText { get; set; } = "2.50";

        [UIValue("frontDistanceText")]
        public string FrontDistanceText { get; set; } = "0.20";

        [UIAction("#post-parse")]
        public void PostParse()
        {
            RefreshTexts();
        }

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
            RefreshTexts();
        }

        [UIAction("toggleEnable")]
        public void ToggleEnable()
        {
            if (Plugin.Config == null) return;
            Plugin.Config.Enabled = !Plugin.Config.Enabled;
            if (!Plugin.Config.Enabled)
            {
                Plugin.Controller?.StopAndHideAll();
            }
            Plugin.SaveConfig();
            RefreshTexts();
        }

        [UIAction("incBack")]
        public void IncBack()
        {
            if (Plugin.Config == null) return;
            Plugin.Config.BackOffsetMeters = Mathf.Min(15.0f, (float)Math.Round(Plugin.Config.BackOffsetMeters + 0.25f, 2));
            Plugin.SaveConfig();
            RefreshTexts();
        }

        [UIAction("decBack")]
        public void DecBack()
        {
            if (Plugin.Config == null) return;
            Plugin.Config.BackOffsetMeters = Mathf.Max(0.5f, (float)Math.Round(Plugin.Config.BackOffsetMeters - 0.25f, 2));
            Plugin.SaveConfig();
            RefreshTexts();
        }

        [UIAction("incFront")]
        public void IncFront()
        {
            if (Plugin.Config == null) return;
            Plugin.Config.FrontDistanceMeters = Mathf.Min(2.0f, (float)Math.Round(Plugin.Config.FrontDistanceMeters + 0.05f, 2));
            Plugin.SaveConfig();
            RefreshTexts();
        }

        [UIAction("decFront")]
        public void DecFront()
        {
            if (Plugin.Config == null) return;
            Plugin.Config.FrontDistanceMeters = Mathf.Max(0.05f, (float)Math.Round(Plugin.Config.FrontDistanceMeters - 0.05f, 2));
            Plugin.SaveConfig();
            RefreshTexts();
        }

        public void RefreshTexts()
        {
            if (Plugin.Config != null)
            {
                EnableButtonText = Plugin.Config.Enabled ? "ON" : "OFF";
                BackDistanceText = Plugin.Config.BackOffsetMeters.ToString("0.00");
                FrontDistanceText = Plugin.Config.FrontDistanceMeters.ToString("0.00");
            }
            NotifyPropertyChanged(nameof(EnableButtonText));
            NotifyPropertyChanged(nameof(BackDistanceText));
            NotifyPropertyChanged(nameof(FrontDistanceText));
        }
    }
}
