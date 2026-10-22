using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MortalHelper.Core
{
    public enum SlotTransactionState { Prepared, Committed, RolledBack, Broken }

    // One file that a save operation is allowed to change. `Existed` and
    // `OriginalHash` are captured before anything is written, so a later
    // rollback never has to guess from "does the backup file exist".
    public sealed class SlotTransactionEntry
    {
        public string Path;
        public bool Existed;
        public string OriginalHash;
        public string BackupPath;
    }

    public sealed class SlotRollbackReport
    {
        public bool Complete;
        public bool Restored;
        public string Detail = "";
        public readonly List<string> Blockers = new List<string>();
    }

    // A small write-ahead journal for a numbered save slot.
    //
    // The native save file is written by the game, not by this mod, so the
    // transaction cannot stage-and-swap the whole slot. Instead the journal
    // records the exact pre-write state of every participant file and only
    // claims a commit after the caller has re-read and verified the new pair.
    // A crash at any point therefore leaves one of two recoverable states:
    // "prepared" (roll back to the recorded original) or "committed" (clean up).
    //
    // Deliberate rules:
    //  * A missing or unreadable backup never means "the original was absent".
    //    It means the slot is unrecoverable and nothing else may be touched.
    //  * Rollback verifies every backup against the recorded hash before it
    //    writes anything, so a partially copied backup cannot become the save.
    //  * A failed rollback leaves the journal, the backups and any interrupted
    //    markers in place and reports the exact paths.
    public sealed class SlotTransaction
    {
        public const string Suffix = ".mhtx";
        private const string Header = "slot-transaction 1";
        private const int MaximumEntries = 24;
        private const int MaximumFile = 65536;

        // Fault-injection seam: the contract tests compile this file into their
        // own assembly and throw from here to simulate a crash between steps.
        internal static Action<string> Fault { get; set; }

        private readonly List<SlotTransactionEntry> entries = new List<SlotTransactionEntry>();

        public string JournalPath { get; private set; }
        public string Operation { get; private set; }
        public SlotTransactionState State { get; private set; }
        public string ReplacementHash { get; private set; }
        public List<SlotTransactionEntry> Entries => entries;

        private SlotTransaction(string journalPath, string operation, SlotTransactionState state)
        {
            JournalPath = journalPath;
            Operation = operation ?? "";
            State = state;
        }

        public static string JournalFor(string targetPath)
        {
            return targetPath == null ? null : targetPath + Suffix;
        }

        public static bool Exists(string journalPath)
        {
            return !string.IsNullOrEmpty(journalPath) && File.Exists(journalPath);
        }

        public static string HashFile(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(stream));
        }

        // Captures the current state of every participant and persists it.
        // Throws before touching anything if the journal already exists or a
        // backup cannot be produced and re-verified.
        public static SlotTransaction Begin(string journalPath, string operation, string[] paths)
        {
            if (string.IsNullOrEmpty(journalPath)) throw new ArgumentException("缺少事务日志路径。", nameof(journalPath));
            if (paths == null || paths.Length == 0 || paths.Length > MaximumEntries)
                throw new ArgumentException("事务参与文件数量无效。", nameof(paths));
            if (File.Exists(journalPath))
                throw new InvalidOperationException("上一个存档事务尚未解决：" + journalPath);

            var created = new List<string>();
            var transaction = new SlotTransaction(journalPath, operation, SlotTransactionState.Prepared);
            try
            {
                for (int index = 0; index < paths.Length; index++)
                {
                    string path = paths[index];
                    if (string.IsNullOrEmpty(path)) throw new ArgumentException("事务参与文件路径为空。", nameof(paths));
                    var entry = new SlotTransactionEntry { Path = path, Existed = File.Exists(path) };
                    if (entry.Existed)
                    {
                        entry.OriginalHash = HashFile(path);
                        entry.BackupPath = journalPath + ".b" + index.ToString(CultureInfo.InvariantCulture);
                        Step("backup:" + index);
                        File.Copy(path, entry.BackupPath, true);
                        created.Add(entry.BackupPath);
                        Step("verify-backup:" + index);
                        if (HashFile(entry.BackupPath) != entry.OriginalHash)
                            throw new IOException("备份副本与原始文件不一致：" + entry.Path);
                    }
                    transaction.entries.Add(entry);
                }
                Step("journal-write");
                transaction.Persist();
                return transaction;
            }
            catch
            {
                foreach (string path in created) TryDelete(path);
                throw;
            }
        }

        public static SlotTransaction Read(string journalPath)
        {
            if (string.IsNullOrEmpty(journalPath) || !File.Exists(journalPath)) return null;
            var info = new FileInfo(journalPath);
            if (info.Length <= 0 || info.Length > MaximumFile) throw new InvalidDataException("存档事务日志大小无效。");
            var lines = new List<string>();
            using (var reader = new StreamReader(journalPath, new UTF8Encoding(false), false))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }
            if (lines.Count < 5 || lines[0] != Header) throw new InvalidDataException("存档事务日志头无效。");
            string operation = Unescape(Value(lines[1], "op"));
            var state = ParseState(Value(lines[2], "state"));
            string replacement = Value(lines[3], "new");
            int count = ParseCount(Value(lines[4], "n"));
            if (lines.Count != 5 + count) throw new InvalidDataException("存档事务日志条目数量不符。");
            var transaction = new SlotTransaction(journalPath, operation, state)
            {
                ReplacementHash = replacement == "-" ? null : replacement
            };
            for (int index = 0; index < count; index++)
            {
                string[] parts = lines[5 + index].Split('|');
                if (parts.Length != 5 || parts[0] != index.ToString(CultureInfo.InvariantCulture))
                    throw new InvalidDataException("存档事务日志条目无效。");
                if (parts[1] != "0" && parts[1] != "1") throw new InvalidDataException("存档事务日志条目状态无效。");
                var entry = new SlotTransactionEntry
                {
                    Existed = parts[1] == "1",
                    OriginalHash = parts[2] == "-" ? null : parts[2],
                    Path = Unescape(parts[3]),
                    BackupPath = parts[4] == "-" ? null : Unescape(parts[4])
                };
                if (string.IsNullOrEmpty(entry.Path)) throw new InvalidDataException("存档事务日志条目路径为空。");
                if (entry.Existed && (string.IsNullOrEmpty(entry.OriginalHash) || string.IsNullOrEmpty(entry.BackupPath)))
                    throw new InvalidDataException("存档事务日志缺少原始哈希或备份路径。");
                transaction.entries.Add(entry);
            }
            return transaction;
        }

        // Records the hash of the freshly written replacement once the caller
        // has verified it. Written before any superseded companion is removed so
        // an interrupted cleanup can be completed forward instead of throwing a
        // valid new native save away.
        public void RecordReplacement(string hash)
        {
            if (string.IsNullOrEmpty(hash)) throw new ArgumentException("替换文件哈希为空。", nameof(hash));
            ReplacementHash = hash;
            Persist();
        }

        public void Commit()
        {
            State = SlotTransactionState.Committed;
            Persist();
        }

        public void Complete()
        {
            for (int index = 0; index < entries.Count; index++)
            {
                string backup = entries[index].BackupPath;
                if (string.IsNullOrEmpty(backup)) continue;
                Step("cleanup-backup:" + index);
                TryDelete(backup);
            }
            Step("journal-delete");
            TryDelete(JournalPath);
            TryDelete(JournalPath + ".tmp");
        }

        public SlotRollbackReport Rollback()
        {
            var report = new SlotRollbackReport();
            if (State == SlotTransactionState.RolledBack || State == SlotTransactionState.Committed)
            {
                report.Complete = true;
                Complete();
                return report;
            }
            // Phase 1: prove every backup is usable before writing anything.
            foreach (var entry in entries)
            {
                if (!entry.Existed) continue;
                if (!CanRestore(entry))
                {
                    report.Blockers.Add(entry.Path);
                    continue;
                }
            }
            if (report.Blockers.Count > 0)
            {
                State = SlotTransactionState.Broken;
                TryPersist();
                report.Complete = false;
                report.Detail = "备份缺失或已损坏，未改动任何文件：" + string.Join("、", report.Blockers.ToArray());
                return report;
            }
            // Phase 2: restore, verifying each write against the recorded hash.
            bool complete = true;
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                try
                {
                    Step("restore:" + index);
                    if (entry.Existed)
                    {
                        File.Copy(entry.BackupPath, entry.Path, true);
                        if (HashFile(entry.Path) != entry.OriginalHash)
                        {
                            report.Blockers.Add(entry.Path);
                            complete = false;
                            continue;
                        }
                    }
                    else if (File.Exists(entry.Path)) File.Delete(entry.Path);
                    report.Restored = true;
                }
                catch (Exception error)
                {
                    report.Blockers.Add(entry.Path + "（" + error.Message + "）");
                    complete = false;
                }
            }
            if (!complete)
            {
                State = SlotTransactionState.Broken;
                TryPersist();
                report.Complete = false;
                report.Detail = "回退未完成，已保留事务日志与备份：" + string.Join("、", report.Blockers.ToArray());
                return report;
            }
            State = SlotTransactionState.RolledBack;
            TryPersist();
            report.Complete = true;
            report.Detail = "已按事务日志恢复到写入前状态。";
            Complete();
            return report;
        }

        private bool CanRestore(SlotTransactionEntry entry)
        {
            if (string.IsNullOrEmpty(entry.BackupPath) || string.IsNullOrEmpty(entry.OriginalHash)) return false;
            if (!File.Exists(entry.BackupPath)) return false;
            try { return HashFile(entry.BackupPath) == entry.OriginalHash; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private void Persist()
        {
            var text = new StringBuilder(Header).Append('\n');
            text.Append("op=").Append(Escape(Operation)).Append('\n');
            text.Append("state=").Append(StateCode(State)).Append('\n');
            text.Append("new=").Append(string.IsNullOrEmpty(ReplacementHash) ? "-" : ReplacementHash).Append('\n');
            text.Append("n=").Append(entries.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                text.Append(index.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(entry.Existed ? '1' : '0').Append('|')
                    .Append(string.IsNullOrEmpty(entry.OriginalHash) ? "-" : entry.OriginalHash).Append('|')
                    .Append(Escape(entry.Path)).Append('|')
                    .Append(string.IsNullOrEmpty(entry.BackupPath) ? "-" : Escape(entry.BackupPath)).Append('\n');
            }
            byte[] bytes = new UTF8Encoding(false).GetBytes(text.ToString());
            string temporary = JournalPath + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(JournalPath)) File.Replace(temporary, JournalPath, null);
                else File.Move(temporary, JournalPath);
            }
            finally { TryDelete(temporary); }
        }

        // A journal that cannot be rewritten must not masquerade as a clean one:
        // the previous version stays on disk, which keeps the slot blocked, and
        // the caller reports the failure with its own message.
        private void TryPersist()
        {
            try { Persist(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Step(string name)
        {
            var hook = Fault;
            if (hook != null) hook(name);
        }

        private static void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string StateCode(SlotTransactionState state)
        {
            switch (state)
            {
                case SlotTransactionState.Prepared: return "prepared";
                case SlotTransactionState.Committed: return "committed";
                case SlotTransactionState.RolledBack: return "rolledback";
                case SlotTransactionState.Broken: return "broken";
                default: throw new ArgumentOutOfRangeException(nameof(state));
            }
        }

        private static SlotTransactionState ParseState(string value)
        {
            switch (value)
            {
                case "prepared": return SlotTransactionState.Prepared;
                case "committed": return SlotTransactionState.Committed;
                case "rolledback": return SlotTransactionState.RolledBack;
                case "broken": return SlotTransactionState.Broken;
                default: throw new InvalidDataException("存档事务状态无效。");
            }
        }

        private static int ParseCount(string value)
        {
            int count;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count) ||
                count <= 0 || count > MaximumEntries)
                throw new InvalidDataException("存档事务条目数量无效。");
            return count;
        }

        private static string Value(string line, string key)
        {
            string prefix = key + "=";
            if (line == null || !line.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("存档事务字段缺失：" + key);
            return line.Substring(prefix.Length);
        }

        private static string Escape(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Unescape(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch (FormatException error) { throw new InvalidDataException("存档事务字段编码无效。", error); }
        }
    }
}
