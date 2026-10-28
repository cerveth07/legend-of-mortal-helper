using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Lean.Localization;
using Mortal.Core;
using Mortal.Story;
using UnityEngine;

namespace MortalHelper.Game
{
    // Names and descriptions come from the currently loaded game, never an exported
    // plot dictionary. No Execute/Check/GetResult methods are called to name a branch.
    internal static class HintNames
    {
        private static readonly Dictionary<string, FlagData> Flags = new Dictionary<string, FlagData>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MissionData> Missions = new Dictionary<string, MissionData>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, StoryData> Stories = new Dictionary<string, StoryData>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ScriptableObject> Switches = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ScriptableObject> Conditions = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ScriptableObject> Positions = new Dictionary<string, ScriptableObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, TextAsset> Sources = new Dictionary<string, TextAsset>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> OpeningKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> LocaleKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, RelationshipStatType> RelationshipKeys = BuildRelationshipKeys();
        private static readonly Regex StoryTextCall = new Regex("^\\s*say\\s*\\(\\s*luamanager\\s*\\.\\s*GetStoryText\\s*\\(\\s*\"([^\"\\r\\n]+)\"", RegexOptions.CultureInvariant | RegexOptions.Multiline);
        private static readonly Regex LongComment = new Regex(@"^--\[(=*)\[", RegexOptions.CultureInvariant);
        private static readonly Regex Tags = new Regex(@"</?(?:color|size|b|i|u|s|material|quad)\b[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.CultureInvariant);
        private static bool prepared;
        private static object localeTable;
        private static int localeCount = -1;

        internal static void Clear()
        {
            prepared = false;
            Flags.Clear(); Missions.Clear(); Stories.Clear(); Switches.Clear(); Conditions.Clear(); Positions.Clear();
            Sources.Clear(); OpeningKeys.Clear();
        }

        internal static void Prepare()
        {
            if (prepared) return;
            // One scan per story scene, not one scan per hint line or GUI frame.
            foreach (var asset in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                if (asset == null) continue;
                if (asset is FlagData flag) Add(Flags, flag.name, flag);
                else if (asset is MissionData mission)
                { Add(Missions, mission.name, mission); Add(Missions, mission.Name, mission); }
                else if (asset is StoryData story) Add(Stories, story.name, story);
                else if (asset is SwitchResultData) Add(Switches, asset.name, asset);
                else if (asset is ConditionResultData) Add(Conditions, asset.name, asset);
                else if (asset is PositionResultData) Add(Positions, asset.name, asset);
            }
            foreach (var asset in Resources.FindObjectsOfTypeAll<TextAsset>())
                if (asset != null) Add(Sources, asset.name, asset);
            prepared = true;
        }

        private static void Add<T>(Dictionary<string, T> map, string key, T value) where T : class
        {
            if (string.IsNullOrEmpty(key)) return;
            T previous;
            // Two different assets with the same key are ambiguous. Keep the ID
            // visible rather than attach one chapter's meaning to another chapter.
            if (map.TryGetValue(key, out previous) && !ReferenceEquals(previous, value)) map[key] = null;
            else if (!map.ContainsKey(key)) map.Add(key, value);
        }

        internal static void RegisterScript(string key, string source)
        {
            if (string.IsNullOrEmpty(key)) return;
            OpeningKeys[key] = "";
            string closing = null;
            foreach (string raw in (source ?? "").Split('\n'))
            {
                string line = raw;
                if (closing != null)
                {
                    int at = line.IndexOf(closing, StringComparison.Ordinal);
                    if (at < 0) continue;
                    line = line.Substring(at + closing.Length); closing = null;
                }
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("--", StringComparison.Ordinal))
                {
                    var comment = LongComment.Match(trimmed);
                    if (comment.Success)
                    {
                        closing = "]" + comment.Groups[1].Value + "]";
                        if (trimmed.IndexOf(closing, comment.Length, StringComparison.Ordinal) >= 0) closing = null;
                    }
                    continue;
                }
                var match = StoryTextCall.Match(line);
                if (!match.Success) continue;
                OpeningKeys[key] = match.Groups[1].Value;
                break;
            }
        }

        internal static string Localized(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var table = LeanLocalization.CurrentTranslations;
            if (table == null) return null;
            if (!ReferenceEquals(table, localeTable) || table.Count != localeCount)
            {
                LocaleKeys.Clear();
                foreach (string candidate in table.Keys) Add(LocaleKeys, candidate, candidate);
                localeTable = table; localeCount = table.Count;
            }
            string canonical;
            if (!table.ContainsKey(key) && (!LocaleKeys.TryGetValue(key, out canonical) || canonical == null)) return null;
            else canonical = table.ContainsKey(key) ? key : LocaleKeys[key];
            string text = LeanLocalization.GetTranslationText(canonical);
            return string.IsNullOrWhiteSpace(text) || string.Equals(text, canonical, StringComparison.OrdinalIgnoreCase) ? null : text;
        }

        internal static string Compact(string text, int maximum = 72)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string clean = Spaces.Replace(Tags.Replace(text.Replace("\\n", " ").Replace("\\r", " "), ""), " ").Trim();
            if (clean.Length <= maximum) return clean;
            int length = maximum;
            if (length > 0 && char.IsHighSurrogate(clean[length - 1])) length--;
            return clean.Substring(0, length) + "…";
        }

        private static string Unknown(string category, string id) => category + "（编号 " + Compact(id, 60) + "）";
        private static bool Human(string text, string key) => !string.IsNullOrWhiteSpace(text) &&
            !string.Equals(text, key, StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(text, @"^[A-Za-z0-9_./:-]+$");

        internal static string Character(string id)
        {
            string name = Localized("Character/" + id) ?? Localized("CombatCharacter/Name/" + id);
            if (name != null) return Compact(name);
            int number;
            if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
                Enum.IsDefined(typeof(RelationshipStatType), number))
            {
                var type = (RelationshipStatType)number;
                name = Localized("Character/" + ScriptKey(type));
                return name == null ? type.ToString() : Compact(name);
            }
            RelationshipStatType role;
            if (RelationshipKeys.TryGetValue(id ?? "", out role)) return role.ToString();
            return Unknown("角色", id);
        }

        private static Dictionary<string, RelationshipStatType> BuildRelationshipKeys()
        {
            var result = new Dictionary<string, RelationshipStatType>(StringComparer.OrdinalIgnoreCase);
            foreach (RelationshipStatType type in Enum.GetValues(typeof(RelationshipStatType)))
            {
                string key = ScriptKey(type);
                if (!string.IsNullOrEmpty(key)) result[key] = type;
            }
            return result;
        }

        private static string ScriptKey(RelationshipStatType type)
        {
            var field = typeof(RelationshipStatType).GetField(type.ToString());
            if (field != null)
                foreach (object attribute in field.GetCustomAttributes(false))
                {
                    var property = attribute.GetType().GetProperty("StringValue");
                    if (property != null && property.PropertyType == typeof(string)) return property.GetValue(attribute, null) as string;
                }
            return type.ToString();
        }

        internal static string Talent(string id)
        {
            string text = Compact(Localized("PlayerTalent/Name/" + id));
            return text.Length > 0 ? text : Unknown("技能", id);
        }

        internal static string StatValue(string key, string value)
        {
            if (string.Equals(key, "title", StringComparison.OrdinalIgnoreCase) || key == "稱號")
            {
                string title = Compact(Localized("PlayerInfo/Title/" + value));
                return title.Length > 0 ? title : value;
            }
            if (string.Equals(key, "lover", StringComparison.OrdinalIgnoreCase) || key == "愛人") return Character(value);
            return value;
        }

        internal static string Flag(string id)
        {
            string localized = Localized("Flag/Name/" + id);
            if (localized != null) return Compact(localized);
            Prepare();
            FlagData flag;
            return Flags.TryGetValue(id ?? "", out flag) && flag != null ? Flag(flag) : Unknown("剧情记录", id);
        }

        internal static string Flag(FlagData flag)
        {
            if (flag == null) return "未命名剧情记录";
            string name = Localized(flag.GetNameKey());
            if (name != null) return Compact(name);
            return Human(flag.DevNote, flag.name) ? Compact(flag.DevNote) : Unknown("剧情记录", flag.name);
        }

        internal static string Script(string key)
        {
            string text = Localized("LegendInfo/" + key);
            if (text != null) return Compact(text);
            Prepare();
            StoryData story;
            if (Stories.TryGetValue(key ?? "", out story) && story != null)
                return Compact(Localized(story.GetKeyName()) ?? story.Text);
            string opening;
            if (!OpeningKeys.TryGetValue(key ?? "", out opening))
            {
                TextAsset source;
                Sources.TryGetValue(key ?? "", out source);
                // Read exactly the same resource path as LuaManager, but never
                // compile/run the target script just to explain a destination.
                if (source == null && LuaManager.Instance != null)
                {
                    string prefix = GameAccess.Field<string>(LuaManager.Instance, "_scriptPrefix");
                    if (!string.IsNullOrEmpty(prefix)) source = Resources.Load<TextAsset>(prefix + "/ChineseTraditional/" + key);
                }
                RegisterScript(key, source == null ? "" : source.text);
                OpeningKeys.TryGetValue(key ?? "", out opening);
            }
            text = Localized("Story/" + opening);
            return text != null ? "对白摘录：" + Compact(text, 56) : Unknown("后续剧情", key);
        }

        internal static string Checkpoint(string key, string kind)
        {
            Prepare();
            var map = kind == "Position" ? Positions : kind == "Condition" ? Conditions : Switches;
            ScriptableObject data;
            if (map.TryGetValue(key ?? "", out data) && data != null)
            {
                string note = GameAccess.Field<string>(data, "_devNote");
                if (Human(note, key)) return Compact(note);
                string definition = OptionConditions.Definition(data);
                if (!string.IsNullOrEmpty(definition)) return definition;
            }
            return Unknown(kind == "Position" ? "地点检查" : "分支判定", key);
        }

        internal static string Mission(string id, string stage)
        {
            Prepare();
            MissionData mission;
            string title = null, next = null;
            if (id == "main" && MissionManagerData.Instance != null) mission = MissionManagerData.Instance.Main;
            else Missions.TryGetValue(id ?? "", out mission);
            if (mission != null)
            {
                string desc = GameAccess.Field<string>(mission, "_desc");
                title = Human(desc, id) ? Compact(desc) : Human(mission.Name, id) ? Compact(mission.Name) : null;
                var states = GameAccess.Field<MissionCheckData[]>(mission, "_missions");
                if (states != null)
                    foreach (var state in states)
                        if (state != null && state.Key == stage)
                        {
                            desc = GameAccess.Field<string>(state, "_desc");
                            if (Human(desc, stage)) next = Compact(desc);
                            break;
                        }
            }
            title = title ?? Unknown("任务", id);
            return string.IsNullOrEmpty(stage) ? title : title + " → " + (next ?? Unknown("阶段", stage));
        }

        internal static string Scene(string key)
        {
            switch (key)
            {
                case "Combat": return "进入单挑战斗";
                case "Battle": return "进入群战战斗";
                case "Free": return "返回自由活动";
                case "Story": return "继续剧情对话";
                case "Break": return "进入休息剧情";
                case "Title": return "返回标题画面";
                case "GameOver": return "进入游戏结束画面";
                default: return Unknown("场景", key);
            }
        }

        internal static string Consequence(string raw, UiLang language)
        {
            string text = raw;
            if (raw.StartsWith("分支点 ", StringComparison.Ordinal)) text = UiLanguage.Translate("分支判定：", language) + Checkpoint(raw.Substring(4), "Switch");
            // The parser also puts AddStory records here. "Content" does not
            // falsely promise that a recorded passage is the next executed script.
            else if (raw.StartsWith("剧情 ", StringComparison.Ordinal)) text = UiLanguage.Translate("剧情内容：", language) + Script(raw.Substring(3));
            else if (raw.StartsWith("下一段 ", StringComparison.Ordinal)) text = UiLanguage.Translate("下一段剧情：", language) + Script(raw.Substring(4));
            else if (raw.StartsWith("记录 ", StringComparison.Ordinal)) text = UiLanguage.Translate("新增纪事：", language) + Script(raw.Substring(3));
            else if (raw.StartsWith("场景 ", StringComparison.Ordinal)) text = Scene(raw.Substring(3));
            return Natural(text, language);
        }

        internal static string Natural(string text, UiLang language) =>
            language == UiLang.Hant ? UiLanguage.Convert(text, true) :
            language == UiLang.Hans ? UiLanguage.Convert(text, false) : UiLanguage.Translate(text, language);
    }
}
