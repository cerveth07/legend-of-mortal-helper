using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using Fungus;
using HarmonyLib;
using MoonSharp.Interpreter;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    [Serializable]
    internal sealed class AnytimeLuaRandomState
    {
        public int Next, NextP;
        public int[] Seeds;
    }

    // Numbered slots keep the native file format. A sidecar describes how to
    // reconstruct the suspended Lua call from a script-entry snapshot.
    internal static class AnytimeStory
    {
        private const int MaximumEvents = 20000;
        private const int MaximumSidecarBytes = 16000000;
        private static readonly MethodInfo CreateSave = AccessTools.Method(typeof(SaveSystem), "CreateSaveData");
        private static readonly MethodInfo GetPath = AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile");
        private static readonly List<AnytimeStoryEvent> Events = new List<AnytimeStoryEvent>();
        private static readonly HashSet<string> StandardLuaTables = DiscoverLuaLibraries();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Script, Dictionary<string, Table>>
            LuaLibraries = new System.Runtime.CompilerServices.ConditionalWeakTable<Script, Dictionary<string, Table>>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Script, Dictionary<string, LuaApiIdentity>>
            LuaRootApis = new System.Runtime.CompilerServices.ConditionalWeakTable<Script, Dictionary<string, LuaApiIdentity>>();
        private sealed class LuaApiIdentity
        {
            internal DataType Type;
            internal object Reference;
        }
        internal static void RememberLuaRootApis(Script interpreter)
        {
            LuaRootApis.GetValue(interpreter, owner => {
                var apis = new Dictionary<string, LuaApiIdentity>(StringComparer.Ordinal);
                foreach (var pair in owner.Globals.Pairs)
                    if (pair.Key.Type == DataType.String && (pair.Value.Type == DataType.Function ||
                        pair.Value.Type == DataType.ClrFunction || pair.Value.Type == DataType.UserData))
                        apis.Add(pair.Key.String, new LuaApiIdentity { Type = pair.Value.Type,
                            Reference = pair.Value.Type == DataType.Function ? (object)pair.Value.Function :
                                pair.Value.Type == DataType.ClrFunction ? (object)pair.Value.Callback : pair.Value.UserData });
                return apis;
            });
        }
        private static bool SameLuaApi(LuaApiIdentity expected, DynValue actual)
        {
            if (expected.Type != actual.Type) return false;
            if (actual.Type == DataType.Function) return ReferenceEquals(expected.Reference, actual.Function);
            if (actual.Type == DataType.ClrFunction) return ReferenceEquals(expected.Reference, actual.Callback);
            if (actual.Type == DataType.UserData) return ReferenceEquals(expected.Reference, actual.UserData);
            return false;
        }
        private static HashSet<string> DiscoverLuaLibraries()
        {
            var names = new HashSet<string>(StringComparer.Ordinal) {
                "_G", "math", "string", "table", "coroutine", "os", "io", "debug", "package", "bit32", "utf8"
            };
            // Read the actual bundled MoonSharp module declarations. In this game
            // they include dynamic and json in addition to the usual Lua modules.
            foreach (var type in typeof(Script).Assembly.GetTypes())
                foreach (MoonSharpModuleAttribute module in type.GetCustomAttributes(typeof(MoonSharpModuleAttribute), false))
                    if (!string.IsNullOrEmpty(module.Namespace)) names.Add(module.Namespace);
            return names;
        }
        internal static void RememberLuaLibraries(Script interpreter)
        {
            LuaLibraries.GetValue(interpreter, owner => {
                var libraries = new Dictionary<string, Table>(StringComparer.Ordinal);
                foreach (var name in StandardLuaTables)
                {
                    var value = owner.Globals.Get(name);
                    if (value.Type == DataType.Table) libraries.Add(name, value.Table);
                }
                return libraries;
            });
        }
        private static bool IsLuaLibrary(Script interpreter, string name, Table table)
        {
            Dictionary<string, Table> libraries;
            Table original;
            return LuaLibraries.TryGetValue(interpreter, out libraries) && libraries.TryGetValue(name, out original) &&
                ReferenceEquals(table, original);
        }
        private static readonly List<int> WaitTokens = new List<int>();
        private static readonly List<string> WaitKinds = new List<string>();
        private static readonly List<string> WaitKeys = new List<string>();
        private static Script lua;
        private static string script, scene, baseline, baselineRandomState, baselineLuaRandomState, invalid;
        private static AnytimeLuaValue[] baselineGlobals;
        private static string pendingSlot, pendingPath;
        private static SlotTransaction pendingTransaction;
        private static string blockedReason;
        private static AnytimeStoryRecord pendingRecord, playback;
        private static int cursor, tokenSerial;
        private static int? replayDice;
        private static int replayDiceSelection;
        private static bool sceneReady, targetReached;
        private static bool returningTitle;
        private static float targetAt, lastProgress;
        // Replay executes the real script. Letting the engine run its own waits fast is the
        // only way to keep that from reading as "fast-forwarding the story"; the player's
        // configured speed is put back as soon as the target is reached.
        private const float ReplayScale = 15f;
        private static bool speedBoosted;
        private static float replayStartAt;
        // A failed recovery keeps the mutation gate closed, but the input lock it takes must
        // be released on its own: holding it forever leaves the player unable to quit.
        private static float failedAt;
        private static bool inputReleased;
        internal static float ReplayElapsed => Replaying && replayStartAt > 0f ? Time.unscaledTime - replayStartAt : 0f;
        internal static int ReplayDone => cursor;
        internal static int ReplayTotal => playback == null ? 0 : playback.Events.Length;

        internal static bool Replaying => playback != null && !Failed && !Verified;
        internal static bool Verified { get; private set; }
        internal static bool Failed { get; private set; }
        // True once the failure exit gave the input back; until then the failure still owns
        // the screen and the cover explains why the game is not responding.
        internal static bool InputReleased => inputReleased;
        internal static float FailureElapsed => Failed && failedAt > 0f ? Time.unscaledTime - failedAt : 0f;
        internal static string Failure { get; private set; }
        private static bool SameScript(string left, string right) =>
            string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
        internal static string BlockReason
        {
            get
            {
                string active = SceneManager.GetActiveScene().name;
                if (active != "Story" && active != "Break") return null;
                if (Failed) return "剧情恢复未通过核对，请重新读取原生存档。";
                if (Replaying) return "正在恢复剧情，暂不能存档。";
                if (Plugin.Instance == null || !Plugin.Instance.Gate.CanInteract) return "恢复锁定期间不能存档。";
                if (SceneController.Instance == null || SceneController.Instance.IsLoading || SceneController.Instance.IsPrepare)
                    return "请等场景加载完成。";
                if (invalid != null) return invalid;
                if (SaveSystem.Instance == null || lua == null || string.IsNullOrEmpty(baseline) || baselineGlobals == null ||
                    !string.Equals(active, scene, StringComparison.Ordinal) ||
                    !SameScript(script, PlayerStatManagerData.Instance?.CurrentStoryScript))
                    return "当前剧情尚未建立可重放的脚本起点。";
                if (AnytimeDice.Active)
                {
                    if (AnytimeDice.SaveKey == null) return "轮盘正在演出或提交，请在掷骰前或结果页等待时存档。";
                }
                else if (DiceAdapter.Dialog != null && DiceAdapter.Dialog.isActiveAndEnabled)
                    return "当前轮盘尚未建立可恢复的等待状态。";
                else if (WaitTokens.Count != 1 || (WaitKinds[0] != "Say" && WaitKinds[0] != "Choose"))
                    return "请在对话、选项或骰子稳定等待时存档。";
                if (Events.Count >= MaximumEvents) return "本段剧情记录过长，请继续到下一个脚本后存档。";
                return null;
            }
        }

        internal static string NumericEditBlockReason
        {
            get
            {
                if (pendingRecord != null) return "正在保存存档，请稍候。";
                if (NumericShopRefresh.Open) return null;
                if (Events.Count >= MaximumEvents) return "本段记录已满，请进入下一段剧情后修改。";
                if (AnytimeDice.Active)
                    return AnytimeDice.SaveKey == null ? "请等轮盘动画结束后修改。" : null;
                if (WaitTokens.Count != 1 || (WaitKinds[0] != "Say" && WaitKinds[0] != "Choose"))
                    return "请在对话、选项或骰子停稳后修改。";
                return null;
            }
        }
        internal static void RecordNumericEdit(string key, int before, int after)
        {
            if (NumericShopRefresh.Open)
            {
                // Native shop transactions are not part of the story journal.
                // Never certify a same-script save that could not replay them.
                invalid = "商店中已修改数值，请进入下一段剧情后再保存中途档。";
                return;
            }
            string kind = AnytimeDice.Active ? "Dice" : WaitKinds[0];
            string wait = AnytimeDice.Active ? AnytimeDice.SaveKey : WaitKeys[0];
            Record(new AnytimeStoryEvent { Kind = "Numeric", Key = StoryNumericEdits.Pack(kind, wait, key), Choice = before, Number = after });
        }
        private static void ReplayNumericEdits(string kind, string key)
        {
            int next;
            try { next = StoryNumericEdits.Apply(playback.Events, cursor, kind, key, NumericEdits.ApplyRecorded); }
            catch (Exception error)
            {
                Fail("附带的数值修改无法重放：" + error.GetBaseException().Message);
                return;
            }
            if (next != cursor) { cursor = next; lastProgress = Time.unscaledTime; }
        }
        internal static void ReplayDiceNumericEdits()
        {
            if (!Replaying || cursor >= playback.Events.Length) return;
            try
            {
                string recorded = StoryNumericEdits.WaitKey(playback.Events[cursor], "Dice");
                if (recorded == null) return;
                AnytimeDice.RestoreTargetRange(recorded);
                ReplayNumericEdits("Dice", AnytimeDice.SaveKey);
            }
            catch (Exception error) { Fail("轮盘数值修改无法重放：" + error.GetBaseException().Message); }
        }

        internal static bool BeforeNativeSave(string slot)
        {
            string reason = BlockReason;
            if (reason != null) { AnytimeNative.SaveFailed(reason); return false; }
            try
            {
                if (slot == null || slot.Length != 3 || slot == "000" ||
                    slot.Any(c => c < '0' || c > '9'))
                    throw new InvalidDataException("请选择游戏系统菜单中的编号槽位。");
                AbortNativeSave();
                string path = PathFor(slot);
                if (string.IsNullOrEmpty(path)) throw new IOException("无法定位原生存档槽位。");
                AnytimeNative.SettleSlot(slot, path);
                if (File.Exists(JournalFor(path)))
                    throw new InvalidDataException("此槽位有上次未完成的存档事务；请先读取该槽位让插件自动恢复。");
                // A slot with no native file cannot own any companion. Move them
                // aside so the "-required without sidecar" check below cannot
                // block this slot forever, and keep them for manual recovery.
                if (!File.Exists(path)) AnytimeNative.QuarantineOrphans(path);
                // Legacy 0.1.x interruption markers carry no journal, so the only
                // safe answer is to stop. They are only meaningful while a native
                // file exists: with no .dat there is nothing that could be torn.
                bool nativeExists = File.Exists(path);
                if (nativeExists && (File.Exists(path + ".mha-pending") || File.Exists(path + ".mhduel-pending")))
                    throw new InvalidDataException("此槽位有上一版本中断的存档记录，请先恢复备份。");
                if (File.Exists(path + ".mha-required") && !File.Exists(path + ".mha"))
                    throw new InvalidDataException("此槽位缺少必需的剧情附档，请先恢复完整备份。");
                if (File.Exists(path + ".mhduel-required") && !File.Exists(path + ".mhduel"))
                    throw new InvalidDataException("此槽位缺少必需的单挑附档，请先恢复完整备份。");
                // SameScript treats "both sides unavailable" as equal, so the guard above
                // can pass with an empty script name. PrepareLoad refuses an empty Script,
                // so committing here would create a slot this plugin can never load again.
                if (string.IsNullOrEmpty(script)) throw new InvalidDataException("当前剧情尚未建立可重放的脚本起点。");
                var record = new AnytimeStoryRecord {
                    Client = Application.version, Baseline = baseline, Script = script, Scene = scene,
                    WaitKind = AnytimeDice.Active ? "Dice" : WaitKinds[0],
                    WaitKey = AnytimeDice.Active ? AnytimeDice.SaveKey : WaitKeys[0],
                    BaselineRandomState = baselineRandomState,
                    RandomState = JsonUtility.ToJson(UnityEngine.Random.state),
                    BaselineLuaRandomState = baselineLuaRandomState,
                    LuaRandomState = CaptureLuaRandom(lua),
                    BaselineGlobals = baselineGlobals, ExpectedGlobals = CaptureGlobals(lua),
                    Events = Events.ToArray(), Expected = Snapshot()
                };
                if (string.IsNullOrEmpty(record.BaselineLuaRandomState) || string.IsNullOrEmpty(record.LuaRandomState) ||
                    string.IsNullOrEmpty(record.BaselineRandomState) || record.BaselineRandomState == "{}" ||
                    string.IsNullOrEmpty(record.RandomState) || record.RandomState == "{}")
                    throw new InvalidDataException("无法序列化游戏随机状态。");
                // The pre-write budget must use the same unit and the same fields as the
                // encoder, or an accepted save is rolled back after the native write.
                if (string.IsNullOrEmpty(record.Expected) ||
                    AnytimeStoryCodec.Measure(record) > AnytimeStoryCodec.Limit)
                    throw new InvalidDataException("当前进度超出附加记录容量。");
                // Snapshot the slot before the game writes anything into it.
                pendingTransaction = SlotTransaction.Begin(JournalFor(path), "story", new[] {
                    path, path + ".mha", path + ".mha-required", path + ".mhduel", path + ".mhduel-required"
                });
                pendingSlot = slot; pendingPath = path; pendingRecord = record;
                File.WriteAllText(path + ".mha-required", "3", new UTF8Encoding(false));
                return true;
            }
            catch (Exception error)
            {
                string message = "剧情存档准备失败：" + error.Message;
                if (pendingTransaction != null)
                {
                    var report = pendingTransaction.Rollback();
                    pendingTransaction = null;
                    if (report.Complete) message = "剧情存档准备失败，槽位已回退：" + error.Message;
                    else
                    {
                        blockedReason = report.Detail;
                        message = "剧情存档准备失败且回退未完成；请保留 .mhtx 事务日志及其备份：" +
                            error.Message + "；" + report.Detail;
                    }
                }
                ClearPending();
                AnytimeNative.SaveFailed(message);
                return false;
            }
        }

        internal static void AfterNativeSave(string saveFile, object gameSave)
        {
            if (pendingRecord == null || !string.Equals(saveFile, pendingPath, StringComparison.Ordinal)) return;
            try
            {
                var attempted = gameSave as GameSave;
                if (attempted == null || !SameSnapshot(attempted, pendingRecord.Expected))
                    throw new InvalidDataException("原生存档与准备保存的剧情状态不一致。");
                string nativeHash = HashFile(saveFile);
                if (nativeHash != HashObject(attempted)) throw new IOException("原生存档未完整写入。");
                pendingRecord.NativeHash = nativeHash;
                string side = saveFile + ".mha", temp = side + ".tmp";
                string json = AnytimeStoryCodec.Write(pendingRecord);
                if (Encoding.UTF8.GetByteCount(json) > MaximumSidecarBytes) throw new InvalidDataException("剧情附加记录过大。");
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (File.Exists(side)) File.Replace(temp, side, null); else File.Move(temp, side);
                // Read the committed pair before claiming the transaction.
                ReadRecord(saveFile, attempted);
                if (pendingTransaction != null)
                {
                    pendingTransaction.RecordReplacement(nativeHash);
                    pendingTransaction.Commit();
                }
                AnytimeNative.FinishOperation(pendingTransaction, saveFile);
                ClearPending();
            }
            catch (Exception error)
            {
                bool restored = RollBack();
                AnytimeNative.SaveFailed(restored
                    ? "剧情存档未完成，槽位已回退：" + error.Message
                    : "剧情存档未完成，自动回退失败；已保留 .mhtx 事务日志与备份：" + error.Message +
                        (blockedReason == null ? "" : "；" + blockedReason));
            }
        }

        internal static void AbortNativeSave() { if (pendingRecord != null) RollBack(); }

        internal static string JournalFor(string path) => SlotTransaction.JournalFor(path);

        // Replaces ForgetAfterOrdinarySave. A verified ordinary native save owns
        // the slot, so the story companions are retired through the same journal
        // that protects the write; an interrupted retirement is finished later by
        // AnytimeNative.SettleSlot instead of leaving the slot unreadable.
        internal static bool OrdinalSaveVerified(string saveFile, object gameSave)
        {
            if (pendingRecord != null || !(gameSave is GameSave)) return false;
            try { return HashFile(saveFile) == HashObject(gameSave); }
            catch (Exception error) { Plugin.Instance?.Report(error); return false; }
        }

        internal static void BeforeNativeLoad()
        {
            AnytimeDice.Reset();
            // A new load owns the segment: a stale invalidation from the previous one must not
            // leak into it, or the fresh segment refuses to save for no visible reason.
            invalid = null;
            playback = null; Failed = Verified = false; Failure = null;
            sceneReady = targetReached = returningTitle = false; replayDice = null; UnlockInput();
            RestoreReplaySpeed();
            replayStartAt = 0f;
        }

        internal static void PrepareLoad(string slot, ref GameSave save)
        {
            if (save == null) return;
            string path = PathFor(slot);
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path + ".mha-pending"))
                throw new InvalidDataException("此槽位的剧情存档中断；请从备份恢复后再读档。");
            if (File.Exists(path + ".mha-rollback") || File.Exists(path + ".mha.rollback"))
                Report("此槽位仍保留上一版本的 .mha-rollback 备份；读取不依赖它，请勿手动删除。");
            if (!File.Exists(path + ".mha"))
            {
                if (!File.Exists(path + ".mha-required")) return;
                throw new InvalidDataException("此剧情编号档缺少配套的 .mha 剧情记录，不能安全恢复。请恢复完整备份。");
            }
            // The sidecar must describe this exact native file. When it does not,
            // the native file was replaced by something newer, so the companion is
            // stale rather than missing: keep it out of the way and let the player
            // load the complete native save they actually have.
            string nativeHash = HashFile(path);
            string recorded = ReadSidecarHeader(path);
            if (recorded != null && recorded != nativeHash)
            {
                if (File.Exists(path + ".mha-required"))
                    throw new InvalidDataException("此槽位的剧情附档与原生档不匹配（附档必需标记仍在），" +
                        "不能确认哪一个才是完整存档。请从完整备份恢复后再读取。");
                Quarantine(path + ".mha");
                Report("该槽位的剧情附档属于更早的存档，已保留为 .mha.stale 并按原生存档载入。");
                return;
            }
            AnytimeStoryRecord record;
            try { record = ReadRecord(path, save); }
            catch (Exception error) when (error is InvalidDataException || error is FormatException)
            {
                // A sidecar written by an older build cannot be replayed by this one, but the
                // native file beside it is a valid save. Keep that save usable instead of
                // refusing the load: the extra point is lost, the progress is not.
                Quarantine(path + ".mha");
                Report("该槽位的剧情附档来自旧版本或与存档不匹配（" + error.Message +
                    "），已保留为 .mha.stale 并按原生存档载入。");
                return;
            }
            var opening = JsonUtility.FromJson<GameSave>(record.Baseline);
            if (opening == null || opening.CurrentScene != record.Scene ||
                string.IsNullOrEmpty(record.Script) || record.Events == null ||
                record.BaselineGlobals == null || record.ExpectedGlobals == null ||
                string.IsNullOrEmpty(record.BaselineRandomState) || string.IsNullOrEmpty(record.RandomState) ||
                string.IsNullOrEmpty(record.BaselineLuaRandomState) || string.IsNullOrEmpty(record.LuaRandomState) ||
                (record.WaitKind != "Say" && record.WaitKind != "Choose" && record.WaitKind != "Dice"))
                throw new InvalidDataException("剧情附加记录缺少可重放的起点或目标。");
            if (record.Events.Length > MaximumEvents) throw new InvalidDataException("剧情事件记录超限。");
            foreach (var entry in record.Events)
                if (entry == null || (entry.Kind != "Say" && entry.Kind != "Choose" && entry.Kind != "Random" && entry.Kind != "Dice" && entry.Kind != "DiceRoll" && entry.Kind != "Numeric"))
                    throw new InvalidDataException("剧情事件类型无效。");
            // Native PlayerStatManagerData.Load resumes StartStoryScript, not
            // CurrentStoryScript (which its Save never writes). Use the recorded
            // execution entry, then restore the original metadata at ScriptEntering.
            opening.StartStoryScript = record.Script;
            opening.TimeTick = save.TimeTick;
            // The read state only lives in memory until the game writes the universe file,
            // and a native load replaces it from disk: persist what this session read first,
            // or the lines just read come back unread.
            try { SaveSystem.Instance?.SaveUniverseData(); }
            catch (Exception error) { Plugin.Instance?.Report(error); }
            playback = record; save = opening; cursor = 0;
            sceneReady = false; targetReached = false; targetAt = 0f;
            replayDice = null;
            replayStartAt = Time.unscaledTime;
            lastProgress = Time.unscaledTime; Verified = Failed = false; Failure = null;
            Plugin.Instance?.Log("[Anytime] replay start slot=" + slot + " script=" + record.Script +
                " events=" + record.Events.Length + " scale=" + ReplayScale);
            Events.Clear(); WaitTokens.Clear(); WaitKinds.Clear(); WaitKeys.Clear();
            LockInput();
        }

        internal static void AfterNativeLoadScene() { if (Replaying) sceneReady = true; }

        internal static void Tick()
        {
            if (Failed)
            {
                RestoreReplaySpeed();
                float waited = Time.unscaledTime - failedAt;
                if (!inputReleased) LockInput();
                var controller = SceneController.Instance;
                // A missing or still-loading scene must not be forced into another transition.
                bool loading = controller == null || controller.IsLoading || controller.IsPrepare;
                if (FailureExit.ShouldRequestTitle(loading, returningTitle, waited))
                {
                    // Latched only after the call returns: a throwing request must be
                    // retried next frame, not silently cancelled forever.
                    try { controller.LoadTitle(); returningTitle = true; }
                    catch (Exception error) { ReleaseFailureInput("恢复失败且无法回到标题：" + error.Message); return; }
                }
                if (FailureExit.ShouldReleaseInput(waited))
                    ReleaseFailureInput("恢复失败");
                return;
            }
            if (!Replaying)
            {
                RestoreReplaySpeed();
                return;
            }
            LockInput();
            BoostReplaySpeed();
            // Measure a stall, not the total elapsed time: a long script that keeps
            // consuming events is making progress even when the whole replay takes
            // several minutes.
            if (FailureExit.ShouldAbortForMissingScene(sceneReady, Time.unscaledTime - replayStartAt))
            {
                Fail("原生加载在 " + (int)FailureExit.SceneReadyLimit + " 秒内没有就绪。");
                return;
            }
            if (Time.unscaledTime - lastProgress > 120f)
            { Fail("剧情回放超过两分钟没有匹配到记录的下一个事件（已重放 " + cursor + " 个）。"); return; }
            if (!sceneReady || !targetReached || Time.unscaledTime - targetAt < .25f) return;
            try
            {
                if (cursor != playback.Events.Length || (playback.WaitKind == "Dice"
                    ? AnytimeDice.SaveKey == null || AnytimeDice.SaveKey != playback.WaitKey
                    : WaitTokens.Count != 1 || WaitKinds[0] != playback.WaitKind || WaitKeys[0] != playback.WaitKey))
                    throw new InvalidDataException("对话等待点与记录不同。");
                if (!SameGlobals(CaptureGlobals(lua), playback.ExpectedGlobals))
                    throw new InvalidDataException("Lua 剧情变量与存档内容不同。");
                // Native Save copies the live Unity RNG into FreeRandomState.
                // Presentation also consumes that RNG while replaying, so restore
                // the recorded target seed before taking the verification snapshot.
                // Keep every native field, including all four RNG words, checked.
                UnityEngine.Random.state = JsonUtility.FromJson<UnityEngine.Random.State>(playback.RandomState);
                string actual = Snapshot();
                if (!SameSnapshot(actual, playback.Expected))
                    throw new InvalidDataException("剧情状态与存档内容不同。" + SnapshotMismatch(playback.Expected, actual));
                RestoreLuaRandom(lua, playback.LuaRandomState);
                baseline = playback.Baseline; baselineGlobals = playback.BaselineGlobals;
                baselineRandomState = playback.BaselineRandomState;
                baselineLuaRandomState = playback.BaselineLuaRandomState;
                script = playback.Script; scene = playback.Scene;
                Events.Clear(); Events.AddRange(playback.Events);
                playback = null; Verified = true; invalid = null; UnlockInput();
                RestoreReplaySpeed();
                StoryReplay.OnNativeLoadCompleted();
                if (Plugin.Instance != null)
                {
                    Plugin.Instance.Status = "剧情已恢复到存档位置，状态核对通过。";
                    Plugin.Instance.Log("[Anytime] " + Plugin.Instance.Status);
                    Plugin.Instance.Log("[Anytime] replay done " + ReplaySeconds() + "s events=" + cursor +
                        " script=" + script + " scale=" + ReplayScale);
                }
                replayStartAt = 0f;
            }
            catch (Exception error) { Fail(error.Message); }
        }

        internal static void Reset()
        {
            AnytimeDice.Reset();
            AbortNativeSave(); playback = null; Verified = Failed = false; Failure = null;
            blockedReason = null;
            lua = null; script = scene = baseline = baselineRandomState = baselineLuaRandomState = invalid = null; baselineGlobals = null;
            Events.Clear(); WaitTokens.Clear(); WaitKinds.Clear(); WaitKeys.Clear();
            replayDice = null; sceneReady = targetReached = false; UnlockInput();
            returningTitle = false;
            RestoreReplaySpeed();
            replayStartAt = 0f;
            failedAt = 0f; inputReleased = false;
        }

        // The save cannot be replayed from the entry the game actually started. Stop the
        // replay before it consumes anything, let the script run normally from its own entry,
        // and refuse new saves in this segment (the state is no longer a known checkpoint).
        private static void DegradeFromEntry(string reason)
        {
            string detail = Describe(reason);
            playback = null;
            Failed = false;
            Verified = false;
            Failure = null;
            invalid = reason;
            inputReleased = true;
            UnlockInput();
            StoryReplay.OnNativeLoadCompleted();
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Status = "该中途档无法精确恢复（" + reason + "），已按本段脚本入口继续；本段不再提供中途存档。";
                try { Plugin.Instance.Log("[Anytime] degraded: " + detail); }
                catch (Exception) { }
            }
        }

        // The native load watchdog gave up: stop waiting for a scene that is not coming.
        internal static void NativeLoadAborted(string reason)
        {
            if (Replaying) Fail(reason);
        }

        internal static void EnvironmentFailed(Exception error)
        {
            string reason = "Lua 包装安装失败：" + error.Message;
            if (Replaying) Fail(reason);
            else { baseline = null; invalid = reason; }
            Plugin.Instance?.Report(error);
        }

        internal static void ScriptEntering(Script interpreter)
        {
            lua = interpreter;
            WaitTokens.Clear(); WaitKinds.Clear(); WaitKeys.Clear();
            try { AnytimeStoryEnvironmentPatch.EnsureInstalled(lua); }
            catch (Exception error) { EnvironmentFailed(error); return; }
            DynValue wrappedFlag = lua == null ? null : lua.Globals.Get("__mortal_anytime_wrapped");
            bool wrapped = wrappedFlag != null && wrappedFlag.Type == DataType.Boolean && wrappedFlag.Boolean;
            if (!wrapped)
            {
                if (Replaying) Fail("Lua 对话/选项包装未安装，无法重放。");
                else { baseline = null; invalid = "当前 Lua 环境缺少对话或选项函数，无法安全存档。"; }
                return;
            }
            if (Replaying)
            {
#if !NO_PROBE
                AnytimeStoryDiagnostics.TraceFlag("ScriptEntering", MissionManagerData.Instance?.GetFlag("S9200"));
#endif
                if (!SameScript(playback.Script, PlayerStatManagerData.Instance?.CurrentStoryScript))
                {
                    // Nothing has been restored yet: the game is about to run its own entry
                    // script. Keep it playable and refuse to certify this save.
                    DegradeFromEntry("剧情脚本与存档起点不一致（存档 " + playback.Script + "，当前 " +
                        (PlayerStatManagerData.Instance?.CurrentStoryScript ?? "<none>") + "）");
                    return;
                }
                try
                {
                    var original = JsonUtility.FromJson<GameSave>(playback.Baseline);
                    PlayerStatManagerData.Instance.SetStartScript(original.StartStoryScript);
                    RestoreBaselineFlags(original);
                    RestoreGlobals(lua, playback.BaselineGlobals);
                    UnityEngine.Random.state = JsonUtility.FromJson<UnityEngine.Random.State>(playback.BaselineRandomState);
                    RestoreLuaRandom(lua, playback.BaselineLuaRandomState);
                }
                catch (Exception error) { Fail("Lua 起点还原失败：" + error.Message); }
                return;
            }
            Events.Clear(); invalid = null;
            try
            {
                script = PlayerStatManagerData.Instance?.CurrentStoryScript;
                scene = SceneManager.GetActiveScene().name;
                if (scene != "Story" && scene != "Break") { baseline = null; return; }
                baseline = Snapshot();
                baselineRandomState = JsonUtility.ToJson(UnityEngine.Random.state);
                if (string.IsNullOrEmpty(baselineRandomState) || baselineRandomState == "{}")
                    throw new InvalidDataException("无法记录游戏随机状态。");
                baselineGlobals = CaptureGlobals(lua);
                baselineLuaRandomState = CaptureLuaRandom(lua);
                try { Plugin.Instance?.Log("[Anytime] baseline captured scene=" + scene + " script=" + script +
                    " globals=" + baselineGlobals.Length); }
                catch (Exception) { }
                // Whether the name we record is the script the game will re-enter is exactly
                // what broke the ch2_2_3_* restores; record both names when diagnosing.
                try
                {
                    if (Plugin.Instance != null && Plugin.Instance.Options.DiagnoseScript.Value)
                    {
                        var entry = JsonUtility.FromJson<GameSave>(baseline);
                        Plugin.Instance.Log("[Anytime] baseline script=" + script + " entry=" +
                            (entry == null ? "<none>" : entry.StartStoryScript) +
                            " current=" + (entry == null ? "<none>" : entry.CurrentStoryScript));
                    }
                }
                catch (Exception) { }
            }
            catch (Exception error)
            {
                baseline = null;
                invalid = "无法记录剧情起点：" + error.Message;
                try { Plugin.Instance?.Log("[Anytime] " + invalid); }
                catch (Exception) { }
            }
        }

        // A failed replay must not throw inside native or Lua code: the exception escapes into
        // the game script coroutine and leaves it dead, which is exactly the "restore failed and
        // now the game is stuck" state. Every wrapper answers neutrally instead, so the script
        // keeps running from where the game put it.
        internal static bool BeforeSay(string text)
        {
            if (Failed) return false;
            string key = Digest(text);
            if (!Replaying) return false;
            ReplayNumericEdits("Say", key);
            if (cursor == playback.Events.Length) { Reach("Say", key); return false; }
            var next = playback.Events[cursor];
            if (next.Kind != "Say" || next.Key != key) { AbortReplay("剧情对话顺序与存档不同。"); return false; }
            cursor++; lastProgress = Time.unscaledTime; return true;
        }
        internal static void SayDone(string text) { Record(new AnytimeStoryEvent { Kind = "Say", Key = Digest(text) }); }
        internal static int BeforeChoose(string key, int count)
        {
            if (Failed) return 0;
            if (!Replaying) return 0;
            string digest = Digest(key);
            ReplayNumericEdits("Choose", digest);
            if (cursor == playback.Events.Length) { if (playback.WaitKind != "Dice") Reach("Choose", digest); return 0; }
            var next = playback.Events[cursor];
            if (next.Kind != "Choose" || next.Key != digest || next.Choice < 1 || next.Choice > count)
            { AbortReplay("剧情选项与存档不同。"); return 0; }
            cursor++; lastProgress = Time.unscaledTime; return next.Choice;
        }
        internal static void ChooseDone(string key, int choice)
        { Record(new AnytimeStoryEvent { Kind = "Choose", Key = Digest(key), Choice = choice }); }
        internal static double RandomDraw(double drawn)
        {
            if (Failed) return drawn;
            if (Replaying)
            {
                if (cursor >= playback.Events.Length || playback.Events[cursor].Kind != "Random")
                { AbortReplay("剧情随机事件与存档不同。"); return drawn; }
                lastProgress = Time.unscaledTime;
                return playback.Events[cursor++].Number;
            }
            Record(new AnytimeStoryEvent { Kind = "Random", Number = drawn });
            return drawn;
        }
        internal static void BeforeDice(ref int random)
        {
            if (Failed) return;
            if (!Replaying) { AnytimeDice.InjectRollRaw(random); return; }
            if (cursor >= playback.Events.Length || playback.Events[cursor].Kind != "Dice")
            { AbortReplay("轮盘判定次数与存档不同。"); return; }
            replayDiceSelection = (int)playback.Events[cursor].Number;
            replayDice = playback.Events[cursor++].Choice;
            lastProgress = Time.unscaledTime;
            random = replayDice.Value;
            AnytimeDice.InjectRollRaw(random);
        }
        internal static void AfterDice(DiceCheckResult result)
        {
            if (Failed) return;
            if (Replaying)
            {
                if (!replayDice.HasValue || result == null || result.Random != replayDice.Value)
                { AbortReplay("轮盘判定结果与存档不同。"); return; }
                if (replayDiceSelection > 0) result.Result = replayDiceSelection;
                replayDice = null; replayDiceSelection = 0;
            }
            else if (result != null)
                Record(new AnytimeStoryEvent { Kind = "Dice", Choice = result.Random, Number = result.Result });
        }
        internal static bool TryPeekDiceRoll(out string key, out int paid)
        {
            key = null; paid = 0;
            if (!Replaying || cursor >= playback.Events.Length || playback.Events[cursor].Kind != "DiceRoll") return false;
            var entry = playback.Events[cursor]; key = entry.Key; paid = entry.Choice; return true;
        }
        internal static void RecordDiceRoll(string key, int paid)
        {
            if (Failed) return;
            if (!Replaying) { Record(new AnytimeStoryEvent { Kind = "DiceRoll", Key = key, Choice = paid }); return; }
            string expected; int expectedPaid;
            if (!TryPeekDiceRoll(out expected, out expectedPaid) || expected != key || paid != expectedPaid)
            { AbortReplay("骰子范围或命运消耗与记录不同。"); return; }
            cursor++; lastProgress = Time.unscaledTime;
        }
        internal static bool ReachDice(string key)
        {
            if (!Replaying || cursor != playback.Events.Length || playback.WaitKind != "Dice") return false;
            AnytimeDice.RestoreTargetRange(playback.WaitKey);
            if (AnytimeDice.SaveKey != playback.WaitKey) { AbortReplay("骰子等待状态与存档不同。"); return false; }
            if (!targetReached) { targetReached = true; targetAt = Time.unscaledTime; } return true;
        }
        internal static void FailDice(string reason)
        { if (Replaying) Fail(reason); else invalid = "骰子存档不可用：" + reason; }
        internal static bool SkipWait()
        {
            if (Failed) return false;
            return Replaying && !targetReached;
        }
        internal static int EnterWait(string kind, string key)
        {
            if (Failed) return 0;
            int token = ++tokenSerial;
            WaitTokens.Add(token); WaitKinds.Add(kind); WaitKeys.Add(Digest(key));
            return token;
        }
        internal static void LeaveWait(int token)
        {
            int index = WaitTokens.IndexOf(token);
            if (index < 0) return;
            WaitTokens.RemoveAt(index); WaitKinds.RemoveAt(index); WaitKeys.RemoveAt(index);
        }

        private static void Reach(string kind, string key)
        {
            if (kind != playback.WaitKind || key != playback.WaitKey)
            { AbortReplay("剧情等待位置与存档不同。"); return; }
            targetReached = true; targetAt = Time.unscaledTime;
        }
        private static void Record(AnytimeStoryEvent item)
        {
            if (Replaying || baseline == null || invalid != null) return;
            if (Events.Count == MaximumEvents) { invalid = "剧情事件记录超限。"; return; }
            Events.Add(item);
        }
        // True while the failure still owns the game (input held, title pending).
        internal static bool Frozen => Failed && !inputReleased;

        // Last resort of the failure path: the recovery is over (the gate stays failed so no
        // edits or saves are certified), but the player keeps control of the game.
        private static void ReleaseFailureInput(string reason)
        {
            if (inputReleased) return;
            inputReleased = true;
            UnlockInput();
            string message = FailureExit.FailReason(reason) + "：已解除输入锁，请自行回到标题后重新读取原生存档。";
            if (Plugin.Instance != null)
            {
                Plugin.Instance.Status = message;
                try { Plugin.Instance.Log("[Anytime] failure exit released input; reason=" + reason); }
                catch (Exception) { }
            }
        }

        // The engine's own waits scale with time, so the replay can run fast without
        // changing its order or its side effects.
        private static void BoostReplaySpeed()
        {
            if (Plugin.Instance == null || !sceneReady) return;   // loading is not what we compress
            if (Time.timeScale <= 0f) return;   // the game paused itself; do not resume it
            speedBoosted = true;
            if (!Mathf.Approximately(Time.timeScale, ReplayScale)) Time.timeScale = ReplayScale;
        }

        internal static void RestoreReplaySpeed()
        {
            if (!speedBoosted) return;
            speedBoosted = false;
            if (Plugin.Instance == null) return;
            // Only undo our own value: the game may have set its own in the meantime.
            if (Mathf.Approximately(Time.timeScale, ReplayScale))
                Time.timeScale = Plugin.Instance.Options.ValidSpeed;
        }

        private static string ReplaySeconds()
        {
            float seconds = replayStartAt > 0f ? Time.unscaledTime - replayStartAt : 0f;
            return (Mathf.Round(seconds * 10f) / 10f).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void Fail(string reason)
        {
            // Describe before clearing playback, so the report names the exact
            // position instead of only the symptom.
            string detail = Describe(reason);
            Failed = true; Failure = reason; playback = null; LockInput();
            failedAt = Time.unscaledTime; inputReleased = false;
            RestoreReplaySpeed();
            replayStartAt = 0f;
            Plugin.Instance?.Gate.Fail("剧情存档恢复失败：" + reason);
            if (Plugin.Instance != null) Plugin.Instance.Status = "剧情恢复失败：" + reason + "；请重新读取原生存档。";
            try { Plugin.Instance?.Log("[Anytime] " + detail); } catch (Exception) { }
        }
        // Diagnostics deliberately spell out "none" for an unavailable value and
        // never substitute a default that could be mistaken for a match.
        private static string Describe(string reason)
        {
            var text = new StringBuilder("story recovery failed: reason=").Append(reason);
            text.Append("; scene=").Append(scene ?? "<none>");
            text.Append("; script=").Append(script ?? "<none>");
            text.Append("; live-script=").Append(PlayerStatManagerData.Instance?.CurrentStoryScript ?? "<none>");
            if (playback != null)
            {
                text.Append("; target=").Append(playback.WaitKind).Append(':').Append(Short(playback.WaitKey));
                text.Append("; progress=").Append(cursor).Append('/').Append(playback.Events.Length);
                text.Append("; last-replayed=").Append(cursor > 0 ? Event(playback.Events[cursor - 1]) : "<none>");
                text.Append("; failing=").Append(cursor < playback.Events.Length ? Event(playback.Events[cursor]) : "<recorded end>");
                text.Append("; expected-bytes=").Append(playback.Expected == null ? "<none>" : playback.Expected.Length.ToString());
            }
            else text.Append("; progress=<no active replay>");
            text.Append("; waits=").Append(WaitTokens.Count);
            if (WaitTokens.Count == 1) text.Append('(').Append(WaitKinds[0]).Append(':').Append(Short(WaitKeys[0])).Append(')');
            text.Append("; pending-slot=").Append(pendingSlot ?? "<none>");
            text.Append("; transaction=").Append(pendingTransaction == null ? "<none>" : pendingTransaction.State.ToString());
            if (blockedReason != null) text.Append("; rollback-blocked=").Append(blockedReason);
            text.Append("; locked-inputs=").Append(InteractionLock.Held ? "held" : "free");
            return text.ToString();
        }
        private static string Event(AnytimeStoryEvent entry) =>
            entry == null ? "<null>" : entry.Kind + ":" + Short(entry.Key);
        private static string Short(string digest) =>
            string.IsNullOrEmpty(digest) ? "<empty>" : digest.Substring(0, Math.Min(8, digest.Length));
        // A mismatch ends the replay but must never throw into the game: these helpers run
        // inside Lua coroutines and Harmony patches on native code, where an exception unwinds
        // the caller and can skip its own cleanup. Every caller answers with a neutral value.
        private static void AbortReplay(string reason)
        {
            Fail(reason);
        }
        private static string SnapshotMismatch(string expected, string actual)
        {
            string detail;
            try { detail = AnytimeStoryDiagnostics.Difference(expected, actual); }
            catch (Exception error) { detail = "差异解析失败：" + error.Message; }
#if !NO_PROBE
            // Development only: these dumps are the complete player progress. The
            // release build must not write diagnostic artefacts (docs/IMPLEMENTATION.md).
            try
            {
                string folder = Path.Combine(BepInEx.Paths.BepInExRootPath, "MortalHelperDiagnostics");
                Directory.CreateDirectory(folder);
                string stem = Path.Combine(folder, "story-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(stem + ".expected.json", expected, new UTF8Encoding(false));
                File.WriteAllText(stem + ".actual.json", actual, new UTF8Encoding(false));
                Plugin.Instance?.Log("[Anytime] snapshot diagnostics=" + stem + "; " + detail);
            }
            catch (Exception error)
            {
                try { Plugin.Instance?.Log("[Anytime] snapshot diagnostics unavailable: " + error.Message + "; " + detail); }
                catch (Exception) { }
            }
#else
            try { Plugin.Instance?.Log("[Anytime] snapshot mismatch: " + detail); } catch (Exception) { }
#endif
            return detail;
        }
        private static string Snapshot()
        {
            var save = SaveSystem.Instance;
            if (save == null || CreateSave == null) throw new InvalidOperationException("存档服务未就绪。");
            var data = (GameSave)CreateSave.Invoke(save, null);
            if (data == null) throw new InvalidDataException("原生进度快照为空。");
            data.IsDemo = GameAccess.Field<bool>(save, "_isDemo");
            string json = JsonUtility.ToJson(data);
            if (string.IsNullOrEmpty(json) || json == "{}") throw new InvalidDataException("无法序列化原生进度。");
            return json;
        }
        private static bool SameSnapshot(GameSave data, string json) => SameSnapshot(JsonUtility.ToJson(data), json);
        private static bool SameSnapshot(string left, string right)
        {
            var a = JsonUtility.FromJson<GameSave>(left); var b = JsonUtility.FromJson<GameSave>(right);
            if (a == null || b == null) return false;
            // Display preferences belong to the save, but are not gameplay state.
            Titles.StripMetadata(a);
            Titles.StripMetadata(b);
            a.TimeTick = b.TimeTick = 0;
            return JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        }
        private static AnytimeLuaValue[] CaptureGlobals(Script interpreter)
        {
            if (interpreter == null) throw new InvalidDataException("Lua 解释器不可用。");
            Dictionary<string, LuaApiIdentity> apis;
            if (!LuaRootApis.TryGetValue(interpreter, out apis)) throw new InvalidDataException("Lua API 身份尚未记录。");
            foreach (var api in apis)
                if (!SameLuaApi(api.Value, interpreter.Globals.Get(api.Key)))
                    throw new InvalidDataException("Lua 原始 API 已被移除或重绑定：" + api.Key);
            var values = new List<AnytimeLuaValue>();
            var tables = new HashSet<Table>();
            int capturedEntries = 0;
            foreach (TablePair pair in interpreter.Globals.Pairs)
            {
                // MoonSharp owns this registry table; it is not story state.
                if (pair.Key.Type != DataType.String || pair.Key.String == "_MOONSHARP") continue;
                DynValue value = pair.Value;
                if (value.Type == DataType.Table)
                {
                    if (!IsLuaLibrary(interpreter, pair.Key.String, value.Table))
                    {
                        if (!tables.Add(value.Table)) throw new InvalidDataException("Lua 表存在共享引用，暂不能安全保存：" + pair.Key.String);
                        values.Add(CaptureTable(pair.Key.String, value.Table, tables, ref capturedEntries));
                    }
                    continue;
                }
                if (value.Type == DataType.String) values.Add(new AnytimeLuaValue { Key = pair.Key.String, Kind = "s", Text = value.String });
                else if (value.Type == DataType.Number) values.Add(new AnytimeLuaValue { Key = pair.Key.String, Kind = "n", Number = value.Number });
                else if (value.Type == DataType.Boolean) values.Add(new AnytimeLuaValue { Key = pair.Key.String, Kind = "b", Boolean = value.Boolean });
                else if (value.Type != DataType.Nil && value.Type != DataType.Void)
                {
                    LuaApiIdentity original;
                    if (!apis.TryGetValue(pair.Key.String, out original) || !SameLuaApi(original, value))
                        throw new InvalidDataException("剧情含不支持的 Lua 全局对象：" + pair.Key.String + ":" + value.Type);
                }
            }
            values.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            return values.ToArray();
        }
#if !NO_PROBE
        internal static string ReadOnlySelfTest()
        {
            var probe = new Script(CoreModules.Preset_Complete);
            RememberLuaLibraries(probe);
            RememberLuaRootApis(probe);
            probe.DoString("number = 42; text = '中文'; flag = true; sayoptions = {'甲', '乙'}; sayoptions.enabled = true; sayoptions.nested = {inner={n=7}}");
            var values = CaptureGlobals(probe);
            bool globalsOk = !values.Any(v => v.Key == "_MOONSHARP" || v.Key == "dynamic" || v.Key == "json") &&
                values.Any(v => v.Key == "number") && values.Any(v => v.Key == "text") &&
                values.Any(v => v.Key == "flag") && values.Any(v => v.Key == "sayoptions" && v.Entries.Length == 4);
            probe.DoString("number = 1; sayoptions[1] = '改'; sayoptions.extra = '删'; sayoptions.nested.inner.n=1; sayoptions.nested.extra='删'");
            RestoreGlobals(probe, values);
            bool restoreOk = SameGlobals(values, CaptureGlobals(probe)) &&
                probe.DoString("return sayoptions[1] == '甲' and sayoptions.extra == nil and number == 42 and sayoptions.nested.inner.n==7 and sayoptions.nested.extra==nil").Boolean;
            bool complexRejected = false;
            probe.DoString("sayoptions.child = function() return 1 end");
            try { CaptureGlobals(probe); }
            catch (InvalidDataException) { complexRejected = true; }
            probe.DoString("sayoptions.child = nil; alias = sayoptions");
            bool aliasRejected = false;
            try { CaptureGlobals(probe); }
            catch (InvalidDataException) { aliasRejected = true; }
            var record = new AnytimeStoryRecord { BaselineGlobals = values, ExpectedGlobals = values,
                Events = new[] { new AnytimeStoryEvent { Kind = "Random", Number = Math.PI } } };
            var reread = AnytimeStoryCodec.Read(AnytimeStoryCodec.Write(record));
            bool codecOk = SameGlobals(values, reread.BaselineGlobals) &&
                SameGlobals(values, reread.ExpectedGlobals) && reread.Events[0].Number == Math.PI;
            bool ok = globalsOk && restoreOk && complexRejected && aliasRejected && codecOk;
            return "globals=" + (ok ? "ok" : "failed") + "; table-restore=" + restoreOk +
                "; unsupported-rejected=" + complexRejected + "; alias-rejected=" + aliasRejected +
                "; sidecar-roundtrip=" + codecOk + "; baseline=" +
                (string.IsNullOrEmpty(baseline) ? "none" : "captured") + "; availability=" +
                (AnytimeNative.Availability ?? "available");
        }
#endif
        private static void RestoreBaselineFlags(GameSave opening)
        {
            // Loading a Unity scene can deserialize asset-backed flags after the
            // native save has applied them. Restore the recorded script-entry
            // state here, before Lua runs, just as we restore its globals and RNG.
            // This uses the baseline, never the desired replay target.
            var manager = MissionManagerData.Instance;
            if (manager == null || opening == null || opening.EventFlags == null || opening.EventFlags.Count > 10000)
                throw new InvalidDataException("剧情起点事件旗标缺失或超限。");
            // Native Save emits every collection entry in order. Collections may
            // share the same FlagData, and distinct assets may also share names.
            // Match the exact native sequence rather than assuming names unique.
            var live = new List<FlagData>();
            if (manager.FlagsCollection == null) throw new InvalidDataException("当前游戏旗标集合缺失。");
            foreach (var collection in manager.FlagsCollection)
            {
                if (collection == null || collection.List == null) throw new InvalidDataException("当前游戏含空旗标集合。");
                live.AddRange(collection.List);
            }
            if (live.Count != opening.EventFlags.Count)
                throw new InvalidDataException("剧情起点旗标数量不同：记录=" + opening.EventFlags.Count + "，当前=" + live.Count);
            var resolved = new Dictionary<FlagData, int>();
            for (int i = 0; i < live.Count; i++)
            {
                var value = opening.EventFlags[i]; var flag = live[i];
                if (value == null || string.IsNullOrEmpty(value.Name) || flag == null || flag.name != value.Name)
                    throw new InvalidDataException("剧情起点旗标不匹配：index=" + i + "，记录=" + (value?.Name ?? "<null>") +
                        "，当前=" + (flag == null ? "<null>" : flag.name));
                int previous;
                if (resolved.TryGetValue(flag, out previous) && previous != value.State)
                    throw new InvalidDataException("剧情起点同一旗标实例含冲突值：" + value.Name + "，index=" + i);
                resolved[flag] = value.State;
            }
            // Validate the complete asset mapping before applying any values.
            foreach (var value in resolved)
            {
                value.Key.State = value.Value;
#if !NO_PROBE
                AnytimeStoryDiagnostics.TraceFlag("BaselineFlagRestore", value.Key, value.Value);
#endif
            }
        }
        private static void RestoreGlobals(Script interpreter, AnytimeLuaValue[] values)
        {
            foreach (var item in values)
            {
                if (item == null || string.IsNullOrEmpty(item.Key)) throw new InvalidDataException("Lua 变量名称无效。");
                if (item.Kind == "s") interpreter.Globals.Set(item.Key, DynValue.NewString(item.Text));
                else if (item.Kind == "n") interpreter.Globals.Set(item.Key, DynValue.NewNumber(item.Number));
                else if (item.Kind == "b") interpreter.Globals.Set(item.Key, DynValue.NewBoolean(item.Boolean));
                else if (item.Kind == "t") RestoreTable(interpreter, item);
                else throw new InvalidDataException("Lua 变量类型无效。");
            }
        }
        private static bool SameGlobals(AnytimeLuaValue[] a, AnytimeLuaValue[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Key != b[i].Key || a[i].Kind != b[i].Kind || a[i].Text != b[i].Text ||
                    a[i].Number != b[i].Number || a[i].Boolean != b[i].Boolean ||
                    !SameEntries(a[i].Entries, b[i].Entries)) return false;
            return true;
        }
        private static AnytimeLuaValue CaptureTable(string name, Table table, HashSet<Table> visited, ref int budget)
        {
            return new AnytimeLuaValue { Key = name, Kind = "t", Entries = CaptureEntries(name, table, visited, 0, ref budget) };
        }
        private static AnytimeLuaEntry[] CaptureEntries(string name, Table table, HashSet<Table> visited, int depth, ref int budget)
        {
            if (depth > 32 || visited.Count > 10000) throw new InvalidDataException("Lua 表嵌套或数量超限：" + name);
            if (table.MetaTable != null) throw new InvalidDataException("Lua 表含元表，无法安全保存：" + name);
            var entries = new List<AnytimeLuaEntry>();
            foreach (var pair in table.Pairs)
            {
                // MoonSharp retains deleted slots in Pairs as Nil tombstones;
                // Lua observes them as absent keys, so they carry no state.
                if (pair.Value.Type == DataType.Nil || pair.Value.Type == DataType.Void) continue;
                if (++budget > 100000 || entries.Count >= 10000) throw new InvalidDataException("Lua 表过大：" + name);
                var entry = new AnytimeLuaEntry();
                if (pair.Key.Type == DataType.String) { entry.KeyKind = "s"; entry.Key = pair.Key.String; }
                else if (pair.Key.Type == DataType.Number) { entry.KeyKind = "n"; entry.Key = pair.Key.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
                else throw new InvalidDataException("Lua 表键类型不支持：" + name + ":" + pair.Key.Type);
                var value = pair.Value;
                if (value.Type == DataType.String) { entry.Kind = "s"; entry.Text = value.String; }
                else if (value.Type == DataType.Number) { entry.Kind = "n"; entry.Number = value.Number; }
                else if (value.Type == DataType.Boolean) { entry.Kind = "b"; entry.Boolean = value.Boolean; }
                else if (value.Type == DataType.Table)
                {
                    if (!visited.Add(value.Table)) throw new InvalidDataException("Lua 表存在循环或共享引用：" + name + "." + entry.Key);
                    entry.Kind = "t"; entry.Entries = CaptureEntries(name + "." + entry.Key, value.Table, visited, depth + 1, ref budget);
                }
                else throw new InvalidDataException("Lua 表值类型不支持：" + name + "." + entry.Key + ":" + value.Type);
                entries.Add(entry);
            }
            entries.Sort((a, b) => { int kind = string.CompareOrdinal(a.KeyKind, b.KeyKind); return kind != 0 ? kind : string.CompareOrdinal(a.Key, b.Key); });
            return entries.ToArray();
        }
        private static void RestoreTable(Script interpreter, AnytimeLuaValue value)
        {
            // Preserve the table object so any API closure holding it sees the
            // restored entries too; do not silently discard unsupported values.
            var existing = interpreter.Globals.Get(value.Key);
            var table = existing.Type == DataType.Table ? existing.Table : new Table(interpreter);
            RestoreEntries(interpreter, table, value.Entries, 0);
            interpreter.Globals.Set(value.Key, DynValue.NewTable(table));
        }
        private static void RestoreEntries(Script interpreter, Table table, AnytimeLuaEntry[] entries, int depth)
        {
            if (entries == null || depth > 32) throw new InvalidDataException("Lua 表内容缺失或嵌套超限。");
            var previous = new Table(interpreter);
            foreach (var pair in table.Pairs.ToArray()) { previous.Set(pair.Key, pair.Value); table.Set(pair.Key, DynValue.Nil); }
            foreach (var entry in entries)
            {
                DynValue key;
                if (entry.KeyKind == "s") key = DynValue.NewString(entry.Key);
                else if (entry.KeyKind == "n") key = DynValue.NewNumber(double.Parse(entry.Key, System.Globalization.CultureInfo.InvariantCulture));
                else throw new InvalidDataException("Lua 表键类型无效。");
                DynValue item;
                if (entry.Kind == "s") item = DynValue.NewString(entry.Text);
                else if (entry.Kind == "n") item = DynValue.NewNumber(entry.Number);
                else if (entry.Kind == "b") item = DynValue.NewBoolean(entry.Boolean);
                else if (entry.Kind == "t")
                {
                    var old = previous.Get(key);
                    var child = old.Type == DataType.Table ? old.Table : new Table(interpreter);
                    RestoreEntries(interpreter, child, entry.Entries, depth + 1);
                    item = DynValue.NewTable(child);
                }
                else throw new InvalidDataException("Lua 表值类型无效。");
                table.Set(key, item);
            }
        }
        private static bool SameEntries(AnytimeLuaEntry[] a, AnytimeLuaEntry[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].KeyKind != b[i].KeyKind || a[i].Key != b[i].Key || a[i].Kind != b[i].Kind ||
                    a[i].Text != b[i].Text || a[i].Number != b[i].Number || a[i].Boolean != b[i].Boolean ||
                    !SameEntries(a[i].Entries, b[i].Entries)) return false;
            return true;
        }
        private static string Digest(string value)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
        }
        private static string HashFile(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(stream));
        }
        private static string HashObject(object value)
        {
            using (var stream = new MemoryStream()) using (var sha = SHA256.Create())
            { new BinaryFormatter().Serialize(stream, value); stream.Position = 0; return Convert.ToBase64String(sha.ComputeHash(stream)); }
        }
        private static readonly MethodInfo GetLuaRandom = AccessTools.Method(typeof(MoonSharp.Interpreter.CoreLib.MathModule), "GetRandom");
        private static readonly MethodInfo SetLuaRandom = AccessTools.Method(typeof(MoonSharp.Interpreter.CoreLib.MathModule), "SetRandom");
        private static readonly FieldInfo LuaRandomNext = AccessTools.Field(typeof(System.Random), "inext");
        private static readonly FieldInfo LuaRandomNextP = AccessTools.Field(typeof(System.Random), "inextp");
        private static readonly FieldInfo LuaRandomSeeds = AccessTools.Field(typeof(System.Random), "SeedArray");
        private static string CaptureLuaRandom(Script interpreter)
        {
            if (GetLuaRandom == null || LuaRandomNext == null || LuaRandomNextP == null || LuaRandomSeeds == null || interpreter == null)
                throw new InvalidDataException("无法读取 Lua 随机源。");
            var random = GetLuaRandom.Invoke(null, new object[] { interpreter }) as System.Random;
            if (random == null || random.GetType() != typeof(System.Random))
                throw new InvalidDataException("Lua 随机源尚未初始化。");
            var state = new AnytimeLuaRandomState {
                Next = (int)LuaRandomNext.GetValue(random), NextP = (int)LuaRandomNextP.GetValue(random),
                Seeds = (int[])((int[])LuaRandomSeeds.GetValue(random)).Clone()
            };
            return JsonUtility.ToJson(state);
        }
        private static void RestoreLuaRandom(Script interpreter, string encoded)
        {
            if (SetLuaRandom == null || LuaRandomNext == null || LuaRandomNextP == null || LuaRandomSeeds == null ||
                interpreter == null || string.IsNullOrEmpty(encoded) || encoded.Length > 4096)
                throw new InvalidDataException("无法恢复 Lua 随机源。");
            var state = JsonUtility.FromJson<AnytimeLuaRandomState>(encoded);
            if (state == null || state.Next < 0 || state.Next > 55 || state.NextP < 0 || state.NextP > 55 ||
                state.Seeds == null || state.Seeds.Length != 56 || state.Seeds.Any(seed => seed < 0 || seed >= int.MaxValue))
                throw new InvalidDataException("Lua 随机源记录无效。");
            var random = new System.Random(0);
            LuaRandomNext.SetValue(random, state.Next);
            LuaRandomNextP.SetValue(random, state.NextP);
            LuaRandomSeeds.SetValue(random, state.Seeds);
            SetLuaRandom.Invoke(null, new object[] { interpreter, random });
        }
        private static string PathFor(string slot) => (string)GetPath.Invoke(SaveSystem.Instance, new object[] { slot });
        // Cheap probe used only to decide whether a sidecar belongs to the native
        // file on disk. A malformed sidecar returns null so ReadRecord still
        // produces the precise structural error for the player.
        private static string ReadSidecarHeader(string nativePath)
        {
            try
            {
                var info = new FileInfo(nativePath + ".mha");
                if (!info.Exists || info.Length <= 0 || info.Length > MaximumSidecarBytes) return null;
                var record = AnytimeStoryCodec.Read(File.ReadAllText(nativePath + ".mha", Encoding.UTF8));
                return record == null ? null : record.NativeHash;
            }
            catch (Exception) { return null; }
        }
        // Never delete a sidecar the player might still need. Renaming keeps it
        // out of the load path while leaving it recoverable by hand; the shared
        // implementation lives in AnytimeNative so the two paths cannot drift apart.
        private static void Quarantine(string path) => AnytimeNative.Quarantine(path);
        private static void Report(string message) => AnytimeNative.Report(message);
        private static AnytimeStoryRecord ReadRecord(string nativePath, GameSave native)
        {
            string side = nativePath + ".mha";
            // Stat first: a swapped or corrupted sidecar must be refused before it is
            // pulled into memory. The codec's own limits are only reached after the read.
            var probe = new FileInfo(side);
            if (!probe.Exists || probe.Length <= 0 || probe.Length > MaximumSidecarBytes)
                throw new InvalidDataException("剧情附加记录大小无效。");
            var bytes = File.ReadAllBytes(side);
            if (bytes.Length == 0 || bytes.Length > MaximumSidecarBytes) throw new InvalidDataException("剧情附加记录大小无效。");
            var record = AnytimeStoryCodec.Read(Encoding.UTF8.GetString(bytes));
            if (record == null || record.Version != 3 || record.Client != Application.version ||
                record.NativeHash != HashFile(nativePath) || native == null ||
                !SameSnapshot(native, record.Expected) || native.CurrentScene != record.Scene)
                throw new InvalidDataException("剧情附加记录与原生存档或当前游戏版本不匹配。");
            return record;
        }
        // Restores the pre-save slot from the journal. It never infers "the slot
        // was empty" from a missing backup: that inference is what used to delete
        // a real save. A journal that cannot be restored is left in place, so the
        // next save or load on the slot keeps refusing instead of guessing.
        private static bool RollBack()
        {
            var transaction = pendingTransaction;
            pendingTransaction = null;
            if (transaction == null) { ClearPending(); return true; }
            SlotRollbackReport report;
            try { report = transaction.Rollback(); }
            catch (Exception error)
            {
                Plugin.Instance?.Report(error);
                report = new SlotRollbackReport { Complete = false, Detail = error.Message };
            }
            if (!report.Complete) blockedReason = report.Detail;
            ClearPending();
            return report.Complete;
        }
        private static void ClearPending()
        {
            pendingTransaction = null;
            pendingSlot = pendingPath = null; pendingRecord = null;
        }
        private static void LockInput() { InteractionLock.Set(InteractionHold.Story, true); }
        private static void UnlockInput() { InteractionLock.Set(InteractionHold.Story, false); }
    }

    public static class AnytimeStoryLua
    {
        public static bool before_say(string text) => AnytimeStory.BeforeSay(text);
        public static void say_done(string text) => AnytimeStory.SayDone(text);
        public static int before_choose(string key, int count) => AnytimeStory.BeforeChoose(key, count);
        public static void choose_done(string key, int choice) => AnytimeStory.ChooseDone(key, choice);
        public static double random_draw(double value) => AnytimeStory.RandomDraw(value);
        public static bool skip_wait() => AnytimeStory.SkipWait();
        public static int enter_wait(string kind, string key) => AnytimeStory.EnterWait(kind, key);
        public static void leave_wait(int token) => AnytimeStory.LeaveWait(token);
    }

    [HarmonyPatch(typeof(LuaEnvironment), "InitEnvironment")]
    internal static class AnytimeStoryEnvironmentPatch
    {
        private static readonly string Preamble = @"
if not __mortal_anytime_wrapped and say and choose and wait and math and math.random then
  __mortal_anytime_wrapped = true
  local raw_say, raw_choose, raw_wait, raw_random = say, choose, wait, math.random
  local raw_timer = choosetimer
  local function options_key(options)
    local pieces = {tostring(#options)}
    for i = 1, #options do
      local text = tostring(options[i]); pieces[#pieces + 1] = tostring(#text) .. ':' .. text
    end
    return table.concat(pieces, '|')
  end
  say = function(text, voice)
    if __mortal_anytime.before_say(text) then return end
    local token = __mortal_anytime.enter_wait('Say', text)
    raw_say(text, voice)
    __mortal_anytime.leave_wait(token)
    __mortal_anytime.say_done(text)
  end
  choose = function(options)
    local key = options_key(options)
    local selected = __mortal_anytime.before_choose(key, #options)
    if selected > 0 then return selected end
    local token = __mortal_anytime.enter_wait('Choose', key)
    local answer = raw_choose(options)
    __mortal_anytime.leave_wait(token)
    __mortal_anytime.choose_done(key, answer)
    return answer
  end
  if raw_timer then
    choosetimer = function(options, duration, default)
      local key = options_key(options) .. ':' .. tostring(duration) .. ':' .. tostring(default)
      local selected = __mortal_anytime.before_choose(key, #options)
      if selected > 0 then return selected end
      local token = __mortal_anytime.enter_wait('Choose', key)
      local answer = raw_timer(options, duration, default)
      __mortal_anytime.leave_wait(token)
      __mortal_anytime.choose_done(key, answer)
      return answer
    end
  end
  wait = function(duration)
    if __mortal_anytime.skip_wait() then return end
    return raw_wait(duration)
  end
  math.random = function(a, b)
    local result
    if a == nil then result = raw_random()
    elseif b == nil then result = raw_random(a)
    else result = raw_random(a, b) end
    return __mortal_anytime.random_draw(result)
  end
end";
        internal static void EnsureInstalled(Script interpreter)
        {
            if (interpreter == null) return;
            DynValue flag = interpreter.Globals.Get("__mortal_anytime_wrapped");
            if (flag.Type == DataType.Boolean && flag.Boolean)
            {
                AnytimeStory.RememberLuaLibraries(interpreter);
                AnytimeStory.RememberLuaRootApis(interpreter);
                return;
            }
            if (!registered) { UserData.RegisterType(typeof(AnytimeStoryLua)); registered = true; }
            interpreter.Globals.Set("__mortal_anytime", UserData.CreateStatic(typeof(AnytimeStoryLua)));
            try { interpreter.DoString(Preamble); }
            catch (Exception error) { LogWrapper(interpreter, error.Message); throw; }
            AnytimeStory.RememberLuaLibraries(interpreter);
            flag = interpreter.Globals.Get("__mortal_anytime_wrapped");
            if (flag.Type == DataType.Boolean && flag.Boolean) AnytimeStory.RememberLuaRootApis(interpreter);
            LogWrapper(interpreter, null);
        }
        private static void LogWrapper(Script interpreter, string error)
        {
            if (Plugin.Instance == null) return;
            try
            {
                var functions = new List<string>();
                foreach (string name in new[] { "say", "choose", "choosetimer", "wait" })
                    if (interpreter.Globals.Get(name).Type == DataType.Function) functions.Add(name);
                DynValue flag = interpreter.Globals.Get("__mortal_anytime_wrapped");
                Plugin.Instance.Log("[Anytime] lua wrapped=" + (flag.Type == DataType.Boolean && flag.Boolean) +
                    " functions=" + string.Join(",", functions.ToArray()) +
                    (error == null ? "" : " error=" + error));
            }
            catch (Exception) { }
        }
        private static void Postfix(LuaEnvironment __instance)
        {
            try { EnsureInstalled(__instance.Interpreter); }
            catch (Exception error) { AnytimeStory.EnvironmentFailed(error); }
        }
        private static bool registered;
    }

    [HarmonyPatch(typeof(LuaManager), "ExecuteLuaScript")]
    internal static class AnytimeStoryScriptPatch
    {
        private static bool Prefix(LuaEnvironment ____luaEnvironment)
        {
            AnytimeStory.ScriptEntering(____luaEnvironment?.Interpreter);
            // A failed restore must not run script side effects or autosaves while the
            // failure still owns the game; once the exit released input, the script has to
            // run again or the player is left in a scene that can never advance.
            return !AnytimeStory.Frozen;
        }
    }

    [HarmonyPatch(typeof(CheckPointManager), "Dice")]
    internal static class AnytimeStoryDicePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ref int __1) { AnytimeStory.BeforeDice(ref __1); }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(DiceCheckResult __result) { AnytimeStory.AfterDice(__result); }
    }
}
