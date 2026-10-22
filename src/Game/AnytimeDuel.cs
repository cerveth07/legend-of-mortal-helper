using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Mortal.Combat;
using Mortal.Core;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MortalHelper.Game
{
    // A numbered native slot contains the start of the fight. The matching
    // .mhduel document describes how to replay to a verified action point.
    // Neither file is usable as an in-combat save without the other one.
    internal static class AnytimeDuel
    {
        [Serializable] private sealed class Draw
        {
            public string Site;
            public int Kind, A, B, Integer;
            public float X, Y, Floating;
        }

        [Serializable] private sealed class Point
        {
            public int Round, DrawCount;
            public string Digest;
        }

        [Serializable] private sealed class Journal
        {
            public int Format = 1;
            public string Client, SceneKey, NativeHash, RandomState, FinalState;
            public List<int> Actions = new List<int>();
            public List<Draw> Draws = new List<Draw>();
            public List<Point> Points = new List<Point>();
        }

        private const int MaxFile = 32 * 1024 * 1024;
        private const int MaxActions = 10000;
        private const int MaxDraws = 1000000;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MHDL-01\n");
        private static readonly MethodInfo GetSlotFile = AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile");
        private static readonly MethodInfo CreateNative = AccessTools.Method(typeof(SaveSystem), "CreateSaveData");
        private static CombatManager manager;
        private static Journal journal;
        private static Journal pending;
        private static byte[] opening, stagedOpening;
        private static string stagedSceneKey;
        private static int pointIndex, drawIndex;
        private static float lastProgress;
        private static bool waiting, injecting, replaying, failed, sceneReady, finalVerified, returningTitle;
        private static string invalid;

        internal static bool Replaying => replaying || pending != null || finalVerified;
        internal static bool Failed => failed;
        internal static string BlockReason
        {
            get
            {
                if (failed) return "单挑恢复未通过核验，请重新读取存档。";
                if (Replaying) return "单挑存档正在恢复。";
                if (!AnytimeDuelRandomPatch.Covered) return "当前游戏版本的单挑随机调用尚未通过覆盖检查。";
                if (manager == null || journal == null) return "请进入一场新单挑。";
                if (invalid != null) return invalid;
                if (opening == null) return "尚未取得单挑开场原生存档。";
                if (!waiting) return "请在双方结算结束且行动按钮出现后保存。";
                if (SceneController.Instance == null || SceneController.Instance.IsLoading || SceneController.Instance.IsPrepare)
                    return "场景仍在切换。";
                if (HasAssist()) return "当前单挑辅助已开启，本场不支持回合存档。";
                return null;
            }
        }

        private static bool HasAssist()
        {
            var o = Plugin.Instance?.Options;
            return o != null && (o.DuelHealth.Value || o.DuelStamina.Value || o.DuelDodge.Value ||
                o.DuelDamage.Value || o.DuelZeroDamage.Value || o.DuelAlwaysHit.Value || o.DuelFirstStrike.Value);
        }

        internal static void CaptureStart(string saveFile, object saved)
        {
            if (Path.GetFileName(saveFile) != "Save_auto_battle.dat" || !(saved is AutoGameSave auto) ||
                auto.GameSave == null || auto.GameSave.CurrentScene != "Combat") return;
            try
            {
                if (!File.Exists(saveFile) || Hash(File.ReadAllBytes(saveFile)) != Hash(NativeObjectBytes(saved)))
                    throw new IOException("自动战前档尚未完成原生写入。");
                stagedOpening = NativeBytes(auto.GameSave);
                stagedSceneKey = auto.GameSave.CurrentSceneKey;
                if (manager != null && !replaying && pending == null && invalid == null &&
                    stagedSceneKey == journal?.SceneKey) opening = stagedOpening;
            }
            catch (Exception e) { Invalidate("无法记录开场：" + e.GetBaseException().Message); }
        }

        internal static bool SaveToSlot(string slot)
        {
            string reason = BlockReason;
            if (reason != null) { AnytimeNative.SaveFailed(reason); return false; }
            try
            {
                if (!Numbered(slot)) throw new InvalidDataException("请选择游戏系统菜单中的编号槽位。");
                if (journal.Points.Count != journal.Actions.Count + 1)
                    throw new InvalidDataException("行动与停点数量不一致。");
                string now = DuelAudit.Capture(manager);
                if (journal.FinalState != now)
                {
                    Invalidate("等待行动期间战斗状态发生变化。");
                    throw new InvalidDataException(invalid);
                }
                var baseSave = ReadNative(opening);
                baseSave.TimeTick = DateTime.Now.Ticks;
                byte[] native = NativeBytes(baseSave);
                journal.Client = Application.version;
                journal.NativeHash = Hash(native);
                journal.RandomState = JsonUtility.ToJson(UnityEngine.Random.state);
                Validate(journal);
                string path = SlotFile(slot);
                AnytimeNative.SettleSlot(slot, path);
                if (SlotTransaction.Exists(SlotTransaction.JournalFor(path)))
                    throw new IOException("此槽位有尚未处理的存档事务，请先读取该槽位让插件自动恢复。");
                if (!File.Exists(path)) AnytimeNative.QuarantineOrphans(path);
                if (File.Exists(path + ".mha-pending") || File.Exists(path + ".mha-rollback") ||
                    File.Exists(path + ".mha.rollback") || File.Exists(path + ".mhduel-pending") ||
                    (File.Exists(path + ".mha-required") && !File.Exists(path + ".mha")) ||
                    (File.Exists(path + ".mhduel-required") && !File.Exists(path + ".mhduel")))
                    throw new IOException("此槽位有尚未处理的存档恢复材料，请先核对该槽位。");
                WritePair(path, native, Encode(journal));
                string message = "单挑回合已写入编号槽 " + slot + "。";
                try
                {
                    SaveSystem.Instance.SetSlot(slot);
                    SaveSystem.Instance.SaveUniverseData();
                }
                catch (Exception e) { message = "编号槽 " + slot + " 已保存，但最近槽位未更新：" + e.GetBaseException().Message; }
                Report(message);
                return true;
            }
            catch (Exception e)
            {
                AnytimeNative.SaveFailed("单挑保存失败：" + e.GetBaseException().Message);
                return false;
            }
        }

        // Call before ExecuteLoadGameData replaces manager state, and only for
        // the manual numbered LoadGameData route. A Combat GameSave without a
        // matching document is refused rather than treated as a fresh fight.
        internal static bool PrepareLoad(string slot, ref GameSave gameSave)
        {
            Reset();
            if (gameSave == null || gameSave.CurrentScene != "Combat") return false;
            try
            {
                // Ordinary native Combat saves have no replay sidecar. A persistent
                // requirement marker distinguishes NEW replay-dependent saves.
                // Legacy unmarked companions remain readable when still present.
                if (!Numbered(slot)) return false;
                string path = SlotFile(slot);
                if (File.Exists(path + ".mhduel-pending"))
                    throw new InvalidDataException("单挑存档提交曾中断；请核对 .dat/.mhduel 与备份后再读取。");
                if (!File.Exists(path + ".mhduel"))
                {
                    if (File.Exists(path + ".mhduel-required"))
                        throw new InvalidDataException("单挑中途档缺少必需的 .mhduel 附档；请恢复完整备份。");
                    return false;
                }
                var loaded = Decode(path + ".mhduel");
                Validate(loaded);
                if (loaded.NativeHash != Hash(File.ReadAllBytes(path)))
                {
                    // The document does not describe this file, so an ordinary
                    // native save has taken the slot over. Keep the document and
                    // let the player load the complete native save they have.
                    if (File.Exists(path + ".mhduel-required"))
                        throw new InvalidDataException("此槽位的单挑附档与原生档不匹配（附档必需标记仍在），" +
                            "不能确认哪一个才是完整存档。请从完整备份恢复后再读取。");
                    Quarantine(path + ".mhduel");
                    Report("该槽位的单挑附档属于更早的存档，已保留为 .mhduel.stale 并按原生存档载入。");
                    return false;
                }
                if (!AnytimeDuelRandomPatch.Covered || loaded.Client != Application.version ||
                    loaded.SceneKey != gameSave.CurrentSceneKey)
                    throw new InvalidDataException("客户端或战斗关卡与单挑记录不匹配。");
                pending = loaded;
                lastProgress = Time.unscaledTime;
                return true;
            }
            catch (Exception e)
            {
                failed = true;
                Plugin.Instance?.Gate.Fail("单挑存档核验失败：" + e.GetBaseException().Message);
                throw new InvalidDataException("拒绝读取不完整的单挑档：" + e.GetBaseException().Message, e);
            }
        }

        internal static void AfterNativeLoadScene()
        {
            sceneReady = true;
            if (finalVerified) CompleteReplay();
        }

        internal static void Tick()
        {
            if (failed)
            {
                if (manager == null) { UnlockInput(); return; }
                LockInput();
                var scene = SceneController.Instance;
                if (!returningTitle && manager != null && scene != null && !scene.IsLoading && !scene.IsPrepare)
                {
                    returningTitle = true;
                    scene.LoadTitle();
                }
                return;
            }
            if (manager != null && !Replaying && HasAssist()) Invalidate("本场曾启用单挑辅助。请在新单挑中重新记录。");
            if (!Replaying) { UnlockInput(); return; }
            LockInput();
            // A stall, not total elapsed time: a long replay that keeps reaching
            // action points is still making progress.
            if (Time.unscaledTime - lastProgress > 120f)
            { Fail("单挑回放超过两分钟没有出现下一个行动点（停点 " + pointIndex + "/" +
                (journal == null ? 0 : journal.Points.Count) + "）。"); return; }
            if (manager != null && Time.timeScale != 1f) Time.timeScale = 1f;
        }

        internal static void Reset()
        {
            manager = null; journal = null; pending = null; opening = null;
            stagedOpening = null; stagedSceneKey = null;
            pointIndex = drawIndex = 0; waiting = injecting = replaying = failed = sceneReady = finalVerified = returningTitle = false;
            lastProgress = 0f;
            invalid = null;
            UnlockInput();
        }

        private static void Begin(CombatManager current)
        {
            manager = current;
            opening = stagedSceneKey == PlayerStatManagerData.Instance.CurrentSceneKey ? stagedOpening : null;
            stagedOpening = null; stagedSceneKey = null;
            waiting = false;
            pointIndex = drawIndex = 0;
            if (pending == null)
            {
                failed = false;
                replaying = false;
                journal = new Journal { SceneKey = PlayerStatManagerData.Instance.CurrentSceneKey };
                if (HasAssist()) Invalidate("本场开始时已开启单挑辅助。");
                return;
            }
            journal = pending;
            pending = null;
            replaying = true;
            if (journal.SceneKey != PlayerStatManagerData.Instance.CurrentSceneKey)
                Fail("战斗关卡与单挑存档不同。");
        }

        private static IEnumerator ObserveActionPoint(IEnumerator source)
        {
            try
            {
                while (true)
                {
                    bool advanced;
                    object current = null;
                    try { advanced = source.MoveNext(); if (advanced) current = source.Current; }
                    catch (Exception e)
                    {
                        if (replaying) Fail("行动点协程异常：" + e.GetBaseException().Message);
                        else Invalidate("行动点协程异常：" + e.GetBaseException().Message);
                        throw;
                    }
                    if (!advanced) break;
                    yield return current;
                }
                try { ArriveAtActionPoint(); }
                catch (Exception e)
                {
                    if (replaying) Fail(e.GetBaseException().Message);
                    else Invalidate("无法记录单挑状态：" + e.GetBaseException().Message);
                }
            }
            finally { (source as IDisposable)?.Dispose(); }
        }

        private static void ArriveAtActionPoint()
        {
                if (manager == null || failed || invalid != null) return;
                lastProgress = Time.unscaledTime;
                string state = DuelAudit.Capture(manager);
                string digest = Hash(Encoding.UTF8.GetBytes(state));
                int round = manager.CurrentRounds;
                if (!replaying)
                {
                    if (journal.Points.Count != journal.Actions.Count)
                        throw new InvalidDataException("单挑行动日志失去同步。");
                    journal.FinalState = state;
                    journal.Points.Add(new Point { Round = round, DrawCount = journal.Draws.Count, Digest = digest });
                    waiting = true;
                    if (journal.Points.Count > MaxActions + 1) Invalidate("本场行动日志达到上限。");
                    return;
                }
                if (pointIndex >= journal.Points.Count) throw new InvalidDataException("出现额外行动点。");
                Point expected = journal.Points[pointIndex];
                if (expected.Round != round || expected.DrawCount != drawIndex || expected.Digest != digest)
                    throw new InvalidDataException("单挑第 " + round + " 回合状态或随机序列不一致。");
                if (pointIndex == journal.Actions.Count)
                {
                    if (drawIndex != journal.Draws.Count || state != journal.FinalState)
                        throw new InvalidDataException("最终行动点没有通过详细状态核验。");
                    var random = JsonUtility.FromJson<UnityEngine.Random.State>(journal.RandomState);
                    if (JsonUtility.ToJson(random) != journal.RandomState)
                        throw new InvalidDataException("保存的随机状态无法恢复。");
                    UnityEngine.Random.state = random;
                    replaying = false;
                    finalVerified = true;
                    if (sceneReady) CompleteReplay();
                }
                else
                {
                    int action = journal.Actions[pointIndex++];
                    injecting = true;
                    try { manager.Action((CombatActionType)action); }
                    finally { injecting = false; }
                }
        }

        private static void CompleteReplay()
        {
            if (!finalVerified || failed) return;
            finalVerified = false;
            waiting = true;
            UnlockInput();
            StoryReplay.OnNativeLoadCompleted();
            Report("单挑第 " + manager.CurrentRounds + " 回合恢复完成，状态已核验。");
        }

        private static bool BeforeAction(CombatActionType action)
        {
            if (failed) return false;
            if (replaying) return injecting;
            if (finalVerified) return false;
            if (manager == null) return true;
            if (invalid != null) return true;
            if (!waiting || journal.Points.Count != journal.Actions.Count + 1) return false;
            try
            {
                if (DuelAudit.Capture(manager) != journal.FinalState)
                    Invalidate("等待期间战斗数据已改变。");
                if (journal.Actions.Count >= MaxActions) Invalidate("本场行动日志达到上限。");
                if (invalid == null) journal.Actions.Add((int)action);
            }
            catch (Exception e) { Invalidate("无法核验行动前状态：" + e.GetBaseException().Message); }
            waiting = false;
            return true;
        }

        private static void End(CombatManager current)
        {
            if (manager != current) return;
            if (Replaying) Fail("到达目标回合前战斗已结束。");
            else Reset();
        }

        private static void Invalidate(string reason)
        {
            if (manager == null || replaying || invalid != null) return;
            invalid = "本场回合保存已停用：" + reason;
            waiting = false;
        }

        private static void Fail(string reason)
        {
            if (failed) return;
            string detail = Describe(reason);
            failed = true;
            replaying = false;
            pending = null;
            waiting = false;
            try { manager?.StopAllCoroutines(); manager?.PlayerAction?.StopAllCoroutines(); manager?.EnemyAction?.StopAllCoroutines(); }
            catch (Exception) { }
            Time.timeScale = 1f;
            LockInput();
            Plugin.Instance?.Gate.Fail("单挑恢复中止：" + reason);
            Report("单挑恢复中止：" + reason + "。请重新读取存档。");
            try { Plugin.Instance?.Log("[Anytime] " + detail); } catch (Exception) { }
        }
        // Names the round, the action and random tape positions, and whether the
        // live state differed from the recorded digest. Missing data is reported
        // as absent rather than defaulted into a value that could look valid.
        private static string Describe(string reason)
        {
            var text = new StringBuilder("duel recovery failed: reason=").Append(reason);
            text.Append("; scene-key=").Append(PlayerStatManagerData.Instance?.CurrentSceneKey ?? "<none>");
            text.Append("; round=").Append(manager == null ? "<none>" : manager.CurrentRounds.ToString());
            text.Append("; recorded-scene-key=").Append(journal == null ? "<none>" : journal.SceneKey ?? "<none>");
            text.Append("; points=").Append(pointIndex).Append('/').Append(journal == null ? -1 : journal.Points.Count);
            text.Append("; actions=").Append(journal == null ? -1 : journal.Actions.Count);
            text.Append("; draws=").Append(drawIndex).Append('/').Append(journal == null ? -1 : journal.Draws.Count);
            text.Append("; last-action=").Append(journal != null && pointIndex > 0 && pointIndex - 1 < journal.Actions.Count
                ? ((CombatActionType)journal.Actions[pointIndex - 1]).ToString() : "<none>");
            text.Append("; waiting=").Append(waiting);
            text.Append("; injecting=").Append(injecting);
            text.Append("; locked-inputs=").Append(InteractionLock.Held ? "held" : "free");
            return text.ToString();
        }

        private static void Report(string message)
        {
            if (Plugin.Instance != null) { Plugin.Instance.Status = message; Plugin.Instance.Log(message); }
        }

        private static void LockInput() { InteractionLock.Set(InteractionHold.Duel, true); }

        private static void UnlockInput() { InteractionLock.Set(InteractionHold.Duel, false); }

        private static bool Numbered(string slot) => slot != null && slot.Length == 3 && slot != "000" &&
            slot.All(c => c >= '0' && c <= '9');
        private static string SlotFile(string slot) => (string)GetSlotFile.Invoke(SaveSystem.Instance, new object[] { slot });

        private static byte[] NativeBytes(GameSave save)
        { return NativeObjectBytes(save); }

        private static byte[] NativeObjectBytes(object save)
        {
            using (var stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, save);
                return stream.ToArray();
            }
        }

        private static GameSave ReadNative(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes)) return (GameSave)new BinaryFormatter().Deserialize(stream);
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        private static byte[] Encode(Journal value)
        {
            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(value));
            if (data.Length > MaxFile - 64) throw new InvalidDataException("单挑记录超过 32 MB。" );
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                writer.Write(data.Length);
                writer.Write(data);
                writer.Write(Convert.FromBase64String(Hash(data)));
                return stream.ToArray();
            }
        }

        private static Journal Decode(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < Magic.Length + 4 + 32 || stream.Length > MaxFile ||
                    !reader.ReadBytes(Magic.Length).SequenceEqual(Magic))
                    throw new InvalidDataException("单挑附档头或文件大小无效。");
                int length = reader.ReadInt32();
                if (length <= 0 || length != stream.Length - Magic.Length - 4 - 32)
                    throw new InvalidDataException("单挑附档长度无效。");
                byte[] data = reader.ReadBytes(length);
                if (!reader.ReadBytes(32).SequenceEqual(Convert.FromBase64String(Hash(data))))
                    throw new InvalidDataException("单挑附档内容校验失败。");
                return JsonUtility.FromJson<Journal>(Encoding.UTF8.GetString(data));
            }
        }

        private static void Validate(Journal value)
        {
            if (value == null || value.Format != 1 || string.IsNullOrEmpty(value.Client) ||
                string.IsNullOrEmpty(value.SceneKey) || string.IsNullOrEmpty(value.NativeHash) ||
                string.IsNullOrEmpty(value.RandomState) || string.IsNullOrEmpty(value.FinalState) ||
                value.Actions == null || value.Points == null || value.Draws == null ||
                value.Actions.Count > MaxActions || value.Draws.Count > MaxDraws ||
                value.Points.Count != value.Actions.Count + 1)
                throw new InvalidDataException("单挑记录结构不完整。");
            int prior = 0;
            foreach (var point in value.Points)
            {
                if (point == null || point.Round < 1 || point.DrawCount < prior ||
                    point.DrawCount > value.Draws.Count || string.IsNullOrEmpty(point.Digest))
                    throw new InvalidDataException("单挑行动点记录无效。");
                prior = point.DrawCount;
            }
            if (prior != value.Draws.Count || value.Points.Last().Digest != Hash(Encoding.UTF8.GetBytes(value.FinalState)))
                throw new InvalidDataException("单挑目标状态校验失败。");
            foreach (var draw in value.Draws)
                if (draw == null || string.IsNullOrEmpty(draw.Site) || draw.Kind < 0 || draw.Kind > 2)
                    throw new InvalidDataException("单挑随机记录无效。");
        }

        private static void Stage(string path, byte[] bytes)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        }

        private static void Replace(string staged, string destination)
        {
            if (File.Exists(destination)) File.Replace(staged, destination, null);
            else File.Move(staged, destination);
        }

        // The native file and the journal must become valid together. The pair is
        // staged, both members are re-read and verified, and only then does the
        // transaction claim a commit; a crash before that point restores the exact
        // pre-save combination recorded in the journal.
        private static void WritePair(string path, byte[] native, byte[] sidecar)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string extra = path + ".mhduel", first = path + ".mhduel-native-stage", second = extra + ".stage";
            string required = path + ".mhduel-required";
            var transaction = SlotTransaction.Begin(SlotTransaction.JournalFor(path), "duel", new[] {
                path, extra, required, path + ".mha", path + ".mha-required"
            });
            try
            {
                Stage(first, native); Stage(second, sidecar);
                if (Hash(File.ReadAllBytes(first)) != Hash(native)) throw new IOException("原生档暂存校验失败。");
                Replace(first, path);
                Replace(second, extra);
                if (Hash(File.ReadAllBytes(path)) != Hash(native) ||
                    Decode(extra).NativeHash != Hash(native)) throw new IOException("双文件提交校验失败。");
                // A duel save owns the slot, so a stale story companion is retired
                // inside the same transaction rather than after it.
                Stage(required, Encoding.ASCII.GetBytes("1"));
                DeleteIfExists(path + ".mha");
                DeleteIfExists(path + ".mha-required");
                transaction.RecordReplacement(SlotTransaction.HashFile(path));
                transaction.Commit();
                transaction.Complete();
            }
            catch (Exception original)
            {
                var report = transaction.Rollback();
                if (!report.Complete)
                    throw new IOException("双文件保存失败且回退未完成；事务日志与备份已保留。",
                        new AggregateException(original, new InvalidOperationException(report.Detail)));
                throw;
            }
            finally
            {
                try { if (File.Exists(first)) File.Delete(first); } catch (Exception) { }
                try { if (File.Exists(second)) File.Delete(second); } catch (Exception) { }
            }
        }

        private static void DeleteIfExists(string path)
        { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }

        // Never delete a companion the player might still need; a rename keeps it
        // out of the load path and leaves it recoverable by hand.
        private static void Quarantine(string path)
        {
            try
            {
                string target = path + ".stale";
                if (File.Exists(target)) File.Delete(target);
                File.Move(path, target);
            }
            catch (Exception error) { Plugin.Instance?.Report(error); }
        }

        private static Draw DrawRandom(Draw requested, Func<Draw> fresh)
        {
            if (manager == null || journal == null || invalid != null || failed) return fresh();
            if (!replaying)
            {
                Draw item = fresh();
                if (journal.Draws.Count == MaxDraws) Invalidate("本场随机日志达到上限。");
                else journal.Draws.Add(item);
                return item;
            }
            if (drawIndex >= journal.Draws.Count) { Fail("随机调用超过已保存记录。"); throw new InvalidDataException("Random tape exhausted"); }
            Draw saved = journal.Draws[drawIndex++];
            if (saved.Site != requested.Site || saved.Kind != requested.Kind || saved.A != requested.A ||
                saved.B != requested.B || saved.X != requested.X || saved.Y != requested.Y)
            { Fail("随机调用顺序或参数变化。"); throw new InvalidDataException("Random call mismatch"); }
            return saved;
        }

        internal static float RandomValue(string site) => DrawRandom(new Draw { Site = site, Kind = 0 },
            () => new Draw { Site = site, Kind = 0, Floating = UnityEngine.Random.value }).Floating;
        internal static int RandomInteger(int low, int high, string site) => DrawRandom(
            new Draw { Site = site, Kind = 1, A = low, B = high },
            () => new Draw { Site = site, Kind = 1, A = low, B = high, Integer = UnityEngine.Random.Range(low, high) }).Integer;
        internal static float RandomFloating(float low, float high, string site) => DrawRandom(
            new Draw { Site = site, Kind = 2, X = low, Y = high },
            () => new Draw { Site = site, Kind = 2, X = low, Y = high, Floating = UnityEngine.Random.Range(low, high) }).Floating;

        [HarmonyPatch(typeof(CombatManager), "Start")]
        private static class BattleBeginPatch
        {
            private static void Postfix(CombatManager __instance)
            {
                try { Begin(__instance); }
                catch (Exception e) { Fail("单挑初始化失败：" + e.GetBaseException().Message); }
            }
        }

        [HarmonyPatch(typeof(CombatUIManager), "ShowAction")]
        private static class ActionPointPatch
        {
            private static void Postfix(ref IEnumerator __result) { __result = ObserveActionPoint(__result); }
        }

        [HarmonyPatch(typeof(CombatManager), "Action")]
        private static class PlayerActionPatch
        {
            private static bool Prefix(CombatActionType __0) => BeforeAction(__0);
        }

        [HarmonyPatch(typeof(CombatManager), "GetNextInput")]
        private static class InputPollPatch
        {
            private static void Postfix(ref bool __result) { if (replaying || finalVerified) __result = true; }
        }

        [HarmonyPatch(typeof(CombatManager), "NextInputAction")]
        private static class DialogueInputPatch
        {
            private static bool Prefix() => !Replaying && !failed;
        }

        [HarmonyPatch(typeof(CombatManager), "OnDisable")]
        private static class BattleEndPatch
        {
            private static void Prefix(CombatManager __instance) { End(__instance); }
        }
    }

    [HarmonyPatch]
    internal static class AnytimeDuelRandomPatch
    {
        private static List<MethodBase> targets;
        internal static bool Covered
        {
            get
            {
                if (targets == null || targets.Count != 67) return false;
                foreach (var method in targets)
                {
                    var info = Harmony.GetPatchInfo(method);
                    if (info == null || !info.Owners.Contains("cerveth.mortal.helper")) return false;
                }
                return true;
            }
        }

        private static MethodInfo Substitute(MethodInfo original)
        {
            if (original.DeclaringType != typeof(UnityEngine.Random)) return null;
            if (original.Name == "get_value") return AccessTools.Method(typeof(AnytimeDuel), "RandomValue");
            if (original.Name != "Range") return null;
            var args = original.GetParameters();
            if (args.Length != 2) return null;
            return AccessTools.Method(typeof(AnytimeDuel), args[0].ParameterType == typeof(int) ? "RandomInteger" : "RandomFloating");
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            targets = new List<MethodBase>();
            var types = typeof(CombatManager).Assembly.GetTypes().Concat(new[] { typeof(DiceUtils), typeof(StatValueReference) });
            foreach (var type in types)
                foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static))
                {
                    if (method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    if (PatchProcessor.GetOriginalInstructions(method).Any(c => c.operand is MethodInfo m && Substitute(m) != null))
                        targets.Add(method);
                }
            return targets;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> input, MethodBase __originalMethod)
        {
            int index = 0;
            foreach (var instruction in input)
            {
                MethodInfo replacement = instruction.operand is MethodInfo method ? Substitute(method) : null;
                if (replacement != null)
                {
                    var site = new CodeInstruction(OpCodes.Ldstr,
                        __originalMethod.Module.Name + ":" + __originalMethod.MetadataToken + ":" + index++);
                    site.labels.AddRange(instruction.labels);
                    site.blocks.AddRange(instruction.blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock));
                    var call = new CodeInstruction(OpCodes.Call, replacement);
                    call.blocks.AddRange(instruction.blocks.Where(b => b.blockType == ExceptionBlockType.EndExceptionBlock));
                    yield return site;
                    yield return call;
                }
                else yield return instruction;
            }
        }
    }

    // A deterministic, read-only fingerprint of the live battle. It intentionally
    // records gameplay data while treating Unity view components as references.
    internal sealed class DuelAudit
    {
        private readonly StringBuilder text = new StringBuilder();
        private readonly HashSet<object> visited = new HashSet<object>(ReferenceIdentity.Instance);
        private static readonly MethodInfo CreateNative = AccessTools.Method(typeof(SaveSystem), "CreateSaveData");
        private readonly CombatManager battle;
        private readonly List<CombatStateEffect> activeEffects = new List<CombatStateEffect>();

        private DuelAudit(CombatManager battle)
        {
            this.battle = battle;
            var field = AccessTools.Field(typeof(CombatActionController), "_effects");
            if (field == null) throw new InvalidDataException("单挑效果集合字段缺失。");
            foreach (var actor in new[] { battle.PlayerAction, battle.EnemyAction })
            {
                if (actor == null || !(field.GetValue(actor) is IList list))
                    throw new InvalidDataException("单挑效果集合无法读取。");
                foreach (var effect in list) if (effect is CombatStateEffect combatEffect) activeEffects.Add(combatEffect);
            }
        }

        internal static string Capture(CombatManager battle)
        {
            var audit = new DuelAudit(battle);
            audit.Write("round", battle.CurrentRounds);
            audit.Write("scene-key", PlayerStatManagerData.Instance.CurrentSceneKey);
            audit.Walk("player", battle.PlayerAction, true, 0);
            audit.Walk("player-stat", battle.PlayerAction?.Stat, true, 0);
            audit.Walk("enemy", battle.EnemyAction, true, 0);
            audit.Walk("enemy-stat", battle.EnemyAction?.Stat, true, 0);
            audit.Effects("player-effect", battle.PlayerAction);
            audit.Effects("enemy-effect", battle.EnemyAction);
            audit.Walk("level", battle.CombatLevel, true, 0);
            audit.Walk("enemy-intent", AccessTools.Field(typeof(CombatManager), "_enemyActionResult")?.GetValue(battle), true, 0);
            var save = (GameSave)CreateNative.Invoke(SaveSystem.Instance, null);
            string restore = save.RestoreData;
            save.RestoreData = null;
            audit.Walk("campaign", save, true, 0);
            if (!string.IsNullOrEmpty(restore)) audit.Walk("restore", JsonUtility.FromJson<GameSave>(restore), true, 0);
            return audit.text.ToString();
        }

        private void Effects(string path, CombatActionController actor)
        {
            var field = AccessTools.Field(typeof(CombatActionController), "_effects");
            if (actor == null || field == null || !(field.GetValue(actor) is IList list))
                throw new InvalidDataException("单挑效果集合无法读取。");
            for (int i = 0; i < list.Count; i++) Walk(path + "[" + i + "]", list[i], true, 0);
            Write(path + ".count", list.Count);
        }

        private void Write(string key, object value)
        {
            string data = value == null ? "<null>" : value is float f ? f.ToString("R", CultureInfo.InvariantCulture)
                : value is double d ? d.ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture);
            text.Append(key.Length).Append(':').Append(key).Append(data.Length).Append(':').Append(data);
            if (text.Length > 4000000) throw new InvalidDataException("单挑现场状态超出校验上限。");
        }

        private void Walk(string path, object value, bool root, int depth)
        {
            if (depth > 40) throw new InvalidDataException("单挑状态嵌套过深。");
            if (value == null) { Write(path, null); return; }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is decimal || value is string)
            {
                Write(path, value); return;
            }
            if (value is Delegate || value is IEnumerator || (!root && value is Component) ||
                (!root && value is UnityEngine.Object)) { Write(path, type.FullName); return; }
            if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
            { Write(path, type.FullName); return; }
            if (value is ConditionalWeakTable<CombatManager, StrongBox<int>> managerCache)
            {
                Write(path, managerCache.TryGetValue(battle, out var entry) ? entry.Value : 0);
                return;
            }
            if (value is ConditionalWeakTable<CombatStateEffect, StrongBox<int>> effectCache)
            {
                for (int i = 0; i < activeEffects.Count; i++)
                    Write(path + "[" + i + "]", effectCache.TryGetValue(activeEffects[i], out var entry) ? entry.Value : 0);
                return;
            }
            if (value is IDictionary<CombatStateEffect, int> effectCounts)
            {
                for (int i = 0; i < activeEffects.Count; i++)
                    Write(path + "[" + i + "]", effectCounts.TryGetValue(activeEffects[i], out var count) ? count : 0);
                return;
            }
            if (value is HashSet<CombatStateEffect> effectSet)
            {
                for (int i = 0; i < activeEffects.Count; i++) Write(path + "[" + i + "]", effectSet.Contains(activeEffects[i]));
                return;
            }
            if (!type.IsValueType && !visited.Add(value)) { Write(path, "<seen>"); return; }
            Write(path + ".type", type.FullName);
            if (value is IDictionary dictionary)
            {
                var entries = new List<DictionaryEntry>();
                foreach (DictionaryEntry entry in dictionary) entries.Add(entry);
                foreach (var entry in entries.OrderBy(e => Convert.ToString(e.Key, CultureInfo.InvariantCulture), StringComparer.Ordinal))
                    Walk(path + "[" + Convert.ToString(entry.Key, CultureInfo.InvariantCulture) + "]", entry.Value, false, depth + 1);
                Write(path + ".count", entries.Count); return;
            }
            if (value is IEnumerable sequence)
            {
                int index = 0;
                foreach (var item in sequence) Walk(path + "[" + index++ + "]", item, false, depth + 1);
                Write(path + ".count", index); return;
            }
            for (Type cursor = type; cursor != null && cursor != typeof(object) &&
                cursor.Namespace != "UnityEngine"; cursor = cursor.BaseType)
                foreach (var field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly).OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    if (field.Name == "TimeTick" || field.Name == "FreeRandomState" || field.Name == "RestoreData") continue;
                    Walk(path + "." + cursor.Name + "." + field.Name, field.GetValue(value), false, depth + 1);
                }
        }

        private sealed class ReferenceIdentity : IEqualityComparer<object>
        {
            internal static readonly ReferenceIdentity Instance = new ReferenceIdentity();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
