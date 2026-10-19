using System.Collections.Generic;
using HarmonyLib;
using Mortal.Core;
using Mortal.Free;

namespace MortalHelper.Game
{
    // Progression conveniences expressed as patches on the game's own panels.
    // Every switch is off by default and every hook only stashes and restores a
    // value the game itself owns, so nothing here writes saves.
    internal static class ProgressionState
    {
        internal static int MartialPoint = -1;
        internal static int CraftPoint = -1;
        internal static int CraftMoney = -1;

        internal static bool FreeSkill => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.SkillFree.Value;
        internal static bool FreeForge => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.ForgeFree.Value;
        internal static bool EndlessActions => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.UnlimitedActions.Value;
        internal static bool MaxAffection => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.MaxAffection.Value;
        internal static bool FastSkill => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.FastSkill.Value;
        internal static bool FastForge => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.FastForge.Value;

        private static GameStat Stat(object panel, string name)
        {
            try { return GameAccess.Field<GameStat>(panel, name); }
            catch (System.Exception) { return null; }
        }

        internal static void StashSkill(MartialPanel panel)
        {
            var stat = Stat(panel, "_martialPoint");
            MartialPoint = stat == null ? -1 : stat.Value;
        }

        internal static void RestoreSkill(MartialPanel panel)
        {
            if (MartialPoint < 0) return;
            var stat = Stat(panel, "_martialPoint");
            if (stat != null && stat.Value != MartialPoint) stat.SetValue(MartialPoint);
            MartialPoint = -1;
        }

        internal static void StashForge(UpgradePanel panel)
        {
            var point = Stat(panel, "_pointData");
            var money = Stat(panel, "_moneyData");
            CraftPoint = point == null ? -1 : point.Value;
            CraftMoney = money == null ? -1 : money.Value;
        }

        internal static void RestoreForge(UpgradePanel panel)
        {
            var point = Stat(panel, "_pointData");
            var money = Stat(panel, "_moneyData");
            if (CraftPoint >= 0 && point != null && point.Value != CraftPoint) point.SetValue(CraftPoint);
            if (CraftMoney >= 0 && money != null && money.Value != CraftMoney) money.SetValue(CraftMoney);
            CraftPoint = -1; CraftMoney = -1;
        }
    }

    [HarmonyPatch(typeof(MartialPanel), "HandleLearnNextItem")]
    internal static class SkillSpendPatch
    {
        private static void Prefix(MartialPanel __instance)
        {
            if (ProgressionState.FreeSkill) ProgressionState.StashSkill(__instance);
        }
        private static System.Exception Finalizer(MartialPanel __instance, System.Exception __exception)
        {
            if (ProgressionState.FreeSkill) ProgressionState.RestoreSkill(__instance);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MartialPanel), "CanUseLearnButton")]
    internal static class SkillButtonPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ProgressionState.FreeSkill) __result = true;
        }
    }

    [HarmonyPatch(typeof(UpgradePanel), "HandleUpgradeNextItem")]
    internal static class ForgeSpendPatch
    {
        private static void Prefix(UpgradePanel __instance)
        {
            if (ProgressionState.FreeForge) ProgressionState.StashForge(__instance);
        }
        private static System.Exception Finalizer(UpgradePanel __instance, System.Exception __exception)
        {
            if (ProgressionState.FreeForge) ProgressionState.RestoreForge(__instance);
            return __exception;
        }
    }

    // Fast levelling only rewrites the point amount handed to the game's own
    // Learn / Upgrade call, so one action finishes the current level while level
    // rewards and conditions still run natively.
    [HarmonyPatch(typeof(Book), "Learn")]
    internal static class FastSkillPatch
    {
        private static void Prefix(Book __instance, ref int __0)
        {
            if (!ProgressionState.FastSkill || __0 <= 0 || __instance.IsMaxLevel) return;
            var data = __instance.GetCurrentMartialData();
            if (data == null) return;
            long remaining = (long)data.Points - __instance.CurrentLevelPoints;
            if (remaining > 0) __0 = (int)System.Math.Min(int.MaxValue, remaining);
        }
    }

    [HarmonyPatch(typeof(UpgradeItemData), "Upgrade")]
    internal static class FastForgePatch
    {
        private static void Prefix(UpgradeItemData __instance, ref int __0)
        {
            if (!ProgressionState.FastForge || __0 <= 0 || __instance.IsMaxLevel) return;
            var level = __instance.GetCurrentItemLevel();
            if (level == null) return;
            long remaining = (long)level.LevelPoints - __instance.CurrentPoints;
            if (remaining > 0) __0 = (int)System.Math.Min(int.MaxValue, remaining);
        }
    }

    [HarmonyPatch(typeof(PositionController), "HasActionPoint")]
    internal static class ActionPointPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ProgressionState.EndlessActions) __result = true;
        }
    }

    [HarmonyPatch(typeof(RelationshipStat), "SetActive")]
    internal static class AffectionActivatePatch
    {
        private static void Postfix(RelationshipStat __instance)
        {
            if (!ProgressionState.MaxAffection || __instance == null || !__instance.Active) return;
            int maximum = GameAccess.Field<int>(__instance, "_max");
            if (maximum > 0 && __instance.Value < maximum) __instance.SetValue(maximum);
        }
    }

    // Removes the per-session practice spend cap: the panel may still show a limit,
    // but the spend handed to the game is clamped to the points actually available.
    internal static class PracticeLimitState
    {
        internal static bool On => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.PracticeLimit.Value;
    }

    [HarmonyPatch(typeof(MartialPanel), "StartHandleUpgarde")]
    internal static class PracticeLimitSpendPatch
    {
        private static void Prefix(MartialPanel __instance, ref int point)
        {
            if (!PracticeLimitState.On) return;
            try
            {
                var stat = GameAccess.Field<GameStat>(__instance, "_martialPoint");
                int available = stat != null ? stat.FinalValue : 0;
                if (available > 0 && point > available) point = available;
            }
            catch (System.Exception) { }
        }
    }

    [HarmonyPatch(typeof(MartialPanel), "CanUseLearnButton")]
    internal static class PracticeLimitButtonPatch
    {
        private static void Postfix(MartialPanel __instance, ref bool __result)
        {
            if (!PracticeLimitState.On || __result) return;
            try
            {
                var stat = GameAccess.Field<GameStat>(__instance, "_martialPoint");
                if (stat != null && stat.FinalValue > 0) __result = true;
            }
            catch (System.Exception) { }
        }
    }

    [HarmonyPatch(typeof(MartialPanel), "UpdateMartialPointInfo")]
    internal static class PracticeLimitLabelPatch
    {
        private static void Postfix(MartialPanel __instance)
        {
            if (!PracticeLimitState.On) return;
            try
            {
                var text = GameAccess.Field<UnityEngine.UI.Text>(__instance, "_maxCountText");
                if (text != null) text.text = "不限";
            }
            catch (System.Exception) { }
        }
    }

    // Checkpoint reminders: story flags are markers, not save points. Read only.
    internal static class CheckpointState
    {
        internal static bool On => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.CheckpointLog.Value;

        internal static void Changed(MissionManagerData manager, string name)
        {
            if (!On || manager == null || string.IsNullOrEmpty(name)) return;
            try
            {
                var flag = manager.GetFlag(name);
                if (flag == null) return;
                Plugin.Instance.Status = "检查点更新：" + flag.name + "=" + flag.State;
            }
            catch (System.Exception) { }
        }
    }

    [HarmonyPatch(typeof(MissionManagerData), "AddFlag")]
    internal static class CheckpointAddPatch
    {
        private static void Postfix(MissionManagerData __instance, string __1) { CheckpointState.Changed(__instance, __1); }
    }

    [HarmonyPatch(typeof(MissionManagerData), "SetFlag")]
    internal static class CheckpointSetPatch
    {
        private static void Postfix(MissionManagerData __instance, string __1) { CheckpointState.Changed(__instance, __1); }
    }
}
