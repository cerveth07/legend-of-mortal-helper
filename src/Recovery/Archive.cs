using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MortalHelper.Recovery
{
    public enum RecoveryKind : byte { Story = 1, Duel = 2 }
    public enum EventKind : byte { Dialogue = 1, Choice = 2, Random = 3, Dice = 4, Action = 5, Checkpoint = 6 }

    public sealed class RecordedEvent
    {
        public EventKind Kind;
        public string Key = "";
        public int Integer;
        public double Number;
        public string Data = "";
    }

    public sealed class RecoveryDocument
    {
        public RecoveryKind Kind;
        public string Client = "";
        public string Script = "";
        public string Scene = "";
        public string Checkpoint = "";
        public string Baseline = "";
        public string ExpectedState = "";
        public string RandomState = "";
        public readonly List<RecordedEvent> Events = new List<RecordedEvent>();
    }

    // A fixed schema: the input never selects CLR types, constructors or callbacks.
    // SHA-256 detects damage, not authorship or trustworthiness.
    public static class Archive
    {
        public const int MaximumBytes = 16 * 1024 * 1024;
        public const int MaximumEvents = 100000;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MHR1");

        public static byte[] Encode(RecoveryDocument document)
        {
            Validate(document);
            using (var data = new MemoryStream())
            using (var writer = new BinaryWriter(data, Utf8, true))
            {
                writer.Write((byte)document.Kind);
                Write(writer, document.Client, 256); Write(writer, document.Script, 2048);
                Write(writer, document.Scene, 64); Write(writer, document.Checkpoint, 4096);
                Write(writer, document.Baseline, 4000000); Write(writer, document.ExpectedState, 4000000);
                Write(writer, document.RandomState, 4096);
                writer.Write(document.Events.Count);
                foreach (var entry in document.Events)
                {
                    writer.Write((byte)entry.Kind); Write(writer, entry.Key, 4096);
                    writer.Write(entry.Integer); writer.Write(entry.Number); Write(writer, entry.Data, 16384);
                    if (data.Length > MaximumBytes - 40) throw new InvalidDataException("记录超过文件预算。");
                }
                writer.Flush();
                byte[] body = data.ToArray();
                using (var output = new MemoryStream())
                using (var envelope = new BinaryWriter(output))
                using (var hash = SHA256.Create())
                {
                    envelope.Write(Magic); envelope.Write(body.Length); envelope.Write(hash.ComputeHash(body));
                    envelope.Write(body); envelope.Flush();
                    if (output.Length > MaximumBytes) throw new InvalidDataException("记录超过文件预算。");
                    return output.ToArray();
                }
            }
        }

        public static RecoveryDocument Decode(Stream input)
        {
            using (var envelope = new BinaryReader(input, Utf8, true))
            {
                if (!Equal(Exact(envelope, 4), Magic)) throw new InvalidDataException("不是新恢复格式。");
                int length = envelope.ReadInt32();
                if (length < 0 || length > MaximumBytes - 40) throw new InvalidDataException("文件长度越界。");
                byte[] expected = Exact(envelope, 32);
                byte[] body = Exact(envelope, length);
                if (input.ReadByte() != -1) throw new InvalidDataException("文件含额外数据。");
                using (var hash = SHA256.Create())
                    if (!Equal(hash.ComputeHash(body), expected)) throw new InvalidDataException("文件校验失败。");
                using (var memory = new MemoryStream(body, false))
                using (var reader = new BinaryReader(memory, Utf8))
                {
                    var document = new RecoveryDocument {
                        Kind = (RecoveryKind)reader.ReadByte(), Client = Read(reader, 256),
                        Script = Read(reader, 2048), Scene = Read(reader, 64), Checkpoint = Read(reader, 4096),
                        Baseline = Read(reader, 4000000), ExpectedState = Read(reader, 4000000),
                        RandomState = Read(reader, 4096)
                    };
                    int count = reader.ReadInt32();
                    if (count < 0 || count > MaximumEvents) throw new InvalidDataException("事件数量越界。");
                    for (int i = 0; i < count; i++)
                        document.Events.Add(new RecordedEvent { Kind = (EventKind)reader.ReadByte(),
                            Key = Read(reader, 4096), Integer = reader.ReadInt32(), Number = reader.ReadDouble(),
                            Data = Read(reader, 16384) });
                    if (memory.Position != memory.Length) throw new InvalidDataException("未识别的数据字段。");
                    Validate(document);
                    return document;
                }
            }
        }

        public static void Validate(RecoveryDocument document)
        {
            if (document == null || (document.Kind != RecoveryKind.Story && document.Kind != RecoveryKind.Duel))
                throw new InvalidDataException("未知恢复种类。");
            if (string.IsNullOrEmpty(document.Client) || string.IsNullOrEmpty(document.Script) ||
                string.IsNullOrEmpty(document.Checkpoint) || string.IsNullOrEmpty(document.Baseline) ||
                string.IsNullOrEmpty(document.ExpectedState)) throw new InvalidDataException("恢复数据不完整。");
            if ((document.Kind == RecoveryKind.Story && document.Scene != "Story" && document.Scene != "Break") ||
                (document.Kind == RecoveryKind.Duel && document.Scene != "Combat"))
                throw new InvalidDataException("恢复种类与场景不匹配。");
            if (document.Events.Count > MaximumEvents) throw new InvalidDataException("事件数量越界。");
            foreach (var entry in document.Events)
            {
                if (entry == null || !Enum.IsDefined(typeof(EventKind), entry.Kind) ||
                    double.IsNaN(entry.Number) || double.IsInfinity(entry.Number))
                    throw new InvalidDataException("事件字段非法。");
                if ((entry.Kind == EventKind.Choice && entry.Integer < 1) ||
                    (entry.Kind == EventKind.Dice && entry.Integer < 0))
                    throw new InvalidDataException("选项编号或骰点非法。");
            }
        }

        // Archive.Digest() used to live here with zero callers; the real hashing helpers
        // are AnytimeStory.Digest, SlotTransaction.HashFile and ReplayStateDigest.

        private static void Write(BinaryWriter writer, string text, int maximum)
        {
            if (text == null || text.Length > maximum) throw new InvalidDataException("文本字段越界。");
            byte[] data = Utf8.GetBytes(text);
            if (data.Length > maximum) throw new InvalidDataException("文本字段越界。");
            writer.Write(data.Length); writer.Write(data);
        }

        private static string Read(BinaryReader reader, int maximum)
        {
            int length = reader.ReadInt32();
            if (length < 0 || length > maximum) throw new InvalidDataException("文本长度越界。");
            return Utf8.GetString(Exact(reader, length));
        }

        private static byte[] Exact(BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException("恢复文件被截断。");
            return bytes;
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int difference = 0;
            for (int i = 0; i < a.Length; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }
}
