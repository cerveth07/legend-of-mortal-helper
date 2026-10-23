using System;
using System.Collections.Generic;
using System.Reflection;
using Mortal.Core;

namespace MortalHelper.Game
{
    // Turns the game's internal keys into the names a player reads. Everything here is
    // read-only: the stat table comes from the game's own enum attributes, item names
    // from the loaded item database.
    internal static class GameNames
    {
        // Game-side stat names (the enum's own member names) → the shorter label the
        // panel already uses on the numeric page.
        private static readonly Dictionary<string, string> StatLabels = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "銀兩", "金钱" }, { "個人貢獻度", "个人贡献" }, { "門派資產", "门派资产" }, { "門派名聲", "门派名声" },
            { "門派人數", "门派人数" }, { "向心力", "向心力" }, { "心理衛生", "心情" }, { "武學點數", "武学点数" },
            { "鍛造", "锻造点数" }, { "毒藥", "炼丹点数" }, { "體力", "体力" }, { "內力", "内力" },
            { "武功拳掌", "拳掌" }, { "武功暗器", "暗器" }, { "武功刀劍", "刀剑" }, { "輕功", "轻功" },
            { "學問", "学问" }, { "嘴力", "嘴力" }, { "抗毒", "抗毒" }, { "抗麻", "抗麻" },
            { "性情", "性情" }, { "處世", "处世" }, { "道德", "道德" }, { "修養", "修养" },
            { "命運", "命运" }, { "陰陽內功", "阴阳内功" } };

        // Fallback for the script keys, used when the game enum cannot be read (game
        // update renamed the type, unit tests without the game): same pairs the enum
        // carries in its StringValue attributes.
        private static readonly Dictionary<string, string> FallbackKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "life", "體力" }, { "stamina", "內力" }, { "dexterity", "輕功" }, { "money", "銀兩" },
            { "charisma", "魅力" }, { "literacy", "學問" }, { "mental", "心理衛生" }, { "fate", "命運" },
            { "disposition", "性情" }, { "behaviour", "處世" }, { "training", "修養" }, { "karma", "道德" },
            { "talking", "嘴力" }, { "faction", "門派規模" }, { "fame", "門派名聲" }, { "people", "門派人數" },
            { "team", "向心力" }, { "weapon", "鍛造" }, { "poison", "毒藥" }, { "action", "行動次數" },
            { "contribution", "個人貢獻度" }, { "cook", "廚藝" }, { "poison-resistance", "抗毒" },
            { "paralysis-resistance", "抗麻" }, { "girl", "娘化" }, { "internal", "陰陽內功" },
            { "change-heart", "變心" }, { "martial-point", "武學點數" }, { "total-martial-point", "全武學點數" },
            { "assets", "門派資產" }, { "faction-contribution", "門派貢獻" } };

        private static Dictionary<string, string> scriptKeys;

        // The label the panel and the hint overlay show for a stat.
        internal static string StatName(GameStat stat)
        {
            if (stat == null) return "";
            string name;
            string key = stat.StatType.ToString();
            return StatLabels.TryGetValue(key, out name) ? name : stat.Name;
        }

        // "training" → "修养", using the game's own enum attributes when they are readable.
        internal static string StatName(string scriptKey)
        {
            if (string.IsNullOrEmpty(scriptKey)) return "";
            string name;
            string stat = ResolveKey(scriptKey);
            if (StatLabels.TryGetValue(stat, out name)) return name;
            return stat;
        }

        private static string ResolveKey(string scriptKey)
        {
            EnsureKeys();
            string name;
            return scriptKeys.TryGetValue(scriptKey, out name) ? name : scriptKey;
        }

        private static void EnsureKeys()
        {
            if (scriptKeys != null) return;
            // A copy per call site: a duplicated key with a different value is a game
            // change worth keeping rather than a crash.
            var map = new Dictionary<string, string>(FallbackKeys, StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (FieldInfo field in typeof(GameStatType).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    try
                    {
                        foreach (object attribute in field.GetCustomAttributes(false))
                        {
                            PropertyInfo value = attribute.GetType().GetProperty("StringValue",
                                BindingFlags.Public | BindingFlags.Instance);
                            if (value == null || value.PropertyType != typeof(string)) continue;
                            string text = value.GetValue(attribute, null) as string;
                            if (string.IsNullOrEmpty(text)) continue;
                            map[text] = field.Name;
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            scriptKeys = map;
        }

        // Item ids become names through the loaded database; an unknown id stays visible.
        internal static string ItemName(int type, string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            try
            {
                ItemDatabase database = ItemDatabase.Instance;
                if (database != null)
                {
                    IGameItem item = database.GetItem((GameItemType)type, id);
                    if (item is ItemData asset) return ItemName(asset);
                }
            }
            catch (Exception) { }
            return id;
        }

        // A condition points at the item asset itself rather than at an id.
        internal static string ItemName(ItemData item)
        {
            if (item == null) return "";
            try
            {
                LocalizationManager manager = LocalizationManager.Instance;
                if (manager != null && manager.LocaleResolver != null)
                {
                    string text = manager.LocaleResolver.GetString(item.GetNameKey());
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
            catch (Exception) { }
            return item.Id;
        }
    }
}

