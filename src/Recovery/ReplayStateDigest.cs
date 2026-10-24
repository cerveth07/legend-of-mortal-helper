using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MortalHelper.Recovery
{
    // Versioned, fail-closed checkpoint fingerprint. A digest of the native
    // snapshot alone is insufficient if the game defers updating it, so callers
    // must also supply live facts and a serialized mission/flag state.
    public static class ReplayStateDigest
    {
        public const string Prefix = "MHRSTATE3:";

        public static string Create(string nativeSaveJson, string missionJson, IEnumerable<string> facts)
        {
            if (string.IsNullOrWhiteSpace(nativeSaveJson))
                throw new InvalidDataException("原生进度快照不可读。");
            if (string.IsNullOrWhiteSpace(missionJson) || missionJson.Trim() == "{}")
                throw new InvalidDataException("任务与剧情标记无法完整序列化；拒绝不可靠的精确恢复校验。");
            if (facts == null) throw new InvalidDataException("状态快照缺少实时游戏字段。");

            var values = new List<string>();
            foreach (string fact in facts)
            {
                if (string.IsNullOrEmpty(fact)) throw new InvalidDataException("状态快照存在无效字段。");
                values.Add(fact);
            }
            if (values.Count == 0) throw new InvalidDataException("状态快照没有实时游戏字段。");
            values.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            AppendPart(builder, NormalizeNativeTimestamp(nativeSaveJson));
            AppendPart(builder, missionJson);
            foreach (string item in values) AppendPart(builder, item);
            using (var hash = SHA256.Create())
                return Prefix + Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        // CreateSaveData stamps the current wall clock on EVERY read. Ignore only
        // that top-level integer; retain every other byte, including nested fields
        // named TimeTick, rather than round-tripping through a partial save model.
        private static string NormalizeNativeTimestamp(string json)
        {
            int depth = 0, start = -1, end = -1;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (c == '{' || c == '[') { depth++; continue; }
                if (c == '}' || c == ']') { depth--; continue; }
                if (c != '"') continue;
                int quote = i++;
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\') i++;
                    i++;
                }
                if (i >= json.Length) throw new InvalidDataException("原生快照字符串未闭合。");
                if (depth != 1 || json.Substring(quote, i - quote + 1) != "\"TimeTick\"") continue;
                int next = i + 1;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                if (next >= json.Length || json[next] != ':') continue; // A string value, not a key.
                if (start >= 0) throw new InvalidDataException("原生快照含重复 TimeTick。");
                next++;
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                start = next;
                while (next < json.Length && json[next] >= '0' && json[next] <= '9') next++;
                end = next;
                long tick;
                if (end == start || !long.TryParse(json.Substring(start, end - start),
                    NumberStyles.None, CultureInfo.InvariantCulture, out tick) || tick <= 0)
                    throw new InvalidDataException("原生快照 TimeTick 无效。");
                while (next < json.Length && char.IsWhiteSpace(json[next])) next++;
                if (next >= json.Length || (json[next] != ',' && json[next] != '}'))
                    throw new InvalidDataException("原生快照 TimeTick 必须是整数。");
                i = end - 1;
            }
            if (depth != 0 || start < 0) throw new InvalidDataException("原生快照缺少有效 TimeTick。");
            return json.Substring(0, start) + "0" + json.Substring(end);
        }

        private static void AppendPart(StringBuilder builder, string value)
        {
            builder.Append(value.Length).Append(':').Append(value).Append(';');
        }
    }
}
