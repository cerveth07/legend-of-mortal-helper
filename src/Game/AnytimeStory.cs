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
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    [Serializable]
    internal sealed class AnytimeStoryEvent
    {
        public string Kind;
        public string Key;
        public int Choice;
        public double Number;
    }

    [Serializable]
    internal sealed class AnytimeLuaValue
    {
        public string Key;
        public string Kind;
        public string Text;
        public double Number;
        public bool Boolean;
    }

    [Serializable]
    internal sealed class AnytimeLuaRandomState
    {
        public int Next, NextP;
        public int[] Seeds;
    }

    [Serializable]
    internal sealed class AnytimeStoryRecord
    {
        public int Version = 2;
        public string Client;
        public string NativeHash;
        public string Baseline;
        public string Expected;
        public string Script;
        public string Scene;
        public string WaitKind;
        public string WaitKey;
        public string BaselineRandomState;
        public string RandomState;
        public string BaselineLuaRandomState;
        public string LuaRandomState;
        public AnytimeLuaValue[] BaselineGlobals;
        public AnytimeLuaValue[] ExpectedGlobals;
        public AnytimeStoryEvent[] Events;
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
        private static readonly HashSet<string> StandardLuaTables = new HashSet<string>(StringComparer.Ordinal) {
            "_G", "math", "string", "table", "coroutine", "os", "io", "debug", "package", "bit32", "utf8"
        };
        private static readonly List<int> WaitTokens = new List<int>();
        private static readonly List<string> WaitKinds = new List<string>();
        private static readonly List<string> WaitKeys = new List<string>();
        private static Script lua;
        private static string script, scene, baseline, baselineRandomState, baselineLuaRandomState, invalid;
        private static AnytimeLuaValue[] baselineGlobals;
        private static string pendingSlot, pendingPath, pendingNativeBackup, pendingSideBackup;
        private static string completedPath, completedHash;
        private static AnytimeStoryRecord pendingRecord, playback;
        private static int cursor, tokenSerial;
        private static int? replayDice;
        private static bool sceneReady, targetReached;
        private static bool returningTitle;
        private static float targetAt, replayStarted;
        private static readonly List<BaseInputModule> LockedInputs = new List<BaseInputModule>();

        internal static bool Replaying => playback != null && !Failed && !Verified;
        internal static bool Verified { get; private set; }
        internal static bool Failed { get; private set; }
        internal static string Failure { get; private set; }
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
                    !string.Equals(script, PlayerStatManagerData.Instance?.CurrentStoryScript, StringComparison.Ordinal))
                    return "当前剧情尚未建立可重放的脚本起点。";
                if (WaitTokens.Count != 1 || (WaitKinds[0] != "Say" && WaitKinds[0] != "Choose"))
                    return "请在对话或选项稳定等待时存档。";
                if (DiceAdapter.Dialog != null && DiceAdapter.Dialog.isActiveAndEnabled)
                    return "轮盘判定进行中，请完成后再存档。";
                if (Events.Count >= MaximumEvents) return "本段剧情记录过长，请继续到下一个脚本后存档。";
                return null;
            }
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
                completedPath = completedHash = null;
                AbortNativeSave();
                string path = PathFor(slot);
                if (string.IsNullOrEmpty(path)) throw new IOException("无法定位原生存档槽位。");
                if (File.Exists(path + ".mha-pending") || File.Exists(path + ".mha-rollback") ||
                    File.Exists(path + ".mha.rollback"))
                    throw new InvalidDataException("此槽位有上次中断的存档记录，请先恢复备份。");
                var record = new AnytimeStoryRecord {
                    Client = Application.version, Baseline = baseline, Script = script, Scene = scene,
                    WaitKind = WaitKinds[0], WaitKey = WaitKeys[0],
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
                if (string.IsNullOrEmpty(record.Expected) || record.Expected.Length > MaximumSidecarBytes)
                    throw new InvalidDataException("当前进度超出附加记录容量。 ");
                string side = path + ".mha";
                pendingNativeBackup = path + ".mha-rollback";
                pendingSideBackup = side + ".rollback";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path)) File.Copy(path, pendingNativeBackup, true);
                if (File.Exists(side)) File.Copy(side, pendingSideBackup, true);
                pendingSlot = slot; pendingPath = path; pendingRecord = record;
                File.WriteAllText(path + ".mha-pending", "1", new UTF8Encoding(false));
                return true;
            }
            catch (Exception error)
            {
                AbortNativeSave();
                DeleteIfExists(pendingNativeBackup);
                DeleteIfExists(pendingSideBackup);
                AnytimeNative.SaveFailed("剧情存档准备失败：" + error.Message);
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
                string json = JsonUtility.ToJson(pendingRecord);
                if (Encoding.UTF8.GetByteCount(json) > MaximumSidecarBytes) throw new InvalidDataException("剧情附加记录过大。");
                using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(json);
                    file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (File.Exists(side)) File.Replace(temp, side, null); else File.Move(temp, side);
                // Read the committed pair before releasing its rollback copies.
                ReadRecord(saveFile, attempted);
                DeleteIfExists(saveFile + ".mha-pending");
                completedPath = saveFile; completedHash = nativeHash;
                ClearPending(true);
            }
            catch (Exception error)
            {
                completedPath = completedHash = null;
                bool restored = RollBack();
                AnytimeNative.SaveFailed(restored
                    ? "剧情存档未完成，槽位已回退：" + error.Message
                    : "剧情存档未完成，自动回退失败；请保留原生档和 .mha-rollback/.mha.rollback 备份：" + error.Message);
            }
        }

        internal static void AbortNativeSave() { if (pendingRecord != null) RollBack(); }

        internal static bool NativeSaveSucceeded(string saveFile, object gameSave)
        {
            if (completedPath != saveFile || completedHash == null || !(gameSave is GameSave)) return false;
            try { return completedHash == HashObject(gameSave) && completedHash == HashFile(saveFile); }
            catch (Exception) { return false; }
        }

        internal static bool ForgetAfterOrdinarySave(string saveFile, object gameSave)
        {
            if (pendingRecord != null || !(gameSave is GameSave)) return false;
            try
            {
                if (HashFile(saveFile) != HashObject(gameSave)) return false;
                DeleteIfExists(saveFile + ".mha");
                DeleteIfExists(saveFile + ".mha-pending");
                DeleteIfExists(saveFile + ".mha-rollback");
                DeleteIfExists(saveFile + ".mha.rollback");
                return true;
            }
            catch (Exception error) { Plugin.Instance?.Report(error); return false; }
        }

        internal static void BeforeNativeLoad()
        {
            playback = null; Failed = Verified = false; Failure = null;
            sceneReady = targetReached = returningTitle = false; replayDice = null; UnlockInput();
        }

        internal static void PrepareLoad(string slot, ref GameSave save)
        {
            if (save == null) return;
            string path = PathFor(slot);
            if (string.IsNullOrEmpty(path)) return;
            if (File.Exists(path + ".mha-pending") || File.Exists(path + ".mha-rollback") ||
                File.Exists(path + ".mha.rollback"))
                throw new InvalidDataException("此槽位的剧情存档中断；请从备份恢复后再读档。");
            if (!File.Exists(path + ".mha"))
                throw new InvalidDataException("此剧情编号档缺少配套的 .mha 剧情记录，不能安全恢复。请恢复完整备份。");
            var record = ReadRecord(path, save);
            var opening = JsonUtility.FromJson<GameSave>(record.Baseline);
            if (opening == null || opening.CurrentScene != record.Scene ||
                opening.CurrentStoryScript != record.Script || record.Events == null ||
                record.BaselineGlobals == null || record.ExpectedGlobals == null ||
                string.IsNullOrEmpty(record.BaselineRandomState) || string.IsNullOrEmpty(record.RandomState) ||
                string.IsNullOrEmpty(record.BaselineLuaRandomState) || string.IsNullOrEmpty(record.LuaRandomState) ||
                (record.WaitKind != "Say" && record.WaitKind != "Choose"))
                throw new InvalidDataException("剧情附加记录缺少可重放的起点或目标。");
            if (record.Events.Length > MaximumEvents) throw new InvalidDataException("剧情事件记录超限。");
            foreach (var entry in record.Events)
                if (entry == null || (entry.Kind != "Say" && entry.Kind != "Choose" && entry.Kind != "Random" && entry.Kind != "Dice"))
                    throw new InvalidDataException("剧情事件类型无效。");
            opening.TimeTick = save.TimeTick;
            playback = record; save = opening; cursor = 0;
            sceneReady = false; targetReached = false; targetAt = 0f;
            replayDice = null;
            replayStarted = Time.unscaledTime; Verified = Failed = false; Failure = null;
            Events.Clear(); WaitTokens.Clear(); WaitKinds.Clear(); WaitKeys.Clear();
            LockInput();
        }

        internal static void AfterNativeLoadScene() { if (Replaying) sceneReady = true; }

        internal static void Tick()
        {
            if (Failed)
            {
                LockInput();
                var controller = SceneController.Instance;
                if (!returningTitle && controller != null && !controller.IsLoading && !controller.IsPrepare)
                {
                    returningTitle = true;
                    controller.LoadTitle();
                }
                return;
            }
            if (!Replaying) return;
            LockInput();
            if (Time.unscaledTime - replayStarted > 120f) { Fail("剧情回放超过两分钟仍未到达存档位置。"); return; }
            if (!sceneReady || !targetReached || Time.unscaledTime - targetAt < .25f) return;
            try
            {
                if (cursor != playback.Events.Length || WaitTokens.Count != 1 ||
                    WaitKinds[0] != playback.WaitKind || WaitKeys[0] != playback.WaitKey)
                    throw new InvalidDataException("对话等待点与记录不同。");
                if (!SameSnapshot(Snapshot(), playback.Expected))
                    throw new InvalidDataException("剧情状态与存档内容不同。");
                if (!SameGlobals(CaptureGlobals(lua), playback.ExpectedGlobals))
                    throw new InvalidDataException("Lua 剧情变量与存档内容不同。");
                UnityEngine.Random.state = JsonUtility.FromJson<UnityEngine.Random.State>(playback.RandomState);
                RestoreLuaRandom(lua, playback.LuaRandomState);
                baseline = playback.Baseline; baselineGlobals = playback.BaselineGlobals;
                baselineRandomState = playback.BaselineRandomState;
                baselineLuaRandomState = playback.BaselineLuaRandomState;
                script = playback.Script; scene = playback.Scene;
                Events.Clear(); Events.AddRange(playback.Events);
                playback = null; Verified = true; UnlockInput();
                StoryReplay.OnNativeLoadCompleted();
                if (Plugin.Instance != null) Plugin.Instance.Status = "剧情已恢复到存档对话或选项，状态核对通过。";
            }
            catch (Exception error) { Fail(error.Message); }
        }

        internal static void Reset()
        {
            AbortNativeSave(); playback = null; Verified = Failed = false; Failure = null;
            completedPath = completedHash = null;
            lua = null; script = scene = baseline = baselineRandomState = baselineLuaRandomState = invalid = null; baselineGlobals = null;
            Events.Clear(); WaitTokens.Clear(); WaitKinds.Clear(); WaitKeys.Clear();
            replayDice = null; sceneReady = targetReached = false; UnlockInput();
            returningTitle = false;
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
                if (playback.Script != PlayerStatManagerData.Instance?.CurrentStoryScript)
                { Fail("剧情脚本与存档起点不一致。"); return; }
                try
                {
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
            }
            catch (Exception error) { baseline = null; invalid = "无法记录剧情起点：" + error.Message; }
        }

        internal static bool BeforeSay(string text)
        {
            if (Failed) throw new InvalidDataException(Failure);
            string key = Digest(text);
            if (!Replaying) return false;
            if (cursor == playback.Events.Length) { Reach("Say", key); return false; }
            var next = playback.Events[cursor];
            if (next.Kind != "Say" || next.Key != key) AbortReplay("剧情对话顺序与存档不同。");
            cursor++; return true;
        }
        internal static void SayDone(string text) { Record(new AnytimeStoryEvent { Kind = "Say", Key = Digest(text) }); }
        internal static int BeforeChoose(string key, int count)
        {
            if (Failed) throw new InvalidDataException(Failure);
            if (!Replaying) return 0;
            string digest = Digest(key);
            if (cursor == playback.Events.Length) { Reach("Choose", digest); return 0; }
            var next = playback.Events[cursor];
            if (next.Kind != "Choose" || next.Key != digest || next.Choice < 1 || next.Choice > count)
                AbortReplay("剧情选项与存档不同。");
            cursor++; return next.Choice;
        }
        internal static void ChooseDone(string key, int choice)
        { Record(new AnytimeStoryEvent { Kind = "Choose", Key = Digest(key), Choice = choice }); }
        internal static double RandomDraw(double drawn)
        {
            if (Failed) throw new InvalidDataException(Failure);
            if (Replaying)
            {
                if (cursor >= playback.Events.Length || playback.Events[cursor].Kind != "Random")
                    AbortReplay("剧情随机事件与存档不同。");
                return playback.Events[cursor++].Number;
            }
            Record(new AnytimeStoryEvent { Kind = "Random", Number = drawn });
            return drawn;
        }
        internal static void BeforeDice(ref int random)
        {
            if (Failed) throw new InvalidDataException(Failure);
            if (!Replaying) return;
            if (cursor >= playback.Events.Length || playback.Events[cursor].Kind != "Dice")
                AbortReplay("轮盘判定次数与存档不同。");
            replayDice = playback.Events[cursor++].Choice;
            random = replayDice.Value;
        }
        internal static void AfterDice(DiceCheckResult result)
        {
            if (Failed) throw new InvalidDataException(Failure);
            if (Replaying)
            {
                if (!replayDice.HasValue || result == null || result.Random != replayDice.Value)
                    AbortReplay("轮盘判定结果与存档不同。");
                replayDice = null;
            }
            else if (result != null)
                Record(new AnytimeStoryEvent { Kind = "Dice", Choice = result.Random });
        }
        internal static bool SkipWait()
        {
            if (Failed) throw new InvalidDataException(Failure);
            return Replaying && !targetReached;
        }
        internal static int EnterWait(string kind, string key)
        {
            if (Failed) throw new InvalidDataException(Failure);
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
                AbortReplay("剧情等待位置与存档不同。");
            targetReached = true; targetAt = Time.unscaledTime;
        }
        private static void Record(AnytimeStoryEvent item)
        {
            if (Replaying || baseline == null || invalid != null) return;
            if (Events.Count == MaximumEvents) { invalid = "剧情事件记录超限。"; return; }
            Events.Add(item);
        }
        private static void Fail(string reason)
        {
            Failed = true; Failure = reason; playback = null; LockInput();
            Plugin.Instance?.Gate.Fail("剧情存档恢复失败：" + reason);
            if (Plugin.Instance != null) Plugin.Instance.Status = "剧情恢复失败：" + reason + "；请重新读取原生存档。";
        }
        private static void AbortReplay(string reason)
        {
            Fail(reason);
            throw new InvalidDataException(reason);
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
            a.TimeTick = b.TimeTick = 0;
            return JsonUtility.ToJson(a) == JsonUtility.ToJson(b);
        }
        private static AnytimeLuaValue[] CaptureGlobals(Script interpreter)
        {
            if (interpreter == null) throw new InvalidDataException("Lua 解释器不可用。");
            var values = new List<AnytimeLuaValue>();
            foreach (TablePair pair in interpreter.Globals.Pairs)
            {
                // MoonSharp owns this registry table; it is not story state.
                if (pair.Key.Type != DataType.String || pair.Key.String == "__mortal_anytime" ||
                    pair.Key.String == "_MOONSHARP") continue;
                DynValue value = pair.Value;
                if (value.Type == DataType.Table && !StandardLuaTables.Contains(pair.Key.String))
                    throw new InvalidDataException("剧情包含暂不支持的 Lua 表变量：" + pair.Key.String);
                if (value.Type == DataType.String) values.Add(new AnytimeLuaValue { Key = pair.Key.String, Kind = "s", Text = value.String });
                else if (value.Type == DataType.Number) values.Add(new AnytimeLuaValue { Key = pair.Key.String, Kind = "n", Number = value.Number });
                else if (value.Type == DataType.Boolean) values.Add(new AnytimeLuaValue { Key = pair.Key.String, Kind = "b", Boolean = value.Boolean });
            }
            values.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
            return values.ToArray();
        }
        private static void RestoreGlobals(Script interpreter, AnytimeLuaValue[] values)
        {
            foreach (var item in values)
            {
                if (item == null || string.IsNullOrEmpty(item.Key)) throw new InvalidDataException("Lua 变量名称无效。");
                if (item.Kind == "s") interpreter.Globals.Set(item.Key, DynValue.NewString(item.Text));
                else if (item.Kind == "n") interpreter.Globals.Set(item.Key, DynValue.NewNumber(item.Number));
                else if (item.Kind == "b") interpreter.Globals.Set(item.Key, DynValue.NewBoolean(item.Boolean));
                else throw new InvalidDataException("Lua 变量类型无效。");
            }
        }
        private static bool SameGlobals(AnytimeLuaValue[] a, AnytimeLuaValue[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Key != b[i].Key || a[i].Kind != b[i].Kind || a[i].Text != b[i].Text ||
                    a[i].Number != b[i].Number || a[i].Boolean != b[i].Boolean) return false;
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
        private static AnytimeStoryRecord ReadRecord(string nativePath, GameSave native)
        {
            string side = nativePath + ".mha";
            var bytes = File.ReadAllBytes(side);
            if (bytes.Length == 0 || bytes.Length > MaximumSidecarBytes) throw new InvalidDataException("剧情附加记录大小无效。");
            var record = JsonUtility.FromJson<AnytimeStoryRecord>(Encoding.UTF8.GetString(bytes));
            if (record == null || record.Version != 2 || record.Client != Application.version ||
                record.NativeHash != HashFile(nativePath) || native == null ||
                !SameSnapshot(native, record.Expected) || native.CurrentScene != record.Scene)
                throw new InvalidDataException("剧情附加记录与原生存档或当前游戏版本不匹配。");
            return record;
        }
        private static bool RollBack()
        {
            bool restored = false;
            try
            {
                if (pendingPath != null)
                {
                    if (File.Exists(pendingNativeBackup)) File.Copy(pendingNativeBackup, pendingPath, true);
                    else DeleteIfExists(pendingPath);
                    string side = pendingPath + ".mha";
                    if (File.Exists(pendingSideBackup)) File.Copy(pendingSideBackup, side, true);
                    else DeleteIfExists(side);
                    DeleteIfExists(pendingPath + ".mha-pending");
                }
                restored = true;
            }
            catch (Exception error) { Plugin.Instance?.Report(error); }
            finally { ClearPending(restored); }
            return restored;
        }
        private static void ClearPending(bool clean)
        {
            if (clean)
            {
                try { DeleteIfExists(pendingNativeBackup); }
                catch (Exception error) { Plugin.Instance?.Report(error); }
                try { DeleteIfExists(pendingSideBackup); }
                catch (Exception error) { Plugin.Instance?.Report(error); }
            }
            pendingSlot = pendingPath = pendingNativeBackup = pendingSideBackup = null; pendingRecord = null;
        }
        private static void DeleteIfExists(string path) { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
        private static void LockInput()
        {
            var system = EventSystem.current;
            if (system == null) return;
            foreach (var input in system.GetComponents<BaseInputModule>())
                if (input.enabled) { if (!LockedInputs.Contains(input)) LockedInputs.Add(input); input.enabled = false; }
        }
        private static void UnlockInput()
        {
            foreach (var input in LockedInputs) if (input != null) input.enabled = true;
            LockedInputs.Clear();
        }
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
            if (flag.Type == DataType.Boolean && flag.Boolean) return;
            if (!registered) { UserData.RegisterType(typeof(AnytimeStoryLua)); registered = true; }
            interpreter.Globals.Set("__mortal_anytime", UserData.CreateStatic(typeof(AnytimeStoryLua)));
            interpreter.DoString(Preamble);
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
        private static void Prefix(LuaEnvironment ____luaEnvironment)
        { AnytimeStory.ScriptEntering(____luaEnvironment?.Interpreter); }
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
