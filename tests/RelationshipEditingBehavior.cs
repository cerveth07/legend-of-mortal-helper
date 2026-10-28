// Production numeric writes and story edit replay with managed game stand-ins.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mortal.Core;
using MortalHelper.Core;
using MortalHelper.Game;

namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager
    {
        public static string Current = "Free";
        public static Scene GetActiveScene() => new Scene { name = Current };
    }
}
namespace Mortal.Core
{
    public enum GameStatType { 銀兩 }
    public sealed class GameStat
    {
        public GameStatType StatType;
        public int Value, Min, Max = 9999;
        public void SetValue(int value) { Value = value; }
    }
    public sealed class StatList
    {
        public List<GameStat> List = new List<GameStat>();
        public GameStat Get(GameStatType type) => List.Find(stat => stat.StatType == type);
    }
    public enum RelationshipStatType { Known = 6, Inactive = 8 }
    public sealed class RelationshipStat
    {
        public RelationshipStatType Type;
        public int Value, _max = 50;
        public bool Active = true;
        public void SetValue(int value) { Value = value; }
    }
    public sealed class RelationshipList { public List<RelationshipStat> List = new List<RelationshipStat>(); }
    public sealed class FlagData { public string name; public int State; }
    public sealed class FlagCollectionData { public List<FlagData> List = new List<FlagData>(); }
    public sealed class MissionManagerData
    {
        public static MissionManagerData Instance = new MissionManagerData();
        public FlagCollectionData[] FlagsCollection = { new FlagCollectionData() };
    }
    public sealed class PlayerTalentData
    {
        public string Id;
        public int Level, _maxLevel = 9;
        public void Setup(int level) { Level = level; }
    }
    public sealed class PlayerTalentDataCollection { public List<PlayerTalentData> List = new List<PlayerTalentData>(); }
    public sealed class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public StatList Stats = new StatList();
        public RelationshipList Relationships = new RelationshipList();
        public PlayerTalentDataCollection Talents = new PlayerTalentDataCollection();
    }
    public enum GameItemType { Books = 1 }
    public sealed class ItemData { public string Id; public int CurrentCount; public void SetCount(int count) { CurrentCount = count; } }
    public sealed class ItemDataCollection { public List<ItemData> List = new List<ItemData>(); }
    public sealed class ItemDatabase
    {
        public static ItemDatabase Instance = new ItemDatabase();
        public ItemDataCollection Books = new ItemDataCollection(), Miscs = new ItemDataCollection(),
            Special = new ItemDataCollection(), Consumables = new ItemDataCollection();
    }
}
namespace MortalHelper.Game
{
    public sealed class Plugin
    {
        public static Plugin Instance = new Plugin();
        public OperationGate Gate = new OperationGate();
        public void Log(string message) { }
    }
    public static class GameAccess
    {
        public static bool CanEditValues = false;
        public static string NumericEditBlockReason;
        public static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
    }
    public static class NumericShopRefresh
    {
        public static bool Open;
        public static void Refresh(bool items) { }
    }
    public static class AnytimeStory
    {
        internal static readonly List<AnytimeStoryEvent> Edits = new List<AnytimeStoryEvent>();
        public static string WaitKind = "Say", WaitKey = "native-wait";
        public static bool FailRecord;
        public static void RecordNumericEdit(string key, int before, int after)
        {
            if (FailRecord) throw new IOException("Injected journal failure");
            Edits.Add(new AnytimeStoryEvent { Kind = "Numeric", Key = StoryNumericEdits.Pack(WaitKind, WaitKey, key), Choice = before, Number = after });
        }
    }
    public static class AnytimeDuel
    {
        public static readonly List<Tuple<string, int, int>> Edits = new List<Tuple<string, int, int>>();
        public static bool FailRecord;
        public static void RecordNumericEdit(string key, int before, int after)
        {
            if (FailRecord) throw new IOException("Injected duel journal failure");
            Edits.Add(Tuple.Create(key, before, after));
        }
    }
}
internal static class RelationshipEditingBehavior
{
    private static int checks;
    private static RelationshipStat person = new RelationshipStat { Type = RelationshipStatType.Known, Value = 10 };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Reject(Action action, string message)
    {
        int before = person.Value;
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException || error is InvalidDataException)
        {
            Check(person.Value == before, message + " leaves affection unchanged");
            return;
        }
        throw new Exception(message + " was accepted");
    }
    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Run()
    {
        var list = PlayerStatManagerData.Instance.Relationships.List;
        list.Add(null); list.Add(person);
        var inactive = new RelationshipStat { Type = RelationshipStatType.Inactive, Active = false };
        list.Add(inactive);
        foreach (string scene in new[] { "Free", "Story", "Break", "Combat" })
        {
            UnityEngine.SceneManagement.SceneManager.Current = scene;
            person.Value = 10; AnytimeStory.Edits.Clear(); AnytimeDuel.Edits.Clear();
            Check(!GameAccess.CanEditValues, "old free-roam-only gate stays closed in stand-in");
            NumericEdits.SetRelationship(person, 35);
            Check(person.Value == 35 && person.Active && !inactive.Active, scene + " allows active character without unlocking anyone");
            if (scene == "Story" || scene == "Break")
            {
                Check(AnytimeStory.Edits.Count == 1 && AnytimeStory.Edits[0].Choice == 10 &&
                    AnytimeStory.Edits[0].Number == 35, scene + " records before/after at wait boundary");
                person.Value = 10;
                int cursor = StoryNumericEdits.Apply(AnytimeStory.Edits.ToArray(), 0, "Say", "native-wait", NumericEdits.ApplyRecorded);
                Check(cursor == 1 && person.Value == 35, scene + " replays production relationship write");
            }
            else if (scene == "Combat")
            {
                var edit = AnytimeDuel.Edits.Single();
                Check(edit.Item1 == "relationship:6" && edit.Item2 == 10 && edit.Item3 == 35, "duel edit uses stable numeric character ID");
                person.Value = 10; NumericEdits.ApplyRecorded(edit.Item1, edit.Item2, edit.Item3);
                Check(person.Value == 35, "duel edit applies through production replay dispatcher");
            }
            else Check(AnytimeStory.Edits.Count == 0 && AnytimeDuel.Edits.Count == 0, "free roam needs no replay journal");
        }
        foreach (string wait in new[] { "Say", "Choose", "Dice" })
        {
            UnityEngine.SceneManagement.SceneManager.Current = "Story";
            AnytimeStory.WaitKind = wait; AnytimeStory.Edits.Clear(); person.Value = 10;
            NumericEdits.SetRelationship(person, 20); NumericEdits.SetRelationship(person, 10);
            int cursor = StoryNumericEdits.Apply(AnytimeStory.Edits.ToArray(), 0, wait, "native-wait", NumericEdits.ApplyRecorded);
            Check(cursor == 2 && person.Value == 10, wait + " preserves edit and undo order at saved target");
        }
        foreach (string reason in new[] { "not loaded", "loading", "recovering", "animation", "settlement", "journal full" })
        {
            GameAccess.NumericEditBlockReason = reason;
            Reject(() => NumericEdits.SetRelationship(person, 20), reason);
        }
        GameAccess.NumericEditBlockReason = null;
        Plugin.Instance.Gate.Fail("blocked");
        Reject(() => NumericEdits.SetRelationship(person, 20), "mutation gate remains enforced");
        Plugin.Instance.Gate.NativeLoadStarted(); Plugin.Instance.Gate.NativeLoadCompleted();
        Reject(() => NumericEdits.SetRelationship(person, -1), "negative affection");
        Reject(() => NumericEdits.SetRelationship(person, 51), "character-specific cap");
        Reject(() => NumericEdits.SetRelationship(inactive, 20), "inactive character");
        Reject(() => NumericEdits.SetRelationship(null, 20), "null character");
        Reject(() => NumericEdits.SetRelationship(new RelationshipStat { Type = person.Type }, 20), "stale character reference after load");
        var player = PlayerStatManagerData.Instance;
        PlayerStatManagerData.Instance = null;
        Reject(() => NumericEdits.SetRelationship(person, 20), "unloaded player");
        PlayerStatManagerData.Instance = player;
        foreach (string key in new[] { "relationship:999", "relationship:bad", "relationship:-6", "relationship:8" })
            Reject(() => NumericEdits.ApplyRecorded(key, 10, 20), "invalid replay target " + key);
        Reject(() => NumericEdits.ApplyRecorded("relationship:6", 9, 20), "replay before-value mismatch");
        Reject(() => NumericEdits.ApplyRecorded("relationship:6", 10, 51), "replay above cap");
        Reject(() => NumericEdits.ApplyRecorded("relationship:6", 10, -1), "negative replay value");
        AnytimeStory.FailRecord = true;
        Reject(() => NumericEdits.SetRelationship(person, 20), "story journal failure rolls write back");
        AnytimeStory.FailRecord = false;
        UnityEngine.SceneManagement.SceneManager.Current = "Combat"; AnytimeDuel.FailRecord = true;
        Reject(() => NumericEdits.SetRelationship(person, 20), "duel journal failure rolls write back");
        AnytimeDuel.FailRecord = false;
        UnityEngine.SceneManagement.SceneManager.Current = "Free";
        NumericEdits.SetRelationship(person, 0); NumericEdits.SetRelationship(person, 50);
        Check(person.Value == 50, "inclusive zero and character cap boundaries");
        var stat = new GameStat(); player.Stats.List.Add(stat);
        NumericEdits.SetStat(stat, 20); NumericEdits.ApplyRecorded("stat:銀兩", 20, 30);
        Check(stat.Value == 30, "existing stat writes and replay remain supported");
        var item = new ItemData { Id = "book" }; ItemDatabase.Instance.Books.List.Add(item);
        NumericEdits.SetItem(GameItemType.Books, item, 2); NumericEdits.ApplyRecorded("item:1:book", 2, 3);
        Check(item.CurrentCount == 3, "existing item writes and replay remain supported");
        GrowthChecks();
        Console.WriteLine("PASS: " + checks + " production relationship-edit checks (managed stand-ins; no Unity runtime)");
    }
    private static void Invalid(Action action, string message)
    {
        try { action(); }
        catch (Exception error) when (error is InvalidOperationException || error is InvalidDataException)
        { checks++; return; }
        throw new Exception(message + " was accepted");
    }
    private static void GrowthChecks()
    {
        var collection = MissionManagerData.Instance.FlagsCollection[0];
        var faction = new FlagData { name = "H00001", State = 5 };
        var unrelated = new FlagData { name = "H04001", State = 5 };
        collection.List.AddRange(new[] { null, faction, unrelated });
        Check(NumericEdits.FactionAffinities.GetLength(0) == 16, "16 verified faction entries");
        foreach (string scene in new[] { "Free", "Story", "Break", "Combat" })
        {
            UnityEngine.SceneManagement.SceneManager.Current = scene;
            faction.State = 5; AnytimeStory.Edits.Clear(); AnytimeDuel.Edits.Clear();
            NumericEdits.SetFactionAffinity(faction, -20);
            Check(faction.State == -20 && unrelated.State == 5, scene + " edits the signed faction value only");
            if (scene == "Story" || scene == "Break")
            {
                var edit = AnytimeStory.Edits.Single();
                Check(edit.Key.EndsWith("\nfaction:H00001"), "story records faction identity");
                faction.State = 5;
                Check(StoryNumericEdits.Apply(AnytimeStory.Edits.ToArray(), 0, AnytimeStory.WaitKind,
                    AnytimeStory.WaitKey, NumericEdits.ApplyRecorded) == 1 && faction.State == -20, "faction story round trip");
            }
            else if (scene == "Combat")
            {
                var edit = AnytimeDuel.Edits.Single(); faction.State = 5;
                NumericEdits.ApplyRecorded(edit.Item1, edit.Item2, edit.Item3);
                Check(faction.State == -20, "faction duel round trip");
            }
            else Check(AnytimeStory.Edits.Count == 0 && AnytimeDuel.Edits.Count == 0, "free faction edit needs no journal");
        }
        UnityEngine.SceneManagement.SceneManager.Current = "Free";
        NumericEdits.SetFactionAffinity(faction, int.MinValue);
        NumericEdits.SetFactionAffinity(faction, int.MaxValue);
        Check(faction.State == int.MaxValue, "no invented 0/100 cap on native integer flags");
        Invalid(() => NumericEdits.SetFactionAffinity(unrelated, 8), "unrelated character flag");
        Invalid(() => NumericEdits.SetFactionAffinity(new FlagData { name = faction.name }, 8), "stale flag");
        Invalid(() => NumericEdits.SetFactionAffinity(null, 8), "null flag");
        GameAccess.NumericEditBlockReason = "loading";
        Invalid(() => NumericEdits.SetFactionAffinity(faction, 8), "faction blocked during loading");
        Check(faction.State == int.MaxValue, "blocked faction write leaves state alone");
        GameAccess.NumericEditBlockReason = null;
        collection.List.Add(new FlagData { name = faction.name });
        Invalid(() => NumericEdits.SetFactionAffinity(faction, 8), "ambiguous flag id");
        collection.List.RemoveAt(collection.List.Count - 1);
        collection.List.Add(faction);
        Check(NumericEdits.FactionAffinity(faction.name) == faction, "same asset aliases are safe");
        collection.List.RemoveAt(collection.List.Count - 1);
        foreach (string key in new[] { "faction:H04001", "faction:missing", "faction:" })
            Invalid(() => NumericEdits.ApplyRecorded(key, 5, 8), "unverified replay target");
        Invalid(() => NumericEdits.ApplyRecorded("faction:H00001", 5, 8), "faction before mismatch");
        foreach (string scene in new[] { "Story", "Combat" })
        {
            UnityEngine.SceneManagement.SceneManager.Current = scene;
            AnytimeStory.FailRecord = AnytimeDuel.FailRecord = true;
            Invalid(() => NumericEdits.SetFactionAffinity(faction, 8), "faction journal failure");
            Check(faction.State == int.MaxValue, "journal failure rolls faction back");
            AnytimeStory.FailRecord = AnytimeDuel.FailRecord = false;
        }
        var player = PlayerStatManagerData.Instance;
        var tactics = new PlayerTalentData { Id = "9100", Level = 2 };
        var medicine = new PlayerTalentData { Id = "2101", Level = 0, _maxLevel = 7 };
        var other = new PlayerTalentData { Id = "8101", Level = 0, _maxLevel = 1 };
        player.Talents.List.AddRange(new[] { null, tactics, medicine, other });
        Check(NumericEdits.TalentLevels.GetLength(0) == 6, "six verified level editors");
        GameAccess.CanEditValues = false;
        Invalid(() => NumericEdits.SetTalentLevel(tactics, 3), "non-idle talent edit");
        Check(tactics.Level == 2, "non-idle edit leaves loaded effects alone");
        UnityEngine.SceneManagement.SceneManager.Current = "Free";
        GameAccess.CanEditValues = true;
        NumericEdits.SetTalentLevel(tactics, 9); NumericEdits.SetTalentLevel(medicine, 7);
        Check(tactics.Level == 9 && medicine.Level == 7 && other.Level == 0, "native per-talent limits and exact targets");
        Invalid(() => NumericEdits.SetTalentLevel(tactics, 10), "tactics cap");
        Invalid(() => NumericEdits.SetTalentLevel(medicine, 8), "medicine cap");
        Invalid(() => NumericEdits.SetTalentLevel(medicine, -1), "negative level");
        Invalid(() => NumericEdits.SetTalentLevel(other, 1), "unlisted talent");
        Invalid(() => NumericEdits.SetTalentLevel(null, 1), "null talent");
        Invalid(() => NumericEdits.SetTalentLevel(new PlayerTalentData { Id = tactics.Id }, 1), "stale talent");
        player.Talents.List.Add(new PlayerTalentData { Id = tactics.Id });
        Invalid(() => NumericEdits.SetTalentLevel(tactics, 1), "ambiguous talent");
        player.Talents.List.RemoveAt(player.Talents.List.Count - 1);
        GameAccess.NumericEditBlockReason = "recovering";
        Invalid(() => NumericEdits.SetTalentLevel(tactics, 1), "numeric gate still applies to talents");
        Check(tactics.Level == 9, "blocked talent remains unchanged");
        GameAccess.NumericEditBlockReason = null;
        NumericEdits.SetTalentLevel(tactics, 0); NumericEdits.SetTalentLevel(medicine, 0);
        Check(tactics.Level == 0 && medicine.Level == 0, "level edits can be undone to unlearned");
        PlayerStatManagerData.Instance = null;
        Invalid(() => NumericEdits.SetTalentLevel(tactics, 1), "unloaded talent");
        PlayerStatManagerData.Instance = player;
        var missions = MissionManagerData.Instance; MissionManagerData.Instance = null;
        Invalid(() => NumericEdits.SetFactionAffinity(faction, 1), "unloaded flag manager");
        MissionManagerData.Instance = missions;
        GameAccess.CanEditValues = false;
    }
}
