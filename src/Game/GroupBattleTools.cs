using System;
using HarmonyLib;
using Mortal.Battle;

namespace MortalHelper.Game
{
    // Group battle (Mortal.Battle) helpers. Separate from the duel patches: the
    // duel is turn based (Mortal.Combat) and never shares these types.
    internal static class GroupBattleTools
    {
        internal static bool Is(Character character, EntityType type)
        {
            if (character == null) return false;
            try
            {
                var entity = GameAccess.Field<Entity>(character, "_entity");
                return entity != null && entity.CurrentType == type;
            }
            catch (Exception) { return false; }
        }

        internal static Character Player()
        {
            try
            {
                var manager = GameLevelManager.Instance;
                return manager == null ? null : GameAccess.Field<Character>(manager, "_player");
            }
            catch (Exception) { return null; }
        }
    }

    [HarmonyPatch(typeof(Character), "TakeDamage")]
    internal static class GroupDamagePatch
    {
        private static void Prefix(Character __instance, ref int __0)
        {
            if (__0 <= 0 || __instance.IsDead || !Plugin.CanModifyProgress) return;
            if (GroupBattleTools.Is(__instance, EntityType.Player) && Plugin.Instance.Options.GroupHealth.Value)
                __0 = 0;
            else if (GroupBattleTools.Is(__instance, EntityType.Enemy) && Plugin.Instance.Options.GroupDown.Value &&
                __instance.Health != null)
                __0 = Math.Max(__0, __instance.Health.CurrentHealth);
        }
    }

    [HarmonyPatch(typeof(CharacterMana), "Update")]
    internal static class GroupManaPatch
    {
        private static void Postfix(CharacterMana __instance)
        {
            if (!Plugin.CanModifyProgress || !Plugin.Instance.Options.GroupMana.Value) return;
            var player = GroupBattleTools.Player();
            if (player == null || __instance.gameObject != player.gameObject) return;
            // The setter is private; Modify is the game's own path and keeps the UI
            // and OnManaPctChanged listeners in sync.
            int missing = __instance.MaxMana - __instance.CurrentMana;
            if (missing > 0) __instance.Modify(missing);
        }
    }

    [HarmonyPatch(typeof(SkillButton), "CanProcess")]
    internal static class GroupCooldownPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (Plugin.CanModifyProgress && Plugin.Instance.Options.GroupCooldown.Value) __result = true;
        }
    }

    [HarmonyPatch(typeof(GameLevelManager), "Update")]
    internal static class GroupTimePatch
    {
        private static void Postfix(GameLevelManager __instance)
        {
            if (!Plugin.CanModifyProgress || !Plugin.Instance.Options.GroupFreeze.Value) return;
            try
            {
                int maximum = GameAccess.Field<int>(__instance, "_maxSessionTime");
                int current = GameAccess.Field<int>(__instance, "_currentSessionTime");
                if (current < maximum) GameAccess.Set(__instance, "_currentSessionTime", maximum);
            }
            catch (Exception) { }
        }
    }
}
