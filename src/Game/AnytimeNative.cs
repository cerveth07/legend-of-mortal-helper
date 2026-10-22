using System;
using System.IO;
using HarmonyLib;
using Mortal.Combat;
using Mortal.Core;
using Mortal.Story;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    // The game's numbered slots remain the only user-facing save and load path.
    // Unlocking their button never grants permission to write an incomplete save:
    // the SaveGameData prefix checks the same state immediately before I/O.
    internal static class AnytimeNative
    {
        [ThreadStatic] private static string savingSlot;
        [ThreadStatic] private static bool savingStory;
        [ThreadStatic] private static string loadingSlot;
        private static string offeredScene;
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
            if (SceneManager.GetActiveScene().name == "Combat")
            {
                if (Enabled)
                {
                    if (AnytimeDuel.SaveToSlot(slot))
                    {
                        try { ForgetStorySidecar(slot); }
                        catch (Exception error) { Plugin.Instance.Report(error); }
                    }
                    return false;
                }
            }
            bool story = Enabled && StoryScene;
            if (story && !AnytimeStory.BeforeNativeSave(slot))
                return false;
            savingSlot = slot;
            savingStory = story;
            return true;
        }

        internal static void SavedData(string path, object data)
        {
            AnytimeDuel.CaptureStart(path, data);
            if (savingSlot == null) return;
            if (savingStory)
            {
                AnytimeStory.AfterNativeSave(path, data);
                // A successful story commit supersedes a prior duel record.
                if (AnytimeStory.NativeSaveSucceeded(path, data)) DeleteIfExists(path + ".mhduel");
            }
            else if (AnytimeStory.ForgetAfterOrdinarySave(path, data))
                DeleteIfExists(path + ".mhduel");
        }

        internal static void EndSave(SaveScope previous)
        {
            if (savingStory) AnytimeStory.AbortNativeSave();
            savingSlot = previous.Slot;
            savingStory = previous.Story;
        }

        private static void ForgetStorySidecar(string slot)
        {
            string path = (string)AccessTools.Method(typeof(SaveSystem), "GetSaveDataFile")
                .Invoke(SaveSystem.Instance, new object[] { slot });
            DeleteIfExists(path + ".mha");
            DeleteIfExists(path + ".mha-required");
        }

        private static void DeleteIfExists(string path)
        { if (File.Exists(path)) File.Delete(path); }

        internal static void BeginLoad(SaveSystem save, out string previous)
        {
            previous = loadingSlot;
            loadingSlot = save == null ? null : save.CurrentSlot;
        }

        internal static void EndLoad(string previous) { loadingSlot = previous; }

        internal static void PrepareLoad(ref GameSave data)
        {
            if (data == null || loadingSlot == null || Plugin.Instance == null) return;
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
            if (AnytimeNative.CanOffer) { AnytimeNative.MarkOffered(); __instance.ToggleSaveButton(true); }
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
