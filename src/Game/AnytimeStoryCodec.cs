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

    // Numeric edits occur inside an already established native wait. Completed
    // dialogue/choice events follow them, so edits must replay before that wait.
    internal static class StoryNumericEdits
    {
        internal static string Pack(string kind, string waitKey, string target)
        { return kind + ":" + waitKey + "\n" + target; }
        internal static string WaitKey(AnytimeStoryEvent entry, string kind)
        {
            if (entry == null || entry.Kind != "Numeric" || entry.Key == null || !entry.Key.StartsWith(kind + ":", StringComparison.Ordinal)) return null;
            int split = entry.Key.IndexOf('\n');
            return split < kind.Length + 2 ? null : entry.Key.Substring(kind.Length + 1, split - kind.Length - 1);
        }
        internal static int Apply(AnytimeStoryEvent[] events, int cursor, string kind, string key, Action<string, int, int> apply)
        {
            while (cursor < events.Length && key != null && events[cursor] != null && events[cursor].Kind == "Numeric" && WaitKey(events[cursor], kind) == key)
            {
                var entry = events[cursor];
                if (double.IsNaN(entry.Number) || double.IsInfinity(entry.Number) || entry.Number < int.MinValue ||
                    entry.Number > int.MaxValue || entry.Number != Math.Truncate(entry.Number)) throw new InvalidDataException("数值修改记录无效。");
                string target = entry.Key.Substring(entry.Key.IndexOf('\n') + 1);
                if (string.IsNullOrEmpty(target) || target.Length > 128) throw new InvalidDataException("数值修改目标无效。");
                apply(target, entry.Choice, (int)entry.Number); cursor++;
            }
            return cursor;
        }
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
        // Language the checkpoint was recorded in; -1 for records written before v4.
        // Replay keys are digests of the localised text, so this decides whether the
        // recorded dialogue can be matched at all.
        public int Language = -1;
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
        // Shared with the save path (AnytimeStory.BeforeNativeSave) so the pre-write
        // budget and the encoder cannot disagree about what "too large" means.
        internal const int Limit = 12000000;

        // The pre-write budget must cover every variable-length field, not just Expected:
        // otherwise a save is accepted, the game rewrites the slot, and the encoder then
        // refuses the payload after the fact.
        internal static long Measure(AnytimeStoryRecord record)
        {
            if (record == null) throw new InvalidDataException("剧情附加记录缺失。");
            long total = 32;
            foreach (var value in new[] { record.Client, record.NativeHash, record.Baseline, record.Expected,
                record.Script, record.Scene, record.WaitKind, record.WaitKey, record.BaselineRandomState,
                record.RandomState, record.BaselineLuaRandomState, record.LuaRandomState })
                total += value == null ? 1 : Encoding.UTF8.GetByteCount(value) + 4L;
            total += ValuesSize(record.BaselineGlobals);
            total += ValuesSize(record.ExpectedGlobals);
            if (record.Version >= 4) total += 4;
            if (record.Events != null)
                foreach (var entry in record.Events)
                {
                    if (entry == null) throw new InvalidDataException("剧情事件为空。");
                    total += 12 + (entry.Kind == null ? 1 : Encoding.UTF8.GetByteCount(entry.Kind)) +
                        (entry.Key == null ? 1 : Encoding.UTF8.GetByteCount(entry.Key));
                }
            return total;
        }

        private static long ValuesSize(AnytimeLuaValue[] values)
        {
            long total = 8;
            if (values == null) return total;
            foreach (var value in values)
            {
                if (value == null) throw new InvalidDataException("剧情变量记录为空。");
                total += 16 + Bytes(value.Key) + Bytes(value.Kind) + Bytes(value.Text);
                total += EntriesSize(value.Entries, 0);
            }
            return total;
        }

        // Mirrors the encoder so the pre-write budget cannot under-count a nested table.
        private static long EntriesSize(AnytimeLuaEntry[] entries, int depth)
        {
            if (entries != null && (depth > 32 || entries.Length > 10000))
                throw new InvalidDataException("剧情表嵌套或长度超限。");
            long total = 8;
            if (entries == null) return total;
            foreach (var entry in entries)
            {
                if (entry == null) throw new InvalidDataException("剧情表记录为空。");
                total += 20 + Bytes(entry.KeyKind) + Bytes(entry.Key) + Bytes(entry.Kind) + Bytes(entry.Text);
                total += EntriesSize(entry.Entries, depth + 1);
            }
            return total;
        }

        // A null text is written as the int -1, so it still costs four bytes; counting one
        // would let the pre-write budget under-count a sparse record.
        private static long Bytes(string value) => value == null ? 4L : Encoding.UTF8.GetByteCount(value) + 4L;

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
                // Appended after the event list so a v3 body still parses byte for byte.
                if (record.Version >= 4) writer.Write(record.Language);
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
                try
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
                    record.Language = record.Version >= 4 ? reader.ReadInt32() : -1;
                    if (stream.Position != stream.Length) throw new InvalidDataException("剧情附档含多余数据。");
                    return record;
                }
                // A truncated body must not surface as a raw .NET exception: the whole
                // entry point promises InvalidDataException for unusable input.
                catch (EndOfStreamException error) { throw new InvalidDataException("剧情附档被截断。", error); }
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
            byte[] bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw new InvalidDataException("剧情附档被截断。");
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("剧情附档编码损坏。", error); }
        }
    }
}
