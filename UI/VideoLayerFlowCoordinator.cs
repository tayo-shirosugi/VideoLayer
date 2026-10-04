using BeatSaberMarkupLanguage;
using HMUI;

namespace VideoLayer.UI
{
    public class VideoLayerFlowCoordinator : FlowCoordinator
    {
        private VideoLayerViewController _viewController;

        protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
        {
            if (firstActivation)
            {
                SetTitle("VideoLayer", ViewController.AnimationType.In);
                showBackButton = true;

                if (_viewController == null)
                {
                    _viewController = BeatSaberUI.CreateViewController<VideoLayerViewController>();
                }

                ProvideInitialViewControllers(_viewController);
            }
        }

        protected override void BackButtonWasPressed(ViewController topViewController)
        {
            BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this, ViewController.AnimationDirection.Horizontal);
        }
    }
}
