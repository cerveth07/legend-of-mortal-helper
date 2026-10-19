using System;
using System.IO;
using HarmonyLib;
using Mortal.Core;
using MortalHelper.Recovery;
using UnityEngine;

namespace MortalHelper.Game
{
    // Playback half of precise recovery: restore the recorded baseline save, then
    // inject the recorded dice results and option choices in order until the stop
    // point, and finally compare a state summary. Any mismatch aborts instead of
    // guessing, because continuing on a desynced replay corrupts progress quietly.
    internal static class StoryReplay
    {
        internal static bool Active { get; private set; }
        internal static RecoveryDocument Document { get; private set; }
        internal static int Position { get; private set; }
        internal static string Status { get; private set; } = "未开始";

        internal static RecordedEvent Next =>
            Document != null && Position < Document.Events.Count ? Document.Events[Position] : null;

        internal static int? PendingDice { get; private set; }
        private static float lastProgress;
        private const float StallSeconds = 120f;

        internal static string ExpectedChoice
        {
            get
            {
                var next = Next;
                return next != null && next.Kind == EventKind.Choice ? next.Data : null;
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
                if (!File.Exists(Path.Combine(folder, "slot-" + slot.ToString("D3") + ".mhr"))) return "槽位 " + slot + " 还没有检查点";
            }
            catch (Exception) { return "读取记录目录失败"; }
            return "";
        }

        internal static bool Start(int slot)
        {
            if (Active) { Status = "已经在重放中。"; return false; }
            if (StoryRecovery.Recording) { Status = "请先停止记录再恢复。"; return false; }
            try
            {
                string folder = StoryRecovery.Directory;
                if (folder == null) { Status = "找不到记录目录。"; return false; }
                var store = new ArchiveStore(folder);
                var document = store.Load(slot);
                if (document == null || document.Kind != RecoveryKind.Story) { Status = "该槽位不是剧情检查点。"; return false; }
                var save = JsonUtility.FromJson<GameSave>(document.Baseline);
                if (save == null) { Status = "基线存档无法解析。"; return false; }
                // Sanity check: a parsed baseline must at least carry the values the
                // game itself writes, otherwise JsonUtility silently produced defaults.
                if (save.TimeTick <= 0 || string.IsNullOrEmpty(save.CurrentStoryScript))
                {
                    Status = "基线存档解析后缺少关键字段（TimeTick=" + save.TimeTick + "，脚本=" + (save.CurrentStoryScript ?? "null") +
                        "），已中止以免用不完整数据读档。";
                    Log(Status);
                    return false;
                }
                Log("parsed baseline tick=" + save.TimeTick + " script=" + save.CurrentStoryScript + " scene=" + save.CurrentScene);
                Active = true; Document = document; Position = 0; PendingDice = null;
                lastProgress = UnityEngine.Time.unscaledTime;
                var load = AccessTools.Method(typeof(SaveSystem), "ExecuteLoadGameData");
                if (load == null) { Stop("找不到游戏读档方法，已中止。"); return false; }
                Log("baseline " + document.Baseline.Length + " chars, events " + document.Events.Count);
                load.Invoke(SaveSystem.Instance, new object[] { save });
                Status = "已载入基线，开始重放（" + document.Events.Count + " 个事件）。请等待剧情自动推进。";
                Log("baseline applied");
                if (document.Events.Count == 0) Finish();
                return true;
            }
            catch (Exception error)
            {
                Stop("恢复失败：" + error.Message);
                return false;
            }
        }

        // Playback only stops itself; if the story never reaches the next recorded
        // interaction the player is left with injections pending, so give up loudly.
        internal static void Tick()
        {
            if (!Active) return;
            if (Position > 0) { /* progress resets the clock in Take/Choice */ }
            if (UnityEngine.Time.unscaledTime - lastProgress > StallSeconds)
                Stop("超过 " + (int)StallSeconds + " 秒没有匹配到记录的交互，重放已中止。");
        }

        internal static void Stop(string reason)
        {
            Active = false; PendingDice = null;
            Status = reason;
            Log("stopped: " + reason);
        }

        internal static void Finish()
        {
            string expected = Document == null ? "" : Document.ExpectedState;
            string actual = StoryRecovery.Summary();
            Active = false; PendingDice = null;
            Status = expected == actual
                ? "已到达检查点并通过状态校验；控制权交还给你。"
                : "已到达检查点，但状态摘要与记录不一致（记录 " + expected + " / 现在 " + actual + "）。";
            Log("finish: " + Status);
        }

        // Called by the dice adapter when a roll dialog opens during playback.
        internal static bool TakeDice()
        {
            var next = Next;
            if (next == null || next.Kind != EventKind.Dice) return false;
            PendingDice = next.Integer;
            Position++;
            lastProgress = UnityEngine.Time.unscaledTime;
            Log("inject dice=" + next.Integer + " (" + Position + "/" + Document.Events.Count + ")");
            if (Position >= Document.Events.Count) { /* finish after the roll resolves */ }
            return true;
        }

        // Called when an option with this text appears during playback.
        internal static bool TakeChoice(string text)
        {
            var next = Next;
            if (next == null || next.Kind != EventKind.Choice) return false;
            if (!string.Equals(next.Data, text, StringComparison.Ordinal)) return false;
            Position++;
            Log("inject choice=" + text + " (" + Position + "/" + Document.Events.Count + ")");
            if (Position >= Document.Events.Count) Finish();
            return true;
        }

        private static bool clicking;

        // Called when a normal option is rendered during playback. The native click
        // is what advances the story, so the game stays in charge of its own flow.
        internal static void TryChoice(UnityEngine.UI.Button button, string text)
        {
            if (!Active || clicking || button == null) return;
            var next = Next;
            if (next == null || next.Kind != EventKind.Choice) return;
            if (!string.Equals(next.Data, text, StringComparison.Ordinal)) return;
            clicking = true;
            lastProgress = UnityEngine.Time.unscaledTime;
            try
            {
                Position++;
                Log("click choice=" + text + " (" + Position + "/" + Document.Events.Count + ")");
                button.onClick.Invoke();
                if (Position >= Document.Events.Count) Finish();
            }
            catch (Exception error) { Stop("选择注入失败：" + error.Message); }
            finally { clicking = false; }
        }

        internal static void AfterDice()
        {
            PendingDice = null;
            if (Active && Position >= Document.Events.Count) Finish();
        }

        private static void Log(string message)
        {
            try { Plugin.Instance?.Log("Replay: " + message); } catch (Exception) { }
        }
    }
}
