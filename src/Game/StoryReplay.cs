using System;
using System.IO;
using System.Text;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Recovery;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Playback half of precise recovery, driven by RecoveryController so the phase
    // order, order/key checks and failure locking also protect the real path.
    // The baseline is the recorded save JSON; after loading it the game is brought
    // into that scene exactly like the load panel does, then recorded events are
    // injected and the state summary is verified.
    internal static class StoryReplay
    {
        private const float VerifyDelay = 1f;
        private const float StallSeconds = 120f;

        private static RecoveryController controller;
        private static string status = "未开始";
        private static int diceOrdinal;
        private static string diceScript;
        private static float lastProgress, verifyAt;
        private static bool clicking;
        private static string choiceSnapshot;
        private static int choiceStableFrame, startFrame;
        private static long menuGeneration, clickedMenuGeneration = -1;
        private static int clickedEventPosition = -1;
        private static float choiceStableAt;
        private static bool loadingBaseline, baselineScenePending;
        private static string baselineScene;
        private static float baselineSceneDeadline;

        internal static bool Active => controller != null && (controller.Replaying || controller.Finished);

        // The native LoadCurrentScene operation may trigger a scene change during
        // or shortly after loading the baseline. It is not a user-initiated abort.
        internal static bool IsExpectedBaselineSceneChange(string scene)
        {
            if (loadingBaseline)
            {
                baselineScenePending = false;
                return true;
            }
            if (baselineScenePending && Time.unscaledTime <= baselineSceneDeadline &&
                string.Equals(scene, baselineScene, StringComparison.Ordinal))
            {
                baselineScenePending = false;
                return true;
            }
            return false;
        }
        internal static int Position { get; private set; }
        internal static int? PendingDice { get; private set; }
        internal static string Status => status;
        internal static bool LoadingBaseline => loadingBaseline;

        // Native option content changes mark a new menu population. Even when
        // the same MenuDialog instance and the same text are reused, a newly
        // rendered menu may contain the next distinct recorded choice.
        internal static void NoteOptionContentUpdated()
        {
            if (controller == null || !controller.Replaying) return;
            menuGeneration++;
            choiceSnapshot = null; // wait for the new menu to settle
        }

        // Called after successful native data apply AND matching scene completion.
        internal static void OnNativeLoadCompleted()
        {
            if (loadingBaseline || Plugin.Instance == null) return;
            if (controller != null) Stop("已使用原生存档替代实验恢复。");
            // The gate may already have been closed by the load-stall watchdog or by
            // a replay failure. Reopening it here would certify an unfinished load.
            if (!Plugin.Instance.Gate.AwaitingNativeLoad)
            {
                status = "原生存档已载入，但恢复锁已关闭；请重新读取一个有效的原生存档。";
                return;
            }
            Plugin.Instance.Gate.NativeLoadCompleted();
            status = "原生存档已载入；实验恢复锁已解除。";
        }

        internal sealed class Host : IReplayHost
        {
            public string Client => "lom:" + Application.version;

            public void LoadBaseline(string baseline)
            {
                var save = JsonUtility.FromJson<GameSave>(baseline);
                if (save == null) throw new InvalidDataException("基线存档无法解析。");
                if (save.TimeTick <= 0 || string.IsNullOrEmpty(save.CurrentStoryScript))
                    throw new InvalidDataException("基线存档解析后缺少关键字段（TimeTick=" + save.TimeTick +
                        "，脚本=" + (save.CurrentStoryScript ?? "null") + "）。");
                var load = AccessTools.Method(typeof(SaveSystem), "ExecuteLoadGameData");
                if (load == null) throw new InvalidOperationException("找不到游戏读档方法。");
                loadingBaseline = true;
                baselineScene = save.CurrentScene;
                baselineScenePending = true;
                baselineSceneDeadline = Time.unscaledTime + 10f;
                try
                {
                    load.Invoke(SaveSystem.Instance, new object[] { save });
                    // Rebuild missions and scene, but omit native SaveUniverseData:
                    // an unverified experimental replay must not write game files.
                    if (MissionManagerData.Instance != null) MissionManagerData.Instance.UpdateCheckMissions();
                    if (SceneController.Instance != null) SceneController.Instance.LoadCurrentScene();
                    Log("baseline applied tick=" + save.TimeTick + " scene=" + (save.CurrentScene ?? "null"));
                }
                finally { loadingBaseline = false; }
            }

            public string CaptureState() { return StoryRecovery.Summary(); }

            public void Abort(string reason)
            {
                Log("abort: " + reason);
                if (string.IsNullOrEmpty(status)) status = reason;
            }
        }

        // Why a restore cannot start right now; empty means it can.
        internal static string Blocker(int slot, bool fromBackup = false)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Options.ExperimentalRecovery.Value)
                return "实验恢复默认关闭；随机状态重放尚未通过实机验收（配置 Recovery/ExperimentalEnabled）";
            if (Active) return "正在重放中";
            if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract)
                return "恢复锁定中；请从原生存档重新读档后再试";
            if (StoryRecovery.Recording) return "正在记录，请先停止记录";
            try
            {
                string folder = StoryRecovery.Directory;
                if (folder == null) return "找不到记录目录";
                string path = new ArchiveStore(folder).PathFor(slot) + (fromBackup ? ".bak" : "");
                if (!File.Exists(path))
                    return "槽位 " + slot + (fromBackup ? " 没有历史备份" : " 还没有检查点");
            }
            catch (Exception) { return "读取记录目录失败"; }
            return "";
        }

        internal static bool Start(int slot, bool fromBackup = false)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Options.ExperimentalRecovery.Value)
            { status = "实验恢复默认关闭；请先阅读随机状态与实机验收限制，再在配置 Recovery/ExperimentalEnabled 中明确开启。"; return false; }
            if (controller != null && controller.Replaying) { status = "已经在重放中。"; return false; }
            if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract)
            { status = "恢复操作被锁定；请先通过游戏原生读档恢复安全状态。"; return false; }
            if (StoryRecovery.Recording) { status = "请先停止记录再恢复。"; return false; }
            try
            {
                string folder = StoryRecovery.Directory;
                if (folder == null) { status = "找不到记录目录。"; return false; }
                var store = new ArchiveStore(folder);
                var document = store.Load(slot, fromBackup);
                if (document == null || document.Kind != RecoveryKind.Story) { status = "该槽位不是剧情检查点。"; return false; }
                var opening = JsonUtility.FromJson<GameSave>(document.Baseline);
                if (opening == null || opening.CurrentScene != document.Scene)
                { status = "检查点场景与原生基线不一致，拒绝恢复。"; return false; }
                if (document.ExpectedState == null ||
                    !document.ExpectedState.StartsWith(ReplayStateDigest.Prefix, StringComparison.Ordinal))
                {
                    status = "该检查点使用旧版状态摘要；请重新录制后再恢复。";
                    return false;
                }
                if (document.Checkpoint == null ||
                    !document.Checkpoint.StartsWith("manual:", StringComparison.Ordinal))
                {
                    // VerifyAtEnd refuses any other shape, so accepting it here would only
                    // strand the gate in Replaying at the very end of the replay.
                    status = "该检查点不是手动检查点，无法校验；请重新录制。";
                    return false;
                }
                Position = 0; diceOrdinal = 0; diceScript = null; PendingDice = null;
                verifyAt = 0f; lastProgress = Time.unscaledTime; startFrame = Time.frameCount;
                choiceSnapshot = null; menuGeneration = 0;
                clickedMenuGeneration = -1; clickedEventPosition = -1;
                baselineScenePending = false;
                controller = new RecoveryController(Plugin.Instance.Gate);
                controller.Start(document, new Host());
                status = "已载入" + (fromBackup ? "历史备份" : "基线") + "，开始重放 " + document.Events.Count + " 个事件。";
                Log("started: " + document.Events.Count + " events, checkpoint=" + document.Checkpoint);
                if (document.Events.Count == 0) ScheduleVerify();
                return true;
            }
            catch (Exception error)
            {
                Stop("恢复失败：" + error.Message);
                return false;
            }
        }

        internal static void Stop(string reason)
        {
            // Without this the status line reports a stop that never happened every time the
            // player loads a save normally.
            bool active = controller != null || PendingDice != null || baselineScenePending;
            if (controller != null && controller.Replaying) controller.Cancel(reason);
            controller = null;
            PendingDice = null;
            baselineScenePending = false;
            choiceSnapshot = null; clickedMenuGeneration = -1; clickedEventPosition = -1;
            if (active)
            {
                status = reason ?? "重放已停止。";
                Log("stopped: " + status);
            }
        }

        internal static void Tick()
        {
            if (controller == null) return;
            if (verifyAt > 0f)
            {
                if (Time.unscaledTime >= verifyAt) VerifyNow();
                return;
            }
            if (!controller.Replaying) { controller = null; return; }
            if (Time.unscaledTime - lastProgress > StallSeconds)
            {
                Stop("超过 " + (int)StallSeconds + " 秒没有匹配到记录的交互，重放已中止。");
                return;
            }
            // Do not act on controls belonging to the outgoing scene.
            if (Time.frameCount <= startFrame + 1 || !GameAccess.Loaded) return;
            if (baselineScenePending)
            {
                if (Time.unscaledTime > baselineSceneDeadline) baselineScenePending = false;
                else if (!string.Equals(SceneManager.GetActiveScene().name, baselineScene,
                    StringComparison.Ordinal)) return;
            }
            var next = controller.NextEvent;
            if (next != null && next.Kind == EventKind.Dice && !PendingDice.HasValue &&
                DiceAdapter.Dialog != null && DiceAdapter.Dialog.isActiveAndEnabled)
                TakeDice(); // Also covers a dialog enabled during baseline loading.
            if (controller != null && controller.Replaying) DriveChoice();
        }

        private static void ScheduleVerify()
        {
            if (verifyAt > 0f) return;
            verifyAt = Time.unscaledTime + VerifyDelay;
            status = "事件已全部注入，等待状态校验…";
        }

        private static void VerifyNow()
        {
            verifyAt = 0f;
            var current = controller;
            controller = null;
            baselineScenePending = false;
            try
            {
                current.VerifyAtEnd();
                status = "已到达检查点并通过状态校验；控制权交还给你。";
            }
            catch (Exception error)
            {
                status = "校验失败：" + error.Message;
                // The controller is already detached, so nothing else can move the gate out of
                // Replaying; without this the shared gate can never be reopened.
                try { current?.Cancel(status); }
                catch (Exception) { try { Plugin.Instance?.Gate.Fail(status); } catch (Exception) { } }
            }
            Log(status);
        }

        // Called when a roll dialog opens or a fully loaded dice dialog is found.
        internal static void TakeDice()
        {
            if (controller == null || !controller.Replaying || PendingDice.HasValue) return;
            if (controller.NextEvent == null || controller.NextEvent.Kind != EventKind.Dice) return;
            if (!StoryRecovery.ScriptKnown) { Stop("无法确定当前剧情脚本名，重放已中止。"); return; }
            string script = StoryRecovery.CurrentScript;
            if (script != diceScript) { diceScript = script; diceOrdinal = 0; }
            string key = StoryRecovery.DiceKey(script, diceOrdinal + 1);
            RecordedEvent consumed;
            if (!controller.TryConsume(EventKind.Dice, key, out consumed))
            {
                Stop("判定「" + key + "」与记录不一致，重放已中止。");
                return;
            }
            diceOrdinal++;
            Position++;
            lastProgress = Time.unscaledTime;
            PendingDice = consumed.Integer;
            Log("dice " + consumed.Integer + " (" + Position + ")");
            // Verification starts on the actual dice result, never on dialog open.
        }

        internal static void ConfirmDice(int actual)
        {
            if (!PendingDice.HasValue) return;
            int expected = PendingDice.Value;
            if (actual != expected)
            {
                Stop("原生骰子结果与记录不一致（预期 " + expected + "，实际 " + actual + "）。");
                return;
            }
            PendingDice = null;
            lastProgress = Time.unscaledTime;
            if (controller != null && controller.Finished) ScheduleVerify();
        }

        // Menu content callbacks run while buttons are being populated, so choosing
        // there incorrectly treats unrelated options as out-of-order events. Observe
        // the *complete* active menu during Update and wait for a stable snapshot.
        private static void DriveChoice()
        {
            if (clicking || controller == null || !controller.Replaying || PendingDice.HasValue) return;
            var next = controller.NextEvent;
            if (next == null || next.Kind != EventKind.Choice) { choiceSnapshot = null; return; }
            var menu = MenuDialog.GetMenuDialog();
            if (menu == null || !menu.IsActive())
            {
                choiceSnapshot = null;
                clickedMenuGeneration = -1; clickedEventPosition = -1;
                return;
            }
            var buttons = menu.GetComponentsInChildren<NormalOptionButton>(false);
            if (!StoryRecovery.ScriptKnown) { Stop("无法确定当前剧情脚本名，重放已中止。"); return; }
            var fingerprint = new StringBuilder(StoryRecovery.CurrentScript).Append(':')
                .Append(menu.GetInstanceID());
            Button match = null;
            string matchedText = null;
            int matches = 0, available = 0;
            foreach (var option in buttons)
            {
                if (option == null || !option.gameObject.activeInHierarchy) continue;
                Button button = option.GetComponent<Button>();
                if (button == null) continue;
                Text text;
                try { text = GameAccess.Field<Text>(option, "_source"); }
                catch (Exception) { continue; }
                if (text == null) continue;
                available++;
                fingerprint.Append('|').Append(option.GetInstanceID()).Append(':')
                    .Append(text.text).Append(':').Append(button.interactable);
                if (button.interactable &&
                    StoryRecovery.ChoiceKey(StoryRecovery.CurrentScript, text.text) == next.Key)
                {
                    match = button; matchedText = text.text; matches++;
                }
            }
            if (available == 0) { choiceSnapshot = null; return; }
            string snapshot = fingerprint.ToString();
            if (!string.Equals(snapshot, choiceSnapshot, StringComparison.Ordinal))
            {
                choiceSnapshot = snapshot;
                choiceStableFrame = Time.frameCount;
                choiceStableAt = Time.unscaledTime;
                return;
            }
            if (Time.frameCount - choiceStableFrame < 2 ||
                Time.unscaledTime - choiceStableAt < 0.15f) return;
            // Block a second click on the *same population* of the native
            // menu, but allow the next recorded event after UpdateContent or
            // a hide/show cycle, even when text and menu instance are identical.
            if (clickedEventPosition == Position - 1 &&
                clickedMenuGeneration == menuGeneration) return;
            if (matches == 0) return; // No target in this menu; never consume another option.
            if (matches > 1)
            {
                Stop("有多个同名剧情选项，无法唯一确认重放目标。");
                return;
            }
            RecordedEvent consumed;
            if (!controller.TryConsume(EventKind.Choice, next.Key, out consumed))
            {
                Stop("待重放选项已失效，重放已中止。");
                return;
            }
            // Mark before invoking: a synchronous game callback can construct the
            // next menu, and the previous one must never be clicked twice.
            clickedMenuGeneration = menuGeneration;
            clickedEventPosition = Position;
            clicking = true;
            try
            {
                Position++;
                lastProgress = Time.unscaledTime;
                Log("choice " + matchedText + " (" + Position + ")");
                match.onClick.Invoke();
                if (controller != null && controller.Finished && !PendingDice.HasValue)
                    ScheduleVerify();
            }
            catch (Exception error) { Stop("选择注入失败：" + error.Message); }
            finally { clicking = false; }
        }

        private static void Log(string message)
        {
            try { Plugin.Instance?.Log("Replay: " + message); } catch (Exception) { }
        }
    }
}
