using System.Reflection;
using HarmonyLib;
using Mortal.Story;
using UnityEngine;

namespace MortalHelper.Game
{
    internal static class NumericShopRefresh
    {
        private static readonly MethodInfo UpdateTotal = AccessTools.Method(typeof(ShopListPanel), "UpdateTotal");

        internal static bool Open
        {
            get
            {
                foreach (var shop in Resources.FindObjectsOfTypeAll<ShopPanel>())
                    if (shop.isActiveAndEnabled && shop.gameObject.scene.IsValid() && GameAccess.Field<bool>(shop, "_enableUI")) return true;
                return false;
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
                // Purchase affordability is live, but the total's color is cached.
                foreach (var list in Resources.FindObjectsOfTypeAll<ShopListPanel>())
                    if (list.isActiveAndEnabled && list.gameObject.scene.IsValid()) UpdateTotal.Invoke(list, null);
            }
        }
    }
}
