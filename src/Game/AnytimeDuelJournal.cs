using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MortalHelper.Game
{
    internal sealed class DuelDraw
    {
        public string Site;
        public int Kind, A, B, Integer;
        public float X, Y, Floating;
    }

    internal sealed class DuelPoint
    {
        public int Round, DrawCount;
        public string Digest;
    }

    internal sealed class DuelEdit
    {
        public int PointIndex, Before, After;
        public string Key;
    }

    [Flags]
    internal enum DuelAssist
    {
        None = 0, Health = 1, Stamina = 2, Dodge = 4, Damage = 8,
        ZeroDamage = 16, AlwaysHit = 32, FirstStrike = 64
    }

    internal sealed class DuelJournal
    {
        public int Format = 4;
        public int OpeningAssists;
        public List<int> ActionAssists = new List<int>();
        public string Client, SceneKey, NativeHash, RandomState, FinalState;
        public List<int> Actions = new List<int>();
        public List<DuelDraw> Draws = new List<DuelDraw>();
        public List<DuelPoint> Points = new List<DuelPoint>();
        public List<DuelEdit> Edits = new List<DuelEdit>();
    }

    // Explicit fields preserve every draw and float bit. Unity JsonUtility can
    // omit custom nested classes in a runtime-loaded plug-in assembly.
    internal static class DuelJournalCodec
    {
        internal const int MaxFile = 32 * 1024 * 1024;
        internal const int MaxActions = 10000;
        internal const int MaxDraws = 1000000;
        internal const int MaxEdits = 10000;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MHDL-04\n");

        internal static byte[] Encode(DuelJournal value)
        {
            Validate(value);
            byte[] data;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(value.Format);
                writer.Write(value.Client); writer.Write(value.SceneKey); writer.Write(value.NativeHash);
                writer.Write(value.RandomState); writer.Write(value.FinalState);
                writer.Write(value.OpeningAssists);
                writer.Write(value.Actions.Count);
                foreach (int action in value.Actions) writer.Write(action);
                writer.Write(value.ActionAssists.Count);
                foreach (int assists in value.ActionAssists) writer.Write(assists);
                writer.Write(value.Draws.Count);
                foreach (var draw in value.Draws)
                {
                    writer.Write(draw.Site); writer.Write(draw.Kind);
                    writer.Write(draw.A); writer.Write(draw.B); writer.Write(draw.Integer);
                    writer.Write(draw.X); writer.Write(draw.Y); writer.Write(draw.Floating);
                }
                writer.Write(value.Points.Count);
                foreach (var point in value.Points)
                { writer.Write(point.Round); writer.Write(point.DrawCount); writer.Write(point.Digest); }
                writer.Write(value.Edits.Count);
                foreach (var edit in value.Edits)
                { writer.Write(edit.PointIndex); writer.Write(edit.Key); writer.Write(edit.Before); writer.Write(edit.After); }
                data = stream.ToArray();
            }
            if (data.Length > MaxFile - Magic.Length - 36)
                throw new InvalidDataException("单挑记录超过 32 MB。");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic); writer.Write(data.Length); writer.Write(data);
                writer.Write(Hash(data));
                return stream.ToArray();
            }
        }

        internal static DuelJournal Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length + 36 || bytes.Length > MaxFile)
                throw new InvalidDataException("单挑附档大小无效。");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                byte[] header = reader.ReadBytes(Magic.Length);
                if (Encoding.ASCII.GetString(header) == "MHDL-01\n")
                    throw new InvalidDataException("旧版单挑附档可能遗漏随机记录，无法保证完整恢复，请重新保存。");
                bool legacy = Encoding.ASCII.GetString(header) == "MHDL-02\n";
                bool v3 = Encoding.ASCII.GetString(header) == "MHDL-03\n";
                if (!legacy && !v3 && !header.SequenceEqual(Magic)) throw new InvalidDataException("单挑附档头无效。");
                int length = reader.ReadInt32();
                if (length <= 0 || length != bytes.Length - Magic.Length - 36)
                    throw new InvalidDataException("单挑附档长度无效。");
                byte[] data = reader.ReadBytes(length);
                if (!reader.ReadBytes(32).SequenceEqual(Hash(data)))
                    throw new InvalidDataException("单挑附档内容校验失败。");
                using (var payload = new MemoryStream(data, false))
                using (var fields = new BinaryReader(payload, new UTF8Encoding(false, true)))
                {
                    var value = new DuelJournal { Format = fields.ReadInt32(),
                        Client = fields.ReadString(), SceneKey = fields.ReadString(), NativeHash = fields.ReadString(),
                        RandomState = fields.ReadString(), FinalState = fields.ReadString() };
                    if (value.Format != (legacy ? 2 : v3 ? 3 : 4))
                        throw new InvalidDataException("单挑附档头与格式版本不一致。");
                    if (!legacy) value.OpeningAssists = fields.ReadInt32();
                    int count = Count(fields, MaxActions, 4);
                    for (int i = 0; i < count; i++) value.Actions.Add(fields.ReadInt32());
                    if (legacy)
                        for (int i = 0; i < value.Actions.Count; i++) value.ActionAssists.Add(0);
                    else
                    {
                        count = Count(fields, MaxActions, 4);
                        for (int i = 0; i < count; i++) value.ActionAssists.Add(fields.ReadInt32());
                    }
                    count = Count(fields, MaxDraws, 30);
                    for (int i = 0; i < count; i++) value.Draws.Add(new DuelDraw {
                        Site = fields.ReadString(), Kind = fields.ReadInt32(), A = fields.ReadInt32(),
                        B = fields.ReadInt32(), Integer = fields.ReadInt32(), X = fields.ReadSingle(),
                        Y = fields.ReadSingle(), Floating = fields.ReadSingle() });
                    count = Count(fields, MaxActions + 1, 10);
                    for (int i = 0; i < count; i++) value.Points.Add(new DuelPoint {
                        Round = fields.ReadInt32(), DrawCount = fields.ReadInt32(), Digest = fields.ReadString() });
                    if (!legacy && !v3)
                    {
                        count = Count(fields, MaxEdits, 13);
                        for (int i = 0; i < count; i++) value.Edits.Add(new DuelEdit {
                            PointIndex = fields.ReadInt32(), Key = fields.ReadString(),
                            Before = fields.ReadInt32(), After = fields.ReadInt32() });
                    }
                    if (payload.Position != payload.Length)
                        throw new InvalidDataException("单挑附档包含未识别内容。");
                    value.Format = 4;
                    Validate(value);
                    return value;
                }
            }
        }

        private static int Count(BinaryReader reader, int maximum, int minimumBytes)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumBytes)
                throw new InvalidDataException("单挑附档集合长度无效。");
            return count;
        }

        internal static void Validate(DuelJournal value)
        {
            if (value == null || value.Format != 4 || string.IsNullOrEmpty(value.Client) ||
                string.IsNullOrEmpty(value.SceneKey) || string.IsNullOrEmpty(value.NativeHash) ||
                string.IsNullOrEmpty(value.RandomState) || string.IsNullOrEmpty(value.FinalState) ||
                value.Actions == null || value.Points == null || value.Draws == null || value.ActionAssists == null || value.Edits == null ||
                value.ActionAssists.Count != value.Actions.Count ||
                value.Actions.Count > MaxActions || value.Draws.Count > MaxDraws || value.Edits.Count > MaxEdits ||
                value.Points.Count != value.Actions.Count + 1)
                throw new InvalidDataException("单挑记录结构不完整。");
            int priorEditPoint = -1;
            var editValues = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var edit in value.Edits)
            {
                if (edit == null || edit.PointIndex < priorEditPoint || edit.PointIndex < 0 ||
                    edit.PointIndex >= value.Points.Count || string.IsNullOrEmpty(edit.Key) || edit.Key.Length > 128)
                    throw new InvalidDataException("单挑数值修改记录无效。");
                if (edit.PointIndex != priorEditPoint) editValues.Clear();
                if (editValues.TryGetValue(edit.Key, out int previous) && previous != edit.Before)
                    throw new InvalidDataException("单挑同一行动点的数值修改记录不连续。");
                editValues[edit.Key] = edit.After;
                priorEditPoint = edit.PointIndex;
            }
            ValidateAssistMask(value.OpeningAssists);
            foreach (int assists in value.ActionAssists) ValidateAssistMask(assists);
            if (value.Actions.Any(action => action < 0 || action > 7))
                throw new InvalidDataException("单挑行动类型无效。");
            int priorDraw = 0, priorRound = 0;
            foreach (var point in value.Points)
            {
                if (point == null || point.Round <= priorRound || point.DrawCount < priorDraw ||
                    point.DrawCount > value.Draws.Count || string.IsNullOrEmpty(point.Digest))
                    throw new InvalidDataException("单挑行动点记录无效。");
                priorDraw = point.DrawCount; priorRound = point.Round;
            }
            if (priorDraw != value.Draws.Count || value.Points.Last().Digest !=
                Convert.ToBase64String(Hash(Encoding.UTF8.GetBytes(value.FinalState))))
                throw new InvalidDataException("单挑目标状态校验失败。");
            foreach (var draw in value.Draws)
            {
                if (draw == null || string.IsNullOrEmpty(draw.Site) || draw.Kind < 0 || draw.Kind > 2 ||
                    !Finite(draw.X) || !Finite(draw.Y) || !Finite(draw.Floating))
                    throw new InvalidDataException("单挑随机记录无效。");
                if (draw.Kind == 0 && (draw.Floating < 0f || draw.Floating > 1f))
                    throw new InvalidDataException("单挑随机值超出范围。");
            }
        }

        internal static void ValidateAssistMask(int mask)
        {
            const int known = (int)(DuelAssist.Health | DuelAssist.Stamina | DuelAssist.Dodge | DuelAssist.Damage |
                DuelAssist.ZeroDamage | DuelAssist.AlwaysHit | DuelAssist.FirstStrike);
            if ((mask & ~known) != 0 ||
                ((mask & (int)DuelAssist.Damage) != 0 && (mask & (int)DuelAssist.ZeroDamage) != 0))
                throw new InvalidDataException("单挑辅助配置记录无效。");
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static byte[] Hash(byte[] value)
        { using (var sha = SHA256.Create()) return sha.ComputeHash(value); }
    }
}

namespace MortalHelper.Game
{
    internal static class DuelStateDifference
    {
        // DuelAudit uses length-prefixed key/value strings, not JSON. Values can
        // contain colons, newlines or arbitrary Lua text without ambiguity.
        internal static string Compare(string expected, string actual)
        {
            var first = Parse(expected); var second = Parse(actual);
            var text = new StringBuilder();
            int count = 0;
            foreach (string key in first.Keys.Concat(second.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal))
            {
                bool hasFirst = first.TryGetValue(key, out var a), hasSecond = second.TryGetValue(key, out var b);
                if (hasFirst && hasSecond && a == b) continue;
                count++;
                text.Append("path=").Append(key).Append("\nexpected=").Append(hasFirst ? Escape(a) : "<absent>")
                    .Append("\nactual=").Append(hasSecond ? Escape(b) : "<absent>").Append('\n');
            }
            return "different fields=" + count + "\n" + (count == 0 && expected != actual ?
                "Field values match; traversal order differs.\n" : text.ToString());
        }

        private static Dictionary<string, string> Parse(string state)
        {
            if (state == null || state.Length > 4000000) throw new InvalidDataException("单挑状态长度无效。");
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            int position = 0;
            while (position < state.Length)
            {
                string key = Read(state, ref position), value = Read(state, ref position);
                if (result.ContainsKey(key)) throw new InvalidDataException("单挑状态字段重复：" + key);
                result.Add(key, value);
            }
            return result;
        }

        private static string Read(string state, ref int position)
        {
            int colon = state.IndexOf(':', position);
            if (colon < 0 || colon - position > 10 || !int.TryParse(state.Substring(position, colon - position), out int length) ||
                length < 0 || length > state.Length - colon - 1)
                throw new InvalidDataException("单挑状态长度前缀无效。");
            position = colon + 1 + length;
            return state.Substring(colon + 1, length);
        }

        private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
    }
}

namespace MortalHelper.Game
{
    internal static class DuelFlagRestorePlan
    {
        internal static void Validate(string[] liveNames, int[] liveAliases, string[] savedNames, int[] savedStates)
        {
            if (liveNames == null || liveAliases == null || savedNames == null || savedStates == null ||
                liveNames.Length > 10000 || liveNames.Length != savedNames.Length ||
                liveAliases.Length != liveNames.Length || savedStates.Length != savedNames.Length)
                throw new InvalidDataException("单挑开场旗标集合长度不匹配。");
            var aliases = new Dictionary<int, int>();
            for (int i = 0; i < liveNames.Length; i++)
            {
                if (string.IsNullOrEmpty(savedNames[i]) || liveNames[i] != savedNames[i] || liveAliases[i] < 0)
                    throw new InvalidDataException("单挑开场旗标不匹配：index=" + i + "，记录=" + savedNames[i] + "，当前=" + liveNames[i]);
                if (aliases.TryGetValue(liveAliases[i], out int prior) && prior != savedStates[i])
                    throw new InvalidDataException("单挑开场同一旗标实例含冲突值：" + savedNames[i] + "，index=" + i);
                aliases[liveAliases[i]] = savedStates[i];
            }
        }
    }
}
