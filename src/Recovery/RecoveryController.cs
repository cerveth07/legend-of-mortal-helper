using System;
using System.Globalization;
using System.IO;
using MortalHelper.Core;

namespace MortalHelper.Recovery
{
    // Owns the sequence a caller would otherwise have to reproduce by hand:
    // preflight -> load baseline -> replay events -> verify -> idle. Every
    // failure leaves the gate locked, so an unverified progress can never be
    // presented as a normal save.
    public sealed class RecoveryController
    {
        private readonly OperationGate gate = new OperationGate();
        private readonly ReplayCursor cursor;
        private RecoveryDocument document;
        private IReplayHost host;

        public RecoveryController() { cursor = new ReplayCursor(gate); }
        public RecoveryPhase Phase => gate.Phase;
        public string Reason => gate.Reason;
        public bool Replaying => gate.Phase == RecoveryPhase.Replaying;

        public void Start(RecoveryDocument candidate, IReplayHost target)
        {
            if (!gate.CanInteract) throw new InvalidOperationException("上一次恢复尚未处理完。");
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (target == null) throw new ArgumentNullException(nameof(target));
            cursor.Prepare(candidate, target.Client, candidate.Kind);
            document = candidate;
            host = target;
            gate.Advance(RecoveryPhase.Loading);
            try { host.LoadBaseline(candidate.Baseline); }
            catch (Exception error) { Abort("加载原生基线失败：" + error.Message); throw; }
            gate.Advance(RecoveryPhase.Replaying);
        }

        // True when the recorded events are exhausted and this interaction is the
        // one the recording stopped in front of. The player finishes that
        // interaction; the adapter must not consume it.
        public bool IsStopPoint(EventKind kind, string key)
        {
            if (!Replaying || !cursor.Complete) return false;
            return CheckpointOf(kind, key) == document.Checkpoint;
        }

        public bool TryConsume(EventKind kind, string key, out RecordedEvent consumed)
        {
            consumed = null;
            if (!Replaying) return false;
            // Adapters may call this before checking IsStopPoint. The interaction
            // the recording stopped in front of belongs to the player, and asking
            // for it is not an error.
            if (IsStopPoint(kind, key)) return false;
            try
            {
                consumed = cursor.Consume(kind, key);
                return true;
            }
            catch (InvalidDataException error) { Abort(error.Message); return false; }
        }

        public void Verify()
        {
            if (!Replaying) throw new InvalidOperationException("当前不在恢复阶段。");
            string state;
            try { state = host.CaptureState(); }
            catch (Exception error) { Abort("读取恢复后状态失败：" + error.Message); throw; }
            try { cursor.Verify(document.Checkpoint, state); }
            catch (InvalidDataException error) { Abort(error.Message); throw; }
            document = null;
            host = null;
        }

        // Explicit abort, for a scene change, a manual edit or a user cancel.
        public void Cancel(string reason)
        {
            if (gate.CanInteract) return;
            Abort(reason ?? "恢复已中止。");
        }

        // Clears a failure only after the player has acknowledged it or loaded a
        // native save. Silent recovery would hide a failed verification.
        public void Acknowledge()
        {
            if (gate.Phase != RecoveryPhase.Failed) return;
            gate.NativeLoadCompleted();
        }

        private void Abort(string reason)
        {
            gate.Fail(reason);
            IReplayHost callback = host;
            host = null;
            document = null;
            callback?.Abort(reason);
        }

        // Matches the checkpoint text RecordingSession.Capture writes: the pending
        // interaction kind followed by its key.
        private static string CheckpointOf(EventKind kind, string key)
        {
            return ((int)kind).ToString(CultureInfo.InvariantCulture) + ":" + (key ?? "");
        }
    }
}
