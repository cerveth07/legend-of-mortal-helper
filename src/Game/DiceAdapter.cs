using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    internal static class DiceAdapter
    {
        internal static readonly InteractionSession Session = new InteractionSession();
        internal static DiceMenuDialog Dialog;
        private static DiceWidget widget;
        private static int? queuedOption;
        private static bool nativeInvocation;
        // Mode: 0 自然, 1 指定点数, 2 直接选项. Only modes 1 and 2 touch the roll.
        internal static bool Enabled => Plugin.Active && (Plugin.Instance.Options.Mode.Value != 0 || StoryReplay.Active);
        // Set only while a recorded roll is being injected; Commit reads it instead
        // of the configured point.
        internal static int? ReplayPoint;
        internal static bool PointMode => Plugin.Instance != null && Plugin.Instance.Options.Mode.Value == 1;
        internal static bool OptionMode => Plugin.Instance != null && Plugin.Instance.Options.Mode.Value == 2;
        internal static bool Ready => Enabled && Dialog != null && Dialog.isActiveAndEnabled && Session.Phase == RollPhase.Input;

        internal static void Open(DiceMenuDialog dialog)
        {
            Close();
            Dialog = dialog;
            Session.Open();
            if (StoryReplay.Active) StoryReplay.TakeDice();
        }

        internal static void Close()
        {
            if (widget != null) UnityEngine.Object.Destroy(widget.gameObject);
            widget = null; Dialog = null; queuedOption = null; Session.Close();
        }

        internal static void Tick()
        {
            if (Dialog == null || !Dialog.isActiveAndEnabled) return;
            if (!Enabled || !PointMode)
            {
                if (widget != null) { UnityEngine.Object.Destroy(widget.gameObject); widget = null; }
                if (!OptionMode) queuedOption = null;
                if (!Enabled) return;
            }
            if (!PointMode)
            {
                BindChoices();
                return;
            }
            if (widget == null)
            {
                Text reference = GameAccess.Field<Text>(Dialog, "_maxDiceText");
                var root = new GameObject("MortalHelper.Dice", typeof(RectTransform));
                root.transform.SetParent(reference.transform.parent, false);
                widget = root.AddComponent<DiceWidget>();
                widget.Initialize(reference);
            }
            widget.gameObject.SetActive(true);
            Session.Rearm(CanReroll());
            BindChoices();
            widget.Refresh();
            InjectReplayPoint();
        }

        // During playback the recorded roll is committed and the native start is
        // pressed by the game itself, exactly like a normal submit.
        private static void InjectReplayPoint()
        {
            if (!StoryReplay.Active || !StoryReplay.PendingDice.HasValue || !Ready) return;
            ReplayPoint = StoryReplay.PendingDice.Value;
            try { Dialog.PressStartButton(); }
            catch (System.Exception error) { Plugin.Instance.Report(error); }
            finally { ReplayPoint = null; StoryReplay.AfterDice(); }
        }

        private static void BindChoices()
        {
            if (!OptionMode) return;
            foreach (Button button in Dialog.CachedButtons ?? new Button[0])
            {
                if (button == null || button.GetComponent<DiceOption>() == null) continue;
                var click = button.GetComponent<ChoicePointer>() ?? button.gameObject.AddComponent<ChoicePointer>();
                click.Owner = Dialog; click.Generation = Session.Generation;
            }
        }

        internal static bool CanReroll()
        {
            if (Dialog == null) return false;
            var button = GameAccess.Field<MenuToggleButton>(Dialog, "_restartButton");
            var panel = GameAccess.Field<GameObject>(Dialog, "_continuePanel");
            return button != null && button.Active && button.gameObject.activeInHierarchy && panel != null &&
                panel.activeInHierarchy && !GameAccess.Field<bool>(Dialog, "_pressRestartButton");
        }

        internal static bool ValidInput()
        {
            if (!Enabled || Dialog == null) return true;
            if (!PointMode) return true;
            int? point;
            return queuedOption.HasValue || Session.RequestedOption.HasValue || InteractionSession.ParsePoint(Plugin.Instance.Options.Point.Value,
                GameAccess.Field<int>(Dialog, "_diceMin"), GameAccess.Field<int>(Dialog, "_diceMax"), out point);
        }

        internal static void Choose(ChoicePointer pointer)
        {
            if (!OptionMode || !Ready || pointer.Owner != Dialog || pointer.Generation != Session.Generation) return;
            Button[] buttons = Dialog.CachedButtons;
            int index = Array.FindIndex(buttons, b => b != null && b.gameObject == pointer.gameObject);
            if (index < 0 || !pointer.gameObject.activeInHierarchy) return;
            queuedOption = index + 1;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            nativeInvocation = true;
            try
            {
                if (CanReroll())
                {
                    var click = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                    AccessTools.Method(typeof(DiceMenuDialog), "RestartButtonClick").Invoke(Dialog, new object[] { click });
                }
                else Dialog.PressStartButton();
            }
            finally { queuedOption = null; nativeInvocation = false; }
        }

        internal static bool Commit()
        {
            if (!Enabled || Dialog == null) return true;
            if (!Ready || (!nativeInvocation && (GameAccess.Typing || Plugin.Instance.Panel.Visible))) return false;
            string error;
            string pointValue = ReplayPoint.HasValue ? ReplayPoint.Value.ToString() : (PointMode ? Plugin.Instance.Options.Point.Value : "");
            bool ok = Session.Commit(Session.Generation, pointValue,
                GameAccess.Field<int>(Dialog, "_diceMin"), GameAccess.Field<int>(Dialog, "_diceMax"),
                queuedOption, Dialog.CachedButtons.Length, out error);
            if (!ok) Plugin.Instance.Status = error;
            return ok;
        }

        internal static bool AllowShortcut(bool reroll)
        {
            if (!Enabled || Dialog == null) return true;
            return Ready && ValidInput() && (nativeInvocation || (!GameAccess.Typing && !Plugin.Instance.Panel.Visible)) && (!reroll || CanReroll());
        }

        internal static void BeforeRoll(ref int random)
        {
            if (!Enabled || Dialog == null) return;
            // Covers the native automatic-dice route without depending on a click event.
            if (Session.Phase == RollPhase.Input && !Commit()) return;
            if (Session.Phase != RollPhase.Committed) return;
            if (Session.RequestedPoint.HasValue)
            {
                random = Session.RequestedPoint.Value;
                GameAccess.Set(Dialog, "_currentRandomValue", random);
            }
            if (Session.HasOverride) Plugin.Instance.Gate.NotifyMutation("判定结果被修改");
        }

        internal static void AfterRoll(DiceCheckResult result)
        {
            if (Enabled && Session.Phase == RollPhase.Committed && result != null && Session.RequestedOption.HasValue)
                result.Result = Session.RequestedOption.Value;
            // Replay injects the raw roll, so the tape must store Random, not the
            // bonus-adjusted Result.
            if (result != null) StoryRecovery.NoteDice(result.Random);
            Session.Resolved();
        }
    }

    public sealed class ChoicePointer : MonoBehaviour, IPointerClickHandler
    {
        internal DiceMenuDialog Owner;
        internal long Generation;
        public void OnPointerClick(PointerEventData data)
        {
            if (data.button == PointerEventData.InputButton.Left) DiceAdapter.Choose(this);
        }
    }

    [HarmonyPatch(typeof(DiceMenuDialog), "OnEnable")]
    internal static class OpenDicePatch
    {
        private static void Postfix(DiceMenuDialog __instance) { DiceAdapter.Open(__instance); }
    }

    // The native gate only offers the reroll/range stage while 命運 > 0, which also
    // blocked direct choice at zero fate. Keep the stage available outside replay.
    [HarmonyPatch(typeof(DiceMenuDialog), "CheckRevolution")]
    internal static class DiceRerollStagePatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!DiceAdapter.Enabled || StoryReplay.Active) return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(DiceMenuDialog), "OnDisable")]
    internal static class CloseDicePatch
    {
        private static void Postfix() { DiceAdapter.Close(); }
    }

    [HarmonyPatch]
    internal static class DiceSubmitPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DiceMenuDialog), "PressStartButton");
            yield return AccessTools.Method(typeof(DiceMenuDialog), "PressRestartButton");
        }
        private static bool Prefix() { return DiceAdapter.Commit(); }
    }

    [HarmonyPatch]
    internal static class DiceCostGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DiceMenuDialog), "RestartButtonClick");
            yield return AccessTools.Method(typeof(DiceMenuDialog), "DestinyRestartAction");
        }
        private static bool Prefix() { return DiceAdapter.AllowShortcut(true); }
    }

    [HarmonyPatch]
    internal static class DiceKeyboardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "NextInputAction", "DestinyAction", "RangeAction" })
                yield return AccessTools.Method(typeof(DiceMenuDialog), name);
        }
        private static bool Prefix() { return !DiceAdapter.Enabled || (!GameAccess.Typing && !Plugin.Instance.Panel.Visible); }
    }

    [HarmonyPatch(typeof(DiceMenuDialog), "GetPressInput")]
    internal static class DiceWaitPatch
    {
        private static void Postfix(DiceMenuDialog __instance, ref bool __result)
        {
            if (DiceAdapter.Enabled && __instance == DiceAdapter.Dialog)
                __result = !GameAccess.Typing && DiceAdapter.ValidInput() && GameAccess.Field<bool>(__instance, "_pressStartButton");
        }
    }

    [HarmonyPatch(typeof(CheckPointManager), "Dice")]
    internal static class DiceResultPatch
    {
        private static void Prefix(ref int __1) { DiceAdapter.BeforeRoll(ref __1); }
        private static void Postfix(DiceCheckResult __result) { DiceAdapter.AfterRoll(__result); }
    }
}
