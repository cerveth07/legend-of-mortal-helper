using System;
using System.Collections.Generic;
using System.IO;
using Mortal.Core;
using MortalHelper.Recovery;
using UnityEngine;

namespace MortalHelper.Game
{
    // Stage one of precise recovery: record what a checkpoint needs, namely the
    // current native save serialised to JSON plus every dice result and option
    // choice that happened after the recording started. Writing it back is NOT
    // implemented: restoring a baseline has to feed the game its own save data and
    // then replay events, which cannot be verified without playing the game.
    internal static class StoryRecovery
    {
        private const int MaximumBaseline = 3500000;
        private const string Folder = "mhr";
        private static readonly List<RecordedEvent> Events = new List<RecordedEvent>();
        private static string baseline, client, script;
        private static int serial;

        internal static bool Recording { get; private set; }
        internal static int Count => Events.Count;
        internal static string LastFile { get; private set; } = "";
        internal static string Status { get; set; } = "尚未记录";

        internal static string Directory
        {
            get
            {
                try
                {
                    string config = Path.GetDirectoryName(Plugin.Instance.Options.Config.ConfigFilePath);
                    string path = Path.Combine(config, Folder);
                    System.IO.Directory.CreateDirectory(path);
                    return path;
                }
                catch (Exception) { return null; }
            }
        }

        internal static bool Begin()
        {
            if (Recording) { Status = "已经在记录中。"; return false; }
            string json;
            try { json = SaveSystem.Instance == null ? null : SaveSystem.Instance.GetCurrentSaveData(); }
            catch (Exception error) { Status = "读取当前进度失败：" + error.Message; return false; }
            if (string.IsNullOrEmpty(json)) { Status = "当前没有可记录的进度（需要进入周目且已完成加载）。"; return false; }
            if (json.Length > MaximumBaseline) { Status = "当前进度快照过大（" + json.Length + " 字符），超过上限。"; return false; }
            baseline = json;
            client = "lom:" + Application.version + ":" + json.Length;
            script = string.IsNullOrEmpty(HintAdapter.ScriptName) ? "unknown" : HintAdapter.ScriptName;
            Events.Clear();
            serial = 0;
            Recording = true;
            Status = "已开始记录（脚本 " + script + "）";
            return true;
        }

        internal static void Stop()
        {
            Recording = false;
            Status = "已停止记录，共 " + Events.Count + " 个事件。";
        }

        internal static void NoteDice(int value)
        {
            Add(EventKind.Dice, "dice:" + (++serial), value);
        }

        internal static void NoteChoice(string text)
        {
            Add(EventKind.Choice, "choice:" + (++serial), 1);
            if (!string.IsNullOrEmpty(text)) Events[Events.Count - 1].Data = text.Length > 200 ? text.Substring(0, 200) : text;
        }

        private static void Add(EventKind kind, string key, int value)
        {
            if (!Recording) return;
            if (Events.Count >= Archive.MaximumEvents) { Stop(); return; }
            Events.Add(new RecordedEvent { Kind = kind, Key = key, Integer = value });
        }

        // Deterministic summary of the values that influence later story state.
        internal static string Summary()
        {
            try
            {
                var stats = PlayerStatManagerData.Instance.Stats;
                var builder = new System.Text.StringBuilder();
                foreach (GameStatType type in new[]
                {
                    GameStatType.銀兩, GameStatType.體力, GameStatType.內力, GameStatType.命運,
                    GameStatType.學問, GameStatType.武學點數, GameStatType.行動次數, GameStatType.道德
                })
                {
                    var stat = stats.Get(type);
                    builder.Append(type).Append('=').Append(stat == null ? "-1" : stat.Value).Append(';');
                }
                return builder.ToString();
            }
            catch (Exception) { return "unknown"; }
        }

        internal static bool Save(int slot)
        {
            if (!Recording) { Status = "先开始记录再保存检查点。"; return false; }
            string folder = Directory;
            if (folder == null) { Status = "无法创建记录目录。"; return false; }
            try
            {
                var document = new RecoveryDocument
                {
                    Kind = RecoveryKind.Story,
                    Client = client,
                    Script = script,
                    Scene = "Story",
                    Checkpoint = "manual:" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    Baseline = baseline,
                    ExpectedState = Summary()
                };
                document.Events.AddRange(Events);
                var store = new ArchiveStore(folder);
                store.Save(slot, document);
                LastFile = store.PathFor(slot);
                var loaded = store.Load(slot, true);
                Status = "检查点已保存（" + document.Events.Count + " 个事件，备份可读：" + (loaded != null) + "）：" + Path.GetFileName(LastFile);
                return true;
            }
            catch (Exception error)
            {
                Status = "保存检查点失败：" + error.Message;
                return false;
            }
        }
    }
}
