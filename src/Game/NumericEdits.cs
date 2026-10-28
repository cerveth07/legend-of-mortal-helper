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
        internal static bool SetStat(GameStat stat, int value)
        {
            if (stat == null || !Stats.Contains(stat.StatType.ToString()) ||
                !PlayerStatManagerData.Instance.Stats.List.Contains(stat)) throw new InvalidOperationException("属性列表已变化，请重新打开本页。");
            string key = "stat:" + stat.StatType;
            return Change(key, stat.Value, value, v => stat.SetValue(v), () => stat.Value, false, stat.Min, stat.Max);
        }
        internal static bool SetItem(GameItemType type, ItemData item, int value)
        {
            string key = "item:" + ((int)type).ToString(CultureInfo.InvariantCulture) + ":" + item.Id;
            if (FindItem(key) != item) throw new InvalidOperationException("物品列表已变化，请重新打开本页。");
            return Change(key, item.CurrentCount, value, v => item.SetCount(v), () => item.CurrentCount, true, 0, 999999);
        }
        internal static void SetRelationship(RelationshipStat relationship, int value)
        {
            if (relationship == null) throw new InvalidOperationException("角色列表已变化，请重新打开本页。");
            string key = "relationship:" + ((int)relationship.Type).ToString(CultureInfo.InvariantCulture);
            if (FindRelationship(key) != relationship)
                throw new InvalidOperationException("角色已下场或列表已变化，请重新打开本页。");
            int maximum = GameAccess.Field<int>(relationship, "_max");
            Change(key, relationship.Value, value, v => relationship.SetValue(v), () => relationship.Value, false, 0, maximum);
        }
        // Only verified faction-affinity flags belong here. Other H/M flags also
        // encode characters, routes and event states and must never appear as factions.
        internal static readonly string[,] FactionAffinities = {
            { "H00001", "青城派" }, { "H00002", "全真教" }, { "H00003", "峨嵋派" },
            { "H00004", "崆峒派" }, { "H00005", "嵩山派" }, { "H00006", "点苍派" },
            { "H01001", "南宫世家" }, { "H01002", "锦香宫" }, { "H01003", "上官世家" },
            { "H02001", "丐帮" }, { "H02002", "飞石帮" }, { "H02003", "泥教" },
            { "M21000", "崆峒·飞天门" }, { "M21001", "崆峒·玄功门" },
            { "M21002", "崆峒·夺魄门" }, { "M21003", "崆峒·铁拳门" }
        };
        internal static readonly string[,] TalentLevels = {
            { "9100", "战术" }, { "2101", "医术" }, { "9500", "儒学" },
            { "9501", "道学" }, { "9502", "佛学" }, { "9503", "形意拳" }
        };
        private static bool Listed(string[,] entries, string id)
        {
            for (int i = 0; i < entries.GetLength(0); i++) if (entries[i, 0] == id) return true;
            return false;
        }
        internal static FlagData FactionAffinity(string id)
        {
            if (!Listed(FactionAffinities, id)) return null;
            var collections = MissionManagerData.Instance?.FlagsCollection;
            if (collections == null) return null;
            FlagData found = null;
            foreach (var collection in collections)
            {
                if (collection?.List == null) continue;
                foreach (var flag in collection.List)
                {
                    if (flag == null || flag.name != id) continue;
                    if (found != null && found != flag) return null;
                    found = flag;
                }
            }
            return found;
        }
        internal static PlayerTalentData TalentLevel(string id)
        {
            if (!Listed(TalentLevels, id)) return null;
            var list = PlayerStatManagerData.Instance?.Talents?.List;
            if (list == null) return null;
            PlayerTalentData found = null;
            foreach (var talent in list)
            {
                if (talent == null || talent.Id != id) continue;
                if (found != null && found != talent) return null;
                found = talent;
            }
            return found;
        }
        internal static string TalentEditBlockReason => GameAccess.CanEditValues ? null :
            "请在自由活动空闲时，关闭游戏内菜单后修改天赋等级。";
        internal static void SetFactionAffinity(FlagData flag, int value)
        {
            if (flag == null || FactionAffinity(flag.name) != flag)
                throw new InvalidOperationException("门派好感数据已变化或缺失，请重新打开本页。");
            // Native FlagCollectionData.Set writes a signed Int32 without clamping.
            Change("faction:" + flag.name, flag.State, value, v => flag.State = v,
                () => flag.State, false, int.MinValue, int.MaxValue);
        }
        internal static void SetTalentLevel(PlayerTalentData talent, int value)
        {
            if (TalentEditBlockReason != null) throw new InvalidOperationException(TalentEditBlockReason);
            if (talent == null || TalentLevel(talent.Id) != talent)
                throw new InvalidOperationException("天赋数据已变化或缺失，请重新打开本页。");
            Change("talent:" + talent.Id, talent.Level, value, v => talent.Setup(v),
                () => talent.Level, false, 0, GameAccess.Field<int>(talent, "_maxLevel"));
        }
        // The write layer enforces the range itself: the panel is not the only caller
        // (undo closures call SetStat/SetItem directly) and an out-of-range value would
        // be persisted by the next native save.
        private static bool Change(string key, int before, int value, Action<int> write, Func<int> read,
            bool item, int minimum, int maximum)
        {
            string reason = GameAccess.NumericEditBlockReason;
            if (reason != null) throw new InvalidOperationException(reason);
            if (value == before) return true;
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
            try { NumericShopRefresh.Refresh(item); return true; }
            catch (Exception error)
            {
                try { Plugin.Instance?.Log("[Shop] refresh failed after numeric edit: " + error); }
                catch (Exception) { }
                return false;
            }
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
            else if (key != null && key.StartsWith("faction:", StringComparison.Ordinal))
            {
                var flag = FactionAffinity(key.Substring(8));
                if (flag == null || flag.State != before)
                    throw new InvalidDataException("门派好感修改前状态不同或数据缺失：" + key);
                flag.State = after;
            }
            else if (key != null && key.StartsWith("relationship:", StringComparison.Ordinal))
            {
                var relationship = FindRelationship(key);
                if (relationship == null || relationship.Value != before || after < 0 ||
                    after > GameAccess.Field<int>(relationship, "_max"))
                    throw new InvalidDataException("好感修改前状态或允许范围不同：" + key);
                relationship.SetValue(after);
                if (relationship.Value != after) throw new InvalidDataException("好感修改结果不同：" + key);
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
        private static RelationshipStat FindRelationship(string key)
        {
            const string prefix = "relationship:";
            int id;
            if (key == null || !key.StartsWith(prefix, StringComparison.Ordinal) ||
                !int.TryParse(key.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out id)) return null;
            var list = PlayerStatManagerData.Instance?.Relationships?.List;
            if (list == null) return null;
            foreach (var relationship in list)
                if (relationship != null && relationship.Active && (int)relationship.Type == id) return relationship;
            return null;
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
