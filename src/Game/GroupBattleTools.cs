using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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

    // Protect the final health deduction: TakeDamage can override a zero input,
    // and damage-over-time can reach ModifyHealth without calling TakeDamage.
    [HarmonyPatch(typeof(CharacterHealth), "ModifyHealth")]
    internal static class GroupAllyHealthPatch
    {
        private static void Prefix(CharacterHealth __instance, ref int __0)
        {
            if (__0 >= 0 || __instance.CurrentHealth <= 0 || !Plugin.CanModifyProgress ||
                !Plugin.Instance.Options.GroupAllyHealth.Value) return;
            var character = __instance.GetComponent<Character>();
            if (GroupBattleTools.Is(character, EntityType.Friend)) __0 = 0;
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

    [HarmonyPatch(typeof(SkillButton), "Update")]
    internal static class GroupCooldownPatch
    {
        private static void Prefix(SkillButton __instance)
        {
            if (!Plugin.CanModifyProgress || !Plugin.Instance.Options.GroupCooldown.Value) return;
            // Runs for every skill button every frame; reflection writes box and re-set the
            // value, so only write when it would actually change.
            if (GameAccess.Field<float>(__instance, "_currentCooldownTime") != 0f)
                GameAccess.Set(__instance, "_currentCooldownTime", 0f);
            // Native Update still checks pause/readiness/game-over/event/active and
            // completes the cooldown through its own UI and state transitions.
        }
    }

    [HarmonyPatch]
    internal static class GroupTimePatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(GameLevelManager), "StartSessionTime"));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var elapsed = AccessTools.Field(typeof(GameLevelManager), "_currentSessionTime");
            var setter = AccessTools.Method(typeof(GroupTimePatch), "SetElapsed");
            int replaced = 0;
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Stfld && Equals(instruction.operand, elapsed))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = setter;
                    replaced++;
                }
                yield return instruction;
            }
            // Degrade this single feature instead of throwing out of PatchAll, which would
            // uninstall every patch including the recovery ones.
            if (replaced != 1) Unsupported = true;
        }

        internal static bool Unsupported;

        private static void SetElapsed(GameLevelManager manager, int value)
        {
            int previous = GameAccess.Field<int>(manager, "_currentSessionTime");
            if (Plugin.CanModifyProgress && Plugin.Instance.Options.GroupFreeze.Value &&
                !manager.IsGameOver && value > previous)
            {
                int maximum = GameAccess.Field<int>(manager, "_maxSessionTime");
                GameAccess.Set(manager, "_maxSessionTime", (int)Math.Min(int.MaxValue, (long)maximum + value - previous));
            }
            // Preserve elapsed time so timed battle events still run once each.
            GameAccess.Set(manager, "_currentSessionTime", value);
        }
    }
}
