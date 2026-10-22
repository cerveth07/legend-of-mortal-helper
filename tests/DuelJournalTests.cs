using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MortalHelper.Game;

internal static class DuelJournalTests
{
    private static int checks;
    private static void Check(bool ok, string name)
    { if (!ok) throw new Exception(name); checks++; }
    private static void Reject(Action test, string name)
    { try { test(); } catch (InvalidDataException) { checks++; return; } throw new Exception(name); }
    private static string Digest(string value)
    { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value))); }
    private static DuelJournal Example()
    {
        var value = new DuelJournal { Client = "1.0.0", SceneKey = "決鬥_001", NativeHash = Digest("native"),
            RandomState = "{\"s0\":123}", FinalState = "state:玩家體力=45" };
        value.Actions.Add(2); value.Actions.Add(4);
        value.Draws.Add(new DuelDraw { Site = "Combat.dll:100:0", Kind = 0, Floating = 0.12345679f });
        value.Draws.Add(new DuelDraw { Site = "Combat.dll:101:1", Kind = 1, A = -9, B = 21, Integer = 20 });
        value.Draws.Add(new DuelDraw { Site = "Combat.dll:102:2", Kind = 2, X = -3.1234567f, Y = 10.5f, Floating = 0.9876543f });
        value.Points.Add(new DuelPoint { Round = 1, DrawCount = 1, Digest = Digest("first") });
        value.Points.Add(new DuelPoint { Round = 2, DrawCount = 2, Digest = Digest("second") });
        value.Points.Add(new DuelPoint { Round = 4, DrawCount = 3, Digest = Digest(value.FinalState) });
        return value;
    }
    private static byte[] Seal(byte[] payload)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        using (var sha = SHA256.Create())
        { writer.Write(Encoding.ASCII.GetBytes("MHDL-02\n")); writer.Write(payload.Length);
          writer.Write(payload); writer.Write(sha.ComputeHash(payload)); return stream.ToArray(); }
    }
    public static int Run()
    {
        var original = Example();
        byte[] saved = DuelJournalCodec.Encode(original);
        var restored = DuelJournalCodec.Decode(saved);
        Check(restored.Client == original.Client && restored.SceneKey == original.SceneKey &&
            restored.RandomState == original.RandomState && restored.FinalState == original.FinalState &&
            restored.NativeHash == original.NativeHash, "metadata preserved");
        Check(restored.Actions.SequenceEqual(original.Actions), "all actions restored");
        Check(restored.Draws.Count == 3 && restored.Points.Count == 3, "custom lists present");
        for (int i = 0; i < 3; i++)
        {
            var a = original.Draws[i]; var b = restored.Draws[i];
            Check(a.Site == b.Site && a.Kind == b.Kind && a.A == b.A && a.B == b.B &&
                a.Integer == b.Integer && BitConverter.GetBytes(a.X).SequenceEqual(BitConverter.GetBytes(b.X)) &&
                BitConverter.GetBytes(a.Y).SequenceEqual(BitConverter.GetBytes(b.Y)) &&
                BitConverter.GetBytes(a.Floating).SequenceEqual(BitConverter.GetBytes(b.Floating)), "draw bits " + i);
            Check(original.Points[i].Round == restored.Points[i].Round &&
                original.Points[i].DrawCount == restored.Points[i].DrawCount &&
                original.Points[i].Digest == restored.Points[i].Digest, "checkpoint " + i);
        }
        Check(saved.SequenceEqual(DuelJournalCodec.Encode(restored)), "exact durable round trip");
        // Continue after restore and save again, including an unchanged random value.
        restored.Actions.Add(3); restored.Draws.Add(new DuelDraw { Site = "next", Kind = 0, Floating = 1f });
        restored.FinalState = "state:next";
        restored.Points.Add(new DuelPoint { Round = 5, DrawCount = 4, Digest = Digest(restored.FinalState) });
        var again = DuelJournalCodec.Decode(DuelJournalCodec.Encode(restored));
        Check(again.Actions.Count == 3 && again.Draws.Count == 4 && again.Points.Last().Round == 5,
            "restored journal remains appendable");
        byte[] broken = (byte[])saved.Clone(); broken[20] ^= 1;
        Reject(() => DuelJournalCodec.Decode(broken), "corruption rejected");
        Reject(() => DuelJournalCodec.Decode(saved.Take(saved.Length - 1).ToArray()), "truncation rejected");
        Reject(() => DuelJournalCodec.Decode(saved.Concat(new byte[] { 0 }).ToArray()), "trailing byte rejected");
        byte[] legacy = (byte[])saved.Clone(); legacy[6] = (byte)'1';
        Reject(() => DuelJournalCodec.Decode(legacy), "old lossy format rejected");
        var bad = Example(); bad.Points[2].DrawCount = 2;
        Reject(() => DuelJournalCodec.Encode(bad), "unconsumed draw rejected");
        bad = Example(); bad.Points[2].Digest = Digest("wrong");
        Reject(() => DuelJournalCodec.Encode(bad), "state mismatch rejected");
        bad = Example(); bad.Actions[0] = 999;
        Reject(() => DuelJournalCodec.Encode(bad), "unknown action rejected");
        bad = Example(); bad.Points[1].Round = 1;
        Reject(() => DuelJournalCodec.Encode(bad), "repeated round rejected");
        bad = Example(); bad.Draws[0].Floating = float.NaN;
        Reject(() => DuelJournalCodec.Encode(bad), "non-finite random rejected");
        bad = Example(); bad.Draws[0].Floating = 1.01f;
        Reject(() => DuelJournalCodec.Encode(bad), "random outside bounds rejected");
        byte[] payload = saved.Skip(12).Take(saved.Length - 44).ToArray();
        Reject(() => DuelJournalCodec.Decode(Seal(payload.Concat(new byte[] { 0 }).ToArray())),
            "well-hashed unknown payload rejected");
        // A valid checksum must not allow a malicious or corrupt collection count.
        byte[] excessive = (byte[])payload.Clone();
        using (var stream = new MemoryStream(excessive, true))
        using (var reader = new BinaryReader(stream, Encoding.UTF8))
        {
            reader.ReadInt32();
            for (int i = 0; i < 5; i++) reader.ReadString();
            int offset = (int)stream.Position;
            Buffer.BlockCopy(BitConverter.GetBytes(int.MaxValue), 0, excessive, offset, 4);
        }
        Reject(() => DuelJournalCodec.Decode(Seal(excessive)), "oversized count rejected before allocation");
        // No draws/actions is a legitimate save at the first stable action point.
        var first = Example(); first.Actions.Clear(); first.Draws.Clear(); first.Points.RemoveRange(0, 2);
        first.Points[0].DrawCount = 0;
        Check(DuelJournalCodec.Decode(DuelJournalCodec.Encode(first)).Draws.Count == 0, "zero draw checkpoint");
        string before = "3:hp\n2:104:buff3:a:b";
        string after = "3:hp\n1:94:buff3:a:c";
        string difference = DuelStateDifference.Compare(before, after);
        Check(difference.Contains("path=hp\n") && difference.Contains("expected=10") && difference.Contains("actual=9"), "audit differences identify exact field and values");
        Check(difference.Contains("path=buff") && difference.Contains("a:b") && difference.Contains("a:c"), "colons in state values remain unambiguous");
        Check(DuelStateDifference.Compare(before, before).Contains("different fields=0"), "identical audit state differs");
        Reject(() => DuelStateDifference.Compare("5:short", before), "truncated audit state accepted");
        var names = new[] { "campaign-A", "duplicate-name", "duplicate-name" };
        var states = new[] { 0, 1, 2 };
        DuelFlagRestorePlan.Validate(names, new[] { 0, 1, 2 }, names, states);
        Check(states[0] == 0 && states[1] == 1 && states[2] == 2, "separate assets sharing a name must preserve separate baseline values");
        DuelFlagRestorePlan.Validate(names, new[] { 0, 1, 1 }, names, new[] { 0, 2, 2 });
        Check(true, "shared flag asset with consistent values accepted");
        Reject(() => DuelFlagRestorePlan.Validate(names, new[] { 0, 1, 1 }, names, states), "aliased flag conflicting values accepted");
        Reject(() => DuelFlagRestorePlan.Validate(names, new[] { 0, 1, 2 }, new[] { "campaign-A", "wrong", "duplicate-name" }, states), "changed flag identity accepted");
        Reject(() => DuelFlagRestorePlan.Validate(names, new[] { 0, 1, 2 }, new[] { "campaign-A" }, states), "changed flag collection size accepted");
        Console.WriteLine("PASS: " + checks + " duel journal checks");
        return checks;
    }
}
