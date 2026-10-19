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
        internal string Status = "开发构建：尚未完成实机验收";
        internal MainPanel Panel;
        private Harmony patches;

        private void Awake()
        {
            Instance = this;
            Options = new Settings(Config);
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
            try { DiceAdapter.Tick(); ProgressionTools.Maintain(); }
            catch (Exception error) { DiceAdapter.Close(); Options.Mode.Value = 0; Report(error); }
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
        private void SceneChanged(Scene previous, Scene next) { DiceAdapter.Close(); HintAdapter.Clear(); Panel?.ClearSelection(); }
        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            DiceAdapter.Close(); HintAdapter.Clear();
            if (Options.Speed.Value != 1f) Time.timeScale = 1f;
            Panel?.Dispose(); patches?.UnpatchSelf(); Config.Save();
            Instance = null;
        }
        internal void Report(Exception error) { Status = error.Message; Logger.LogError(error); }
        internal static bool Active => Instance != null && Instance.Options.Enabled.Value && Instance.Gate.CanInteract;
    }
}
