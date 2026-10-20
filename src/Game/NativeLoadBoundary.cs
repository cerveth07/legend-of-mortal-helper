using HarmonyLib;
using Mortal.Core;

namespace MortalHelper.Game
{
    [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData")]
    internal static class NativeLoadBoundary
    {
        private static void Prefix()
        {
            Plugin.Instance?.Panel?.ClearSelection();
            DuelTools.Dialogue.Clear();
            DiceAdapter.Close();
            HintAdapter.Clear();
        }

        // Prefix runs for baseline and native loads. Only a *successful* external
        // native load is allowed to clear a failed experimental recovery.
        private static void Postfix()
        {
            if (!StoryReplay.LoadingBaseline)
                StoryReplay.OnNativeLoadCompleted();
        }
    }
}
