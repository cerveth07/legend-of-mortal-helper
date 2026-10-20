using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MortalHelper.Recovery
{
    // Versioned, fail-closed checkpoint fingerprint. A digest of the native
    // snapshot alone is insufficient if the game defers updating it, so callers
    // must also supply live facts and a serialized mission/flag state.
    public static class ReplayStateDigest
    {
        public const string Prefix = "MHRSTATE2:";

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
            AppendPart(builder, nativeSaveJson);
            AppendPart(builder, missionJson);
            foreach (string item in values) AppendPart(builder, item);
            using (var hash = SHA256.Create())
                return Prefix + Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        private static void AppendPart(StringBuilder builder, string value)
        {
            builder.Append(value.Length).Append(':').Append(value).Append(';');
        }
    }
}
