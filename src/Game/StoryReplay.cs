using System;
using System.IO;
using HarmonyLib;
using Mortal.Core;
using MortalHelper.Recovery;
using UnityEngine;

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

        internal static bool Active => controller != null && (controller.Replaying || controller.Finished);
        internal static int Position { get; private set; }
        internal static int? PendingDice { get; private set; }
        internal static string Status => status;

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
                load.Invoke(SaveSystem.Instance, new object[] { save });
                // Applying data alone never changes scene; the load panel does these
                // three calls afterwards, so replay must do the same.
                SaveSystem.Instance.SaveUniverseData();
                if (MissionManagerData.Instance != null) MissionManagerData.Instance.UpdateCheckMissions();
                if (SceneController.Instance != null) SceneController.Instance.LoadCurrentScene();
                Log("baseline applied tick=" + save.TimeTick + " scene=" + (save.CurrentScene ?? "null"));
            }

            public string CaptureState() { return StoryRecovery.Summary(); }

            public void Abort(string reason)
            {
                Log("abort: " + reason);
                if (string.IsNullOrEmpty(status)) status = reason;
            }
        }

        // Why a restore cannot start right now; empty means it can.
        internal static string Blocker(int slot)
        {
            if (Active) return "正在重放中";
            if (StoryRecovery.Recording) return "正在记录，请先停止记录";
            try
            {
                string folder = StoryRecovery.Directory;
                if (folder == null) return "找不到记录目录";
                if (!File.Exists(Path.Combine(folder, "slot-" + slot.ToString("D3") + ".mhr")))
                    return "槽位 " + slot + " 还没有检查点";
            }
            catch (Exception) { return "读取记录目录失败"; }
            return "";
        }

        internal static bool Start(int slot)
        {
            if (controller != null && controller.Replaying) { status = "已经在重放中。"; return false; }
            if (StoryRecovery.Recording) { status = "请先停止记录再恢复。"; return false; }
            try
            {
                string folder = StoryRecovery.Directory;
                if (folder == null) { status = "找不到记录目录。"; return false; }
                var store = new ArchiveStore(folder);
                var document = store.Load(slot);
                if (document == null || document.Kind != RecoveryKind.Story) { status = "该槽位不是剧情检查点。"; return false; }
                controller = new RecoveryController();
                controller.Start(document, new Host());
                Position = 0; diceOrdinal = 0; diceScript = null; PendingDice = null;
                verifyAt = 0f; lastProgress = Time.unscaledTime;
                status = "已载入基线，开始重放 " + document.Events.Count + " 个事件。";
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
            if (controller != null && controller.Replaying) controller.Cancel(reason);
            controller = null;
            PendingDice = null;
            status = reason ?? "重放已停止。";
            Log("stopped: " + status);
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
                Stop("超过 " + (int)StallSeconds + " 秒没有匹配到记录的交互，重放已中止。");
        }

        private static void ScheduleVerify()
        {
            verifyAt = Time.unscaledTime + VerifyDelay;
            status = "事件已全部注入，等待状态校验…";
        }

        private static void VerifyNow()
        {
            verifyAt = 0f;
            var current = controller;
            controller = null;
            try
            {
                current.VerifyAtEnd();
                status = "已到达检查点并通过状态校验；控制权交还给你。";
            }
            catch (Exception error)
            {
                status = "校验失败：" + error.Message;
            }
            Log(status);
        }

        // Called when a roll dialog opens; consumes the matching recorded event.
        internal static void TakeDice()
        {
            if (controller == null || !controller.Replaying) return;
            string script = StoryRecovery.CurrentScript;
            if (script != diceScript) { diceScript = script; diceOrdinal = 0; }
            string key = StoryRecovery.DiceKey(script, ++diceOrdinal);
            RecordedEvent consumed;
            if (!controller.TryConsume(EventKind.Dice, key, out consumed))
            {
                Stop("判定「" + key + "」与记录不一致，重放已中止。");
                return;
            }
            Position++;
            lastProgress = Time.unscaledTime;
            PendingDice = consumed.Integer;
            Log("dice " + consumed.Integer + " (" + Position + ")");
            if (controller.Finished) ScheduleVerify();
        }

        // Called when a story option is rendered; clicks it when it matches the tape.
        internal static void TryChoice(UnityEngine.UI.Button button, string text)
        {
            if (controller == null || !controller.Replaying || clicking || button == null) return;
            string key = StoryRecovery.ChoiceKey(StoryRecovery.CurrentScript, text);
            RecordedEvent consumed;
            if (!controller.TryConsume(EventKind.Choice, key, out consumed))
            {
                Stop("选项「" + text + "」与记录不一致，重放已中止。");
                return;
            }
            clicking = true;
            try
            {
                Position++;
                lastProgress = Time.unscaledTime;
                Log("choice " + text + " (" + Position + ")");
                button.onClick.Invoke();
                if (controller.Finished) ScheduleVerify();
            }
            catch (Exception error) { Stop("选择注入失败：" + error.Message); }
            finally { clicking = false; }
        }

        internal static void AfterDice() { PendingDice = null; }

        private static void Log(string message)
        {
            try { Plugin.Instance?.Log("Replay: " + message); } catch (Exception) { }
        }
    }
}
