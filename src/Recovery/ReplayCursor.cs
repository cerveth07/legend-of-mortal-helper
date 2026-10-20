using System;
using System.IO;
using MortalHelper.Core;

namespace MortalHelper.Recovery
{
    public sealed class ReplayCursor
    {
        private readonly OperationGate gate;
        private RecoveryDocument document;
        public int Position { get; private set; }
        public bool Complete => document != null && Position == document.Events.Count;
        public ReplayCursor(OperationGate gate) { this.gate = gate; }

        public void Prepare(RecoveryDocument candidate, string client, RecoveryKind kind)
        {
            if (!gate.CanInteract) throw new InvalidOperationException("已有恢复任务或失败状态尚未处理。");
            Archive.Validate(candidate);
            if (candidate.Client != client || candidate.Kind != kind)
                throw new InvalidDataException("客户端或恢复种类不匹配。");
            // Freeze a validated snapshot. A caller cannot alter it during replay.
            RecoveryDocument frozen;
            using (var stream = new MemoryStream(Archive.Encode(candidate))) frozen = Archive.Decode(stream);
            gate.Begin();
            document = frozen;
            Position = 0;
        }

        // Read-only lookahead lets game adapters find the intended option
        // without consuming (or rejecting) every other visible menu button.
        public RecordedEvent Peek()
        {
            if (gate.Phase != RecoveryPhase.Replaying || document == null || Complete) return null;
            var item = document.Events[Position];
            return new RecordedEvent { Kind = item.Kind, Key = item.Key, Integer = item.Integer,
                Number = item.Number, Data = item.Data };
        }

        public RecordedEvent Consume(EventKind kind, string key)
        {
            if (gate.Phase != RecoveryPhase.Replaying || document == null || Complete)
                throw Stop("事件超出恢复边界。");
            var item = document.Events[Position];
            if (item.Kind != kind || item.Key != key) throw Stop("执行顺序或内容与记录不同。");
            Position++;
            return new RecordedEvent { Kind = item.Kind, Key = item.Key, Integer = item.Integer,
                Number = item.Number, Data = item.Data };
        }

        public void Verify(string checkpoint, string actualState)
        {
            if (gate.Phase != RecoveryPhase.Replaying || !Complete ||
                checkpoint != document.Checkpoint) throw Stop("尚未到达目标停点。");
            gate.Advance(RecoveryPhase.Verifying);
            if (actualState != document.ExpectedState) throw Stop("恢复后的状态与保存时不一致。");
            gate.Advance(RecoveryPhase.Idle);
        }

        private InvalidDataException Stop(string reason) { gate.Fail(reason); return new InvalidDataException(reason); }
    }
}
