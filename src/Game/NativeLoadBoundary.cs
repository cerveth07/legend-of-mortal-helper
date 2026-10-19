using HarmonyLib;
using Mortal.Core;

namespace MortalHelper.Game
{
    [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData")]
    internal static class NativeLoadBoundary
    {
        private static void Prefix()
        {
            Plugin.Instance?.Panel.ClearSelection();
            DuelTools.Dialogue.Clear();
            DiceAdapter.Close();
            HintAdapter.Clear();
        }
    }
}
