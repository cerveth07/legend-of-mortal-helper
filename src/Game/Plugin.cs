using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.Mono;
using HarmonyLib;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    [BepInPlugin("cerveth.mortal.helper", "活侠·万象录", "0.1.0")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static Plugin Instance;
        internal Settings Options;
        internal readonly OperationGate Gate = new OperationGate();
        internal string Status = "";
        internal MainPanel Panel;
        private Harmony patches;

        private void Awake()
        {
            Instance = this;
            Options = new Settings(Config);
            // Self test needs Update to keep running while the window is unfocused.
            // Diagnostics (probe, layout tour, editable preview) need Update and OnGUI
            // to keep running while the window is unfocused; normal play does not.
#if !NO_PROBE
            if (Options.SelfTest.Value) Application.runInBackground = true;
#endif
            if (Options.AutoTour.Value || Options.PreviewEditable.Value)
                Application.runInBackground = true;
            Panel = new MainPanel(this);
            try
            {
                foreach (string path in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
                {
                    string file = Path.GetFileName(path);
                    if (file.Equals("MortalFateHelper.dll", StringComparison.OrdinalIgnoreCase) ||
                        file.Equals("LOMSave2.dll", StringComparison.OrdinalIgnoreCase) ||
                        file.StartsWith("DiceMaster", StringComparison.OrdinalIgnoreCase) ||
                        file.Equals("ControlMyDestiny.dll", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("检测到旧辅助插件 " + file + "；请先将其移出 plugins 后重启。");
                }
                patches = new Harmony("cerveth.mortal.helper");
                patches.PatchAll(typeof(Plugin).Assembly);
                SceneManager.activeSceneChanged += SceneChanged;
                Logger.LogInfo("Independent helper 0.1.0 initialized; experimental build.");
                Logger.LogInfo("Display: " + Panel.Describe());
            }
            catch (Exception error)
            {
                patches?.UnpatchSelf();
                Options.Enabled.Value = false;
                Report(error);
            }
        }

        private void Update()
        {
            Panel?.Drain();
            Tour();
            if (!GameAccess.Typing && Input.GetKeyDown(KeyCode.F3))
            {
                Options.SortRecent.Value = !Options.SortRecent.Value;
                Options.Config.Save();
                SaveSlotSort.Refresh();
                Status = Options.SortRecent.Value ? "读档列表已按最近存档排序。" : "读档列表已恢复原有顺序。";
            }
            if (!GameAccess.Typing && Input.GetKeyDown(KeyCode.F2) && !string.IsNullOrEmpty(SaveSlotComments.Selected))
                Panel.OpenSaveSettings();
            if (!GameAccess.Typing && Input.GetKeyDown(Options.Toggle.Value))
            {
                Panel.Visible = !Panel.Visible;
                Logger.LogInfo("Panel toggled: " + Panel.Describe());
            }
            try
            {
                DiceAdapter.Tick(); ProgressionTools.Maintain(); StoryReplay.Tick();
#if !NO_PROBE
                Probe.Tick();
#endif
            }
            catch (Exception error) { DiceAdapter.Close(); Report(error); }
        }

        private void OnGUI()
        {
            // An exception thrown here repeats every frame and is otherwise silent
            // in the plugin log, so report the first one only.
            try { Panel?.Draw(); HintAdapter.Draw(); }
            catch (Exception error)
            {
                if (guiError) return;
                guiError = true;
                Report(error);
            }
        }

        private bool guiError;
        internal void Log(string message) { Logger.LogInfo(message); }

        // Applied only when the value changes: forcing timeScale every frame would
        // fight the game's own pause and slow-motion handling.
        internal void ApplySpeed(float value)
        {
            value = Mathf.Clamp(value, 0.25f, 5f);
            Options.Speed.Value = value;
            Time.timeScale = value;
        }
        private void SceneChanged(Scene previous, Scene next)
        {
            DiceAdapter.Close(); HintAdapter.Clear(); Panel?.ClearSelection();
            // A replay must never survive a scene change: the follow-up script would
            // keep receiving injections meant for the previous one.
            if (!StoryReplay.IsExpectedBaselineSceneChange(next.name) && StoryReplay.Active)
                StoryReplay.Stop("场景已切换，重放已中止。");
        }
        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            DiceAdapter.Close(); HintAdapter.Clear();
            if (Options.Speed.Value != 1f) Time.timeScale = 1f;
            Panel?.Dispose(); patches?.UnpatchSelf(); Config.Save();
            Instance = null;
        }

        // Diagnostic self-tour: drives the panel through every page and writes a
        // screenshot per page, so layout can be checked without touching the mouse.
        private float tourNext;
        private int tourPage;
        private int tourStage;
        private bool tourStarted;
        private bool tourLogged;
        private void Tour()
        {
            if (!Options.AutoTour.Value)
            {
                if (!tourLogged) { tourLogged = true; Logger.LogInfo("[Tour] disabled"); }
                return;
            }
            if (tourPage == 0 && !tourStarted)
            {
                tourStarted = true;
                Options.PreviewEditable.Value = true;
                Panel.Visible = true;
                tourNext = Time.unscaledTime + 1.6f;
                Logger.LogInfo("[Tour] start");
                return;
            }
            if (Time.unscaledTime < tourNext) return;
            string dir = System.IO.Path.Combine(Paths.ConfigPath, "mh");
            if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
            if (tourStage == 0)
            {
                if (tourPage >= TourViews.Length)
                {
                    if (!Options.AutoTour.Value) return;
                    Options.AutoTour.Value = false;
                    Options.PreviewEditable.Value = false;
                    Config.Save();
                    Logger.LogInfo("[Tour] finished");
                    return;
                }
                ApplyTourView(TourViews[tourPage]);
                Logger.LogInfo("[Tour] " + TourViews[tourPage] + " " + Panel.Geo());
                tourStage = 1;
                tourNext = Time.unscaledTime + 1.5f;
                return;
            }
            string file = System.IO.Path.Combine(dir, "tour-" + tourPage + "-" + TourViews[tourPage] + ".png");
            ScreenCapture.CaptureScreenshot(file);
            Logger.LogInfo("[Tour] shot " + file);
            tourPage++;
            tourStage = 0;
            tourNext = Time.unscaledTime + 1.5f;
        }

        private static readonly string[] TourViews =
        {
            "story", "story-recall", "battle-duel", "battle-group",
            "growth", "growth-relation", "numeric-top", "numeric-bottom", "save", "badge"
        };

        private void ApplyTourView(string view)
        {
            Panel.Visible = true;
            switch (view)
            {
                case "story":
                    Panel.SetView(0, 0, 0); Panel.Page = 0; Panel.RecoveryOpen = false; break;
                case "story-recall":
                    Panel.SetView(1, 0, 0); Panel.Page = 0; break;
                case "battle-duel":
                    Panel.SetView(0, 0, 0); Panel.Page = 1; break;
                case "battle-group":
                    Panel.SetView(0, 1, 0); Panel.Page = 1; break;
                case "growth":
                    Panel.SetView(0, 0, 0); Panel.Page = 2; break;
                case "growth-relation":
                    Panel.SetView(0, 0, 1); Panel.Page = 2; break;
                case "numeric-top":
                    Panel.Page = 3; break;
                case "numeric-bottom":
                    Panel.Page = 3; Panel.ScrollToEnd(3); break;
                case "save":
                    Panel.Page = 4; break;
                case "badge":
                    Panel.Visible = false; break;
            }
        }

        internal void Report(Exception error) { Status = error.Message; Logger.LogError(error); }
        internal static bool Active => Instance != null && Instance.Options.Enabled.Value && Instance.Gate.CanInteract;
    }
}
