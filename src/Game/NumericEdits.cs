using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Mortal.Core;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    internal static class NumericEdits
    {
        internal static readonly HashSet<string> Stats = new HashSet<string>(new[] {
            "銀兩","個人貢獻度","門派資產","門派名聲","門派人數","向心力","心理衛生","武學點數","鍛造","毒藥",
            "體力","內力","武功拳掌","武功暗器","武功刀劍","輕功","學問","嘴力","抗毒","抗麻",
            "性情","處世","道德","修養","命運","陰陽內功" });
        internal static string SaveNote => NumericShopRefresh.Open &&
            (SceneManager.GetActiveScene().name == "Story" || SceneManager.GetActiveScene().name == "Break")
                ? "请继续到下一段剧情后再保存中途档。" : "";
        internal static void SetStat(GameStat stat, int value)
        {
            if (stat == null || !Stats.Contains(stat.StatType.ToString()) ||
                !PlayerStatManagerData.Instance.Stats.List.Contains(stat)) throw new InvalidOperationException("属性列表已变化，请重新打开本页。");
            string key = "stat:" + stat.StatType;
            Change(key, stat.Value, value, v => stat.SetValue(v), () => stat.Value, false, stat.Min, stat.Max);
        }
        internal static void SetItem(GameItemType type, ItemData item, int value)
        {
            string key = "item:" + ((int)type).ToString(CultureInfo.InvariantCulture) + ":" + item.Id;
            if (FindItem(key) != item) throw new InvalidOperationException("物品列表已变化，请重新打开本页。");
            Change(key, item.CurrentCount, value, v => item.SetCount(v), () => item.CurrentCount, true, 0, 999999);
        }
        // The write layer enforces the range itself: the panel is not the only caller
        // (undo closures call SetStat/SetItem directly) and an out-of-range value would
        // be persisted by the next native save.
        private static void Change(string key, int before, int value, Action<int> write, Func<int> read,
            bool item, int minimum, int maximum)
        {
            string reason = GameAccess.NumericEditBlockReason;
            if (reason != null) throw new InvalidOperationException(reason);
            if (value == before) return;
            if (minimum > maximum || value < minimum || value > maximum)
                throw new InvalidOperationException("数值超出允许范围（" + minimum + "～" + maximum + "）。");
            Plugin.Instance.Gate.NotifyMutation();
            write(value);
            int after = read();
            string scene = SceneManager.GetActiveScene().name;
            try
            {
                if (scene == "Story" || scene == "Break") AnytimeStory.RecordNumericEdit(key, before, after);
                else if (scene == "Combat") AnytimeDuel.RecordNumericEdit(key, before, after);
            }
            catch (Exception error)
            {
                // A journal that cannot record this edit must not be left behind by live
                // state that already changed: restore the old value, then fail loudly.
                try { write(before); } catch (Exception) { }
                throw new InvalidOperationException("数值修改未能记入中途档记录，已还原："
                    + error.GetBaseException().Message, error);
            }
            NumericShopRefresh.Refresh(item);
        }
        internal static void ApplyRecorded(string key, int before, int after)
        {
            if (key != null && key.StartsWith("stat:", StringComparison.Ordinal))
            {
                string name = key.Substring(5); GameStatType type;
                if (!Stats.Contains(name) || !Enum.TryParse(name, out type)) throw new InvalidDataException("属性修改记录无效。");
                var stat = PlayerStatManagerData.Instance.Stats.Get(type);
                if (stat == null || stat.Value != before || after < stat.Min || after > stat.Max)
                    throw new InvalidDataException("属性修改前状态或允许范围不同：" + name);
                stat.SetValue(after);
                if (stat.Value != after) throw new InvalidDataException("属性修改结果不同：" + name);
            }
            else
            {
                var item = FindItem(key);
                if (item == null || item.CurrentCount != before || after < 0 || after > 999999)
                    throw new InvalidDataException("物品修改前状态或允许范围不同：" + key);
                item.SetCount(after);
                if (item.CurrentCount != after) throw new InvalidDataException("物品修改结果不同：" + key);
            }
        }
        private static ItemData FindItem(string key)
        {
            if (key == null || !key.StartsWith("item:", StringComparison.Ordinal)) return null;
            int separator = key.IndexOf(':', 5), type;
            if (separator < 0 || !int.TryParse(key.Substring(5, separator - 5), out type)) return null;
            var db = ItemDatabase.Instance;
            ItemDataCollection collection = type == 1 ? db.Books : type == 2 ? db.Miscs : type == 3 ? db.Special : type == 4 ? db.Consumables : null;
            if (collection?.List == null) return null;
            string id = key.Substring(separator + 1);
            foreach (var item in collection.List) if (item != null && item.Id == id) return item;
            return null;
        }
    }
}
