using System;
using System.Collections.Generic;
using System.IO;

namespace MortalHelper.Recovery
{
    // Recording is a transaction: an unknown interaction or mutation invalidates
    // the whole chain until a new baseline is explicitly established.
    public sealed class RecordingSession
    {
        private RecoveryDocument baseline;
        private readonly List<RecordedEvent> events = new List<RecordedEvent>();
        private long generation;
        private long serial;
        private long pending;
        private EventKind pendingKind;
        private string pendingKey;
        public string InvalidReason { get; private set; } = "尚无恢复基线。";
        public bool Valid => baseline != null && InvalidReason == null;
        public bool Waiting => pending != 0;
        public long Generation => generation;

        public void Begin(RecoveryDocument seed)
        {
            // Baselines are supplied by a game adapter. No sharing of mutable objects.
            Reset();
            using (var stream = new MemoryStream(Archive.Encode(seed))) baseline = Archive.Decode(stream);
            baseline.Events.Clear();
            InvalidReason = null;
        }

        public long Enter(EventKind kind, string key)
        {
            if (!Valid) return 0;
            if (pending != 0)
            {
                Invalidate("嵌套交互尚未验证，当前恢复链停用。");
                return 0;
            }
            if (kind != EventKind.Dialogue && kind != EventKind.Choice && kind != EventKind.Dice && kind != EventKind.Action)
                throw new ArgumentException("该事件不是可等待交互。", nameof(kind));
            if (string.IsNullOrEmpty(key) || key.Length > 4096) throw new ArgumentException("无效交互标识。", nameof(key));
            pendingKind = kind; pendingKey = key; pending = ++serial;
            return pending;
        }

        public void Finish(long token, int result)
        {
            if (!Valid) return;
            if (token == 0 || token != pending) { Invalidate("过期的交互完成回调。"); return; }
            if ((pendingKind == EventKind.Choice || pendingKind == EventKind.Dice) && result < 1)
            { Invalidate("交互未返回有效选项。"); return; }
            Add(new RecordedEvent { Kind = pendingKind, Key = pendingKey, Integer = result });
            pending = 0; pendingKey = null;
        }

        public void Random(string callIdentity, double value)
        {
            if (!Valid) return;
            // Skipping a completed interaction during replay must not lose random
            // calls hidden inside it. Unsupported nesting fails closed for now.
            if (Waiting) { Invalidate("交互内部发生随机调用，尚未建立可验证的重放规则。"); return; }
            if (double.IsNaN(value) || double.IsInfinity(value)) { Invalidate("随机结果非法。"); return; }
            Add(new RecordedEvent { Kind = EventKind.Random, Key = callIdentity, Number = value });
        }

        public RecoveryDocument Capture(string state)
        {
            if (!Valid || !Waiting) throw new InvalidOperationException(InvalidReason ?? "当前不是可恢复停点。");
            baseline.Checkpoint = ((int)pendingKind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + pendingKey;
            baseline.ExpectedState = state;
            baseline.Events.Clear();
            baseline.Events.AddRange(events);
            using (var stream = new MemoryStream(Archive.Encode(baseline))) return Archive.Decode(stream);
        }

        public void Invalidate(string reason) { InvalidReason = reason ?? "恢复链已失效。"; pending = 0; pendingKey = null; }
        public void Reset() { baseline = null; events.Clear(); generation++; Invalidate("尚无恢复基线。"); }

        private void Add(RecordedEvent entry)
        {
            if (events.Count >= Archive.MaximumEvents) { Invalidate("记录已达上限。"); return; }
            events.Add(entry);
        }
    }
}
