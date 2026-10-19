using System;
using System.IO;

namespace MortalHelper.Recovery
{
    public sealed class ArchiveStore
    {
        private readonly string directory;
        public ArchiveStore(string directory) { this.directory = Path.GetFullPath(directory); }

        public void Save(int slot, RecoveryDocument document)
        {
            string path = PathFor(slot);
            byte[] bytes = Archive.Encode(document); // validate before touching any existing file
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
