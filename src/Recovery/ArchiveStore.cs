using System;
using System.IO;

namespace MortalHelper.Recovery
{
    public sealed class ArchiveStore
    {
        private readonly string directory;
        public ArchiveStore(string directory) { this.directory = Path.GetFullPath(directory); }

        // Test seam: the contract tests compile into this assembly and force the
        // non-atomic path so the fallback is exercised instead of only compiled.
        internal static bool ForceCopyFallback = false;

        public void Save(int slot, RecoveryDocument document)
        {
            string path = PathFor(slot);
            byte[] bytes = Archive.Encode(document); // validate before touching any existing file
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string backup = path + ".bak";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                bool exists = File.Exists(path);
                // A corrupt primary must never be promoted to backup: the older
                // backup is then the only usable history the player has left.
                bool previousUsable = exists && IsReadable(path);
                if (!exists) File.Move(temporary, path);
                else if (!previousUsable) File.Copy(temporary, path, true);
                else if (ForceCopyFallback) CopyReplacingPrimary(temporary, path, backup);
                else
                {
                    try { File.Replace(temporary, path, backup); }
                    catch (Exception error) when (IsReplaceUnavailable(error))
                    { CopyReplacingPrimary(temporary, path, backup); }
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // File.Replace is unavailable on some file systems, network shares and
        // restricted sandboxes. The fallback copies the previous file to the
        // backup first, so a torn primary copy leaves the last good archive in
        // .bak while the truncated primary fails its own length and checksum.
        private static bool IsReplaceUnavailable(Exception error)
        {
            return error is UnauthorizedAccessException || error is IOException ||
                error is NotSupportedException || error is PlatformNotSupportedException;
        }

        private static void CopyReplacingPrimary(string temporary, string path, string backup)
        {
            File.Copy(path, backup, true);
            File.Copy(temporary, path, true);
        }

        private static bool IsReadable(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > Archive.MaximumBytes) return false;
                    Archive.Decode(stream);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        public RecoveryDocument Load(int slot, bool backup = false)
        {
            string path = PathFor(slot) + (backup ? ".bak" : "");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > Archive.MaximumBytes) throw new InvalidDataException("恢复文件过大。");
                return Archive.Decode(stream);
            }
        }

        public string PathFor(int slot)
        {
            if (slot < 1 || slot > 999) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(directory, "slot-" + slot.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + ".mhr");
        }
    }
}
