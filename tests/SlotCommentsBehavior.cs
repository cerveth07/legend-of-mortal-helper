// Production comment persistence and rendering against managed game stand-ins.
// Temporary files only; no player saves or installed configuration are accessed.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MortalHelper.Game;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string method) { } }
    public static class AccessTools { public static MethodInfo Method(Type type, string name) => type.GetMethod(name); }
}
namespace UnityEngine.UI { public sealed class Text { public string text; } }
namespace UnityEngine
{
    public class GameObject { public bool activeInHierarchy = true; public T GetComponentInParent<T>() where T : class => null; }
    public class Component { public GameObject gameObject = new GameObject(); }
    public static class Resources { public static T[] FindObjectsOfTypeAll<T>() => new T[0]; }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem { public static EventSystem current; public UnityEngine.GameObject currentSelectedGameObject; }
}
namespace Mortal.Core
{
    public sealed class SaveSlotPanel : UnityEngine.Component { }
    public sealed class RecentSaveSlotPanel : UnityEngine.Component { }
    public sealed class AutoSaveSlotPanel : UnityEngine.Component { }
    public sealed class SaveGamePanel { }
    public sealed class GameSave { public long TimeTick; }
    public sealed class Transform { public void SetSiblingIndex(int index) { } }
    public sealed class LoadSlotPanel : UnityEngine.Component { public Transform transform = new Transform(); }
    public sealed class LoadGamePanel { public static LoadGamePanel Instance; public void UpdateSlot() { } }
}
namespace MortalHelper.Game
{
    public enum UiLang { Hans, Hant }
    public sealed class Config { public string ConfigFilePath; }
    public sealed class IntOption { public int Value = 99; }
    public sealed class BoolOption { public bool Value; }
    public sealed class Options
    {
        public Config Config = new Config(); public UiLang LanguageKind;
        public IntOption SlotCount = new IntOption(); public BoolOption SortRecent = new BoolOption();
    }
    public sealed class Plugin
    {
        public static Plugin Instance = new Plugin(); public static bool Active = true;
        public Options Options = new Options(); public int Reports;
        public void Report(Exception error) { Reports++; }
        public void Log(string message) { }
    }
    public static class GameAccess
    {
        public static T Field<T>(object target, string name) => (T)target.GetType().GetField(name).GetValue(target);
        public static void Set<T>(object target, string name, T value) => target.GetType().GetField(name).SetValue(target, value);
    }
    public static class AnytimeNative
    {
        public static string Folder;
        public static bool Numbered(string slot) => slot != null && slot.Length == 3 && slot != "000" && slot.All(char.IsDigit);
        public static string PathFor(string slot) => Path.Combine(Folder, "Save_" + slot + ".dat");
    }
    public static class AnytimeStory
    {
        public static int Verifications;
        public static bool OrdinalSaveVerified(string path, object data)
        {
            Verifications++;
            return File.Exists(path) && File.ReadAllText(path) == data as string;
        }
    }
}
internal static class SlotCommentsBehavior
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void ResetCache()
    {
        typeof(SaveSlotComments).GetField("loaded", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
        var map = (System.Collections.IDictionary)typeof(SaveSlotComments).GetField("Map", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        map.Clear();
    }
    private sealed class Panel
    {
        public string _slot = "001";
        public UnityEngine.UI.Text[] _timeText = { new UnityEngine.UI.Text { text = "Native save\n〔备注〕Old" }, null };
    }
    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Run()
    {
        string folder = Path.Combine(Path.GetTempPath(), "mortal-slot-comments-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Plugin.Instance.Options.Config.ConfigFilePath = Path.Combine(folder, "config.cfg");
            AnytimeNative.Folder = folder;
            SaveSlotComments.Selected = "002";
            SaveSlotComments.Set("001", " Old checkpoint "); SaveSlotComments.Set("002", "Other save");
            ResetCache();
            Check(SaveSlotComments.Get("001") == "Old checkpoint", "ordinary reads and restart keep existing notes");
            string path = AnytimeNative.PathFor("001");
            SaveSlotComments.NativeSaved("001", path, "new save");
            Check(SaveSlotComments.Get("001") == "Old checkpoint", "missing file cannot clear note");
            File.WriteAllText(path, "old save");
            SaveSlotComments.NativeSaved("001", path, "new save");
            Check(SaveSlotComments.Get("001") == "Old checkpoint", "failed or rolled-back write keeps old note");
            File.WriteAllText(path, "new save");
            SaveSlotComments.NativeSaved("001", AnytimeNative.PathFor("002"), "new save");
            SaveSlotComments.NativeSaved("auto", path, "new save");
            SaveSlotComments.NativeSaved(null, path, "new save");
            Check(SaveSlotComments.Get("001") == "Old checkpoint", "unrelated path or autosave cannot clear selected manual note");
            SaveSlotComments.NativeSaved("001", path, "new save");
            Check(SaveSlotComments.Get("001") == "" && SaveSlotComments.Get("002") == "Other save", "successful ordinary overwrite clears only its own slot");
            Check(SaveSlotComments.Selected == "002", "save target is independent of UI selection");
            ResetCache();
            Check(SaveSlotComments.Get("001") == "" && !File.ReadAllText(SaveSlotComments.FilePath).Contains("Old checkpoint"), "clearing persists to disk and across restart");
            int verified = AnytimeStory.Verifications;
            SaveSlotComments.NativeSaved("001", path, "new save");
            Check(AnytimeStory.Verifications == verified, "slots without notes skip expensive save verification");
            foreach (string slot in new[] { "001", "002" })
            {
                SaveSlotComments.Set(slot, "Before committed story/duel save");
                SaveSlotComments.Saved(slot);
                Check(SaveSlotComments.Get(slot) == "", "committed story/duel path clears note " + slot);
            }
            SaveSlotComments.Set("000", "Special slot"); SaveSlotComments.Saved("000");
            Check(SaveSlotComments.Get("000") == "Special slot", "non-numbered save notifications leave notes alone");
            SaveSlotComments.Set("001", "Keep if comment file cannot be updated");
            using (var locked = new FileStream(SaveSlotComments.FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                SaveSlotComments.Saved("001");
            Check(Plugin.Instance.Reports == 1 && SaveSlotComments.Get("001") == "Keep if comment file cannot be updated", "note I/O failure is reported without propagating into committed save rollback");

            foreach (string marker in new[] { "〔备注〕", "〔備註〕" })
            {
                Check(SaveSlotCommentText.Format("Native save\n" + marker + "Old", "", false) == "Native save", "clearing strips old localized suffix");
                Check(SaveSlotCommentText.Format("Native save\n" + marker + "Old", "New", false) == "Native save\n〔备注〕New", "editing replaces suffix without duplicates");
            }
            Check(SaveSlotCommentText.Format("Native save\n〔备注〕Old", "New", true) == "Native save\n〔備註〕New", "traditional marker survives refreshed note");
            Check(SaveSlotCommentText.Format("Native save  ", "", false) == "Native save  ", "unannotated native text remains unchanged");
            var panel = new Panel(); SaveSlotComments.Set("001", "");
            SaveSlotCommentText.Append(panel);
            Check(panel._timeText[0].text == "Native save", "production UI append removes stale suffix when note is gone");
            SaveSlotComments.Set("001", "New"); SaveSlotCommentText.Append(panel); SaveSlotCommentText.Append(panel);
            Check(panel._timeText[0].text == "Native save · New", "repeated UI refresh stays idempotent on one line");
            SaveSlotComments.Set("001", "八字备注测试长文本"); SaveSlotCommentText.Append(panel);
            Check(!panel._timeText[0].text.Contains("\n") && panel._timeText[0].text.EndsWith("…"), "long preview is bounded to the native row");
            Console.WriteLine("PASS: " + checks + " production slot-comment checks (temporary files; no player saves)");
        }
        finally { Directory.Delete(folder, true); }
    }
}
