// Production names, condition descriptions and effect rendering; synthetic data only.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Core;
using MortalHelper.Game;
using UnityEngine;

namespace UnityEngine
{
    public class Object { public string name; }
    public class ScriptableObject : Object { }
    public sealed class TextAsset : Object { public string text; }
    public static class Resources
    {
        public static List<Object> Assets = new List<Object>(); public static int Scans;
        public static Func<string, Object> Loader = key => null; public static int Loads;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object { Scans++; return Assets.OfType<T>().ToArray(); }
        public static T Load<T>(string key) where T : Object { Loads++; return Loader(key) as T; }
    }
}
namespace Lean.Localization
{
    public sealed class LeanTranslation { public object Data; }
    public static class LeanLocalization
    {
        public static Dictionary<string, LeanTranslation> CurrentTranslations = new Dictionary<string, LeanTranslation>();
        public static string GetTranslationText(string key) => CurrentTranslations.TryGetValue(key, out var value) ? value.Data as string : null;
    }
}
namespace Mortal.Core
{
    public sealed class StringValueAttribute : Attribute
    { public string StringValue { get; } public StringValueAttribute(string value) { StringValue = value; } }
    public enum RelationshipStatType { [StringValue("brother1")] 大師兄 = 1, [StringValue("girl6")] 夏侯蘭 = 606 }
    public sealed class RelationshipStat { public RelationshipStatType Type; }
    public enum GameStatType { [StringValue("karma")] 道德, [StringValue("money")] 銀兩 }
    public sealed class GameStat { public GameStatType StatType; public int Value, Min, Max = 100; public string Name; }
    public sealed class StatList { public GameStat Get(GameStatType type) => null; }
    public sealed class PlayerStatManagerData { public static PlayerStatManagerData Instance; public StatList Stats = new StatList(); }
    public sealed class FlagData : ScriptableObject { public string _devNote; public string DevNote => _devNote; public string GetNameKey() => "Flag/Name/" + name; }
    public sealed class StoryData : ScriptableObject { public string Text; public string GetKeyName() => "LegendInfo/" + name; }
    public sealed class MissionCheckData { public string Key, _desc; }
    public sealed class MissionData : ScriptableObject { public string Name, _desc; public MissionCheckData[] _missions; }
    public sealed class MissionManagerData { public static MissionManagerData Instance; public MissionData Main; }
    public enum LogicOperatorType { AND, OR }
    public enum StatCompareType { Equal, NotEqual, Less, LessEqual, Greater, GreaterEqual }
    public enum StatGroupType { Sum, Min, Max }
    public enum StatCheckType { 常數, 角色數值, 好感度, 旗標, 書籍, 雜物, 貴重品, 隨機值, 數值群組, 遊戲時間, 條件設定 }
    public sealed class StatValueReference
    {
        public StatCheckType _checkType; public int _constant, _randomMin = 1, _randomMax = 100;
        public GameStat _stat; public RelationshipStat _relationship; public FlagData _flag;
        public ItemData _book, _misc, _specialItem; public StatGroupVariable _statGroup;
    }
    public sealed class StatGroupVariable
    {
        public static int Evaluations; public List<StatValueReference> _value = new List<StatValueReference>(); public StatGroupType _groupType;
        public int GetResult() { Evaluations++; throw new Exception("Description evaluated game state/RNG"); }
    }
    public sealed class StatCompareItem { public StatCompareType _compareOp; public StatGroupVariable _value1, _value2; }
    public sealed class ConditionResultItem { public LogicOperatorType _logicOp; public List<StatCompareItem> _items = new List<StatCompareItem>(); }
    public sealed class ConditionResultData : ScriptableObject { public string _devNote; public int _count = 17; public List<ConditionResultItem> _items = new List<ConditionResultItem>(); }
    public enum GameItemType { Book = 1 }
    public interface IGameItem { }
    public sealed class ItemData : IGameItem { public string Id; public string GetNameKey() => "Item/" + Id; }
    public sealed class ItemDatabase { public static ItemDatabase Instance; public IGameItem GetItem(GameItemType type, string id) => null; }
    public interface ILocaleResolver { string GetString(string key); }
    public sealed class LocalizationManager { public static LocalizationManager Instance; public ILocaleResolver LocaleResolver; }
}
namespace Mortal.Story
{
    public sealed class SwitchResultData : ScriptableObject
    { public string _devNote; public bool _useCount; public int _count = 23; public List<ConditionResultItem> _items = new List<ConditionResultItem>(); }
    public sealed class PositionResultData : ScriptableObject { public string _devNote; }
    public sealed class CheckPointManager { public static CheckPointManager Instance; }
    public sealed class LuaManager { public static LuaManager Instance; public string _scriptPrefix; }
}
namespace MortalHelper.Game
{
    public static class GameAccess
    {
        public static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
    public static class NumericEdits
    {
        public static readonly HashSet<string> Stats = new HashSet<string>();
        public static void SetStat(GameStat stat, int value) => throw new Exception("Name lookup wrote a stat");
    }
}
internal static class HintNameBehavior
{
    private static int checks;
    private static void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
    private static void Text(string key, string value) => Lean.Localization.LeanLocalization.CurrentTranslations[key] = new Lean.Localization.LeanTranslation { Data = value };
    private static StatGroupVariable Group(StatValueReference value) => new StatGroupVariable { _value = new List<StatValueReference> { value } };
    private static string Render(string call)
    {
        var effect = ScriptHints.Read("--index:O_test\nstatmodifymanager." + call)["O_test"].Effects.Single();
        return HintNames.Natural(HintText.Effect(effect, false, UiLang.Hans), UiLang.Hans);
    }
    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Run()
    {
        Text("Character/brother1", "唐布衣"); Text("Character/girl6", "夏侯蘭");
        Text("PlayerTalent/Name/9102", "鐵琵琶功"); Text("Flag/Name/KNOWN", "門派好感度");
        Text("LegendInfo/Chapter_1", "\\n<b>你来到山门。</b>\n随后遇见故人。");
        Text("PlayerInfo/Title/2", "测试称号"); Text("Story/D_OPEN_001", "这是目标剧情的对白。");
        var flag = new FlagData { name = "M20010", _devNote = "掌門病情" };
        var mission = new MissionData { name = "M_TEST", Name = "M_TEST", _desc = "练武场事务",
            _missions = new[] { new MissionCheckData { Key = "STAGE_END", _desc = "等待归队" } } };
        var comparison = new StatCompareItem { _compareOp = StatCompareType.GreaterEqual,
            _value1 = Group(new StatValueReference { _checkType = StatCheckType.角色數值, _stat = new GameStat { StatType = GameStatType.道德, Value = 7 } }),
            _value2 = Group(new StatValueReference { _checkType = StatCheckType.常數, _constant = 40 }) };
        var checkpoint = new SwitchResultData { name = "S_Kar_GE_40", _items = new List<ConditionResultItem> {
            new ConditionResultItem { _items = new List<StatCompareItem> { comparison } } } };
        var condition = new ConditionResultData { name = "COND", _devNote = "已经见过故人" };
        var location = new PositionResultData { name = "POS", _devNote = "抵达练武场" };
        var story = new StoryData { name = "RECORD_ONLY", Text = "尚未提供翻译的剧情记录。" };
        Resources.Assets.AddRange(new UnityEngine.Object[] { flag, mission, checkpoint, condition, location, story,
            new TextAsset { name = "TARGET", text = "say(luamanager.GetStoryText(\"D_OPEN_001\"))" } });
        Check(HintNames.Character("brother1") == "唐布衣", "script relationship key becomes character name");
        Check(HintNames.Natural(HintNames.Compact("鐵琵琶功"), UiLang.Hans) == "铁琵琶功", "traditional skill names have simplified iron character");
        Check(HintNames.Natural(HintNames.Character("606"), UiLang.Hans) == "夏侯兰", "numeric character ID uses enum key and native translation");
        Check(HintNames.Talent("9102") == "鐵琵琶功", "talent ID uses native name");
        Check(HintNames.Flag("KNOWN") == "門派好感度", "localized flag name wins");
        Check(HintNames.Flag("M20010") == "掌門病情", "developer flag meaning is readable without internal ID");
        Check(HintNames.Script("chapter_1") == "你来到山门。 随后遇见故人。", "case alias uses official story record and strips formatting");
        Check(HintNames.Script("RECORD_ONLY") == story.Text, "loaded story record is a fallback");
        Check(HintNames.Script("TARGET") == "对白摘录：这是目标剧情的对白。", "loaded destination source supplies a labelled dialogue quote");
        HintNames.RegisterScript("CURRENT", "say(luamanager.GetStoryText(\"D_OPEN_001\"))");
        Check(HintNames.Script("CURRENT").Contains("目标剧情"), "current script is readable even without a loaded TextAsset");
        LuaManager.Instance = new LuaManager { _scriptPrefix = "LuaScripts" };
        Resources.Loader = key => key == "LuaScripts/ChineseTraditional/NOT_LOADED" ?
            new TextAsset { name = "NOT_LOADED", text = "say(luamanager.GetStoryText(\"D_OPEN_001\"))" } : null;
        Check(HintNames.Script("NOT_LOADED").Contains("目标剧情"), "unloaded destination is read through native resource path without execution");
        int loads = Resources.Loads; HintNames.Script("NOT_LOADED");
        Check(Resources.Loads == loads, "destination source is loaded once per scene");
        HintNames.Script("MISSING_SOURCE"); loads = Resources.Loads; HintNames.Script("MISSING_SOURCE");
        Check(Resources.Loads == loads, "missing destination lookups are cached instead of repeated every GUI frame");
        Check(HintNames.Checkpoint("S_Kar_GE_40", "Switch") == "道德 ≥ 40", "unnamed checkpoint renders its actual comparison");
        Check(StatGroupVariable.Evaluations == 0 && checkpoint._count == 23 && comparison._value1._value[0]._stat.Value == 7,
            "definition rendering does not evaluate or mutate stats/checkpoint counters");
        Check(HintNames.Checkpoint("COND", "Condition") == "已经见过故人", "condition note maps to readable sentence");
        Check(HintNames.Checkpoint("POS", "Position") == "抵达练武场", "location note maps to readable place check");
        Check(HintNames.Mission("M_TEST", "STAGE_END") == "练武场事务 → 等待归队", "mission and stage descriptions both replace keys");
        Check(HintNames.Mission("M_TEST", "missing").Contains("编号 missing"), "unknown mission stage is explicit, never guessed");
        Check(HintNames.Scene("Combat") == "进入单挑战斗" && HintNames.Scene("GameOver") == "进入游戏结束画面", "scene IDs become actions");
        Check(HintNames.Consequence("场景 Combat", UiLang.Ja) == "一騎打ちへ進む" && HintNames.Consequence("场景 Combat", UiLang.Ko) == "일대일 전투로 진입", "scene labels retain Japanese and Korean UI support");
        Check(HintNames.StatValue("title", "2") == "测试称号" && HintNames.StatValue("lover", "606") == "夏侯蘭", "enumerated stat values become names");
        Check(HintNames.StatValue("money", "100") == "100", "ordinary numbers are not mistaken for IDs");
        Check(Render("Character(\"brother1\",1)") == "唐布衣好感 +1", "inline relationship rendering uses actual person");
        Check(Render("SetCharacter(\"girl6\",30)") == "夏侯兰好感 = 30", "absolute relationship value is readable");
        string talentOutput = Render("AddTalent(\"9102\",1)");
        Check(talentOutput == "天赋 铁琵琶功 +1", "production talent effect uses translated name: " + talentOutput);
        Check(Render("AddFlag(\"M20010\",-2)") == "剧情标记 掌门病情 -2", "negative flag changes have one sign");
        Check(Render("SetFlag(\"M20010\",1)") == "剧情标记 掌门病情 = 1", "multi-valued flags are not falsely called completed");
        Check(Render("Mission(\"M_TEST\",\"STAGE_END\")") == "任务：练武场事务 → 等待归队", "production mission renderer uses both descriptions");
        Check(HintNames.Consequence("分支点 S_Kar_GE_40", UiLang.Hans) == "分支判定：道德 ≥ 40", "parsed destination is converted at presentation boundary");
        Check(HintNames.Consequence("剧情 Chapter_1", UiLang.Hans).Contains("你来到山门"), "story destination gets actual summary");
        Check(HintNames.Consequence("下一段 Chapter_1", UiLang.Hans).StartsWith("下一段剧情：") &&
            HintNames.Consequence("下一段 Chapter_1", UiLang.Hans).Contains("你来到山门"), "upstream next-script label uses native text");
        Check(HintNames.Consequence("记录 Chapter_1", UiLang.Hans).StartsWith("新增纪事："), "recorded story is distinguished from a jump");
        Check(HintText.Condition("分支 S_Kar_GE_40 = 2 且 分支 unknown = 1", UiLang.Hans).Contains("道德 ≥ 40"), "combined branch labels become readable");
        var nested = ScriptHints.Read("--index:O_nested\nlocal r = checkpointmanager.Switch(\"COND\")\nif r == 2 then\nif unknown then\nstatmodifymanager.Player(\"money\",1)\nend\nend")["O_nested"];
        Check(nested.Effects[0].Condition.Contains("分支 COND = 2") && nested.Effects[0].Condition.Contains("未解析"), "nested unresolved conditions do not become unconditional effects");
        Check(HintNames.Consequence("场景 Combat", UiLang.Hans) == "进入单挑战斗", "scene destination gets natural action");
        Check(HintNames.Compact("道德 < 40，性情 > 20") == "道德 < 40，性情 > 20", "comparison symbols are not removed as rich text tags");
        Check(HintNames.Compact(new string('字', 100)).Length == 73, "long plot text is a bounded excerpt");
        Check(HintNames.Flag("unknown").Contains("编号 unknown") && HintNames.Character("unknown").Contains("编号 unknown"), "unknown names remain traceable");
        HintNames.RegisterScript("COMMENT", "-- say(luamanager.GetStoryText(\"D_OPEN_001\"))");
        Check(HintNames.Script("COMMENT").Contains("编号 COMMENT"), "commented-out dialogue is not attributed to a destination");
        HintNames.RegisterScript("LONG_COMMENT", "--[=[\nsay(luamanager.GetStoryText(\"D_OPEN_001\"))\n]=]\nreturn");
        Check(HintNames.Script("LONG_COMMENT").Contains("编号 LONG_COMMENT"), "Lua long comments do not become plot previews");
        Text("PlayerTalent/Name/9102", "別の技名");
        Check(HintNames.Talent("9102") == "別の技名", "language updates do not reuse a stale cached name");
        int scans = Resources.Scans; HintNames.Flag("M20010"); HintNames.Script("RECORD_ONLY");
        Check(Resources.Scans == scans, "GUI name lookups do not rescan all loaded objects");
        comparison._value1 = Group(new StatValueReference { _checkType = StatCheckType.隨機值 });
        Check(OptionConditions.Definition(checkpoint).Contains("随机 1～100") && StatGroupVariable.Evaluations == 0, "random condition description never draws RNG");
        comparison._value1._groupType = StatGroupType.Max;
        Check(OptionConditions.Definition(checkpoint).Contains("最大值（"), "numeric max is described as an operation rather than logical OR");
        checkpoint._useCount = true;
        Check(OptionConditions.Definition(checkpoint).Contains("另有触发次数"), "count-based routing is not passed off as a complete stat condition");
        Resources.Assets.Clear(); Resources.Assets.Add(new FlagData { name = "M20010", _devNote = "新场景的记录" });
        HintNames.Clear();
        Check(HintNames.Flag("M20010") == "新场景的记录", "scene clear discards stale asset references");
        Resources.Assets.Add(new FlagData { name = "M20010", _devNote = "冲突含义" }); HintNames.Clear();
        Check(HintNames.Flag("M20010").Contains("编号 M20010"), "ambiguous duplicated IDs are never assigned a guessed meaning");
        Lean.Localization.LeanLocalization.CurrentTranslations.Remove("Character/brother1");
        Check(HintNames.Natural(HintNames.Character("brother1"), UiLang.Hans) == "大师兄", "native relationship enum supplies a role when localization is absent");
        Console.WriteLine("PASS: " + checks + " production hint-name checks (synthetic data; no Unity runtime or game mutation)");
    }
}
