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
        private static bool replaySubmitting;
        // A patch body that throws runs inside native game code; reporting the same
        // failure from a per-frame poll would flood the log, so latch it.
        private static bool patchFailureReported;
        // The dice controls live in the game UI: the widget is always shown while a
        // wheel is open and clicking an option always picks it. No modes to switch.
        internal static bool Enabled => Plugin.Active;
        // Set only while a recorded roll is being injected; Commit reads it instead
        // of the configured point.
        internal static int? ReplayPoint;
        internal static bool Ready => Enabled && Dialog != null && Dialog.isActiveAndEnabled && Session.Phase == RollPhase.Input;

        // Harmony never catches patch exceptions, so one bad field name would turn a
        // per-frame patch into a per-frame exception inside native input polling.
        internal static void ReportPatchFailure(Exception error)
        {
            if (patchFailureReported) return;
            patchFailureReported = true;
            try { Plugin.Instance?.Report(error); } catch (Exception) { }
        }

        internal static void Open(DiceMenuDialog dialog)
        {
            Close();
            Dialog = dialog;
            Session.Open();
            // StoryReplay observes ready dialogs from Update; OnEnable may occur
            // before the game has assigned the script and opened all controls.
        }

        internal static void Close()
        {
            if (widget != null) UnityEngine.Object.Destroy(widget.gameObject);
            widget = null; Dialog = null; queuedOption = null; Session.Close();
        }

        internal static void Tick()
        {
            if (Dialog == null || !Dialog.isActiveAndEnabled) return;
            if (!Enabled)
            {
                if (widget != null) { UnityEngine.Object.Destroy(widget.gameObject); widget = null; }
                queuedOption = null;
                return;
            }
            if (widget == null)
            {
                Text reference = GameAccess.Field<Text>(Dialog, "_maxDiceText");
                if (reference == null) return;
                var root = new GameObject("MortalHelper.Dice", typeof(RectTransform));
                root.transform.SetParent(reference.transform.parent, false);
                widget = root.AddComponent<DiceWidget>();
                widget.Initialize(reference);
                // Keep the existing world placement, but put controls above the
                // native result panel's transparent raycast surface.
                root.transform.SetParent(Dialog.transform, true);
                widget.RestorePosition();
            }
            widget.transform.SetAsLastSibling();
            widget.gameObject.SetActive(true);
            Session.Rearm(CanReroll());
            BindChoices();
            widget.Refresh();
            InjectReplayPoint();
        }

        // A pending roll must remain pending until CheckPointManager.Dice actually
        // returns the requested raw value. PressStartButton may be rejected by
        // another prefix or defer its result; a button call alone is not success.
        private static void InjectReplayPoint()
        {
            if (!StoryReplay.Active || !StoryReplay.PendingDice.HasValue || !Ready) return;
            ReplayPoint = StoryReplay.PendingDice.Value;
            replaySubmitting = true;
            try
            {
                Dialog.PressStartButton();
                if (Session.Phase == RollPhase.Input)
                    StoryReplay.Stop("原生骰子提交未被接受，重放已中止。");
            }
            catch (System.Exception error)
            {
                StoryReplay.Stop("骰子注入失败：" + error.Message);
                Plugin.Instance.Report(error);
            }
            finally { replaySubmitting = false; ReplayPoint = null; }
        }

        private static void BindChoices()
        {
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
            int? point;
            // When a tape is being injected, the user's saved BasePoint is not
            // authoritative (it may be stale or outside this roll's legal range).
            string pointValue = StoryReplay.PendingDice.HasValue
                ? StoryReplay.PendingDice.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Plugin.Instance.Options.Point.Value;
            return queuedOption.HasValue || Session.RequestedOption.HasValue ||
                InteractionSession.ParsePoint(pointValue,
                    GameAccess.Field<int>(Dialog, "_diceMin"),
                    GameAccess.Field<int>(Dialog, "_diceMax"), out point);
        }

        internal static void Choose(ChoicePointer pointer)
        {
            if (!Ready || StoryReplay.Active || !Plugin.Instance.Gate.CanInteract ||
                pointer.Owner != Dialog || pointer.Generation != Session.Generation) return;
            Button[] buttons = Dialog.CachedButtons;
            if (buttons == null) return;
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
            if (AnytimeDice.Submitting) return true;
            if (AnytimeStory.Replaying) return false;
            if (!Enabled || Dialog == null) return true;
            if (!Ready || (StoryReplay.Active && !replaySubmitting) ||
                (!StoryReplay.Active && !Plugin.Instance.Gate.CanInteract) ||
                (!nativeInvocation && !replaySubmitting &&
                (GameAccess.Typing || Plugin.Instance.Panel.Visible))) return false;
            string error;
            // Culture-independent: ParsePoint reads with InvariantCulture, so a locale
            // with a non-ASCII negative sign must not be able to round-trip a point.
            string pointValue = ReplayPoint.HasValue
                ? ReplayPoint.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : Plugin.Instance.Options.Point.Value;
            // CachedButtons is legitimately null before the dialog has bound its
            // choices (BindChoices already guards it); a throw here aborts the
            // native submit path.
            Button[] cached = Dialog.CachedButtons;
            bool ok = Session.Commit(Session.Generation, pointValue,
                GameAccess.Field<int>(Dialog, "_diceMin"), GameAccess.Field<int>(Dialog, "_diceMax"),
                queuedOption, cached == null ? 0 : cached.Length, out error);
            if (!ok) Plugin.Instance.Status = error;
            return ok;
        }

        internal static bool AllowShortcut(bool reroll)
        {
            if (AnytimeStory.Replaying) return false;
            if (!Enabled || Dialog == null) return true;
            return Ready && Plugin.Instance.Gate.CanInteract && ValidInput() &&
                (nativeInvocation || (!GameAccess.Typing && !Plugin.Instance.Panel.Visible)) &&
                (!reroll || CanReroll());
        }

        internal static void BeforeRoll(ref int random)
        {
            if (AnytimeStory.Replaying) return;
            if (!Enabled || Dialog == null) return;
            // Covers the native automatic-dice route without depending on a click event.
            if (Session.Phase == RollPhase.Input && !Commit()) return;
            if (Session.Phase != RollPhase.Committed) return;
            bool authorizedReplay = StoryReplay.Active && StoryReplay.PendingDice.HasValue &&
                Session.RequestedPoint.HasValue &&
                Session.RequestedPoint.Value == StoryReplay.PendingDice.Value &&
                !Session.RequestedOption.HasValue;
            if (Session.HasOverride && !authorizedReplay && !Plugin.Instance.Gate.CanInteract)
            {
                StoryReplay.Stop("恢复锁定中收到非预期的掷骰修改请求。");
                return;
            }
            if (Session.RequestedPoint.HasValue)
            {
                random = Session.RequestedPoint.Value;
                GameAccess.Set(Dialog, "_currentRandomValue", random);
            }
            if (Session.HasOverride && !authorizedReplay)
                Plugin.Instance.Gate.NotifyMutation("判定结果被修改");
        }

        internal static void AfterRoll(DiceCheckResult result)
        {
            if (!AnytimeStory.Replaying && Enabled && Session.Phase == RollPhase.Committed && result != null && Session.RequestedOption.HasValue)
                result.Result = Session.RequestedOption.Value;
            // Replay injects the raw roll, so the tape must store Random, not the
            // bonus-adjusted Result.
            if (StoryReplay.PendingDice.HasValue)
            {
                if (result == null) StoryReplay.Stop("未收到有效的原生骰子结果。");
                else StoryReplay.ConfirmDice(result.Random);
            }
            if (result != null) StoryRecovery.NoteDice(result.Random);
            Session.Resolved();
        }
    }

    // The game already owns the selection look: DiceOption.Select() swaps to the
    // highlighted sprite, shows the arrow, switches text and plays the button sound.
    // Reuse it instead of inventing a tint, so picking an option feels native.
    public sealed class ChoicePointer : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        internal DiceMenuDialog Owner;
        internal long Generation;
        private DiceOption option;
        private bool choosing;

        private void Awake() { option = GetComponent<DiceOption>(); }

        public void OnPointerEnter(PointerEventData data)
        {
            if (!DiceAdapter.Ready || StoryReplay.Active || !Plugin.Instance.Gate.CanInteract) return;
            Natively(true);
        }

        public void OnPointerExit(PointerEventData data)
        {
            if (choosing) return;
            Natively(false);
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data.button != PointerEventData.InputButton.Left || choosing || !DiceAdapter.Ready ||
                StoryReplay.Active || !Plugin.Instance.Gate.CanInteract) return;
            choosing = true;
            Natively(true);
            StartCoroutine(Pick());
        }

        private System.Collections.IEnumerator Pick()
        {
            yield return new WaitForSecondsRealtime(.1f);
            if (!DiceAdapter.Ready || StoryReplay.Active || !Plugin.Instance.Gate.CanInteract)
            { choosing = false; Natively(false); yield break; }
            DiceAdapter.Choose(this);
            yield return new WaitForSecondsRealtime(.3f);
            Natively(false);
            choosing = false;
        }

        private void Natively(bool selected)
        {
            if (option == null) option = GetComponent<DiceOption>();
            if (option == null) return;
            try { if (selected) option.Select(); else option.UnSelect(); }
            catch (Exception) { }
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
            if ((!DiceAdapter.Enabled && !AnytimeStory.Replaying) || StoryReplay.Active) return true;
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
        private static bool Prefix()
        {
            // A prefix that throws aborts the native press; falling through to the
            // native behaviour is the safe answer when the mod cannot decide.
            try { return DiceAdapter.Commit(); }
            catch (Exception error) { DiceAdapter.ReportPatchFailure(error); return true; }
        }
    }

    [HarmonyPatch]
    internal static class DiceCostGuardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(DiceMenuDialog), "RestartButtonClick");
            yield return AccessTools.Method(typeof(DiceMenuDialog), "DestinyRestartAction");
        }
        private static bool Prefix()
        {
            try { return DiceAdapter.AllowShortcut(true); }
            catch (Exception error) { DiceAdapter.ReportPatchFailure(error); return true; }
        }
    }

    [HarmonyPatch]
    internal static class DiceKeyboardPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "NextInputAction", "DestinyAction", "RangeAction" })
                yield return AccessTools.Method(typeof(DiceMenuDialog), name);
        }
        private static bool Prefix() { return !AnytimeStory.Replaying && (!DiceAdapter.Enabled || (!GameAccess.Typing && !Plugin.Instance.Panel.Visible)); }
    }

    [HarmonyPatch(typeof(DiceMenuDialog), "GetPressInput")]
    internal static class DiceWaitPatch
    {
        private static void Postfix(DiceMenuDialog __instance, ref bool __result)
        {
            // Runs every frame while a wheel is open: nothing may escape from here.
            try
            {
                if (AnytimeDice.WaitingForStart)
                {
                    __result = GameAccess.Field<bool>(__instance, "_pressStartButton");
                    MenuToggleButton start = GameAccess.Field<MenuToggleButton>(__instance, "_startButton");
                    if (start != null && start.gameObject != null) start.gameObject.SetActive(true);
                }
                if (!AnytimeStory.Replaying && DiceAdapter.Enabled && __instance == DiceAdapter.Dialog)
                    __result = !GameAccess.Typing && DiceAdapter.ValidInput() && __result;
            }
            catch (Exception error) { DiceAdapter.ReportPatchFailure(error); }
        }
    }

    [HarmonyPatch(typeof(CheckPointManager), "Dice")]
    internal static class DiceResultPatch
    {
        private static void Prefix(ref int __1) { DiceAdapter.BeforeRoll(ref __1); }
        private static void Postfix(DiceCheckResult __result) { DiceAdapter.AfterRoll(__result); }
    }
}
