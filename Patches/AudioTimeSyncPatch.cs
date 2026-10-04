using HarmonyLib;

namespace VideoLayer.Patches
{
    [HarmonyPatch(typeof(AudioTimeSyncController), "Awake")]
    internal static class AudioTimeSyncAwakePatch
    {
        [HarmonyPostfix]
        private static void Postfix(AudioTimeSyncController __instance)
        {
            Plugin.Controller?.AttachAudioTimeSync(__instance);
        }
    }

    [HarmonyPatch(typeof(AudioTimeSyncController), nameof(AudioTimeSyncController.StopSong))]
    internal static class AudioTimeSyncStopPatch
    {
        [HarmonyPostfix]
        private static void Postfix(AudioTimeSyncController __instance)
        {
            Plugin.Controller?.StopAndHideAll();
        }
    }

    [HarmonyPatch(typeof(AudioTimeSyncController), "OnDestroy")]
    internal static class AudioTimeSyncOnDestroyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(AudioTimeSyncController __instance)
        {
            Plugin.Controller?.StopAndHideAll();
        }
    }
}
