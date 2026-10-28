using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Lean.Localization;
using Mortal.Core;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    internal static class Titles
    {
        internal static readonly TitleHistory History = new TitleHistory();
        private static int loading;
        private static Mortal.Free.FreeManager lastFree;
        private static int lastDisplay = int.MinValue;
        private static bool refreshFailed;
        internal static void Tick()
        {
            if (loading != 0 || !GameAccess.Loaded) return;
            var free = Mortal.Free.FreeManager.Instance;
            int display = Plugin.Instance.Options.Enabled.Value && History.Selected >= 0 ? History.Selected : Current;
            if (free == lastFree && display == lastDisplay) return;
            // Book-keeping happens before the risky call: a throwing Refresh() used to
            // re-run (and re-log an error) on every frame in free roam.
            lastFree = free; lastDisplay = display;
            try { Refresh(); refreshFailed = false; }
            catch (Exception error)
            {
                if (refreshFailed) return;
                refreshFailed = true;
                try { Plugin.Instance?.Report(error); } catch (Exception) { }
            }
        }
        internal static int Current => PlayerStatManagerData.Instance?.Stats?.Get(GameStatType.稱號)?.Value ?? -1;
        internal static string Name(int id) => LeanLocalization.GetTranslationText("PlayerInfo/Title/" + id.ToString(CultureInfo.InvariantCulture));
        internal static string DisplayName(int id)
        {
            string name = Name(id);
            return string.IsNullOrWhiteSpace(name) ? "无称号" : name;
        }

        // Cosmetic metadata does not touch gameplay stats or native menu state.
        // FreeManager.EnableHotKey is false in activity lists and other menus,
        // so the numeric-edit gate incorrectly disabled every title button there.
        internal static string SelectionBlockReason
        {
            get
            {
                if (loading != 0 || !GameAccess.Loaded) return "请先开始游戏或读取存档。";
                if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract || AnytimeStory.Replaying || AnytimeDuel.Replaying)
                    return "存档恢复尚未完成，请等待恢复或重新读取有效存档。";
                return null;
            }
        }

        internal static string Choose(int id)
        {
            string blocked = SelectionBlockReason;
            if (blocked != null) return blocked;
            if (id != -1 && (id > 999 || id < 0 || Name(id) == null)) return "该称号不存在。";
            History.Select(id);
            Refresh();
            return id == -1 ? "称号已跟随剧情；正常存档后保留。" : "已使用「" + DisplayName(id) + "」；正常存档后保留。";
        }
        private static void Refresh()
        {
            var free = Mortal.Free.FreeManager.Instance;
            if (free != null && free.gameObject.activeInHierarchy)
            {
                Text title = GameAccess.Field<Text>(free, "_titleText");
                title.text = Name(Plugin.Instance.Options.Enabled.Value && History.Selected >= 0 ? History.Selected : Current) ?? "";
            }
        }
        internal static void Reset() { History.Reset(Current); }

        // Called on comparison-owned GameSave clones only. Native title stays in
        // every gameplay fingerprint; cosmetic metadata must not break old journals.
        internal static bool StripMetadata(GameSave save)
        {
            if (save == null) return false;
            bool changed = save.Stats != null && save.Stats.RemoveAll(item => item != null && TitleHistory.IsMetadata(item.Key)) > 0;
            if (!string.IsNullOrEmpty(save.RestoreData) && save.RestoreData.Contains(TitleHistory.Prefix))
            {
                var nested = JsonUtility.FromJson<GameSave>(save.RestoreData);
                if (StripMetadata(nested)) { save.RestoreData = JsonUtility.ToJson(nested); changed = true; }
            }
            return changed;
        }
        internal static string StripMetadataJson(string json)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains(TitleHistory.Prefix)) return json;
            var save = JsonUtility.FromJson<GameSave>(json);
            return StripMetadata(save) ? JsonUtility.ToJson(save, true) : json;
        }

        [HarmonyPatch(typeof(GameStat), "RaiseValueChangedEvent")]
        private static class AcquirePatch
        {
            private static void Postfix(GameStat __instance)
            {
                if (loading == 0 && __instance.StatType == GameStatType.稱號) History.Observe(__instance.Value);
            }
        }
        [HarmonyPatch(typeof(PlayerStatManagerData), "ResetData")]
        private static class ResetPatch
        {
            private static void Prefix() { History.Reset(-1); }
            private static void Postfix() { if (loading == 0) Reset(); }
        }
        [HarmonyPatch(typeof(PlayerStatManagerData), "Load")]
        private static class LoadPatch
        {
            private static void Prefix() { loading++; History.Reset(-1); }
            private static void Postfix(GameSave __0)
            {
                var entries = new List<KeyValuePair<string, int>>();
                if (__0?.Stats != null)
                    foreach (var item in __0.Stats)
                        if (item != null) entries.Add(new KeyValuePair<string, int>(item.Key, item.Value));
                History.Load(entries, Current);
            }
            private static Exception Finalizer(Exception __exception)
            {
                loading = Math.Max(0, loading - 1);
                if (__exception != null) History.Reset(-1);
                return __exception;
            }
        }
        [HarmonyPatch(typeof(PlayerStatManagerData), "Save")]
        private static class SavePatch
        {
            private static void Postfix(GameSave __0)
            {
                if (__0?.Stats == null) return;
                __0.Stats.RemoveAll(item => item != null && TitleHistory.IsMetadata(item.Key));
                foreach (var item in History.Save()) __0.Stats.Add(new StatSave { Key = item.Key, Value = item.Value });
            }
        }
        [HarmonyPatch(typeof(LeanLocalizationResolver), "GetString")]
        private static class DisplayPatch
        {
            private static void Prefix(ref string __0)
            {
                if (Plugin.Instance == null || !Plugin.Instance.Options.Enabled.Value || loading != 0 || History.Selected < 0) return;
                if (__0 == "PlayerInfo/Title/" + Current.ToString(CultureInfo.InvariantCulture))
                    __0 = "PlayerInfo/Title/" + History.Selected.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
