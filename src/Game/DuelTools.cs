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
            // Zero damage mirrors the kill switch: the enemy cannot lose health at
            // all (damage over time included), which keeps the fight going.
            else if (DuelTools.IsEnemy(__instance) && Plugin.Instance.Options.DuelZeroDamage.Value) __0 = 0;
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

    // The hit roll multiplies the attacker HitRate by the defender dodge, so a high
    // dodge still avoids a lethal strike. While the kill switch is on, the three
    // resolution handlers receive an explicit hit for the player's attack.
    internal static class DuelForceHit
    {
        internal static bool Wanted(CombatActionController attacker)
        {
            var options = Plugin.Instance.Options;
            if (!Plugin.Active || (!options.DuelDamage.Value && !options.DuelAlwaysHit.Value)) return false;
            var manager = CombatManager.Instance;
            return manager != null && manager.PlayerAction != null && attacker == manager.PlayerAction;
        }
    }

    [HarmonyPatch(typeof(CombatManager), "AttackActionHandle")]
    internal static class ForceHitAttackPatch
    {
        private static void Prefix(CombatActionController __0, ref bool __3)
        {
            if (DuelForceHit.Wanted(__0)) __3 = true;
        }
    }

    [HarmonyPatch(typeof(CombatManager), "WeaponActionHandle")]
    internal static class ForceHitWeaponPatch
    {
        private static void Prefix(CombatActionController __0, ref bool __3)
        {
            if (DuelForceHit.Wanted(__0)) __3 = true;
        }
    }

    [HarmonyPatch(typeof(CombatManager), "UltimateActionHandle")]
    internal static class ForceHitUltimatePatch
    {
        private static void Prefix(CombatActionController __0, ref bool __4)
        {
            if (DuelForceHit.Wanted(__0)) __4 = true;
        }
    }

    // GetBaseDodgeRate is the defender hit multiplier: 0 means never hit, 1 means no
    // dodge. Raising the player's dodge zeroes it; forcing a hit raises the enemy's
    // multiplier to 1 and never lowers it.
    [HarmonyPatch(typeof(CombatActionController), "GetBaseDodgeRate")]
    internal static class DuelHitMultiplierPatch
    {
        private static void Postfix(CombatActionController __instance, ref float __result)
        {
            if (!Plugin.Active || __instance == null || __instance.Stat == null) return;
            var options = Plugin.Instance.Options;
            if (DuelTools.IsPlayer(__instance.Stat))
            {
                if (options.DuelDodge.Value) __result = 0f;
            }
            else if (DuelTools.IsEnemy(__instance.Stat) && (options.DuelAlwaysHit.Value || options.DuelDamage.Value))
            {
                if (__result < 1f) __result = 1f;
            }
        }
    }

    // Winning the action comparison makes the enemy fail its action, so the round
    // resolves as a single player strike: the player effectively goes first.
    [HarmonyPatch(typeof(CombatActionController), "CompareAction")]
    internal static class DuelFirstStrikePatch
    {
        private static void Postfix(CombatActionController __instance, CombatActionResult __0, CombatActionResult __1,
            CombatActionController __2)
        {
            if (!Plugin.Active || __instance == null || __instance.Stat == null || __2 == null || __2.Stat == null) return;
            var options = Plugin.Instance.Options;
            if (!options.DuelFirstStrike.Value && !options.DuelDamage.Value) return;
            if (__0 == null || __1 == null) return;
            if (DuelTools.IsPlayer(__instance.Stat) && DuelTools.IsEnemy(__2.Stat))
            { __0.IsSuccess = true; __1.IsSuccess = false; }
            else if (DuelTools.IsEnemy(__instance.Stat) && DuelTools.IsPlayer(__2.Stat))
            { __1.IsSuccess = true; __0.IsSuccess = false; }
        }
    }
}
