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
        private readonly OperationGate gate;
        private readonly ReplayCursor cursor;
        private RecoveryDocument document;
        private IReplayHost host;
        private bool stopPointReached;

        public RecoveryController() : this(new OperationGate()) { }
        public RecoveryController(OperationGate sharedGate)
        {
            gate = sharedGate ?? throw new ArgumentNullException(nameof(sharedGate));
            cursor = new ReplayCursor(gate);
        }
        public RecoveryPhase Phase => gate.Phase;
        public string Reason => gate.Reason;
        public bool Replaying => gate.Phase == RecoveryPhase.Replaying;

        // True when every recorded event has been consumed; the caller then verifies.
        public bool Finished => Replaying && cursor.Complete;
        public RecordedEvent NextEvent => Replaying ? cursor.Peek() : null;

        // Recordings that stopped without a pending interaction are verified directly
        // after the last event instead of waiting at a stop-point interaction.
        public void VerifyAtEnd()
        {
            if (!Replaying) throw new InvalidOperationException("当前不在恢复阶段。");
            if (!cursor.Complete) throw new InvalidOperationException("记录尚未重放完。");
            if (!document.Checkpoint.StartsWith("manual:", StringComparison.Ordinal))
                throw new InvalidOperationException("必须先确认记录的待处理交互停点。");
            string expected = document.ExpectedState;
            string state;
            try { state = host.CaptureState(); }
            catch (Exception error) { Abort("读取恢复后状态失败：" + error.Message); throw; }
            gate.Advance(RecoveryPhase.Verifying);
            if (state != expected)
            {
                Abort("恢复后的状态与保存时不一致。");
                throw new InvalidDataException("恢复后的状态与保存时不一致。");
            }
            gate.Advance(RecoveryPhase.Idle);
            document = null;
            host = null;
        }

        public void Start(RecoveryDocument candidate, IReplayHost target)
        {
            if (!gate.CanInteract) throw new InvalidOperationException("上一次恢复尚未处理完。");
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            if (target == null) throw new ArgumentNullException(nameof(target));
            // Freeze controller metadata as well as the cursor's event tape.
            RecoveryDocument frozen;
            using (var stream = new MemoryStream(Archive.Encode(candidate))) frozen = Archive.Decode(stream);
            cursor.Prepare(frozen, target.Client, frozen.Kind);
            document = frozen;
            stopPointReached = false;
            host = target;
            gate.Advance(RecoveryPhase.Loading);
            try { host.LoadBaseline(document.Baseline); }
            catch (Exception error) { Abort("加载原生基线失败：" + error.Message); throw; }
            gate.Advance(RecoveryPhase.Replaying);
        }

        // True when the recorded events are exhausted and this interaction is the
        // one the recording stopped in front of. The player finishes that
        // interaction; the adapter must not consume it.
        public bool IsStopPoint(EventKind kind, string key)
        {
            if (!Replaying || !cursor.Complete) return false;
            bool matches = CheckpointOf(kind, key) == document.Checkpoint;
            if (matches) stopPointReached = true;
            return matches;
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
            if (!stopPointReached) throw new InvalidOperationException("尚未确认目标交互停点。");
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

        // Acknowledging an error cannot certify the partially replayed state.
        // The native load boundary must apply data AND finish loading its scene.
        public void Acknowledge()
        {
            if (gate.Phase != RecoveryPhase.Failed) return;
            throw new InvalidOperationException("请通过游戏原生读档解除恢复锁。");
        }

        private void Abort(string reason)
        {
            gate.Fail(reason);
            IReplayHost callback = host;
            host = null;
            document = null;
            callback?.Abort(reason);
        }

        // "kind:key" checkpoint text. No shipped producer writes this form any more:
        // StoryRecovery writes "manual:"+timestamp and finishes through VerifyAtEnd,
        // so the whole stop-point branch is retained only for recorded-file compatibility.
        private static string CheckpointOf(EventKind kind, string key)
        {
            return ((int)kind).ToString(CultureInfo.InvariantCulture) + ":" + (key ?? "");
        }
    }
}
