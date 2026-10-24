using System;
using System.IO;
using MortalHelper.Game;

internal static class StoryContracts
{
    private static int checks;
    private static void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
    public static int Run()
    {
        var record = new AnytimeStoryRecord { Client = "release_1.0.6000.5", NativeHash = "hash==", Baseline = "{\"StartStoryScript\":\"起点\"}",
            Expected = "\u0000\"\\\n中文😀", Script = "ch5_6_3_1", Scene = "Story", WaitKind = "Choose", WaitKey = "key",
            BaselineRandomState = "unity0", RandomState = "unity1", BaselineLuaRandomState = "lua0", LuaRandomState = "lua1",
            BaselineGlobals = new[] { new AnytimeLuaValue { Key = "sayoptions", Kind = "t", Entries = new[] {
                new AnytimeLuaEntry { KeyKind = "n", Key = "1", Kind = "s", Text = "选择一" },
                new AnytimeLuaEntry { KeyKind = "s", Key = "same1", Kind = "n", Number = double.Epsilon } } } },
            ExpectedGlobals = new[] { new AnytimeLuaValue { Key = "bool", Kind = "b", Boolean = true }, new AnytimeLuaValue { Key = "n", Kind = "n", Number = Math.PI } },
            Events = new[] { new AnytimeStoryEvent { Kind = "Choose", Key = "key", Choice = 2 }, new AnytimeStoryEvent { Kind = "Random", Number = 0.12345678901234567 } } };
        string encoded = AnytimeStoryCodec.Write(record);
        var decoded = AnytimeStoryCodec.Read(encoded);
        Check(AnytimeStoryCodec.Write(decoded) == encoded, "all fields and UTF-8 round trip");
        Check(decoded.BaselineGlobals[0].Entries[1].Number == double.Epsilon, "subnormal numeric state");
        Check(decoded.Events[1].Number == record.Events[1].Number, "random double preserved exactly");
        Check(decoded.ExpectedGlobals[0].Boolean && decoded.ExpectedGlobals[1].Number == Math.PI, "expected scalar state");
        Check(decoded.BaselineGlobals[0].Entries[0].KeyKind == "n", "numeric table keys preserved");
        Check(decoded.BaselineLuaRandomState == "lua0" && decoded.LuaRandomState == "lua1", "independent random states");
        record.WaitKind = "Dice"; record.WaitKey = "identity|2|17|23|2|1|99|0|29|1";
        record.Events = new[] { new AnytimeStoryEvent { Kind = "DiceRoll", Key = "0|29", Choice = 1 },
            new AnytimeStoryEvent { Kind = "Dice", Choice = 17, Number = 2 } };
        decoded = AnytimeStoryCodec.Read(AnytimeStoryCodec.Write(record));
        Check(decoded.WaitKind == "Dice" && decoded.WaitKey == record.WaitKey, "full stable dice target preserved");
        Check(decoded.Events[0].Kind == "DiceRoll" && decoded.Events[0].Key == "0|29" && decoded.Events[0].Choice == 1,
            "reroll range and paid-fate history preserved");
        Check(decoded.Events[1].Choice == 17 && decoded.Events[1].Number == 2, "raw dice and forced selection remain independent");
        record.Events = new AnytimeStoryEvent[0]; record.BaselineGlobals = record.ExpectedGlobals = new AnytimeLuaValue[0];
        Check(AnytimeStoryCodec.Read(AnytimeStoryCodec.Write(record)).Events.Length == 0, "zero events at first dialogue");
        foreach (string bad in new[] { "{}", "MHA3\n!!", encoded.Substring(0, encoded.Length - 12), "MHA3\n" + Convert.ToBase64String(new byte[] {3, 0, 0, 0, 255, 255, 255, 127}) })
        {
            bool rejected = false;
            try { AnytimeStoryCodec.Read(bad); }
            catch (Exception e) { rejected = e is InvalidDataException || e is IOException || e is ArgumentException; }
            Check(rejected, "corrupt/truncated/legacy sidecar rejected");
        }
        Console.WriteLine("PASS: " + checks + " story codec behavior checks");
        return checks;
    }
}
