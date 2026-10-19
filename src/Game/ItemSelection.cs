using HarmonyLib;
using Mortal.Core;

namespace MortalHelper.Game
{
    // Mirrors the previous panel's flow: pick an item in the game's own inventory
    // panel, then edit that exact item from the helper. The game selection is only
    // observed, never changed.
    internal static class ItemSelection
    {
        internal static ItemData Selected { get; private set; }

        internal static void Select(IGameItem item)
        {
            Selected = item as ItemData;
        }

        internal static void Clear() { Selected = null; }
    }

    [HarmonyPatch(typeof(ItemStatPanel), "OnItemSelected")]
    internal static class InventorySelectionPatch
    {
        private static void Postfix(IGameItem __0) { ItemSelection.Select(__0); }
    }

    [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData")]
    internal static class InventoryResetPatch
    {
        private static void Prefix() { ItemSelection.Clear(); }
    }
}
