using System;
using System.Collections.Generic;
using System.Linq;
using MortalHelper.Core;

internal static class TitleContracts
{
    internal static void Run(Action<bool, string> check)
    {
        var a = new TitleHistory();
        a.Reset(0); a.Observe(18); a.Select(18);
        var savedA = a.Save().ToArray();
        check(a.Contains(0) && a.Contains(18) && a.Selected == 18, "A title acquisition/selection");
        a.Reset(0);
        check(a.Selected == -1 && !a.Contains(18) && a.Contains(0), "new game must not inherit A titles");
        a.Observe(3); a.Select(3);
        var savedB = a.Save().ToArray();
        a.Load(savedA, 18);
        check(a.Selected == 18 && a.Contains(18) && !a.Contains(3), "load A after B must replace, not merge");
        a.Load(savedB, 3);
        check(a.Selected == 3 && a.Contains(3) && !a.Contains(18), "load B after A must replace, not merge");
        a.Load(null, 7);
        check(a.Selected == -1 && a.Acquired.SequenceEqual(new[] { 7 }), "legacy save seeds only its current native title");
        a.Load(savedA, 18); a.Observe(21);
        a.Load(savedA, 18);
        check(!a.Contains(21), "load older branch rolls acquired titles back");
        a.Reset(0); a.Observe(2); var oldSlot = a.Save().ToArray();
        a.Reset(0); a.Observe(9); oldSlot = a.Save().ToArray();
        a.Load(oldSlot, 9);
        check(a.Contains(9) && !a.Contains(2), "overwritten slot cannot leak previous save titles");
        a.Reset(0); a.Select(25);
        check(a.Selected == 25 && !a.Contains(25), "all-catalog selection does not falsely grant title");
        a.Load(a.Save().ToArray(), 0);
        check(a.Selected == 25 && !a.Contains(25), "unearned cosmetic selection persists per save");
        a.Select(-1);
        check(a.Selected == -1, "follow story option");
        check(!a.Select(-2) && !a.Select(1000) && a.Selected == -1, "invalid selection rejected");
        a.Observe(18); a.Observe(18); a.Observe(-1); a.Observe(1000);
        check(a.Acquired.SequenceEqual(new[] { 0, 18 }), "history unique bounded sorted");
        check(a.Save().SequenceEqual(a.Save()), "capture does not mutate cosmetic state");
        var corrupt = new[] {
            new KeyValuePair<string,int>(TitleHistory.Prefix+"version", 1),
            new KeyValuePair<string,int>(TitleHistory.Prefix+"owned.18", 1),
            new KeyValuePair<string,int>(TitleHistory.Prefix+"owned.18", 1),
            new KeyValuePair<string,int>(TitleHistory.Prefix+"owned.-5", 1),
            new KeyValuePair<string,int>(TitleHistory.Prefix+"owned.bad", 1),
            new KeyValuePair<string,int>(TitleHistory.Prefix+"owned.19", 0),
            new KeyValuePair<string,int>(TitleHistory.Prefix+"selected", 1000),
            new KeyValuePair<string,int>("title", 25) };
        a.Load(corrupt, 3);
        check(a.Acquired.SequenceEqual(new[] { 3, 18 }) && a.Selected == -1, "malformed metadata cannot import unrelated titles");
        a.Load(new[] {new KeyValuePair<string,int>(TitleHistory.Prefix+"version", 99)}, 2);
        check(a.Acquired.SequenceEqual(new[] { 2 }) && a.Selected == -1, "unknown schema uses native current title only");
        check(!TitleHistory.IsMetadata("title") && !TitleHistory.IsMetadata(null) && TitleHistory.IsMetadata("mh.title.selected"), "audit must retain native title key");
    }
}
