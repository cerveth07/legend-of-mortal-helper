using System;
using System.Collections;
using HarmonyLib;
using Mortal.Core;
using UnityEngine.SceneManagement;

namespace MortalHelper.Game
{
    [HarmonyPatch(typeof(SaveSystem), "ExecuteLoadGameData")]
    internal static class NativeLoadBoundary
    {
        private static string expectedScene;
        private static long generation;
        private static bool applied;

        private static void Prefix(GameSave __0, out bool __state)
        {
            __state = !StoryReplay.LoadingBaseline && Plugin.Instance != null;
            Plugin.Instance?.Panel?.ClearSelection();
            DuelTools.Dialogue.Clear();
            DiceAdapter.Close();
            HintAdapter.Clear();
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
