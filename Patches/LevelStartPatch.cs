using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace VideoLayer.Patches
{
    /// <summary>
    /// Capture the exact BeatmapLevel being launched. This runs before GameCore loads,
    /// so front.mp4/back.mp4 can be prepared while the scene is transitioning.
    /// </summary>
    [HarmonyPatch]
    internal static class LevelStartPatch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(StandardLevelScenesTransitionSetupDataSO))
                .Where(m => m.Name == nameof(StandardLevelScenesTransitionSetupDataSO.Init));
        }

        [HarmonyPostfix]
        private static void Postfix(BeatmapLevel beatmapLevel)
        {
            if (beatmapLevel == null || Plugin.Controller == null)
                return;

            Plugin.Controller.PrepareForLevel(beatmapLevel);
        }
    }
}
