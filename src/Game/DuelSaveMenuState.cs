using System;

namespace MortalHelper.Game
{
    internal enum DuelSaveMenuPhase { Closed, Selecting, Confirming, Saving }
    internal enum DuelConfirmationRoute { Ignore, Save, NativeDelete }

    // Save mode is explicit and owns its slot until success, cancel or failure.
    // A slot-list click can never fall through to the native load route.
    internal sealed class DuelSaveMenuState
    {
        internal DuelSaveMenuPhase Phase { get; private set; }
        internal string PendingSlot { get; private set; }
        internal bool Active => Phase != DuelSaveMenuPhase.Closed;
        internal bool AllowNativeLoad => !Active;
        internal static bool NeedsOverwriteConfirmation(bool decodedSave, bool nativeFileExists, bool unresolvedTransaction) =>
            decodedSave || nativeFileExists || unresolvedTransaction;
        internal DuelConfirmationRoute ConfirmationRoute(bool ownsPanel, string nativeDeleteSlot)
        {
            if (Active && ownsPanel)
                return Phase == DuelSaveMenuPhase.Confirming ? DuelConfirmationRoute.Save : DuelConfirmationRoute.Ignore;
            return string.IsNullOrEmpty(nativeDeleteSlot) ? DuelConfirmationRoute.Ignore : DuelConfirmationRoute.NativeDelete;
        }
        internal void Begin() { PendingSlot = null; Phase = DuelSaveMenuPhase.Selecting; }
        internal bool Select(string slot, bool occupied)
        {
            if (Phase != DuelSaveMenuPhase.Selecting) throw new InvalidOperationException("保存槽位选择已失效。");
            if (slot == null || slot.Length != 3 || slot == "000") throw new ArgumentException("请选择编号槽位。");
            foreach (char c in slot) if (c < '0' || c > '9') throw new ArgumentException("请选择编号槽位。");
            PendingSlot = slot;
            Phase = occupied ? DuelSaveMenuPhase.Confirming : DuelSaveMenuPhase.Saving;
            return !occupied;
        }
        internal string Confirm()
        {
            if (Phase != DuelSaveMenuPhase.Confirming) throw new InvalidOperationException("覆盖确认已失效。");
            Phase = DuelSaveMenuPhase.Saving;
            return PendingSlot;
        }
        internal void Close() { PendingSlot = null; Phase = DuelSaveMenuPhase.Closed; }
    }
}
