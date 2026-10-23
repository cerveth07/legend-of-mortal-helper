using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using Mortal.Story;
using Mortal.Core;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace MortalHelper.Game
{
    // Observe native coroutine boundaries; never reconstruct an animation or
    // change the iterator program counter.
    internal static class AnytimeDice
    {
        private static DiceMenuDialog dialog;
        private static string identity;
        private static bool timer, holdRestoredStart, awaitingRaw;
        private static int stage, generation;
        internal static bool Submitting { get; private set; }
        internal static bool Active => dialog != null && dialog.isActiveAndEnabled;
        internal static bool WaitingForStart => Active && stage == 1 && (AnytimeStory.Replaying || holdRestoredStart);
        private static T Field<T>(string name) => GameAccess.Field<T>(dialog, name);
        internal static string SaveKey
        {
            get
            {
                if (!Active || stage == 0 || AnytimeStory.Failed) return null;
                if (stage == 1 && Field<bool>("_pressStartButton")) return null;
                if (stage != 1 && (Field<bool>("_triggerNextInput") || Field<bool>("_pressRestartButton"))) return null;
                var text = new StringBuilder(identity);
                text.Append('|').Append(stage == 1 ? 1 : 2);
                foreach (string name in new[] { "_currentRandomValue", "_resultCount", "_resultSelection",
                    "_currentDiceRangeIndex", "_defaultDiceMax", "_diceMin", "_diceMax" })
                    text.Append('|').Append(Field<int>(name).ToString(CultureInfo.InvariantCulture));
                text.Append('|').Append(Field<bool>("_useFatePoint") ? '1' : '0');
                return text.ToString();
            }
        }
        internal static void OpenSaveMenu()
        {
            if (!AnytimeNative.CanOffer) { AnytimeNative.SaveFailed(AnytimeNative.Availability); return; }
            try
            {
                foreach (var menu in Resources.FindObjectsOfTypeAll<MenuPanel>())
                    if (menu.gameObject.scene == SceneManager.GetActiveScene())
                    {
                        var save = GameAccess.Field<CommonPanel>(menu, "_savePanel");
                        if (save == null) continue;
                        menu.GetComponent<CommonPanel>().Show(true);
                        save.Show(true);
                        return;
                    }
                throw new InvalidOperationException("当前场景没有可用的存档菜单。");
            }
            catch (Exception error) { AnytimeNative.SaveFailed("无法打开骰子存档：" + error.Message); }
        }
        internal static void Reset()
        { generation++; dialog = null; identity = null; stage = 0; Submitting = false; holdRestoredStart = false; awaitingRaw = false; }
        private static string Identity(string kind, string check, string[] options, int destiny, float duration)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write(kind); writer.Write(check ?? ""); writer.Write(destiny); writer.Write(duration);
                    writer.Write(options == null ? -1 : options.Length);
                    if (options != null) foreach (string option in options) writer.Write(option ?? "");
                }
                using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(stream.ToArray()));
            }
        }
        internal static IEnumerator Observe(DiceMenuDialog owner, IEnumerator original, bool isTimer,
            string[] options, int destiny, string check, float duration)
        {
            // Build the wrapper before publishing dialog/identity: its constructor
            // validates the iterator, and a throw must leave the plugin believing no
            // wheel is registered rather than leaving half-set state plus no observer.
            Reset();
            var observed = new Observed(original, generation, isTimer);
            dialog = owner; timer = isTimer;
            identity = Identity(isTimer ? "Timer" : "Roll", check, options, destiny, duration);
            return observed;
        }
        private sealed class Observed : IEnumerator, IDisposable
        {
            private readonly IEnumerator original;
            private readonly FieldInfo state;
            private readonly int owner;
            private readonly bool isTimer;
            internal Observed(IEnumerator original, int owner, bool isTimer)
            {
                this.original = original; this.owner = owner; this.isTimer = isTimer;
                state = AccessTools.Field(original.GetType(), "<>1__state");
                if (state == null) throw new InvalidDataException("轮盘协程没有可识别的等待状态。");
            }
            public object Current => original.Current;
            public void Reset() { throw new NotSupportedException(); }
            public bool MoveNext()
            {
                if (owner != generation) return false;
                stage = 0;
                try
                {
                    bool next = original.MoveNext();
                    if (!next) { Dispose(); return false; }
                    int current = (int)state.GetValue(original);
                    Plugin.Instance?.Log("[AnytimeDice] native state=" + current + " yield=" +
                        (original.Current == null ? "null" : original.Current.GetType().Name));
                    if (original.Current is WaitUntil && (current == 1 || current == 3 || (!isTimer && current == 5)))
                        stage = current;
                    return true;
                }
                catch (Exception error)
                {
                    AnytimeStory.FailDice("轮盘执行或恢复失败：" + error.Message);
                    Dispose(); throw;
                }
            }
            public void Dispose()
            {
                (original as IDisposable)?.Dispose();
                if (owner == generation) AnytimeDice.Reset();
            }
        }
        internal static void RollStarting(DiceMenuDialog owner)
        {
            if (!Active || owner != dialog) return;
            stage = 0; awaitingRaw = true;
            AnytimeStory.RecordDiceRoll(Field<int>("_diceMin") + "|" + Field<int>("_diceMax"),
                Field<bool>("_useFatePoint") ? 1 : 0);
        }
        internal static void InjectRollRaw(int value)
        {
            if (!Active || !awaitingRaw) return;
            awaitingRaw = false;
            GameAccess.Set(dialog, "_currentRandomValue", value);
        }
        internal static void Tick()
        {
            if (!AnytimeStory.Replaying || SaveKey == null) return;
            try
            {
                AnytimeStory.ReplayDiceNumericEdits();
                string key; int paid;
                if (!timer && AnytimeStory.TryPeekDiceRoll(out key, out paid))
                {
                    SetRange(key);
                    Submitting = true;
                    try
                    {
                        if (paid != (Field<bool>("_useFatePoint") ? 1 : 0))
                        {
                            if (stage == 1 || paid != 1) throw new InvalidDataException("轮盘命运消耗记录不一致。");
                            AccessTools.Method(typeof(DiceMenuDialog), "UseFatePoint").Invoke(dialog, null);
                            if (!Field<bool>("_useFatePoint")) throw new InvalidDataException("无法恢复轮盘命运消耗。");
                        }
                        if (stage == 1) dialog.PressStartButton();
                        else if (stage == 3) dialog.PressRestartButton();
                        else throw new InvalidDataException("当前轮盘没有重掷入口。");
                    }
                    finally { Submitting = false; }
                    return;
                }
                if (AnytimeStory.ReachDice(SaveKey)) return;
                Submitting = true;
                try
                {
                    if (stage == 1)
                    {
                        if (!timer) throw new InvalidDataException("轮盘重放缺少掷骰记录。");
                        dialog.PressStartButton();
                    }
                    else dialog.PressContinueButton();
                }
                finally { Submitting = false; }
            }
            catch (Exception error) { AnytimeStory.FailDice("轮盘恢复失败：" + error.GetBaseException().Message); }
        }
        // Range selection is a native UI change without a roll. A save can have
        // a different range from the last recorded roll.
        internal static void RestoreTargetRange(string target)
        {
            if (target == null || SaveKey == null) return;
            string[] parts = target.Split('|');
            if (parts.Length != 10 || parts[0] != identity || parts[1] != (stage == 1 ? "1" : "2")) return;
            SetRange(parts[7] + "|" + parts[8]);
            if (stage == 1) holdRestoredStart = true;
        }
        private static void SetRange(string key)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (Field<int>("_diceMin") + "|" + Field<int>("_diceMax") == key) return;
                AccessTools.Method(typeof(DiceMenuDialog), "ChangeDiceRange").Invoke(dialog, null);
            }
            throw new InvalidDataException("轮盘点数范围与记录不一致。");
        }
    }
    [HarmonyPatch(typeof(DiceMenuDialog), "ExecuteRoll")]
    internal static class AnytimeDiceExecutePatch
    {
        private static void Postfix(DiceMenuDialog __instance, string[] options, int destinyPoint, string checkName, ref IEnumerator __result)
        {
            // Harmony never catches patch exceptions: a throw here would escape into the
            // game's coroutine factory and stop the wheel from being created at all.
            try { __result = AnytimeDice.Observe(__instance, __result, false, options, destinyPoint, checkName, 0); }
            catch (Exception error) { Plugin.Instance?.Report(error); }
        }
    }
    [HarmonyPatch(typeof(DiceMenuDialog), "ShowTimer")]
    internal static class AnytimeDiceTimerPatch
    {
        private static void Postfix(DiceMenuDialog __instance, float duration, ref IEnumerator __result)
        {
            try
            {
                __result = AnytimeDice.Observe(__instance, __result, true, null, 0,
                    GameAccess.Field<string>(__instance, "_header"), duration);
            }
            catch (Exception error) { Plugin.Instance?.Report(error); }
        }
    }
    [HarmonyPatch(typeof(DiceMenuDialog), "Roll")]
    internal static class AnytimeDiceRollPatch
    {
        private static void Prefix(DiceMenuDialog __instance)
        {
            try { AnytimeDice.RollStarting(__instance); }
            catch (Exception error) { Plugin.Instance?.Report(error); }
        }
    }
}
