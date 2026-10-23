using System;
using System.IO;
using MortalHelper.Game;

internal static class NumericEditContracts
{
    internal static void Run(Action<bool, string> check)
    {
        string diceKey = "dice-identity|2|6|1|2|0|100|1|100|0";
        var dice = Edit("Dice", diceKey, "stat:命運", 3, 9);
        check(StoryNumericEdits.WaitKey(dice, "Dice") == diceKey, "numeric dice boundary preserves every pipe-separated component");
        check(StoryNumericEdits.WaitKey(dice, "Say") == null, "numeric boundary kind mismatch");

        foreach (string kind in new[] { "Say", "Choose" })
        {
            var completed = new AnytimeStoryEvent { Kind = kind, Key = "wait", Choice = 1 };
            int applied = 0;
            int cursor = StoryNumericEdits.Apply(new[] { Edit(kind, "wait", "stat:銀兩", 1, 2), completed }, 0,
                kind, "wait", (target, before, after) => { applied++; check(before == 1 && after == 2, "numeric values preserved before " + kind); });
            check(cursor == 1 && applied == 1 && completed.Kind == kind, "numeric edit consumed before completed " + kind);
        }

        int count = 0;
        int targetCursor = StoryNumericEdits.Apply(new[] { dice }, 0, "Dice", diceKey, (target, before, after) => count++);
        check(targetCursor == 1 && count == 1, "numeric edit can be last recorded event at saved target");

        int value = 5;
        var undo = new[] { Edit("Say", "wait", "item:2:0001", 5, 10), Edit("Say", "wait", "item:2:0001", 10, 5) };
        int undone = StoryNumericEdits.Apply(undo, 0, "Say", "wait", (target, before, after) => {
            check(value == before && target == "item:2:0001", "numeric edits and undo retain ordered preconditions");
            value = after;
        });
        check(undone == 2 && value == 5, "consecutive numeric edit and undo restore original quantity");

        int mismatched = StoryNumericEdits.Apply(undo, 0, "Say", "other", (target, before, after) => { throw new Exception("wrong wait applied"); });
        check(mismatched == 0, "different wait identity never consumes numeric edit");
        var old = new[] { new AnytimeStoryEvent { Kind = "Say", Key = "wait" } };
        check(StoryNumericEdits.Apply(old, 0, "Say", "wait", (target, before, after) => { throw new Exception("legacy event applied"); }) == 0,
            "legacy event records remain unchanged");

        foreach (double number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 1.5, (double)int.MaxValue + 1 })
        {
            var invalid = Edit("Say", "wait", "stat:銀兩", 0, number);
            bool rejected = false;
            try { StoryNumericEdits.Apply(new[] { invalid }, 0, "Say", "wait", (target, before, after) => { throw new Exception("bad number applied"); }); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "invalid numeric event value rejected: " + number);
        }
    }

    private static AnytimeStoryEvent Edit(string kind, string wait, string target, int before, double after)
    {
        return new AnytimeStoryEvent { Kind = "Numeric", Key = StoryNumericEdits.Pack(kind, wait, target), Choice = before, Number = after };
    }
}
