using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace VideoLayer.Patches
{
    /// <summary>
    /// Guarantees that video playback and quad rendering are cleanly terminated whenever a level finishes,
    /// whether by normal completion, fail, pause menu exit, or restart.
    /// </summary>
    [HarmonyPatch]
    internal static class LevelFinishPatch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var methods = new List<MethodBase>();

            var std = typeof(StandardLevelScenesTransitionSetupDataSO);
            var mStd = std.GetMethod("Finish", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mStd != null) methods.Add(mStd);

            var mission = typeof(MissionLevelScenesTransitionSetupDataSO);
            var mMission = mission.GetMethod("Finish", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mMission != null) methods.Add(mMission);

            var multi = typeof(MultiplayerLevelScenesTransitionSetupDataSO);
            var mMulti = multi.GetMethod("Finish", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mMulti != null) methods.Add(mMulti);

            var pause = typeof(PauseMenuManager);
            var mMenu = pause.GetMethod("MenuButtonPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mMenu != null) methods.Add(mMenu);

            var mRestart = pause.GetMethod("RestartButtonPressed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mRestart != null) methods.Add(mRestart);

            return methods;
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.Controller?.StopAndHideAll();
        }
    }
}
