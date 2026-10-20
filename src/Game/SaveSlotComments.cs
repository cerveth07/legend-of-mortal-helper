using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Mortal.Core;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Per-slot comments stored beside the config as plain lines (slot TAB comment).
    // The previous panel kept them in SaveSlotComments.ini inside the save folder;
    // this keeps them out of the save directory on purpose and documents the change.
    internal static class SaveSlotComments
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static bool loaded;

        internal static string Selected { get; set; }

        internal static string FilePath
        {
            get
            {
                try { return Path.Combine(Path.GetDirectoryName(Plugin.Instance.Options.Config.ConfigFilePath), "cerveth.mortal.helper.slots.txt"); }
                catch (Exception) { return null; }
            }
        }

        internal static string Get(string slot)
        {
            Load();
            string value;
            return slot != null && Map.TryGetValue(slot, out value) ? value : "";
        }

        internal static void Set(string slot, string comment)
        {
            if (string.IsNullOrEmpty(slot)) return;
            Load();
            if (string.IsNullOrWhiteSpace(comment)) Map.Remove(slot);
            else Map[slot] = comment.Trim();
            Save();
        }

        // Slot -> comment snapshot, ordered by slot number so the panel can list them.
        internal static List<KeyValuePair<string, string>> All()
        {
            Load();
            var list = new List<KeyValuePair<string, string>>(Map);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        internal static void Clear()
        {
            Load();
            Map.Clear();
            Save();
        }

        private static void Load()
        {
            if (loaded) return;
            try
            {
                string path = FilePath;
                if (path == null) throw new IOException("无法定位备注文件。");
                if (!File.Exists(path)) { loaded = true; return; }
                foreach (string line in File.ReadAllLines(path, Utf8))
                {
                    int tab = line.IndexOf('\t');
                    if (tab <= 0) continue;
                    Map[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
                loaded = true;
            }
            catch
            {
                Map.Clear(); loaded = false;
                throw;
            }
        }

        private static void Save()
        {
            string path = FilePath;
            if (path == null) throw new IOException("无法定位备注文件。");
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var builder = new StringBuilder();
                foreach (var pair in Map) builder.Append(pair.Key).Append('\t').Append(pair.Value).Append('\n');
                byte[] bytes = Utf8.GetBytes(builder.ToString());
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            catch
            {
                // Reload the last committed map rather than show an unsaved edit.
                Map.Clear(); loaded = false;
                throw;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    // Appends the stored comment to the slot title text the game itself writes.
    internal static class SaveSlotCommentText
    {
        private const string Marker = "〔备注〕";

        internal static void Append(object panel)
        {
            try
            {
                string slot = GameAccess.Field<string>(panel, "_slot");
                string comment = SaveSlotComments.Get(slot);
                if (string.IsNullOrEmpty(comment)) return;
                var texts = GameAccess.Field<Text[]>(panel, "_titleText");
                if (texts == null) return;
                foreach (var text in texts)
                {
                    if (text == null) continue;
                    string current = text.text ?? "";
                    int at = current.IndexOf(Marker, StringComparison.Ordinal);
                    if (at >= 0) current = current.Substring(0, at);
                    text.text = current.TrimEnd() + "\n" + Marker + comment;
                }
            }
            catch (Exception) { }
        }
    }

    [HarmonyPatch(typeof(SaveSlotPanel), "SetupText")]
    internal static class SaveSlotCommentPatch
    {
        private static void Postfix(SaveSlotPanel __instance) { SaveSlotCommentText.Append(__instance); }
    }

    [HarmonyPatch(typeof(RecentSaveSlotPanel), "SetupText")]
    internal static class RecentSlotCommentPatch
    {
        private static void Postfix(RecentSaveSlotPanel __instance) { SaveSlotCommentText.Append(__instance); }
    }

    [HarmonyPatch(typeof(AutoSaveSlotPanel), "SetupText")]
    internal static class AutoSlotCommentPatch
    {
        private static void Postfix(AutoSaveSlotPanel __instance) { SaveSlotCommentText.Append(__instance); }
    }

    [HarmonyPatch(typeof(SaveSlotPanel), "SelectButton")]
    internal static class SaveSlotSelectPatch
    {
        private static void Postfix(SaveSlotPanel __instance)
        {
            try { SaveSlotComments.Selected = GameAccess.Field<string>(__instance, "_slot"); }
            catch (Exception) { }
        }
    }

    // Slot count comes from _slotCount before CreateSlot instantiates the prefabs,
    // so overriding it there changes only how many rows the panel builds.
    internal static class SaveSlotCount
    {
        internal static int Configured
        {
            get
            {
                var plugin = Plugin.Instance;
                if (plugin == null) return 0;
                int value = plugin.Options.SlotCount.Value;
                return value >= 2 && value <= 999 ? value : 0;
            }
        }

        // The row count the panel actually built is the only honest proof that the
        // configured slot count reached the UI.
        internal static void Report(object panel)
        {
            try
            {
                var slots = GameAccess.Field<Array>(panel, "_saveSlots");
                Plugin.Instance?.Log("[SlotCount] " + (panel == null ? "?" : panel.GetType().Name)
                    + " rows=" + (slots == null ? -1 : slots.Length));
            }
            catch (Exception error) { Plugin.Instance?.Log("[SlotCount] report failed: " + error.Message); }
        }

        internal static void Apply(object panel)
        {
            int count = Configured;
            if (count <= 0) return;
            try
            {
                GameAccess.Set(panel, "_slotCount", count);
                Plugin.Instance?.Log("[SlotCount] applied " + count + " to "
                    + (panel == null ? "?" : panel.GetType().Name));
            }
            catch (Exception error) { Plugin.Instance?.Log("[SlotCount] failed: " + error.Message); }
        }
    }

    [HarmonyPatch(typeof(SaveGamePanel), "CreateSlot")]
    internal static class SavePanelSlotCountPatch
    {
        private static void Prefix(SaveGamePanel __instance) { SaveSlotCount.Apply(__instance); }
        private static void Postfix(SaveGamePanel __instance) { SaveSlotCount.Report(__instance); }
    }

    [HarmonyPatch(typeof(LoadGamePanel), "CreateSlot")]
    internal static class LoadPanelSlotCountPatch
    {
        private static void Prefix(LoadGamePanel __instance) { SaveSlotCount.Apply(__instance); }
        private static void Postfix(LoadGamePanel __instance) { SaveSlotCount.Report(__instance); }
    }

    // Recency sorting only reorders the row siblings the panel already built; slot
    // numbers and save files are untouched.
    internal static class SaveSlotSort
    {
        internal static bool Enabled => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.SortRecent.Value;

        internal static void Apply(LoadGamePanel panel)
        {
            if (!Enabled || panel == null) return;
            try
            {
                var slots = GameAccess.Field<LoadSlotPanel[]>(panel, "_saveSlots");
                if (slots == null) return;
                var ordered = new List<LoadSlotPanel>();
                foreach (var slot in slots) if (slot != null) ordered.Add(slot);
                ordered.Sort((left, right) => Tick(right).CompareTo(Tick(left)));
                for (int i = 0; i < ordered.Count; i++) ordered[i].transform.SetSiblingIndex(i);
            }
            catch (Exception) { }
        }

        private static long Tick(LoadSlotPanel slot)
        {
            try
            {
                var data = GameAccess.Field<GameSave>(slot, "_currentData");
                return data == null ? 0L : data.TimeTick;
            }
            catch (Exception) { return 0L; }
        }

        internal static void Refresh()
        {
            try
            {
                var panel = LoadGamePanel.Instance;
                if (panel == null) return;
                AccessTools.Method(typeof(LoadGamePanel), "UpdateSlot").Invoke(panel, null);
            }
            catch (Exception) { }
        }
    }

    [HarmonyPatch(typeof(LoadGamePanel), "UpdateSlot")]
    internal static class SaveSlotSortPatch
    {
        private static void Postfix(LoadGamePanel __instance) { SaveSlotSort.Apply(__instance); }
    }

    [HarmonyPatch(typeof(RecentSaveSlotPanel), "OnTitleClick")]
    internal static class RecentSlotSelectPatch
    {
        private static void Postfix(RecentSaveSlotPanel __instance)
        {
            try { SaveSlotComments.Selected = GameAccess.Field<string>(__instance, "_slot"); }
            catch (Exception) { }
        }
    }
}
