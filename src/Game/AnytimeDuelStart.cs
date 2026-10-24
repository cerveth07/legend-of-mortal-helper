using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MortalHelper.Game
{
    // A duel save that needs no replay: the native file already describes the fight's
    // starting state, so loading it restarts the battle exactly like the game's own
    // battle autosave. The companion only labels and fingerprints that file.
    internal sealed class DuelStartRecord
    {
        internal int Version = 1;
        internal string Client = "";
        internal string SceneKey = "";
        internal string NativeHash = "";
        internal string SavedAt = "";
    }

    internal static class DuelStartCodec
    {
        internal const int CurrentVersion = 1;
        private const int MaximumBytes = 4096;

        internal static string Encode(DuelStartRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");
            var text = new StringBuilder();
            text.Append("version=").Append(record.Version.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("client=").Append(Line(record.Client)).Append('\n');
            text.Append("scene=").Append(Line(record.SceneKey)).Append('\n');
            text.Append("hash=").Append(Line(record.NativeHash)).Append('\n');
            text.Append("savedAt=").Append(Line(record.SavedAt)).Append('\n');
            return text.ToString();
        }

        internal static DuelStartRecord Decode(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > MaximumBytes)
                throw new InvalidOperationException("战斗开场档标记为空或过大。");
            var record = new DuelStartRecord();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                int split = line.IndexOf('=');
                if (split <= 0 || split == line.Length - 1)
                    throw new InvalidOperationException("战斗开场档标记格式无效。");
                string key = line.Substring(0, split), value = line.Substring(split + 1);
                if (!seen.Add(key)) throw new InvalidOperationException("战斗开场档标记重复：" + key);
                switch (key)
                {
                    case "version":
                        int version;
                        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out version) ||
                            version != CurrentVersion)
                            throw new InvalidOperationException("战斗开场档标记版本不支持。");
                        record.Version = version;
                        break;
                    case "client": record.Client = value; break;
                    case "scene": record.SceneKey = value; break;
                    case "hash": record.NativeHash = value; break;
                    case "savedAt": record.SavedAt = value; break;
                    default: throw new InvalidOperationException("战斗开场档标记含未知字段：" + key);
                }
            }
            // Every field is required: a marker that cannot name its native file is not
            // allowed to stand in for one.
            if (record.SceneKey.Length == 0 || record.NativeHash.Length == 0)
                throw new InvalidOperationException("战斗开场档标记缺少关卡或指纹。");
            return record;
        }

        // Line breaks cannot be represented; refuse rather than write something that
        // would decode differently than it was written.
        private static string Line(string value)
        {
            if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("战斗开场档标记字段为空。");
            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                throw new InvalidOperationException("战斗开场档标记字段含换行。");
            return value;
        }
    }
}
