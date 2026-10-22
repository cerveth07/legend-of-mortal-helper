using System;
using System.Collections.Generic;
using System.IO;
using Mortal.Core;
using MortalHelper.Recovery;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    // Record a native baseline and subsequent dice results and option choices.
    // StoryReplay loads the baseline and verifies the replayed checkpoint state.
    internal static class StoryRecovery
    {
        private const int MaximumBaseline = 3500000;
        private const string Folder = "mhr";
        private static readonly List<RecordedEvent> Events = new List<RecordedEvent>();
        private static string baseline, client, script, scene;
        private static string scriptForDice;
        private static int diceOrdinal;

        internal static bool Recording { get; private set; }
        internal static string ClientId => client;
        internal static int Count => Events.Count;
        internal static int LastInteger => Events.Count == 0 ? 0 : Events[Events.Count - 1].Integer;
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
            if (Plugin.Instance == null || !Plugin.Instance.Options.ExperimentalRecovery.Value)
            { Status = "实验恢复默认关闭：随机状态重放尚未通过实机验收；如需测试，请在配置 Recovery/ExperimentalEnabled 中明确开启。"; return false; }
            if (Recording) { Status = "已经在记录中。"; return false; }
            if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract)
            { Status = "恢复锁定期间不能录制；请先正常读档。"; return false; }
            string active = SceneManager.GetActiveScene().name;
            if ((active != "Story" && active != "Break") || SceneController.Instance == null ||
                SceneController.Instance.IsLoading || SceneController.Instance.IsPrepare)
            { Status = "请在已加载完成的剧情场景开始录制。"; return false; }
            string json;
            try { json = SaveSystem.Instance == null ? null : SaveSystem.Instance.GetCurrentSaveData(); }
            catch (Exception error) { Status = "读取当前进度失败：" + error.Message; return false; }
            if (string.IsNullOrEmpty(json)) { Status = "当前没有可记录的进度（需要进入周目且已完成加载）。"; return false; }
            if (json.Length > MaximumBaseline) { Status = "当前进度快照过大（" + json.Length + " 字符），超过上限。"; return false; }
            GameSave native;
            try { native = JsonUtility.FromJson<GameSave>(json); }
            catch (Exception error) { Status = "原生进度解析失败：" + error.Message; return false; }
            if (native == null || native.CurrentScene != active)
            { Status = "原生进度与当前剧情场景不一致，不能录制。"; return false; }
            baseline = json;
            scene = active;
            client = "lom:" + Application.version;
            script = string.IsNullOrEmpty(HintAdapter.ScriptName) ? "unknown" : HintAdapter.ScriptName;
            Events.Clear();
            scriptForDice = null; diceOrdinal = 0;
            Recording = true;
            Status = "已开始记录（脚本 " + script + "）";
            return true;
        }

        internal static void Stop()
        {
            Recording = false;
            Status = "已停止记录，共 " + Events.Count + " 个事件。";
        }

        // Keys must be reproducible at replay time, so they are derived from the live
        // script name plus either the option text or the dice ordinal inside it.
        internal static string ChoiceKey(string scriptName, string text)
        {
            string script = string.IsNullOrEmpty(scriptName) ? "unknown" : scriptName;
            string option = (text ?? "").Trim();
            if (option.Length > 160) option = option.Substring(0, 160);
            return "C\u0001" + script + "\u0001" + option;
        }

        internal static string DiceKey(string scriptName, int ordinal)
        {
            string script = string.IsNullOrEmpty(scriptName) ? "unknown" : scriptName;
            return "D\u0001" + script + "\u0001" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string CurrentScript => string.IsNullOrEmpty(HintAdapter.ScriptName) ? "unknown" : HintAdapter.ScriptName;

        internal static void NoteDice(int value)
        {
            string script = CurrentScript;
            if (script != scriptForDice) { scriptForDice = script; diceOrdinal = 0; }
            Add(EventKind.Dice, DiceKey(script, ++diceOrdinal), value);
        }

        internal static void NoteChoice(string text)
        {
            // Option markers remain installed even when recording is off.
            // Never index the event list unless this exact click was added.
            if (Add(EventKind.Choice, ChoiceKey(CurrentScript, text), 1) && !string.IsNullOrEmpty(text))
                Events[Events.Count - 1].Data = text.Length > 200 ? text.Substring(0, 200) : text;
        }

        private static bool Add(EventKind kind, string key, int value)
        {
            if (!Recording) return false;
            if (Events.Count >= Archive.MaximumEvents) { Stop(); return false; }
            Events.Add(new RecordedEvent { Kind = kind, Key = key, Integer = value });
            return true;
        }

        // Strict fingerprint: the native save JSON (including story data), serialised
        // mission flags, and *live* stats, relationships and inventory. Missing
        // sources fail closed, never returning a shared success-shaped "unknown".
        // Only the top-level wall-clock TimeTick is normalized by ReplayStateDigest;
        // every other native field remains part of the strict comparison.
        internal static string Summary()
        {
            var save = SaveSystem.Instance;
            var player = PlayerStatManagerData.Instance;
            var mission = MissionManagerData.Instance;
            var items = ItemDatabase.Instance;
            if (save == null || player == null || mission == null || items == null ||
                player.Stats == null || player.Stats.List == null ||
                player.Relationships == null || player.Relationships.List == null)
                throw new InvalidDataException("必要的原生状态源未初始化，无法验证精确恢复。");

            string nativeJson = save.GetCurrentSaveData();
            string missionJson = JsonUtility.ToJson(mission);
            var facts = new List<string>
            {
                "scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                "script=" + CurrentScript
            };
            foreach (var stat in player.Stats.List)
            {
                if (stat == null) throw new InvalidDataException("原生属性列表包含空项。");
                string type = stat.StatType.ToString();
                // These two volatile stats are deliberately covered by the native
                // snapshot, but omitted from the separately captured live values.
                if (type == "行動次數" || type == "命運") continue;
                facts.Add("stat:" + type + "=" + stat.Value);
            }
            foreach (var relation in player.Relationships.List)
            {
                if (relation == null) throw new InvalidDataException("角色关系列表包含空项。");
                facts.Add("relation:" + relation.Type + "=" + relation.Value +
                    ":" + relation.Active);
            }
            AddItems(facts, "book", items.Books);
            AddItems(facts, "misc", items.Miscs);
            AddItems(facts, "special", items.Special);
            AddItems(facts, "consumable", items.Consumables);
            return ReplayStateDigest.Create(nativeJson, missionJson, facts);
        }

        private static void AddItems(List<string> facts, string kind, ItemDataCollection collection)
        {
            if (collection == null || collection.List == null)
                throw new InvalidDataException("物品清单缺失：" + kind);
            foreach (var item in collection.List)
            {
                if (item == null || string.IsNullOrEmpty(item.Id))
                    throw new InvalidDataException("物品清单存在无效条目：" + kind);
                facts.Add("item:" + kind + ":" + item.Id + "=" + item.CurrentCount);
            }
        }

        internal static bool Save(int slot)
        {
            if (!Recording) { Status = "先开始记录再保存检查点。"; return false; }
            if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract)
            { Status = "恢复锁定期间不能保存检查点。"; return false; }
            if (SceneManager.GetActiveScene().name != scene || SceneController.Instance == null ||
                SceneController.Instance.IsLoading || SceneController.Instance.IsPrepare)
            { Status = "剧情场景已切换或仍在加载，请重新开始录制。"; return false; }
            string folder = Directory;
            if (folder == null) { Status = "无法创建记录目录。"; return false; }
            try
            {
                var live = SaveSystem.Instance == null ? null :
                    JsonUtility.FromJson<GameSave>(SaveSystem.Instance.GetCurrentSaveData());
                if (live == null || live.CurrentScene != scene)
                    throw new InvalidDataException("原生进度与当前剧情场景不一致。");
                var document = new RecoveryDocument
                {
                    Kind = RecoveryKind.Story,
                    Client = client,
                    Script = script,
                    Scene = scene,
                    Checkpoint = "manual:" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    Baseline = baseline,
                    ExpectedState = Summary()
                };
                document.Events.AddRange(Events);
                var store = new ArchiveStore(folder);
                store.Save(slot, document);
                LastFile = store.PathFor(slot);
                // The first save has no .bak. Verify the file just written instead
                // of treating a missing *historical* backup as a save failure.
                var loaded = store.Load(slot);
                if (loaded.Kind != document.Kind || loaded.Checkpoint != document.Checkpoint ||
                    loaded.Events.Count != document.Events.Count ||
                    loaded.ExpectedState != document.ExpectedState)
                    throw new InvalidDataException("检查点写入后校验不一致。");
                string backupPath = LastFile + ".bak";
                string backupStatus = "无历史备份";
                if (File.Exists(backupPath))
                {
                    try { store.Load(slot, true); backupStatus = "历史备份可读"; }
                    catch (Exception) { backupStatus = "历史备份不可读"; }
                }
                Status = "检查点已保存（" + document.Events.Count + " 个事件，主档校验通过，" +
                    backupStatus + "）：" + Path.GetFileName(LastFile);
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
