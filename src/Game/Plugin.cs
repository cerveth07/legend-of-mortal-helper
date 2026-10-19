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
    [BepInPlugin("cerveth.mortal.helper", "活侠助手 · 独立重建", "0.1.0")]
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
            if (!GameAccess.Typing && Input.GetKeyDown(Options.Toggle.Value)) Panel.Visible = !Panel.Visible;
            try { DiceAdapter.Tick(); }
            catch (Exception error) { DiceAdapter.Close(); Options.Dice.Value = false; Report(error); }
        }

        private void OnGUI() { Panel?.Draw(); HintAdapter.Draw(); }
        private void SceneChanged(Scene previous, Scene next) { DiceAdapter.Close(); HintAdapter.Clear(); Panel?.ClearSelection(); }
        private void OnDestroy()
        {
            SceneManager.activeSceneChanged -= SceneChanged;
            DiceAdapter.Close(); HintAdapter.Clear();
            Panel?.Dispose(); patches?.UnpatchSelf(); Config.Save();
            Instance = null;
        }
        internal void Report(Exception error) { Status = error.Message; Logger.LogError(error); }
        internal static bool Active => Instance != null && Instance.Options.Enabled.Value && Instance.Gate.CanInteract;
    }
}
