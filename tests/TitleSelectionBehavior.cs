// Executes production Titles against managed stand-ins. Native menus reject
// numeric edits here, reproducing the activity-list state without starting Unity.
using System;
using System.Collections.Generic;
using System.Reflection;
using Mortal.Core;
using MortalHelper.Core;
using MortalHelper.Game;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) { } }
}
namespace Lean.Localization
{
    public static class LeanLocalization
    {
        public static string GetTranslationText(string key) =>
            key == "PlayerInfo/Title/0" ? "Native title" : key == "PlayerInfo/Title/18" ? "Cosmetic title" : null;
    }
}
namespace UnityEngine
{
    public sealed class GameObject { public bool activeInHierarchy = true; }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => throw new NotSupportedException();
        public static string ToJson(object value, bool pretty = false) => throw new NotSupportedException();
    }
}
namespace UnityEngine.UI { public sealed class Text { public string text; } }
namespace Mortal.Core
{
    public enum GameStatType { 稱號 }
    public sealed class GameStat { public GameStatType StatType; public int Value; }
    public sealed class Stats { public GameStat Title = new GameStat(); public GameStat Get(GameStatType type) => Title; }
    public sealed class PlayerStatManagerData
    {
        public static PlayerStatManagerData Instance = new PlayerStatManagerData();
        public Stats Stats = new Stats();
    }
    public sealed class StatSave { public string Key; public int Value; }
    public sealed class GameSave { public List<StatSave> Stats = new List<StatSave>(); public string RestoreData; }
    public sealed class LeanLocalizationResolver { }
}
namespace Mortal.Free
{
    public sealed class FreeManager
    {
        public static FreeManager Instance = new FreeManager();
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public UnityEngine.UI.Text _titleText = new UnityEngine.UI.Text();
    }
}
namespace MortalHelper.Game
{
    public sealed class Flag { public bool Value = true; }
    public sealed class Options { public Flag Enabled = new Flag(); }
    public sealed class Plugin
    {
        public static Plugin Instance = new Plugin();
        public Options Options = new Options();
        public OperationGate Gate = new OperationGate();
        public void Report(Exception error) => throw new Exception("Unexpected refresh error", error);
    }
    public static class GameAccess
    {
        public static bool Loaded = true;
        public static bool CanEditValues = false;
        public static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
    }
    public static class AnytimeStory { public static bool Replaying; }
    public static class AnytimeDuel { public static bool Replaying; }
}
internal static class TitleSelectionBehavior
{
    private static int checks;
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        checks++;
    }
    private static object Patch(string type, string method, params object[] args) =>
        typeof(Titles).GetNestedType(type, BindingFlags.NonPublic)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    private static void CheckBlocked(string message)
    {
        string reason = Titles.SelectionBlockReason;
        Check(reason != null, message + " disables UI");
        string display = Mortal.Free.FreeManager.Instance._titleText.text;
        Check(Titles.Choose(0) == reason && Titles.History.Selected == 18 &&
            Mortal.Free.FreeManager.Instance._titleText.text == display, message + " rejects queued selection without mutation");
    }
    private static void Main()
    {
        Titles.Reset();
        Check(!GameAccess.CanEditValues && Titles.SelectionBlockReason == null,
            "native activity/menu numeric lock must not disable cosmetic titles");
        Check(Titles.Choose(18).Contains("Cosmetic title") && Titles.History.Selected == 18,
            "choose unearned cosmetic title while native menu is open");
        Check(Mortal.Free.FreeManager.Instance._titleText.text == "Cosmetic title" && Titles.Current == 0 && !Titles.History.Contains(18),
            "refresh changes display without granting title or altering native stat");
        Check(Titles.Choose(-1).Contains("跟随剧情") && Titles.History.Selected == -1 &&
            Mortal.Free.FreeManager.Instance._titleText.text == "Native title", "follow story works in native menu");

        Mortal.Free.FreeManager.Instance = null;
        Check(Titles.Choose(18).Contains("Cosmetic title") && Titles.History.Selected == 18,
            "selection works outside free roam; next native view uses cosmetic metadata");
        Mortal.Free.FreeManager.Instance = new Mortal.Free.FreeManager();
        Titles.Tick();
        Check(Mortal.Free.FreeManager.Instance._titleText.text == "Cosmetic title", "entering free roam applies selected appearance");
        Titles.Choose(999); Titles.Choose(-2); Titles.Choose(1000);
        Check(Titles.History.Selected == 18, "invalid or missing titles do not replace selection");

        GameAccess.Loaded = false; CheckBlocked("unloaded/loading scene"); GameAccess.Loaded = true;
        AnytimeStory.Replaying = true; CheckBlocked("story replay"); AnytimeStory.Replaying = false;
        AnytimeDuel.Replaying = true; CheckBlocked("duel replay"); AnytimeDuel.Replaying = false;
        var plugin = Plugin.Instance;
        Plugin.Instance = null; CheckBlocked("uninitialized plugin"); Plugin.Instance = plugin;
        plugin.Gate.Begin(); CheckBlocked("recovery preflight");
        plugin.Gate.Advance(RecoveryPhase.Loading); CheckBlocked("recovery loading");
        plugin.Gate.Advance(RecoveryPhase.Replaying); CheckBlocked("recovery replay");
        plugin.Gate.Advance(RecoveryPhase.Verifying); CheckBlocked("recovery verification");
        plugin.Gate.Advance(RecoveryPhase.Idle);
        plugin.Gate.Fail("test"); CheckBlocked("failed recovery");
        plugin.Gate.NativeLoadStarted(); CheckBlocked("native save loading");
        plugin.Gate.NativeLoadCompleted();

        var save = new GameSave();
        save.Stats.Add(new StatSave { Key = "native-title", Value = Titles.Current });
        Patch("SavePatch", "Postfix", save);
        Patch("LoadPatch", "Prefix");
        Check(Titles.SelectionBlockReason != null && Titles.Choose(0) == Titles.SelectionBlockReason &&
            Titles.History.Selected == -1, "stat loading blocks selection even before scene load flag changes");
        Patch("LoadPatch", "Postfix", save);
        Patch("LoadPatch", "Finalizer", new object[] { null });
        Check(Titles.History.Selected == 18 && Titles.Current == 0 && Titles.SelectionBlockReason == null,
            "save/load restores cosmetic selection without changing native title");
        string key = "PlayerInfo/Title/0";
        object[] args = { key };
        Patch("DisplayPatch", "Prefix", args);
        Check((string)args[0] == "PlayerInfo/Title/18", "native localized display uses selected title");
        plugin.Options.Enabled.Value = false;
        args[0] = key; Patch("DisplayPatch", "Prefix", args);
        Check((string)args[0] == key, "disabled master switch keeps native display");
        plugin.Options.Enabled.Value = true;
        Titles.Choose(-1);
        Check(Titles.Current == 0 && Titles.History.Selected == -1, "restored save can return to story appearance");
        Console.WriteLine("PASS: " + checks + " production title-selection checks (managed stand-ins; no Unity runtime)");
    }
}
