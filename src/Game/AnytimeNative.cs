using System;
using System.IO;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Core;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    // The game's numbered slots remain the only user-facing save and load path.
    // Unlocking their button never grants permission to write an incomplete save:
    // the SaveGameData prefix checks the same state immediately before I/O.
    //
    // Every write that can touch a numbered slot also opens a journal for that
    // slot, so an interrupted save is settled deterministically on the next load
    // or save instead of being guessed from whatever files happen to be left.
    internal static class AnytimeNative
    {
        [ThreadStatic] private static string savingSlot;
        [ThreadStatic] private static bool savingStory;
        [ThreadStatic] private static string loadingSlot;
        private static string offeredScene;
        private static SlotTransaction supersedeTransaction;
        private static string supersedePath;
        internal struct SaveScope
        {
            internal string Slot;
            internal bool Story;
        }

        private static bool Enabled => Plugin.Active && Plugin.Instance.Options.AnytimeSave.Value;
        private static bool StoryScene
        {
            get
            {
                string scene = SceneManager.GetActiveScene().name;
                return scene == "Story" || scene == "Break";
            }
        }

        internal static bool CanOffer => Availability == null;

        internal static bool CanOfferStory => StoryScene && CanOffer;

        internal static string Availability
        {
            get
            {
                if (!Enabled) return "随时存档开关已关闭。";
                if (!Plugin.Instance.Gate.CanInteract) return "恢复流程进行中。";
                string scene = SceneManager.GetActiveScene().name;
                if (scene == "Combat") return AnytimeDuel.BlockReason;
                if (scene == "Story" || scene == "Break") return AnytimeStory.BlockReason;
                return "当前场景不支持随时存档。";
            }
        }

        internal static string ManualLoadSlot => loadingSlot;

        internal static bool OfferedHere => offeredScene == SceneManager.GetActiveScene().name;
        internal static void MarkOffered() { offeredScene = SceneManager.GetActiveScene().name; }
        internal static void ClearOffer() { offeredScene = null; }

        internal static void SaveFailed(string message)
        {
            if (Plugin.Instance == null) return;
            Plugin.Instance.Status = message;
            if (Plugin.Instance.Panel != null) Plugin.Instance.Panel.Visible = true;
            Plugin.Instance.Log(message);
        }

        internal static bool BeforeSave(string slot, out SaveScope previous)
        {
            previous = new SaveScope { Slot = savingSlot, Story = savingStory };
            if (!Enabled && offeredScene == SceneManager.GetActiveScene().name &&
                (offeredScene == "Combat" || offeredScene == "Story" || offeredScene == "Break"))
            {
                SaveFailed("保存菜单由随时存档开放后，开关已关闭；请重新开启功能后再保存。");
                return false;
            }
            if (Enabled && !Plugin.Instance.Gate.CanInteract)
            {
                SaveFailed("恢复期间不能存档。");
                return false;
            }
            // Game-owned autosave slots do not use the numbered manual-save path.
            // Leave them to the native saver while keeping unsafe manual slots guarded.
            if (Enabled && slot != null && slot.StartsWith("auto", StringComparison.OrdinalIgnoreCase))
            {
                savingSlot = null;
                savingStory = false;
                try { Plugin.Instance.Log("[Save] native non-numbered slot=" + slot); }
                catch (Exception) { }
                return true;
            }
            if (SceneManager.GetActiveScene().name == "Combat")
            {
                if (Enabled)
                {
                    // The duel writer owns the whole slot pair and retires a stale
                    // story companion inside its own transaction.
                    AnytimeDuel.SaveToSlot(slot);
                    return false;
                }
            }
            bool story = Enabled && StoryScene;
            if (story && !AnytimeStory.BeforeNativeSave(slot)) return false;
            if (!story && !BeginSupersede(slot)) return false;
            savingSlot = slot;
            savingStory = story;
            return true;
        }

        // An ordinary native save owns the slot afterwards. Snapshot the old
        // companions first so a crash between "the game rewrote the .dat" and
        // "the companions were retired" is finished forward next time instead of
        // leaving a sidecar that rejects a perfectly good native save.
        private static bool BeginSupersede(string slot)
        {
            supersedeTransaction = null;
            supersedePath = null;
            if (!Numbered(slot)) return true;
            try
            {
                string path = PathFor(slot);
                if (string.IsNullOrEmpty(path)) return true;
                SettleSlot(slot, path);
                if (SlotTransaction.Exists(SlotTransaction.JournalFor(path)))
                {
                    SaveFailed("此槽位有上次未完成的存档事务；请先读取该槽位让插件自动恢复。");
                    return false;
                }
                if (!File.Exists(path)) PurgeOrphans(path);
                if (!HasCompanion(path)) return true;
                supersedeTransaction = SlotTransaction.Begin(SlotTransaction.JournalFor(path), "supersede", new[] {
                    path, path + ".mha", path + ".mha-required", path + ".mhduel", path + ".mhduel-required"
                });
                supersedePath = path;
                return true;
            }
            catch (Exception error)
            {
                supersedeTransaction = null;
                supersedePath = null;
                SaveFailed("准备覆盖存档失败：" + error.GetBaseException().Message);
                return false;
            }
        }

        internal static void SavedData(string path, object data)
        {
            AnytimeDuel.CaptureStart(path, data);
            if (savingSlot == null) return;
            if (savingStory)
            {
                AnytimeStory.AfterNativeSave(path, data);
                return;
            }
            if (supersedeTransaction == null || !string.Equals(path, supersedePath, StringComparison.Ordinal)) return;
            var transaction = supersedeTransaction;
            supersedeTransaction = null;
            supersedePath = null;
            try
            {
                if (!AnytimeStory.OrdinalSaveVerified(path, data))
                    throw new IOException("普通存档写入后内容校验不一致。");
                transaction.RecordReplacement(SlotTransaction.HashFile(path));
                FinishOperation(transaction, path);
            }
            catch (Exception error)
            {
                var report = transaction.Rollback();
                SaveFailed(report.Complete
                    ? "普通存档未完成，槽位已回退：" + error.GetBaseException().Message
                    : "普通存档未完成且回退失败；已保留事务日志与备份：" + error.GetBaseException().Message +
                        "；" + report.Detail);
            }
        }

        internal static void EndSave(SaveScope previous)
        {
            if (savingStory) AnytimeStory.AbortNativeSave();
            else AbandonSupersede();
            savingSlot = previous.Slot;
            savingStory = previous.Story;
        }

        private static void AbandonSupersede()
        {
            var transaction = supersedeTransaction;
            supersedeTransaction = null;
            supersedePath = null;
            if (transaction == null) return;
            var report = transaction.Rollback();
            if (!report.Complete) SaveFailed("覆盖前的存档无法完整回退，已保留事务日志与备份：" + report.Detail);
        }

        // Applies the operation's companion retirement, then releases the journal.
        internal static void FinishOperation(SlotTransaction transaction, string path)
        {
            if (transaction == null) return;
            RetireCompanions(transaction.Operation, path);
            transaction.Complete();
        }

        private static void RetireCompanions(string operation, string path)
        {
            bool story = operation == "story", duel = operation == "duel", supersede = operation == "supersede";
            // A story commit supersedes any duel record, and a duel commit
            // supersedes any story record: the slot has exactly one meaning.
            if (story || supersede) { DeleteIfExists(path + ".mhduel"); DeleteIfExists(path + ".mhduel-required"); }
            if (duel || supersede) { DeleteIfExists(path + ".mha"); DeleteIfExists(path + ".mha-required"); }
        }

        // Settles any journal left on the slot. Called before the game reads the
        // native file and before a new write touches the slot.
        internal static void SettleSlot(string slot, string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string journal = SlotTransaction.JournalFor(path);
            SlotTransaction transaction;
            try { transaction = SlotTransaction.Read(journal); }
            catch (Exception error)
            { throw new InvalidDataException("存档事务日志无法读取：" + error.GetBaseException().Message); }
            if (transaction == null) return;
            if (transaction.State == SlotTransactionState.Broken)
                throw new InvalidDataException("此槽位上次存档失败且回退未完成；事务日志与备份仍保留在 " + journal +
                    "。请从完整备份恢复该槽位后再读取。");
            if (transaction.State == SlotTransactionState.Committed || transaction.State == SlotTransactionState.RolledBack)
            { transaction.Complete(); return; }
            // A prepared transaction is finished forward only when the caller had
            // already verified the replacement file it recorded.
            bool forward = !string.IsNullOrEmpty(transaction.ReplacementHash) && File.Exists(path) &&
                SlotTransaction.HashFile(path) == transaction.ReplacementHash;
            if (forward)
            {
                RetireCompanions(transaction.Operation, path);
                transaction.Complete();
                return;
            }
            var report = transaction.Rollback();
            if (!report.Complete)
                throw new InvalidDataException("此槽位上次未完成的存档无法回退：" + report.Detail);
            Report("上次未完成的存档已回退到保存前的状态：" + Path.GetFileName(path));
        }

        // Status and log only: settlement can happen during a load prefix, where
        // forcing the mod panel open would fight the game's own loading screen.
        private static void Report(string message)
        {
            if (Plugin.Instance == null) return;
            Plugin.Instance.Status = message;
            try { Plugin.Instance.Log("[Anytime] " + message); } catch (Exception) { }
        }

        // Removes companion files left on a slot that has no native file at all.
        // Such companions cannot describe anything, so keeping them would only
        // block the next save with a "-required without sidecar" error. The old
        // .mha-rollback copies are deliberately kept: with no .dat present they
        // may be the only surviving copy of a save, and deleting them would be
        // exactly the data loss this journal exists to prevent.
        internal static void PurgeOrphans(string path)
        {
            if (string.IsNullOrEmpty(path) || File.Exists(path)) return;
            DeleteIfExists(path + ".mha");
            DeleteIfExists(path + ".mha-required");
            DeleteIfExists(path + ".mhduel");
            DeleteIfExists(path + ".mhduel-required");
            DeleteIfExists(path + ".mha-pending");
            DeleteIfExists(path + ".mhduel-pending");
        }

        // Called after the game deleted a slot: the companions are now orphans.
        internal static void ForgetSlotFiles(string slot)
        {
            if (!Numbered(slot)) return;
            string path = PathFor(slot);
            if (string.IsNullOrEmpty(path) || File.Exists(path)) return;
            PurgeOrphans(path);
            string journal = SlotTransaction.JournalFor(path);
            if (!SlotTransaction.Exists(journal)) return;
            var transaction = SlotTransaction.Read(journal);
            if (transaction != null) transaction.Complete();
            else DeleteIfExists(journal);
        }

        internal static void BeginLoad(SaveSystem save, out string previous)
        {
            previous = loadingSlot;
            loadingSlot = save == null ? null : save.CurrentSlot;
            if (save == null || !Numbered(loadingSlot)) return;
            try { SettleSlot(loadingSlot, PathFor(loadingSlot)); }
            catch (Exception error)
            {
                Plugin.Instance?.Gate.Fail("读取前无法确认存档事务状态：" + error.GetBaseException().Message);
                throw;
            }
        }

        internal static void EndLoad(string previous) { loadingSlot = previous; }

        internal static void PrepareLoad(ref GameSave data)
        {
            if (data == null || loadingSlot == null || Plugin.Instance == null) return;
            if (StoryReplay.LoadingBaseline) return;
            // LoadGameData may return early for an empty or unreadable slot.
            // Keep the current fight/story journal until native data is applied.
            AnytimeStory.BeforeNativeLoad();
            AnytimeDuel.Reset();
            try
            {
                if (data.CurrentScene == "Combat")
                    AnytimeDuel.PrepareLoad(loadingSlot, ref data);
                else if (data.CurrentScene == "Story" || data.CurrentScene == "Break")
                    AnytimeStory.PrepareLoad(loadingSlot, ref data);
            }
            catch (Exception error)
            {
                Plugin.Instance.Gate.Fail("中途档读取失败：" + error.GetBaseException().Message);
                throw;
            }
        }

        internal static bool Numbered(string slot) =>
            slot != null && slot.Length == 3 && slot != "000" && slot[0] >= '0' && slot[0] <= '9' &&
            slot[1] >= '0' && slot[1] <= '9' && slot[2] >= '0' && slot[2] <= '9';

        internal static string PathFor(string slot)
        {
            string path = (string)AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile")
                .Invoke(SaveSystem.Instance, new object[] { slot });
            if (!string.IsNullOrEmpty(path)) Directory.CreateDirectory(Path.GetDirectoryName(path));
            return path;
        }

        private static bool HasCompanion(string path)
        {
            return File.Exists(path + ".mha") || File.Exists(path + ".mha-required") ||
                File.Exists(path + ".mhduel") || File.Exists(path + ".mhduel-required");
        }

        private static void DeleteIfExists(string path)
        { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
    }

    [HarmonyPatch(typeof(SaveSystem), "SaveGameData", new Type[] { typeof(string) })]
    internal static class AnytimeSavePatch
    {
        private static bool Prefix(string __0, out AnytimeNative.SaveScope __state)
        { return AnytimeNative.BeforeSave(__0, out __state); }

        private static Exception Finalizer(Exception __exception, AnytimeNative.SaveScope __state)
        {
            AnytimeNative.EndSave(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "ExecuteSaveData")]
    internal static class AnytimeSaveDataPatch
    {
        private static void Postfix(string saveFile, object gameSave)
        { AnytimeNative.SavedData(saveFile, gameSave); }
    }

    [HarmonyPatch(typeof(SaveSystem), "LoadGameData")]
    internal static class AnytimeLoadScopePatch
    {
        private static void Prefix(SaveSystem __instance, out string __state)
        { AnytimeNative.BeginLoad(__instance, out __state); }

        private static Exception Finalizer(Exception __exception, string __state)
        {
            AnytimeNative.EndLoad(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData")]
    internal static class AnytimeLoadDataPatch
    {
        [HarmonyPriority(Priority.Low)]
        private static void Prefix(ref GameSave __0) { AnytimeNative.PrepareLoad(ref __0); }
    }

    // The game deletes only the native file. Leftover companions would make the
    // next save in that slot refuse to write ("required companion missing"), so
    // they are retired as soon as the native file is gone.
    [HarmonyPatch(typeof(SaveSystem), "DeleteSaveData")]
    internal static class AnytimeDeletePatch
    {
        private static void Postfix(string __0)
        {
            try { AnytimeNative.ForgetSlotFiles(__0); }
            catch (Exception error) { Plugin.Instance?.Report(error); }
        }
    }

    [HarmonyPatch(typeof(MenuPanel), "ToggleSaveButton")]
    internal static class AnytimeSaveButtonPatch
    {
        private static void Prefix(ref bool active)
        { if (!active && AnytimeNative.CanOffer) { active = true; AnytimeNative.MarkOffered(); } }
    }

    [HarmonyPatch(typeof(MenuPanel), "OnPanelOpen")]
    internal static class AnytimeMenuOpenedPatch
    {
        private static void Postfix(MenuPanel __instance)
        {
            bool available = AnytimeNative.CanOffer;
            try { Plugin.Instance?.Log("[Anytime] save menu " + (available ? "available" : AnytimeNative.Availability)); }
            catch (Exception) { }
            if (available) { AnytimeNative.MarkOffered(); __instance.ToggleSaveButton(true); }
            else if (AnytimeNative.OfferedHere) __instance.ToggleSaveButton(false);
        }
    }

    [HarmonyPatch(typeof(LuaManager), "ToggleSaveButton")]
    internal static class AnytimeStorySaveButtonPatch
    {
        private static bool Prefix(int active)
        { return active != 0 || !AnytimeNative.CanOfferStory; }
    }
}
