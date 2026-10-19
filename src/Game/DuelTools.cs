using System.Collections.Generic;
using HarmonyLib;
using Mortal.Combat;

namespace MortalHelper.Game
{
    internal static class DuelTools
    {
        internal static readonly Queue<string> Dialogue = new Queue<string>();
        internal static bool IsPlayer(CombatStatController controller) => Plugin.Active && CombatManager.Instance != null &&
            CombatManager.Instance.PlayerAction != null && CombatManager.Instance.PlayerAction.Stat == controller;
        internal static bool IsEnemy(CombatStatController controller) => Plugin.Active && CombatManager.Instance != null &&
            CombatManager.Instance.EnemyAction != null && CombatManager.Instance.EnemyAction.Stat == controller;
        internal static void AddLine(string speaker, string text)
        {
            if (!Plugin.Active || string.IsNullOrWhiteSpace(text)) return;
            Dialogue.Enqueue((speaker ?? "") + "：" + text);
            while (Dialogue.Count > 500) Dialogue.Dequeue();
        }
    }

    [HarmonyPatch(typeof(CombatStatController), "ModifyHealth")]
    internal static class DuelHealthPatch
    {
        private static void Prefix(CombatStatController __instance, ref int __0)
        {
            if (__0 >= 0 || !Plugin.Active) return;
            if (DuelTools.IsPlayer(__instance) && Plugin.Instance.Options.DuelHealth.Value) __0 = 0;
            else if (DuelTools.IsEnemy(__instance) && Plugin.Instance.Options.DuelDamage.Value && __instance.CurrentHealth > 0)
                __0 = -__instance.CurrentHealth;
        }
    }

    [HarmonyPatch(typeof(CombatStatController), "ModifyStamina")]
    internal static class DuelStaminaPatch
    {
        private static void Prefix(CombatStatController __instance, ref int __0)
        {
            if (__0 < 0 && DuelTools.IsPlayer(__instance) && Plugin.Instance.Options.DuelStamina.Value) __0 = 0;
        }
    }

    [HarmonyPatch(typeof(CombatStatController), "get_DodgeRate")]
    internal static class DuelDodgePatch
    {
        private static void Postfix(CombatStatController __instance, ref float __result)
        {
            if (DuelTools.IsPlayer(__instance) && Plugin.Instance.Options.DuelDodge.Value) __result = 1f;
        }
    }

    [HarmonyPatch(typeof(CombatDialogUI), "Say")]
    internal static class DuelDialoguePatch
    {
        private static void Postfix(string __0, string __1) { DuelTools.AddLine(__0, __1); }
    }

    [HarmonyPatch(typeof(CombatManager), "Awake")]
    internal static class NewDuelPatch
    {
        private static void Postfix() { DuelTools.Dialogue.Clear(); }
    }
}
