// Runs the actual Lua preamble in the game's MoonSharp interpreter without Unity.
// The host models a finite pool of effects that retire on subsequent frames.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;

internal static class ReplayPacingBehavior
{
    private static string source, managed;
    private static int checks;
    private static void Main(string[] args)
    {
        try
        {
            source = args[0]; managed = args[1];
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
            {
                string path = Path.Combine(managed, new AssemblyName(request.Name).Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            };
            Run(args.Length > 2 && args[2] == "--expect-exhaustion");
        }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); checks++; }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run(bool expectExhaustion)
    {
        string text = File.ReadAllText(source);
        const string marker = "private static readonly string Preamble = @\"";
        int start = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        if (start < marker.Length) throw new Exception("Production Lua preamble not found");
        int end = text.IndexOf("\";", start, StringComparison.Ordinal);
        string preamble = text.Substring(start, end - start).Replace("\"\"", "\"");
        var script = new Script(CoreModules.Preset_Complete);
        var effects = new List<int>(); int peak = 0, resumes = 0;
        script.Globals.Set("native_tweens", DynValue.NewCallback((context, args) =>
        {
            for (int i = 0; i < 6; i++) effects.Add(3);
            peak = Math.Max(peak, effects.Count);
            if (effects.Count > 400) throw new InvalidOperationException("TweenPoolExhausted");
            return DynValue.Nil;
        }));
        script.DoString(@"
replaying = true
say_replayed, choose_replayed, say_raw, choose_raw, timer_raw, wait_raw = 0,0,0,0,0,0
enters, leaves, say_records, choose_records, random_records = 0,0,0,0,0
say = function(text, voice) say_raw=say_raw+1; last_text=text; last_voice=voice; coroutine.yield() end
choose = function(options) choose_raw=choose_raw+1; coroutine.yield(); return 1 end
choosetimer = function(options, duration, fallback) timer_raw=timer_raw+1; coroutine.yield(); return fallback end
wait = function(duration) wait_raw=wait_raw+1; last_duration=duration; coroutine.yield() end
__mortal_anytime = {
 before_say=function(text) if replaying then say_replayed=say_replayed+1; return true end; return false end,
 say_done=function(text) say_records=say_records+1 end,
 before_choose=function(key,count) if replaying then choose_replayed=choose_replayed+1; return 2 end; return 0 end,
 choose_done=function(key,choice) choose_records=choose_records+1; last_choice=choice end,
 skip_wait=function() return replaying end,
 enter_wait=function(kind,key) enters=enters+1; return enters end,
 leave_wait=function(token) leaves=leaves+1 end,
 random_draw=function(value) random_records=random_records+1; return value end
}");
        script.DoString(preamble);
        var fn = script.LoadString(@"
for i=1,500 do native_tweens(); say('recorded-'..i) end
assert(choose({'A','B'})==2)
assert(choosetimer({'A','B'},3,1)==2)
for i=1,500 do native_tweens(); wait(1) end
replaying=false
say('target','voice')
assert(choose({'A','B'})==1)
assert(choosetimer({'A','B'},3,2)==2)
wait(2)
random_value=math.random(1,100)
finished=true
");
        var coroutine = script.CreateCoroutine(fn).Coroutine;
        bool exhausted = false;
        try
        {
            while (coroutine.State != CoroutineState.Dead)
            {
                if (++resumes > 1100) throw new Exception("Replay never reached target");
                coroutine.Resume();
                for (int i = effects.Count - 1; i >= 0; i--)
                    if (--effects[i] <= 0) effects.RemoveAt(i);
            }
        }
        catch (Exception error) when (error.ToString().Contains("TweenPoolExhausted")) { exhausted = true; }
        if (expectExhaustion)
        {
            Check(exhausted && resumes == 1, "Original replay must exhaust pool in its first host frame");
            Console.WriteLine("PASS: reproduced original same-frame tween-pool exhaustion with production Lua and game MoonSharp");
            return;
        }
        Check(!exhausted && peak <= 18, "Skipped interactions must allow native effects to retire");
        Check(script.Globals.Get("finished").Boolean, "Replay reaches its target");
        Check(script.Globals.Get("say_replayed").Number == 500, "Every recorded dialogue consumed once");
        Check(script.Globals.Get("choose_replayed").Number == 2, "Both choice forms preserve recorded selections");
        Check(script.Globals.Get("say_raw").Number == 1 && script.Globals.Get("last_text").String == "target" &&
            script.Globals.Get("last_voice").String == "voice", "Only live target dialogue reaches native say with voice intact");
        Check(script.Globals.Get("choose_raw").Number == 1 && script.Globals.Get("timer_raw").Number == 1,
            "Normal choices retain their native functions");
        Check(script.Globals.Get("wait_raw").Number == 1 && script.Globals.Get("last_duration").Number == 2,
            "Skipped waits yield to host while normal wait duration stays intact");
        Check(script.Globals.Get("say_records").Number == 1 && script.Globals.Get("choose_records").Number == 2,
            "Pacing adds no replayed dialogue or choices to the recording");
        Check(script.Globals.Get("enters").Number == 3 && script.Globals.Get("leaves").Number == 3,
            "Only native waits acquire balanced tokens");
        Check(script.Globals.Get("random_records").Number == 1, "Pacing consumes no additional Lua RNG draws");
        Check(resumes == 1007, "Each skipped interaction yields exactly one frame, normal waits stay native");
        Console.WriteLine("PASS: " + checks + " production replay-pacing checks using game MoonSharp; peak modeled effects=" + peak);
    }
}
