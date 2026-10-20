using System;

namespace MortalHelper.Core
{
    public enum RecoveryPhase { Idle, Preflight, Loading, Replaying, Verifying, Failed, NativeLoading }

    public sealed class OperationGate
    {
        public RecoveryPhase Phase { get; private set; }
        public string Reason { get; private set; }
        public long Revision { get; private set; }
        public bool CanInteract => Phase == RecoveryPhase.Idle;
        public event Action<string> ProgressChanged;

        public void Begin()
        {
            if (!CanInteract) throw new InvalidOperationException("另一次恢复尚未结束。");
            Change(RecoveryPhase.Preflight, "检查恢复文件");
        }

        public void Advance(RecoveryPhase next)
        {
            bool valid = (Phase == RecoveryPhase.Preflight && next == RecoveryPhase.Loading)
                || (Phase == RecoveryPhase.Loading && next == RecoveryPhase.Replaying)
                || (Phase == RecoveryPhase.Replaying && next == RecoveryPhase.Verifying)
                || (Phase == RecoveryPhase.Verifying && next == RecoveryPhase.Idle);
            if (!valid) throw new InvalidOperationException("无效的恢复阶段转换。");
            Change(next, next == RecoveryPhase.Idle ? "恢复已验证" : next.ToString());
        }

        public void Fail(string reason) { Change(RecoveryPhase.Failed, reason ?? "恢复失败"); }
        public void NativeLoadStarted()
        {
            if (Phase != RecoveryPhase.Idle && Phase != RecoveryPhase.Failed)
                throw new InvalidOperationException("必须先中止实验恢复才能原生读档。");
            Change(RecoveryPhase.NativeLoading, "等待原生进度与场景加载完成");
        }
        public void NativeLoadCompleted()
        {
            if (Phase != RecoveryPhase.NativeLoading)
                throw new InvalidOperationException("尚未开始原生读档，不能解除恢复锁。");
            Change(RecoveryPhase.Idle, "原生进度与场景已重新加载");
        }
        public void NotifyMutation(string description)
        {
            if (!CanInteract) throw new InvalidOperationException("恢复期间不能修改进度。");
            Revision++;
            ProgressChanged?.Invoke(description);
        }

        private void Change(RecoveryPhase phase, string reason) { Phase = phase; Reason = reason; }
    }
}
