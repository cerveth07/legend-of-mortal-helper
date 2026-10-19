using System;
using System.Collections.Generic;
using System.IO;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Recovery;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Headless feature probe. It never moves the cursor or steals focus: it only
    // calls game/plugin APIs and invokes buttons programmatically, so it can run
    // while the player uses the machine.
    internal static class Probe
    {
        private const string Slot = "003";
        private static int stage = -1;
        private static float waitUntil, started;
        private static int diceSeen, menuSeen, hintLogged, passes, failures;
        private static int diceBefore = -1;
        private static float diceCheckAt;

        internal static bool Enabled => Plugin.Instance != null && Plugin.Instance.Options.SelfTest.Value;

        internal static void Tick()
        {
            if (!Enabled) return;
            if (diceCheckAt > 0f && Time.unscaledTime >= diceCheckAt)
            {
                diceCheckAt = 0f;
                int now = StoryRecovery.Count;
                string value = now > diceBefore ? StoryRecovery.LastInteger.ToString() : "none";
                Check("dice point mode roll=" + value + " (expected 37)", value == "37");
            }
            if (stage < 0) { if (Time.unscaledTime < waitUntil) return; waitUntil = Time.unscaledTime + 5f; stage = 0; Log("armed"); return; }
            if (Time.unscaledTime < waitUntil) return;
            switch (stage)
            {
                case 0: LoadSave(); break;
                case 1: ApiChecks(); break;
                case 2: Drive(); break;
                case 3: Summary(); break;
            }
        }

        private static void LoadSave()
        {
            var system = SaveSystem.Instance;
            if (system == null) { Log("no save system"); stage = -1; return; }
            system.SetSlot(Slot);
            system.LoadGameData();
            system.SaveUniverseData();
            if (MissionManagerData.Instance != null) MissionManagerData.Instance.UpdateCheckMissions();
            if (SceneController.Instance != null) SceneController.Instance.LoadCurrentScene();
            Log("loaded slot " + Slot + "; scene=" + SceneManager.GetActiveScene().name);
            stage = 1; waitUntil = Time.unscaledTime + 6f;
        }

        // Pure API checks: these do not need any story interaction.
        private static void ApiChecks()
        {
            StoryRecovery.Begin();
            Check("save comment", CommentRoundTrip());
            Check("branch history", BranchRoundTrip());
            Check("game speed", SpeedRoundTrip());
            Check("dice config reachable", DiceConfig());
            Check("slot count config", SlotCountConfig());
            // Keep point mode armed while driving, otherwise DiceAdapter.Enabled is false
            // and the dice dialog is never recognised.
            Plugin.Instance.Options.Mode.Value = 1;
            Plugin.Instance.Options.Point.Value = "37";
            Log("point mode armed (37) for the drive phase");
            started = Time.unscaledTime;
            stage = 2; waitUntil = Time.unscaledTime + 0.3f;
        }

        private static bool CommentRoundTrip()
        {
            try
            {
                string before = SaveSlotComments.Get("003");
                SaveSlotComments.Set("003", "自检备注");
                bool ok = SaveSlotComments.Get("003") == "自检备注" &&
                          File.Exists(SaveSlotComments.FilePath) &&
                          File.ReadAllText(SaveSlotComments.FilePath).Contains("自检备注");
                SaveSlotComments.Set("003", before ?? "");
                return ok;
            }
            catch (Exception error) { Log("comment failed: " + error.Message); return false; }
        }

        private static bool BranchRoundTrip()
        {
            try
            {
                BranchHistory.Clear();
                BranchHistory.Record("probe-script", "选项甲");
                bool ok = BranchHistory.Has("probe-script", "选项甲") &&
                          BranchHistory.Recent(5).Count > 0 &&
                          File.Exists(BranchHistory.FilePath) &&
                          File.ReadAllText(BranchHistory.FilePath).Contains("probe-script");
                BranchHistory.Clear();
                return ok;
            }
            catch (Exception error) { Log("branch failed: " + error.Message); return false; }
        }

        private static bool SpeedRoundTrip()
        {
            try
            {
                Plugin.Instance.ApplySpeed(2f);
                bool ok = Math.Abs(Time.timeScale - 2f) < 0.01f;
                Plugin.Instance.ApplySpeed(1f);
                return ok && Math.Abs(Time.timeScale - 1f) < 0.01f;
            }
            catch (Exception error) { Log("speed failed: " + error.Message); return false; }
        }

        private static bool DiceConfig()
        {
            try
            {
                var options = Plugin.Instance.Options;
                int before = options.Mode.Value;
                options.Mode.Value = 1;
                options.Point.Value = "37";
                bool ok = DiceAdapter.Enabled && DiceAdapter.PointMode && options.Point.Value == "37";
                options.Mode.Value = before;
                return ok;
            }
            catch (Exception error) { Log("dice config failed: " + error.Message); return false; }
        }

        private static bool SlotCountConfig()
        {
            try
            {
                var options = Plugin.Instance.Options;
                int before = options.SlotCount.Value;
                options.SlotCount.Value = 40;
                bool ok = options.SlotCount.Value == 40;
                options.SlotCount.Value = before;
                return ok;
            }
            catch (Exception error) { Log("slot count failed: " + error.Message); return false; }
        }

        // Advances the story through game APIs only and inspects every dialog it gets.
        private static void Drive()
        {
            if (Time.unscaledTime - started > 600f) { stage = 3; waitUntil = Time.unscaledTime; return; }
            try
            {
                if (DiceAdapter.Dialog != null && DiceAdapter.Ready)
                {
                    diceSeen++;
                    RunDice(DiceAdapter.Dialog);
                    waitUntil = Time.unscaledTime + 3f;
                    return;
                }
                var menu = MenuDialog.GetMenuDialog();
                if (menu != null && menu.IsActive())
                {
                    menuSeen++;
                    InspectMenu(menu);
                    waitUntil = Time.unscaledTime + 2f;
                    return;
                }
                var say = SayDialog.GetSayDialog();
                if (say != null && say.isActiveAndEnabled)
                {
                    var input = say.GetComponent<DialogInput>();
                    if (input != null) input.SetNextLineFlag();
                }
            }
            catch (Exception error) { Log("drive failed: " + error.Message); }
            waitUntil = Time.unscaledTime + 0.25f;
        }

        private static void InspectMenu(MenuDialog menu)
        {
            var buttons = new List<Button>();
            int index = 0;
            foreach (var button in menu.GetComponentsInChildren<Button>())
            {
                if (button == null || !button.isActiveAndEnabled) continue;
                var text = button.GetComponentInChildren<Text>();
                string label = text == null ? button.name : text.text;
                hintLogged++;
                Log("menu option " + index + ": " + Trim(label) +
                    " hint=" + Trim(HintAdapter.Description(label)) +
                    " consequences=" + Trim(HintAdapter.Consequences(label)) +
                    " walked=" + BranchHistory.Has(HintAdapter.ScriptName, label));
                buttons.Add(button);
                index++;
            }
            Log("menu options=" + index + " script=" + HintAdapter.ScriptName);
            if (buttons.Count > 0)
            {
                // Choosing through the game's own option button also exercises the branch
                // recorder, which is what the 〔已走〕 label depends on.
                if (!BranchHistory.Enabled) Plugin.Instance.Options.ShowBranches.Value = true;
                var data = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(buttons[0].gameObject, data, ExecuteEvents.pointerClickHandler);
                buttons[0].onClick.Invoke();
                Log("chose option 0; recorded=" + BranchHistory.Has(HintAdapter.ScriptName,
                    buttons[0].GetComponentInChildren<Text>() == null ? "" : buttons[0].GetComponentInChildren<Text>().text));
            }
        }

        private static void RunDice(Mortal.Story.DiceMenuDialog dialog)
        {
            try
            {
                var options = Plugin.Instance.Options;
                int before = StoryRecovery.Count;
                Log("dice dialog range=" + Range(dialog, "_diceMin") + ".." + Range(dialog, "_diceMax") +
                    " results=" + Range(dialog, "_resultCount") + " point=" + options.Point.Value);
                options.Mode.Value = 1;
                options.Point.Value = "37";
                dialog.PressStartButton();
                int after = StoryRecovery.Count;
                string observed = after > before ? StoryRecovery.LastInteger.ToString() : "none";
                diceBefore = before;
                diceCheckAt = Time.unscaledTime + 2f;
                Log("dice started; sampled=" + observed);
            }
            catch (Exception error) { Log("dice run failed: " + error.Message); }
        }

        private static string Range(object owner, string field)
        {
            try
            {
                var info = AccessTools.Field(owner.GetType(), field);
                return info == null ? "?" : info.GetValue(owner).ToString();
            }
            catch (Exception) { return "?"; }
        }

        private static void Summary()
        {
            StoryRecovery.Stop();
            Log("SUMMARY pass=" + passes + " fail=" + failures + " dice=" + diceSeen + " menus=" + menuSeen +
                " hints=" + hintLogged + " events=" + StoryRecovery.Count);
            // Keep looking for a dice check after the first pass; only stop once one
            // was exercised or the overall budget is spent.
            if (diceSeen == 0) { stage = 2; waitUntil = Time.unscaledTime + 1f; return; }
            stage = -1;
        }

        private static void Check(string name, bool ok)
        {
            if (ok) passes++; else failures++;
            Log((ok ? "PASS " : "FAIL ") + name);
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = value.Replace("\n", " ");
            return value.Length > 40 ? value.Substring(0, 40) : value;
        }

        private static void Log(string text)
        {
            try { Plugin.Instance?.Log("Probe: " + text); } catch (Exception) { }
        }
    }
}