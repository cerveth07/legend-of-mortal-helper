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
    [BepInPlugin("cerveth.mortal.helper", "活侠·万象录", "0.1.1")]
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
                Logger.LogInfo("Mortal helper 0.1.1 initialized; experimental build.");
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
                if (!Options.ExperimentalRecovery.Value)
                {
                    if (StoryRecovery.Recording) StoryRecovery.Stop();
                    if (StoryReplay.Active) StoryReplay.Stop("实验恢复已停用；请通过原生读档解除保护锁。");
                }
                AnytimeDice.Tick(); AnytimeStory.Tick(); AnytimeDuel.Tick();
                // Re-assert the input lock: a scene created during a replay starts
                // with its input modules enabled.
                InteractionLock.Refresh();
                NativeLoadBoundary.CheckStalled();
                DiceAdapter.Tick(); ProgressionTools.Maintain(); StoryReplay.Tick();
#if !NO_PROBE
                Probe.Tick();
#endif
            }
            catch (Exception error) { DiceAdapter.Close(); Report(error); }
        }

        private void OnGUI()
        {
            // Keep rendering after transient game/UI errors and rate-limit logging.
            try { Panel?.Draw(); HintAdapter.Draw(); }
            catch (Exception error)
            {
                if (error is ExitGUIException) throw;
                if (Time.unscaledTime < nextGuiError) return;
                nextGuiError = Time.unscaledTime + 5f;
                Report(error);
            }
        }

        private float nextGuiError;
        internal void Log(string message) { Logger.LogInfo(message); }

        // Applied only when the value changes: forcing timeScale every frame would
        // fight the game's own pause and slow-motion handling.
        internal void ApplySpeed(float value)
        {
            // Manual time scaling would perturb replay wait and action ordering.
            if (!Gate.CanInteract)
            {
                Status = "恢复期间不能调整游戏速度。";
                return;
            }
            value = Mathf.Clamp(value, 0.25f, 5f);
            Options.Speed.Value = value;
            Time.timeScale = value;
        }
        private void SceneChanged(Scene previous, Scene next)
        {
            AnytimeNative.ClearOffer();
            DiceAdapter.Close(); HintAdapter.Clear(); Panel?.ClearSelection();
            if (next.name == "Title") { AnytimeStory.Reset(); AnytimeDuel.Reset(); }
            // A replay must never survive a scene change: the follow-up script would
            // keep receiving injections meant for the previous one.
            if (!StoryReplay.IsExpectedBaselineSceneChange(next.name) && StoryReplay.Active)
                StoryReplay.Stop("场景已切换，重放已中止。");
        }
        private void OnDestroy()
        {
            if (tourStarted) FinishTour();
            SceneManager.activeSceneChanged -= SceneChanged;
            DiceAdapter.Close(); HintAdapter.Clear();
            AnytimeStory.Reset(); AnytimeDuel.Reset();
            if (Options.Speed.Value != 1f) Time.timeScale = 1f;
            Panel?.Dispose(); patches?.UnpatchSelf(); Config.Save();
            Instance = null;
        }

        // Read-only layout tour. Scroll changes run in Update, never during IMGUI.
        private float tourNext;
        private int tourPage;
        private int tourStage;
        private int tourOffset;
        private int tourShot;
        private float tourMaximum;
        private bool tourStarted;
        private bool tourLogged;
        private bool tourPreviewBefore;
        private string tourStatusBefore;
        private Action restoreTourPanel;
        private string tourDirectory;

        private void Tour()
        {
            try { TourStep(); }
            catch (Exception error)
            {
                Logger.LogError("[Tour] aborted: " + error);
                FinishTour();
            }
        }

        private void TourStep()
        {
            if (!Options.AutoTour.Value)
            {
                if (tourStarted) FinishTour();
                if (!tourLogged) { tourLogged = true; Logger.LogInfo("[Tour] disabled"); }
                return;
            }
            if (!tourStarted)
            {
                tourPage = tourStage = tourOffset = tourShot = 0;
                tourPreviewBefore = Options.PreviewEditable.Value;
                tourStatusBefore = Status;
                restoreTourPanel = Panel.CaptureTourState();
                tourStarted = true;
                tourDirectory = Path.Combine(Paths.ConfigPath, "mh", "tour-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(tourDirectory);
                // Preview is transient diagnostic state, including on disk.
                bool autoSave = Config.SaveOnConfigSet;
                Config.SaveOnConfigSet = false;
                try { Options.PreviewEditable.Value = true; }
                finally { Config.SaveOnConfigSet = autoSave; }
                Panel.Visible = true;
                tourNext = Time.unscaledTime + .5f;
                Logger.LogInfo("[Tour] start; read-only views, 300px scroll steps; numeric categories sample 0..2400/middle/end, not every item; " + tourDirectory);
                return;
            }
            if (Time.unscaledTime < tourNext) return;
            if (tourPage >= TourViews.Length) { FinishTour(); return; }
            string view = TourViews[tourPage];
            bool numeric = view.StartsWith("numeric-", StringComparison.Ordinal);
            bool anchor = view.EndsWith("-anchor", StringComparison.Ordinal);
            if (tourStage == 0)
            {
                ApplyTourView(view);
                tourOffset = 0;
                // Let this page finish a layout/repaint before using section coordinates.
                tourStage = anchor ? 7 : 1;
            }
            else if (tourStage == 7)
            {
                int section = view == "numeric-attributes-anchor" ? 0 : view == "numeric-items-anchor" ? 1 : 2;
                Panel.TourJumpToSection(section);
                tourStage = 8;
            }
            else if (tourStage == 8)
            {
                CaptureTour(view, view.Substring("numeric-".Length));
                tourStage = 5;
            }
            else if (tourStage == 1)
            {
                CaptureTour(view, "y" + tourOffset);
                tourStage = 5;
            }
            else if (tourStage == 5)
            {
                float actual = Panel.TourScrollY;
                bool end = anchor || view == "badge" || (tourOffset > 0 && actual < tourOffset - 1f);
                if (end) NextTourView();
                else if (numeric && tourOffset >= 2400)
                {
                    // Measure the real clamped end before choosing the middle sample.
                    Panel.ScrollToEnd(Panel.Page);
                    tourStage = 2;
                }
                else if (!numeric && tourOffset >= 3300)
                {
                    Logger.LogInfo("[Tour] long view sample limit; intermediate content after y3300 not exhausted: " + view);
                    Panel.ScrollToEnd(Panel.Page);
                    tourStage = 4;
                }
                else
                {
                    tourOffset += 300;
                    Panel.SetTourPosition(tourOffset);
                    tourStage = 1;
                }
            }
            else if (tourStage == 2)
            {
                tourMaximum = Panel.TourScrollY;
                Panel.SetTourPosition(Mathf.RoundToInt((2400f + tourMaximum) * .5f));
                tourStage = 3;
            }
            else if (tourStage == 3)
            {
                CaptureTour(view, "middle");
                tourStage = 6;
            }
            else if (tourStage == 6)
            {
                Panel.ScrollToEnd(Panel.Page);
                tourStage = 4;
            }
            else
            {
                CaptureTour(view, "end");
                NextTourView();
            }
            // Each screenshot has at least .5s to finish before the next UI change;
            // every changed scroll/view likewise settles for .5s before capture.
            tourNext = Time.unscaledTime + .5f;
        }

        private void CaptureTour(string view, string position)
        {
            string file = Path.Combine(tourDirectory, "tour-" + tourShot.ToString("D3") + "-" + view + "-" + position + ".png");
            string detail = "shot=" + tourShot + " view=" + view + " position=" + position
                + " actualScroll=" + Panel.TourScrollY.ToString("0.0")
                + " statusPreviewFixture=" + (view == "story-notice" ? "true" : "false")
                + " readOnlyLayoutPreview=true recoveryFunctionVerified=false " + Panel.Geo()
                + " screen=" + Screen.width + "x" + Screen.height + " file=" + file;
            ScreenCapture.CaptureScreenshot(file);
            File.AppendAllText(Path.Combine(tourDirectory, "manifest.txt"), detail + Environment.NewLine);
            Logger.LogInfo("[Tour] " + detail);
            tourShot++;
        }

        private void NextTourView() { tourPage++; tourStage = 0; }

        private void FinishTour()
        {
            bool hadStarted = tourStarted;
            tourStarted = false;
            tourLogged = true;
            bool autoSave = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            try
            {
                Options.AutoTour.Value = false;
                if (hadStarted)
                {
                    Options.PreviewEditable.Value = tourPreviewBefore;
                    Status = tourStatusBefore;
                }
                restoreTourPanel?.Invoke();
            }
            catch (Exception error) { Logger.LogError("[Tour] cleanup failed: " + error); }
            finally { restoreTourPanel = null; Config.SaveOnConfigSet = autoSave; }
            try { Config.Save(); }
            catch (Exception error) { Logger.LogError("[Tour] could not save restored settings: " + error); }
            Logger.LogInfo("[Tour] finished; " + tourShot + " screenshots; " + tourDirectory);
        }

        private static readonly string[] TourViews =
        {
            "story", "story-recovery", "story-recall", "battle-duel", "battle-group",
            "growth", "growth-relation", "numeric-books", "numeric-miscs",
            "numeric-special", "numeric-consumables", "save", "language", "badge",
            "numeric-attributes-anchor", "numeric-items-anchor", "numeric-speed-anchor", "story-notice"
        };

        private void ApplyTourView(string view)
        {
            Status = tourStatusBefore;
            Panel.Visible = true;
            Panel.RecoveryOpen = false;
            switch (view)
            {
                case "story-notice":
                    Panel.SetView(0, 0, 0); Panel.Page = 0;
                    Status = "布局预览消息：用于核对消息区域与正文之间的间距，以及较长消息在窄面板中的自动换行。未修改游戏进度。";
                    break;
                case "story":
                case "story-recovery":
                    Panel.SetView(0, 0, 0); Panel.Page = 0;
                    Panel.RecoveryOpen = view == "story-recovery"; break;
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
                case "numeric-attributes-anchor":
                case "numeric-items-anchor":
                case "numeric-speed-anchor":
                    Panel.Page = 3; break;
                case "numeric-books":
                case "numeric-miscs":
                case "numeric-special":
                case "numeric-consumables":
                    Panel.Page = 3;
                    int category = view == "numeric-books" ? 0 : view == "numeric-miscs" ? 1 : view == "numeric-special" ? 2 : 3;
                    Panel.SetTourPosition(0, category); break;
                case "save":
                    Panel.Page = 4; break;
                case "language":
                    Panel.Page = 5; break;
                case "badge":
                    Panel.Visible = false; break;
            }
            Panel.SetTourPosition(0);
        }

        internal void Report(Exception error) { Status = error.Message; Logger.LogError(error); }
        // Installed/active is distinct from permission to mutate progress.
        // Dice playback still requires the native adapters while the shared gate is locked.
        internal static bool Active => Instance != null && Instance.Options.Enabled.Value;
        internal static bool CanModifyProgress => Active && Instance.Gate.CanInteract;
    }
}
