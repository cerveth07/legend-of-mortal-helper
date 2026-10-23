using System.Reflection;
using HarmonyLib;
using Mortal.Story;
using UnityEngine;

namespace MortalHelper.Game
{
    internal static class NumericShopRefresh
    {
        private static readonly MethodInfo UpdateTotal = AccessTools.Method(typeof(ShopListPanel), "UpdateTotal");
        // Resolved by name once; a rename must skip this cosmetic refresh, not throw.
        private static bool missingTotalReported;

        // FindObjectsOfTypeAll is a full object scan. The numeric-edit gate asks this several
        // times per frame on the stat and item pages, so the answer is cached per frame.
        private static int scannedFrame = -1;
        private static bool scannedOpen;

        internal static bool Open
        {
            get
            {
                int frame = Time.frameCount;
                if (frame == scannedFrame) return scannedOpen;
                scannedFrame = frame;
                scannedOpen = false;
                foreach (var shop in Resources.FindObjectsOfTypeAll<ShopPanel>())
                    if (shop.isActiveAndEnabled && shop.gameObject.scene.IsValid() && GameAccess.Field<bool>(shop, "_enableUI"))
                    { scannedOpen = true; break; }
                return scannedOpen;
            }
        }
        internal static void Refresh(bool items)
        {
            if (items)
            {
                // Native rows cache the sell limit; rebuild them after inventory edits.
                foreach (var shop in Resources.FindObjectsOfTypeAll<ShopPanel>())
                    if (shop.isActiveAndEnabled && shop.gameObject.scene.IsValid() && GameAccess.Field<bool>(shop, "_enableUI")) shop.Init();
            }
            else if (Open)
            {
                if (UpdateTotal == null)
                {
                    if (!missingTotalReported)
                    {
                        missingTotalReported = true;
                        Plugin.Instance?.Log("[Shop] ShopListPanel.UpdateTotal unavailable; total refresh disabled.");
                    }
                    return;
                }
                // Purchase affordability is live, but the total's color is cached.
                foreach (var list in Resources.FindObjectsOfTypeAll<ShopListPanel>())
                    if (list.isActiveAndEnabled && list.gameObject.scene.IsValid()) UpdateTotal.Invoke(list, null);
            }
        }
    }
}
