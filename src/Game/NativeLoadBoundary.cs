using System;
using System.Collections;
using HarmonyLib;
using Mortal.Core;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData")]
    internal static class NativeLoadBoundary
    {
        private const float StallSeconds = 60f;
        private static string expectedScene;
        private static long generation;
        private static bool applied;
        private static float beganAt;
        private static bool claimed;

        private static void Prefix(GameSave __0, out bool __state)
        {
            Plugin.Instance?.Panel?.ClearSelection();
            DuelTools.Dialogue.Clear();
            DiceAdapter.Close();
            HintAdapter.Clear();
            // Only a numbered manual load is managed by this mod. Auto saves and
            // the game's own prologue restore also call ExecuteLoadGameData, and
            // claiming the recovery lock for them would never be released because
            // no matching numbered scene load follows.
            bool manual = AnytimeNative.Numbered(AnytimeNative.ManualLoadSlot);
            __state = manual && !StoryReplay.LoadingBaseline && Plugin.Instance != null;
            if (!__state) return;
            StoryReplay.Stop("正在使用原生存档替代实验恢复。");
            if (StoryRecovery.Recording) StoryRecovery.Stop();
            // A second load supersedes any outstanding scene coroutine.
            if (Plugin.Instance.Gate.Phase == Core.RecoveryPhase.NativeLoading)
                Plugin.Instance.Gate.Fail("原生读档已被另一次读档替代。");
            Plugin.Instance.Gate.NativeLoadStarted();
            generation++;
            expectedScene = __0?.CurrentScene;
            applied = false;
            beganAt = Time.unscaledTime;
            claimed = true;
        }

        // Returning here only proves that managers accepted the save data.
        private static void Postfix(bool __state)
        {
            if (__state) applied = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state && __exception != null) Fail(generation, "原生存档数据加载失败。");
            return __exception;
        }

        // A numbered load whose scene never arrives would otherwise hold the
        // recovery lock in NativeLoading forever. The lock is not "released" here:
        // an unfinished load cannot be certified, so the gate moves to Failed and
        // only a load that actually completes may return it to Idle.
        internal static void CheckStalled()
        {
            if (!claimed || Plugin.Instance == null) { claimed = false; return; }
            if (!Plugin.Instance.Gate.AwaitingNativeLoad) { claimed = false; return; }
            var controller = SceneController.Instance;
            bool loading = controller != null && (controller.IsLoading || controller.IsPrepare);
            // A controller that claims to be loading forever would otherwise never be caught:
            // the old check returned early on every frame while IsLoading was true.
            if (!FailureExit.LoadStalled(loading, Time.unscaledTime - beganAt)) return;
            claimed = false;
            string reason = "原生存档场景加载在 " + (int)StallSeconds +
                " 秒内没有完成，当前游戏状态无法确认。操作保持锁定；请重新读取一个有效的原生存档来解除锁定。";
            Plugin.Instance.Gate.NativeLoadAborted(reason);
            // Stop the replays that are waiting for a scene that is not coming, so their own
            // two-minute watchdogs cannot hold input after the gate already gave up.
            AnytimeStory.NativeLoadAborted(reason);
            AnytimeDuel.NativeLoadAborted(reason);
            if (Plugin.Instance.Panel != null) Plugin.Instance.Panel.Visible = true;
            Plugin.Instance.Status = reason;
            try { Plugin.Instance.Log("[Anytime] native load stalled; gate=failed (lock retained)"); }
            catch (Exception) { }
        }

        internal static IEnumerator ObserveScene(IEnumerator native, string scene)
        {
            long ticket = generation;
            bool matching = applied && !string.IsNullOrEmpty(expectedScene) && scene == expectedScene;
            bool completed = false;
            try
            {
                while (native.MoveNext()) yield return native.Current;
                completed = true;
            }
            finally
            {
                (native as IDisposable)?.Dispose();
                if (matching && !completed) Fail(ticket, "原生场景加载未完成；恢复锁保持关闭。");
            }
            if (!matching) yield break;
            // StartLoadScene sets the active scene at its final MoveNext. Allow
            // the new scene's Start callbacks to settle before permitting edits.
            yield return null;
            if (ticket != generation || !applied) yield break;
            if (SceneManager.GetActiveScene().name != expectedScene)
            {
                Fail(ticket, "原生读档场景与存档不一致。");
                yield break;
            }
            applied = false;
            expectedScene = null;
            claimed = false;
            if (AnytimeStory.Failed || AnytimeDuel.Failed)
            {
                Plugin.Instance?.Gate.Fail("中途档恢复未通过校验；请重新读取原生存档。");
                yield break;
            }
            if (AnytimeStory.Replaying)
            {
                AnytimeStory.AfterNativeLoadScene();
                yield break;
            }
            if (AnytimeDuel.Replaying)
            {
                AnytimeDuel.AfterNativeLoadScene();
                yield break;
            }
            StoryReplay.OnNativeLoadCompleted();
        }

        private static void Fail(long ticket, string reason)
        {
            if (ticket != generation) return;
            applied = false;
            expectedScene = null;
            Plugin.Instance?.Gate.Fail(reason);
        }
    }

    [HarmonyPatch(typeof(SceneController), "StartLoadScene")]
    internal static class NativeSceneLoadBoundary
    {
        private static void Postfix(string __0, ref IEnumerator __result)
        {
            __result = NativeLoadBoundary.ObserveScene(__result, __0);
        }
    }
}
