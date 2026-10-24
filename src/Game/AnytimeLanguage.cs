using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MortalHelper.Recovery;

namespace MortalHelper.Game
{
    // Replay keys are SHA-256 digests of the *localised* dialogue text, and the option
    // keys are built from the displayed option strings. A checkpoint therefore only
    // replays in the language it was recorded in: after the player switches the game
    // language, the very first line of the same script digests differently and the
    // replay aborts with "剧情对话顺序与存档不同", which hides the real cause.
    //
    // This records the language with the checkpoint and replays in that language,
    // then puts the player's own choice back. Records written before the field
    // existed carry their language inside the captured Lua string table instead.
    internal static class AnytimeLanguage
    {
        internal const int Unknown = -1;
        private static readonly string[] Names = { "ChineseTraditional", "ChineseSimplified", "Korean" };
        private static readonly string[] Labels = { "繁體中文", "简体中文", "한국어" };

        private static MethodInfo getter, setter;
        private static bool probed;
        private static int restoreTo = Unknown;

        internal static string Describe(int language)
        {
            if (language == Unknown) return "未知";
            return language >= 0 && language < Labels.Length
                ? Labels[language] + "(" + language + ")"
                : "未知(" + language + ")";
        }

        internal static int Current
        {
            get
            {
                Probe();
                if (getter == null) return Unknown;
                try { return (int)getter.Invoke(null, null); }
                catch (Exception) { return Unknown; }
            }
        }

        private static void Probe()
        {
            if (probed) return;
            probed = true;
            try
            {
                Type settings = typeof(Mortal.Core.SaveSystem).Assembly.GetType("Mortal.Core.SystemSettings");
                if (settings == null) return;
                getter = settings.GetMethod("get_Language", BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static) ?? settings.GetMethod("GetLanguage", BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Static);
                setter = settings.GetMethod("SetLanguage", BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static);
                if (setter != null && setter.GetParameters().Length != 1) setter = null;
            }
            catch (Exception error)
            {
                Report("language API probe failed: " + error.Message);
            }
        }

        // Language captured at save time. Unknown when the record predates the field.
        internal static int ForRecord(AnytimeStoryRecord record)
        {
            if (record == null) return Unknown;
            if (record.Language != Unknown) return record.Language;
            return InferFromGlobals(record);
        }

        // Older records dumped the live Lua string table, whose inner keys are the
        // language names the game loaded for that run: enough to name the language.
        internal static int InferFromGlobals(AnytimeStoryRecord record)
        {
            if (record == null) return Unknown;
            int hit = Scan(record.ExpectedGlobals);
            return hit != Unknown ? hit : Scan(record.BaselineGlobals);
        }

        private static int Scan(AnytimeLuaValue[] values)
        {
            if (values == null) return Unknown;
            foreach (var value in values)
            {
                int hit = Scan(value, 0);
                if (hit != Unknown) return hit;
            }
            return Unknown;
        }

        private static int Scan(AnytimeLuaValue value, int depth)
        {
            if (value == null || depth > 8 || value.Entries == null) return Unknown;
            foreach (var entry in value.Entries)
            {
                if (entry == null) continue;
                int hit = NameIndex(entry.Key);
                if (hit != Unknown) return hit;
                int nested = Scan(entry, depth + 1);
                if (nested != Unknown) return nested;
            }
            return Unknown;
        }

        private static int Scan(AnytimeLuaEntry entry, int depth)
        {
            if (entry == null || depth > 8 || entry.Entries == null) return Unknown;
            foreach (var child in entry.Entries)
            {
                if (child == null) continue;
                int hit = NameIndex(child.Key);
                if (hit != Unknown) return hit;
                int nested = Scan(child, depth + 1);
                if (nested != Unknown) return nested;
            }
            return Unknown;
        }

        private static int NameIndex(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 32) return Unknown;
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(text, Names[i], StringComparison.Ordinal)) return i;
            return Unknown;
        }

        // Called once the baseline is loaded and the replay is about to run.
        internal static void EnterReplay(int recorded)
        {
            if (recorded == Unknown) return;
            int current = Current;
            if (current == Unknown || current == recorded) return;
            if (setter == null)
            {
                Report("language differs (" + Describe(current) + " -> " + Describe(recorded) +
                    ") but the game exposes no way to switch it; the replay will most likely fail.");
                return;
            }
            try
            {
                setter.Invoke(null, new object[] { recorded });
                restoreTo = current;
                Report("language switched " + Describe(current) + " -> " + Describe(recorded) + " for the replay");
            }
            catch (Exception error)
            {
                restoreTo = Unknown;
                Report("language switch failed: " + error.Message);
            }
        }

        // Puts the player's own language back: success, failure and cancel all end here.
        internal static void LeaveReplay()
        {
            if (restoreTo == Unknown) return;
            int target = restoreTo;
            restoreTo = Unknown;
            if (setter == null) return;
            try
            {
                setter.Invoke(null, new object[] { target });
                Report("language restored to " + Describe(target));
            }
            catch (Exception error)
            {
                Report("language restore failed: " + error.Message);
            }
        }

        internal static void Reset() { restoreTo = Unknown; }

        // Extra sentence appended to a replay mismatch so the message names the cause
        // instead of blaming the dialogue order alone.
        internal static string Hint(int recorded)
        {
            int current = Current;
            if (recorded == Unknown || current == Unknown || recorded == current) return "";
            return "（该检查点录制于" + Describe(recorded) + "，当前游戏语言为" + Describe(current) + "）";
        }

        private static void Report(string message)
        {
            try { Plugin.Instance?.Log("[Anytime] " + message); } catch (Exception) { }
        }
    }

    // The character-intro panel is a story wait: the script does not continue until the
    // panel coroutine finishes. Replaying re-opens it, and its handler throws inside
    // StoryManager.OnPanelOpen (CustomMenuDialog.InteractableCacheButton on a missing
    // menu dialog), which kills the coroutine and leaves the replay waiting forever
    // (observed at 51/72 of ch2_5_1). It is pure presentation, so skip it while replaying.
    internal static class AnytimeIntroSkip
    {
        private static bool installed;

        internal static void Install(Harmony harmony)
        {
            if (installed) return;
            try
            {
                // Resolved by name: referencing the game type here would break the build
                // (and the plugin) on any version where it is not public.
                Type panel = typeof(Mortal.Core.SaveSystem).Assembly.GetType("Mortal.Core.CharacterIntroPanel");
                MethodInfo target = panel == null ? null : AccessTools.Method(panel, "ShowCharacterIntro");
                if (target == null)
                {
                    Report("character intro panel not found; intro panels stay enabled during replays");
                    return;
                }
                installed = true;
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(AnytimeIntroSkip).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
                Report("character intro panels are skipped while replaying");
            }
            catch (Exception error)
            {
                Report("intro panel patch failed: " + error.Message);
            }
        }

        private static bool Prefix(ref IEnumerator __result)
        {
            if (!AnytimeStory.Replaying) return true;
            __result = Empty();
            return false;
        }

        private static IEnumerator Empty() { yield break; }

        private static void Report(string message)
        {
            try { Plugin.Instance?.Log("[Anytime] " + message); } catch (Exception) { }
        }
    }
}
