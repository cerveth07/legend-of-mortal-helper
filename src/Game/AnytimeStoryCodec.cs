using System;
using System.IO;
using System.Text;

namespace MortalHelper.Game
{
    [Serializable]
    internal sealed class AnytimeStoryEvent
    {
        public string Kind;
        public string Key;
        public int Choice;
        public double Number;
    }

    [Serializable]
    internal sealed class AnytimeLuaValue
    {
        public string Key;
        public string Kind;
        public string Text;
        public double Number;
        public bool Boolean;
        public AnytimeLuaEntry[] Entries;
    }

    [Serializable]
    internal sealed class AnytimeStoryRecord
    {
        public int Version = 3;
        public string Client;
        public string NativeHash;
        public string Baseline;
        public string Expected;
        public string Script;
        public string Scene;
        public string WaitKind;
        public string WaitKey;
        public string BaselineRandomState;
        public string RandomState;
        public string BaselineLuaRandomState;
        public string LuaRandomState;
        public AnytimeLuaValue[] BaselineGlobals;
        public AnytimeLuaValue[] ExpectedGlobals;
        public AnytimeStoryEvent[] Events;
    }

    [Serializable]
    internal sealed class AnytimeLuaEntry
    {
        public string KeyKind, Key, Kind, Text;
        public double Number;
        public bool Boolean;
        public AnytimeLuaEntry[] Entries;
    }

    // Unity's serializer drops plugin-owned class arrays on the Mono player.
    // This format names no runtime types and bounds allocations before reading.
    // Old JSON sidecars fail explicitly instead of silently loading empty arrays.
    internal static class AnytimeStoryCodec
    {
        private const int Limit = 12000000;
        internal static string Write(AnytimeStoryRecord record)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(record.Version);
                foreach (var value in new[] { record.Client, record.NativeHash, record.Baseline, record.Expected,
                    record.Script, record.Scene, record.WaitKind, record.WaitKey, record.BaselineRandomState,
                    record.RandomState, record.BaselineLuaRandomState, record.LuaRandomState }) Text(writer, value);
                Values(writer, record.BaselineGlobals); Values(writer, record.ExpectedGlobals);
                if (record.Events == null) throw new InvalidDataException("剧情事件记录缺失。");
                writer.Write(record.Events.Length);
                foreach (var entry in record.Events)
                {
                    if (entry == null) throw new InvalidDataException("剧情事件为空。");
                    Text(writer, entry.Kind); Text(writer, entry.Key); writer.Write(entry.Choice); writer.Write(entry.Number);
                }
                writer.Flush();
                if (stream.Length > Limit) throw new InvalidDataException("剧情附加记录过大。");
                return "MHA3\n" + Convert.ToBase64String(stream.ToArray());
            }
        }
        internal static AnytimeStoryRecord Read(string encoded)
        {
            if (encoded == null || encoded.Length > 16000000 || !encoded.StartsWith("MHA3\n", StringComparison.Ordinal))
                throw new InvalidDataException("剧情附档格式不支持；请使用完整原始备份或重新保存。");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded.Substring(5)); }
            catch (FormatException error) { throw new InvalidDataException("剧情附档编码损坏。", error); }
            if (bytes.Length > Limit) throw new InvalidDataException("剧情附档超限。");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var record = new AnytimeStoryRecord { Version = reader.ReadInt32(),
                    Client = Text(reader), NativeHash = Text(reader), Baseline = Text(reader), Expected = Text(reader),
                    Script = Text(reader), Scene = Text(reader), WaitKind = Text(reader), WaitKey = Text(reader),
                    BaselineRandomState = Text(reader), RandomState = Text(reader),
                    BaselineLuaRandomState = Text(reader), LuaRandomState = Text(reader),
                    BaselineGlobals = Values(reader), ExpectedGlobals = Values(reader) };
                record.Events = new AnytimeStoryEvent[Count(reader, 20000)];
                for (int i = 0; i < record.Events.Length; i++)
                    record.Events[i] = new AnytimeStoryEvent { Kind = Text(reader), Key = Text(reader), Choice = reader.ReadInt32(), Number = reader.ReadDouble() };
                if (stream.Position != stream.Length) throw new InvalidDataException("剧情附档含多余数据。");
                return record;
            }
        }
        private static void Values(BinaryWriter writer, AnytimeLuaValue[] values)
        {
            if (values == null) throw new InvalidDataException("剧情变量记录缺失。");
            writer.Write(values.Length);
            foreach (var value in values)
            {
                if (value == null) throw new InvalidDataException("剧情变量为空。");
                Text(writer, value.Key); Text(writer, value.Kind); Text(writer, value.Text);
                writer.Write(value.Number); writer.Write(value.Boolean);
                Entries(writer, value.Entries, 0);
            }
        }
        private static AnytimeLuaValue[] Values(BinaryReader reader)
        {
            var values = new AnytimeLuaValue[Count(reader, 10000)];
            for (int i = 0; i < values.Length; i++)
            {
                var value = new AnytimeLuaValue { Key = Text(reader), Kind = Text(reader), Text = Text(reader),
                    Number = reader.ReadDouble(), Boolean = reader.ReadBoolean() };
                value.Entries = Entries(reader, 0);
                values[i] = value;
            }
            return values;
        }
        private static void Entries(BinaryWriter writer, AnytimeLuaEntry[] entries, int depth)
        {
            if (entries != null && (depth > 32 || entries.Length > 10000)) throw new InvalidDataException("剧情表嵌套或长度超限。");
            writer.Write(entries == null ? -1 : entries.Length);
            if (entries == null) return;
            foreach (var entry in entries)
            {
                Text(writer, entry.KeyKind); Text(writer, entry.Key); Text(writer, entry.Kind); Text(writer, entry.Text);
                writer.Write(entry.Number); writer.Write(entry.Boolean);
                Entries(writer, entry.Entries, depth + 1);
            }
        }
        private static AnytimeLuaEntry[] Entries(BinaryReader reader, int depth)
        {
            int count = reader.ReadInt32();
            if (count == -1) return null;
            if (depth > 32) throw new InvalidDataException("剧情表嵌套超限。");
            if (count < 0 || count > 10000 || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("剧情表记录大小无效。");
            var entries = new AnytimeLuaEntry[count];
            for (int i = 0; i < count; i++)
                entries[i] = new AnytimeLuaEntry { KeyKind = Text(reader), Key = Text(reader), Kind = Text(reader),
                    Text = Text(reader), Number = reader.ReadDouble(), Boolean = reader.ReadBoolean(), Entries = Entries(reader, depth + 1) };
            return entries;
        }
        private static int Count(BinaryReader reader, int maximum)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum || count > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("剧情数组大小无效。");
            return count;
        }
        private static void Text(BinaryWriter writer, string value)
        {
            if (value == null) { writer.Write(-1); return; }
            var bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length > Limit) throw new InvalidDataException("剧情文本超限。");
            writer.Write(bytes.Length); writer.Write(bytes);
        }
        private static string Text(BinaryReader reader)
        {
            int size = reader.ReadInt32();
            if (size == -1) return null;
            if (size < 0 || size > Limit || size > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("剧情文本大小无效。");
            return new UTF8Encoding(false, true).GetString(reader.ReadBytes(size));
        }
    }
}
