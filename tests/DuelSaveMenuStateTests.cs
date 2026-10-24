using System;
using MortalHelper.Game;

internal static class DuelSaveMenuStateTests
{
    private static int checks;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
    private static void Reject(Action run, string message)
    {
        try { run(); }
        catch (Exception e) when (e is InvalidOperationException || e is ArgumentException) { checks++; return; }
        throw new Exception(message);
    }
    public static int Run()
    {
        var state = new DuelSaveMenuState();
        Check(!state.Active && state.AllowNativeLoad && state.PendingSlot == null, "normal slot list starts in load mode");
        Reject(() => state.Select("001", false), "click without explicit save mode accepted");
        Reject(() => state.Confirm(), "confirmation before save mode accepted");
        state.Begin();
        Check(state.Active && !state.AllowNativeLoad && state.Phase == DuelSaveMenuPhase.Selecting, "save mode does not block native load");
        foreach (string slot in new[] { "auto", "000", "1", "../", "1a1", null })
            Reject(() => state.Select(slot, false), "non-numbered slot accepted: " + slot);
        Check(state.PendingSlot == null && state.Phase == DuelSaveMenuPhase.Selecting, "invalid slot changed save target");
        Check(!state.Select("003", true), "occupied slot saved before overwrite confirmation");
        Check(state.Active && !state.AllowNativeLoad && state.PendingSlot == "003" && state.Phase == DuelSaveMenuPhase.Confirming,
            "overwrite dialog lost selected slot or unblocked native load");
        Reject(() => state.Select("004", false), "second click changed pending overwrite target");
        Check(state.Confirm() == "003" && state.Active && !state.AllowNativeLoad && state.Phase == DuelSaveMenuPhase.Saving,
            "confirmed target changed or native load unlocked during save");
        Reject(() => state.Confirm(), "double confirmation accepted");
        state.Close();
        Check(!state.Active && state.AllowNativeLoad && state.PendingSlot == null, "successful close did not restore load mode");
        state.Begin(); state.Select("004", true); state.Close();
        Check(!state.Active && state.AllowNativeLoad && state.PendingSlot == null, "cancel retained overwrite target");
        Reject(() => state.Confirm(), "stale confirmation after cancel accepted");
        state.Begin();
        Check(state.Select("012", false) && state.PendingSlot == "012" && state.Active && !state.AllowNativeLoad,
            "empty numbered slot did not go straight to save while blocking load");
        state.Close(); state.Close();
        Check(!state.Active && state.Phase == DuelSaveMenuPhase.Closed, "repeated exception/scene close not idempotent");
        state.Begin(); state.Select("005", true); state.Begin();
        Check(state.PendingSlot == null && state.Phase == DuelSaveMenuPhase.Selecting, "reopened save mode retained stale confirmation");
        state.Close();
        Check(DuelSaveMenuState.NeedsOverwriteConfirmation(false, true, false), "damaged but existing native file did not require overwrite confirmation");
        Check(DuelSaveMenuState.NeedsOverwriteConfirmation(false, false, true), "unresolved transaction did not require confirmation");
        Check(!DuelSaveMenuState.NeedsOverwriteConfirmation(false, false, false), "truly empty slot required confirmation");
        state.Begin(); state.Select("006", true);
        Check(state.ConfirmationRoute(true, "006") == DuelConfirmationRoute.Save, "save confirmation routed to native delete");
        state.Confirm();
        Check(state.ConfirmationRoute(true, "006") == DuelConfirmationRoute.Ignore, "reentrant confirmation while saving was not ignored");
        state.Close();
        Check(state.ConfirmationRoute(false, "") == DuelConfirmationRoute.Ignore, "second confirmation after close routed to native delete");
        Check(state.ConfirmationRoute(false, "007") == DuelConfirmationRoute.NativeDelete, "normal deliberate native deletion was blocked");
        Console.WriteLine("PASS: " + checks + " duel save menu state checks");
        return checks;
    }
}
