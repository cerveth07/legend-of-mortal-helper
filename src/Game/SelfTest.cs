using System;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    // Automated probe for the recovery baseline: it loads a real save through the
    // same call sequence the game load panel uses, then re-applies that exact save
    // JSON through the playback path and reports whether the game lands in the same
    // scene with the same save data. Writes no save files.
    internal static class SelfTest
    {
        private const string Slot = "003";
        private static int step = -1;
        private static float waitUntil;
        private static string first, second, sceneBefore;

        internal static bool Enabled => Plugin.Instance != null && Plugin.Instance.Options.SelfTest.Value;

        internal static void Tick()
        {
            if (!Enabled) return;
            if (step < 0) { Arm(); return; }
            if (Time.unscaledTime < waitUntil) return;
            try
            {
                switch (step)
                {
                    case 0: LoadNative(); break;
                    case 1:
                        sceneBefore = SceneManager.GetActiveScene().name;
                        first = Current();
                        Dump("A", first);
                        Log("scene=" + sceneBefore + " A=" + (first == null ? -1 : first.Length));
                        ReplayApply("re-apply own baseline");
                        Advance(2, 0.5f);
                        break;
                    case 2:
                        second = Current();
                        Dump("B", second);
                        Log("scene=" + SceneManager.GetActiveScene().name + " B=" + (second == null ? -1 : second.Length));
                        Log("verdict: scene " + (SceneManager.GetActiveScene().name == sceneBefore ? "SAME" : "CHANGED") +
                            " / data " + Verdict());
                        step = -1;
                        break;
                }
            }
            catch (Exception error)
            {
                Log("failed in step " + step + ": " + error.GetType().Name + " " + error.Message);
                step = -1;
            }
        }

        private static void Arm()
        {
            if (Time.unscaledTime < waitUntil) return;
            Advance(0, 6f);
            Log("armed; will load slot " + Slot + " through the native flow");
        }

        private static void Advance(int next, float wait)
        {
            step = next;
            waitUntil = Time.unscaledTime + wait;
        }

        // Exactly what LoadSlotPanel.OnTitleClick does, in the same order.
        private static void LoadNative()
        {
            var system = SaveSystem.Instance;
            if (system == null) { Log("no save system"); step = -1; return; }
            system.SetSlot(Slot);
            if (SoundManager.Instance != null) SoundManager.Instance.StopMusic();
            system.LoadGameData();
            system.SaveUniverseData();
            if (MissionManagerData.Instance != null) MissionManagerData.Instance.UpdateCheckMissions();
            if (SceneController.Instance != null) SceneController.Instance.LoadCurrentScene();
            Log("native load issued for slot " + Slot);
            Advance(1, 10f);
        }

        private static void ReplayApply(string why)
        {
            string json = first;
            if (string.IsNullOrEmpty(json)) { Log(why + ": nothing to apply"); return; }
            var save = JsonUtility.FromJson<GameSave>(json);
            if (save == null) { Log(why + ": parse returned null"); return; }
            var method = AccessTools.Method(typeof(SaveSystem), "ExecuteLoadGameData");
            if (method == null) { Log(why + ": no loader"); return; }
            method.Invoke(SaveSystem.Instance, new object[] { save });
            SaveSystem.Instance.SaveUniverseData();
            if (MissionManagerData.Instance != null) MissionManagerData.Instance.UpdateCheckMissions();
            if (SceneController.Instance != null) SceneController.Instance.LoadCurrentScene();
            Log(why + ": applied tick=" + save.TimeTick + " script=" + (save.CurrentStoryScript ?? "null") +
                " scene=" + (save.CurrentScene ?? "null"));
        }

        private static string Current()
        {
            try { return SaveSystem.Instance == null ? null : SaveSystem.Instance.GetCurrentSaveData(); }
            catch (Exception error) { Log("read current failed: " + error.Message); return null; }
        }

        private static void Dump(string name, string json)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(Plugin.Instance.Options.Config.ConfigFilePath);
                string path = System.IO.Path.Combine(dir, "selftest-" + name + ".json");
                System.IO.File.WriteAllText(path, json ?? "", new System.Text.UTF8Encoding(false));
                Log("wrote " + path);
            }
            catch (Exception error) { Log("dump failed: " + error.Message); }
        }

        private static string Verdict()
        {
            if (first == null || second == null) return "no save data";
            if (first == second) return "IDENTICAL";
            int shorter = Math.Min(first.Length, second.Length);
            int at = 0;
            while (at < shorter && first[at] == second[at]) at++;
            return "DIFFERENT at char " + at + " of " + shorter + " (A=" + first.Length + " B=" + second.Length + ")";
        }

        private static void Log(string text)
        {
            try { Plugin.Instance?.Log("SelfTest: " + text); } catch (Exception) { }
        }
    }
}
