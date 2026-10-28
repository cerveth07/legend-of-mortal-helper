using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using MortalHelper.Core;
using MortalHelper.Recovery;
using MortalHelper.Game;

internal static class Contracts
{
    private sealed class LegacySnapshotService { public int Calls; private string CreateSaveData() { Calls++; return "legacy"; } }
    private sealed class CurrentSnapshotService { public bool IncludeLog; private string CreateSaveData(bool includeStoryLog = true) { IncludeLog = includeStoryLog; return "current"; } }
    private sealed class UnsupportedSnapshotService { private string CreateSaveData(int unknown) { return "unsupported"; } }
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception e) when (e is InvalidDataException || e is EndOfStreamException ||
            e is ArgumentException || e is InvalidOperationException || e is DecoderFallbackException ||
            e is IOException) { rejected = true; }
        Check(rejected, message);
    }

    private static RecoveryDocument Document()
    {
        var d = new RecoveryDocument { Kind = RecoveryKind.Story, Client = "test-client", Script = "synthetic",
            Scene = "Story", Checkpoint = "choice:2", Baseline = "{\"test\":1}", ExpectedState = "state-2" };
        d.Events.Add(new RecordedEvent { Kind = EventKind.Choice, Key = "choice:1", Integer = 2 });
        return d;
    }

    private static void Main()
    {
        var legacySnapshot = new LegacySnapshotService();
        Check((string)NativeSnapshot.Create(legacySnapshot) == "legacy" && legacySnapshot.Calls == 1, "legacy native snapshot signature remains supported");
        var currentSnapshot = new CurrentSnapshotService();
        Check((string)NativeSnapshot.Create(currentSnapshot) == "current" && currentSnapshot.IncludeLog, "new native snapshot includes story log like manual save");
        Reject(() => NativeSnapshot.Create(null), "missing snapshot service rejected");
        bool unsupportedSnapshot = false;
        try { NativeSnapshot.Create(new UnsupportedSnapshotService()); }
        catch (MissingMethodException) { unsupportedSnapshot = true; }
        Check(unsupportedSnapshot, "unknown future snapshot signature rejected explicitly");
        Check(UiLanguage.Convert("剧情辅助·万象录", true) == "劇情輔助·萬象錄", "traditional UI conversion failed");
        Check(UiLanguage.Convert("劇情輔助·萬象錄", false) == "剧情辅助·万象录", "simplified UI conversion failed");
        Check(UiLanguage.Convert("群战·郁竹·因为", true) == "群戰·郁竹·因為", "traditional UI wording changed names");
        Check(UiLanguage.Convert("ID 42 / F2", true) == "ID 42 / F2", "non-Chinese UI text changed");
        TitleContracts.Run(Check);
        CheckpointCompatibilityContracts.Run(Check);
        NumericEditContracts.Run(Check);
        var session = new InteractionSession(); string error; int? number;
        Check(!session.Commit(0,"",0,100,null,2,out error),"closed session accepted click");
        session.Open(); long first = session.Generation;
        Check(!session.Commit(first,"101",0,100,null,2,out error),"out of range point accepted");
        Check(session.Commit(first,"101",0,100,2,2,out error),"direct choice did not override invalid point");
        Check(session.RequestedOption == 2 && !session.RequestedPoint.HasValue,"intents leaked");
        Check(!session.Commit(first,"",0,100,1,2,out error),"double submit accepted");
        Check(!session.Rearm(true),"rearmed while rolling");
        session.Resolved(); Check(!session.RequestedOption.HasValue,"choice leaked into next roll");
        Check(!session.Rearm(false) && session.Rearm(true),"reroll availability not respected");
        Check(session.Commit(first,"-50",-50,50,null,2,out error),"negative range rejected");
        session.Close(); session.Open();
        Check(!session.Commit(first,"",0,100,1,2,out error),"stale callback accepted");
        Check(InteractionSession.ParsePoint("",1,2,out number) && !number.HasValue,"natural mode invalid");
        foreach(string value in new[]{"1.5","1e2","999999999999999999","NaN","1,000"})
            Check(!InteractionSession.ParsePoint(value,-100,100,out number),"invalid numeric input accepted");

        string prefix = "--index:O_test\n";
        var hints = ScriptHints.Read(prefix + "statmodifymanager.Player(\"Money\", 5)\nend");
        Check(hints["O_test"].Effects.Count==1,"simple effect not parsed");
        Check(hints["O_test"].Effects[0].Kind==EffectKind.Stat,"stat effect misclassified");
        hints = ScriptHints.Read(prefix + "if a then\nstatmodifymanager.Player(\"Money\", 999)\nend");
        Check(hints["O_test"].Effects.Count==1 && hints["O_test"].Effects[0].Condition!=null && hints["O_test"].Incomplete,
            "conditional effect dropped or claimed definite");
        // A switch alias makes the conditional readable: the local variable name means
        // nothing to a player, the branch id does.
        hints = ScriptHints.Read(prefix + "local r = checkpointmanager.Switch(\"S7\")\nif r == 2 then\n" +
            "statmodifymanager.Character(\"brother2\", 2, 1)\nend");
        Check(hints["O_test"].Effects.Count==1 && hints["O_test"].Effects[0].Condition=="分支 S7 = 2",
            "switch condition not resolved");
        Check(hints["O_test"].Consequences.Count==1 && hints["O_test"].Consequences[0]=="分支点 S7",
            "switch call lost to alias tracking");
        // A function body may never run, so its calls are not listed at all.
        hints = ScriptHints.Read(prefix + "local function f()\nstatmodifymanager.Player(\"Money\", 1)\nend");
        Check(hints["O_test"].Effects.Count==0 && hints["O_test"].Incomplete,"function body effect listed");
        // Real scripts open an option with presentation calls and change stats afterwards;
        // the scan must survive them instead of stopping at the first unfamiliar word.
        hints = ScriptHints.Read(prefix + "runblock(flowcharts.view, \"out\")\n" +
            "stage.hide{character=characters.Get(\"player\"), fadeDuration=0}\n" +
            "statmodifymanager.Player(\"training\", 1, \"\", 1)\nend");
        Check(hints["O_test"].Effects.Count==1 && hints["O_test"].Effects[0].Arguments[0]=="training",
            "presentation call hid the later effect");
        hints = ScriptHints.Read(prefix + "statmodifymanager.Player(\"training\",-1,\"\",1)\n" +
            "wait(statmodifymanager.GetDisplayTime(\"training\"))\n" +
            "statmodifymanager.Player(\"disposition\",1,\"\",1)\nend");
        Check(hints["O_test"].Effects.Count==2,"display-time call ended the scan");
        hints = ScriptHints.Read("--[=[\n--index:FAKE\nstatmodifymanager.Player(\"Money\", 999)\n]=]\n" + prefix + "statmodifymanager.AddMisc('a',-1)");
        Check(hints.Count==1 && hints["O_test"].Effects[0].Arguments[1]=="-1","long comment parsed as code");
        hints = ScriptHints.Read(prefix + "statmodifymanager.AddMisc([=[text --index:FAKE]=],1)");
        Check(hints.Count==1 && hints["O_test"].Effects.Count==1,"long literal mishandled");
        Check(ScriptHints.Read(prefix+"x()\n"+prefix+"x()").Count==0,"duplicate marker retained");
        Check(ScriptHints.Read(prefix+"statmodifymanager.AddMisc('broken,1)").Count==0,"unterminated quote accepted");
        hints=ScriptHints.Read(prefix+"unknown()\nstatmodifymanager.Player('Money',5)");
        Check(hints["O_test"].Effects.Count==1,"unfamiliar call ended the scan");
        hints=ScriptHints.Read(prefix+"statmodifymanager.Player('Money',calculate())");
        Check(hints["O_test"].Effects.Count==0,"nonliteral effect accepted");
        Check(ScriptHints.Read(new string(' ',2000001)).Count==0,"oversized input parsed");
        Check(ScriptHints.Read("foo() --index:FAKE\nstatmodifymanager.Player('Money',1)").Count==0,"inline comment became branch marker");

        var consequence=ScriptHints.Read(prefix+"checkpointmanager.Switch(\"S12\")\nluamanager.SetNextScript(\"S0017_02\")\nend");
        Check(consequence["O_test"].Consequences.Count==2 && consequence["O_test"].Consequences[0]=="分支点 S12","consequence scan failed");
        Check(ScriptHints.Read(prefix+"statmodifymanager.AddStory(\"S03\")")["O_test"].Consequences[0]=="记录 S03","story consequence mislabelled");
        Check(ScriptHints.Read("--[=[\ncheckpointmanager.Switch(\"FAKE\")\n]=]\n"+prefix+"x()")["O_test"].Consequences.Count==0,"comment produced a consequence");
        Check(ScriptHints.Read(prefix+"-- checkpointmanager.Switch(\"FAKE\")\nx()")["O_test"].Consequences.Count==0,"line comment produced a consequence");
        Check(ScriptHints.Read(prefix+"checkpointmanager.Switch(\"A\")\ncheckpointmanager.Switch(\"A\")")["O_test"].Consequences.Count==1,"duplicate consequence kept");
        Check(ScriptHints.Read(prefix+"luamanager.ChangeScene(\"GameOver\",\"10061\",\"\")")["O_test"].Consequences[0]=="场景 GameOver","scene consequence mislabelled");
        // A branch consequence inside a condition is not definite either.
        Check(ScriptHints.Read(prefix+"if a then\ncheckpointmanager.Switch(\"S9\")\nend")["O_test"].Consequences.Count==0,"conditional consequence claimed definite");

        string table = "local A = checkpointmanager.Condition(\"Talk_Option_01\")\n" +
            "option_001[2] = A and \"O_unlocked\" or \"~O_locked\"\n" +
            "option_001[3] = \"O_plain\"\n";
        var locked = ScriptOptions.Read(table);
        Check(locked.Count==2,"condition rows not indexed by both texts");
        Check(locked["O_locked"].Name=="Talk_Option_01" && locked["O_locked"].HasLocked,"locked row lost its requirement");
        Check(locked["O_locked"].TextKey=="O_unlocked" && locked["O_locked"].LockedKey=="O_locked","locked row key pair wrong");
        Check(locked["O_unlocked"].HasLocked,"unlocked row lost the locked variant");
        Check(!locked.ContainsKey("O_plain"),"plain row treated as conditional");
        Check(ScriptOptions.Read("option_001[2] = Unknown and \"O_a\" or \"~O_b\"").Count==0,"unknown variable became a requirement");
        Check(ScriptOptions.Read("-- local A = checkpointmanager.Condition(\"X\")\n-- option_001[2] = A and \"O_a\" or \"~O_b\"\n").Count==0,"commented row became a requirement");
        Check(ScriptOptions.Read("--[[\nlocal A = checkpointmanager.Condition(\"X\")\noption_001[2] = A and \"O_a\" or \"~O_b\"\n]]\n").Count==0,"long comment became a requirement");
        var switched = ScriptOptions.Read("local S = checkpointmanager.Switch(\"S_Kar_GE_40\")\noption_001[1] = S == 1 and \"O_a\" or \"~O_b\"\n");
        Check(switched["O_b"].Kind==RequirementKind.Switch && switched["O_b"].Name=="S_Kar_GE_40","switch requirement misread");

        // A battle-start save is a native file plus a label; the codec must round-trip and
        // refuse anything it cannot name exactly.
        var start = new DuelStartRecord { Client = "release_1.0.6000.5", SceneKey = "1512_01",
            NativeHash = "abc+/=", SavedAt = "2026-10-24 01:00:00" };
        string encoded = DuelStartCodec.Encode(start);
        var decoded = DuelStartCodec.Decode(encoded);
        Check(decoded.SceneKey=="1512_01" && decoded.NativeHash=="abc+/=" && decoded.Client=="release_1.0.6000.5",
            "battle-start marker round trip failed");
        Reject(()=>DuelStartCodec.Decode(encoded.Replace("scene=1512_01\n","")), "marker without scene accepted");
        Reject(()=>DuelStartCodec.Decode(encoded + "extra=1\n"), "marker with unknown field accepted");
        Reject(()=>DuelStartCodec.Decode(encoded + "scene=1512_02\n"), "duplicate marker field accepted");
        Reject(()=>DuelStartCodec.Decode(encoded.Replace("version=1","version=9")), "unsupported marker version accepted");
        Reject(()=>DuelStartCodec.Decode(""), "empty marker accepted");
        Reject(()=>DuelStartCodec.Encode(new DuelStartRecord { Client="x", SceneKey="a\nb", NativeHash="h" }),
            "marker field with newline accepted");
        // Failure-exit policy: a failed recovery keeps the mutation gate closed, but it may
        // never keep the input lock, and it only asks for the title once the scene is stable.
        Check(!FailureExit.ShouldRequestTitle(true, false, 99f), "title requested while a scene was loading");
        Check(!FailureExit.ShouldRequestTitle(false, false, FailureExit.TitleDelay - 0.1f),
            "title requested before the failure settled");
        Check(FailureExit.ShouldRequestTitle(false, false, FailureExit.TitleDelay),
            "stable scene did not request the title");
        Check(!FailureExit.ShouldRequestTitle(false, true, 99f), "title request repeated");
        Check(!FailureExit.ShouldReleaseInput(FailureExit.InputReleaseLimit - 0.1f) &&
            FailureExit.ShouldReleaseInput(FailureExit.InputReleaseLimit), "input release limit wrong");
        Check(FailureExit.FailReason("")=="恢复失败" && FailureExit.FailReason("x")=="x", "failure reason fallback wrong");
        // The recovery must not stay locked behind a scene that is not coming, and a load that
        // claims to be loading forever is not a load that will finish.
        Check(FailureExit.ShouldAbortForMissingScene(false, FailureExit.SceneReadyLimit - 1f) == false &&
            FailureExit.ShouldAbortForMissingScene(false, FailureExit.SceneReadyLimit) == true,
            "missing-scene deadline wrong");
        Check(!FailureExit.ShouldAbortForMissingScene(true, 999f), "ready scene reported as missing");
        Check(!FailureExit.LoadStalled(true, FailureExit.LoadStallHardLimit - 1f) &&
            FailureExit.LoadStalled(true, FailureExit.LoadStallHardLimit), "loading stall window wrong");
        Check(!FailureExit.LoadStalled(false, FailureExit.NativeLoadIdleLimit - 1f) &&
            FailureExit.LoadStalled(false, FailureExit.NativeLoadIdleLimit), "idle stall window wrong");
        Check(FailureExit.LoadStallHardLimit > FailureExit.InputReleaseLimit,
            "hard load limit must outlast the input release window");
        // The pre-write budget must cover every field, not just Expected.
        var measured = new AnytimeStoryRecord { Client="c", NativeHash="h", Baseline=new string('x', 1000),
            Expected=new string('y', 1000), Script="s", Scene="Story", WaitKind="Say", WaitKey="k",
            BaselineRandomState="{}", RandomState="{}", BaselineLuaRandomState="{}", LuaRandomState="{}",
            BaselineGlobals=new AnytimeLuaValue[0], ExpectedGlobals=new AnytimeLuaValue[0],
            Events=new AnytimeStoryEvent[0] };
        Check(AnytimeStoryCodec.Measure(measured) >= 2000, "measure under-counts the variable fields");
        measured.Baseline = new string('x', AnytimeStoryCodec.Limit / 2 + 100);
        measured.Expected = new string('y', AnytimeStoryCodec.Limit / 2 + 100);
        Check(AnytimeStoryCodec.Measure(measured) > AnytimeStoryCodec.Limit,
            "measure does not reach the encoder limit");

        // The budget must be an upper bound on what the encoder actually writes, otherwise a
        // save passes the pre-check and fails only after the native file was replaced.
        var budgeted = new AnytimeStoryRecord { Client="c", NativeHash="h", Baseline="{\"a\":\"b\"}",
            Expected="{\"a\":\"b\"}", Script="ch_1_1", Scene="Story", WaitKind="Say", WaitKey="wk",
            BaselineRandomState="{}", RandomState="{}", BaselineLuaRandomState="{}", LuaRandomState="{}",
            BaselineGlobals=new[] { new AnytimeLuaValue { Key="a", Kind="number", Text="1",
                Entries=new[] { new AnytimeLuaEntry { KeyKind="string", Key="k", Kind="bool", Text=null } } } },
            ExpectedGlobals=new AnytimeLuaValue[0],
            Events=new[] { new AnytimeStoryEvent { Kind="Say", Key="abcdef" },
                new AnytimeStoryEvent { Kind="Numeric", Key=null, Choice=2, Number=7 } } };
        string encodedSample = AnytimeStoryCodec.Write(budgeted);
        long payload = Convert.FromBase64String(encodedSample.Substring("MHA3\n".Length)).Length;
        Check(AnytimeStoryCodec.Measure(budgeted) >= payload,
            "measure under-counts the encoded payload (" + AnytimeStoryCodec.Measure(budgeted) + " < " + payload + ")");
        // The translation tables are dictionary initialisers: a duplicate key throws during
        // static initialisation, which would break every Japanese/Korean lookup at runtime.
        // Touching them here makes that a build failure instead of an in-game one.
        Check(UiText.Lookup("剧情", UiLang.Ja) == "ストーリー", "ja table did not resolve 剧情");
        Check(UiText.Lookup("存档", UiLang.Ko) == "저장", "ko table did not resolve 存档");
        Check(UiText.Lookup("剧情", UiLang.Hant) == null, "ja/ko table answered a chinese-only lookup");
        Check(UiLanguage.Translate("剧情", UiLang.Ja) == "ストーリー" &&
            UiLanguage.Translate("剧情", UiLang.Hans) == "剧情" &&
            UiLanguage.Translate("剧情", UiLang.Hant) == UiLanguage.Convert("剧情", true),
            "language dispatch wrong");
        Check(UiLanguage.Parse("ja") == UiLang.Ja && UiLanguage.Parse("zh-Hant") == UiLang.Hant &&
            UiLanguage.Parse("ko") == UiLang.Ko && UiLanguage.Parse("nonsense") == UiLang.Hans &&
            UiLanguage.Code(UiLang.Ko) == "ko" && UiLanguage.Code(UiLang.Hant) == "zh-Hant",
            "language code mapping wrong");
        Check(UiText.Lookup("未翻译的键", UiLang.Ja) == null, "unknown key was invented");
        byte[] data = Archive.Encode(Document());
        using(var input=new MemoryStream(data)) Check(Archive.Decode(input).Events[0].Integer==2,"round trip failed");
        for(int length=0;length<data.Length;length++)
        {
            int truncated=length;
            Reject(()=>{using(var input=new MemoryStream(data,0,truncated,false))Archive.Decode(input);},"truncation accepted at "+length);
        }
        byte[] damaged=(byte[])data.Clone(); damaged[damaged.Length-1]^=1;
        Reject(()=>{using(var input=new MemoryStream(damaged))Archive.Decode(input);},"tampering accepted");
        byte[] extra=new byte[data.Length+1]; Array.Copy(data,extra,data.Length);
        Reject(()=>{using(var input=new MemoryStream(extra))Archive.Decode(input);},"trailing data accepted");
        byte[] forged=(byte[])data.Clone();forged[40]=255;Resign(forged);
        Reject(()=>{using(var input=new MemoryStream(forged))Archive.Decode(input);},"forged valid checksum bypassed kind validation");
        forged=(byte[])data.Clone();Array.Copy(BitConverter.GetBytes(int.MaxValue),0,forged,41,4);Resign(forged);
        Reject(()=>{using(var input=new MemoryStream(forged))Archive.Decode(input);},"forged field length bypassed budget");
        var bad=Document(); bad.Events[0].Number=double.NaN;
        Reject(()=>Archive.Encode(bad),"NaN accepted");
        bad=Document();bad.Kind=(RecoveryKind)255;Reject(()=>Archive.Encode(bad),"unknown kind accepted");
        // Native dice can legitimately roll zero; options still require 1-based indices.
        var zeroRoll=Document();
        zeroRoll.Events.Add(new RecordedEvent{Kind=EventKind.Dice,Key="dice:zero",Integer=0});
        using(var input=new MemoryStream(Archive.Encode(zeroRoll)))
            Check(Archive.Decode(input).Events[1].Integer==0,"zero dice roll rejected");
        bad=Document();bad.Events.Add(new RecordedEvent{Kind=EventKind.Dice,Key="dice:negative",Integer=-1});
        Reject(()=>Archive.Encode(bad),"negative dice roll accepted");
        bad=Document();bad.Events.Add(new RecordedEvent{Kind=EventKind.Choice,Key="choice:zero",Integer=0});
        Reject(()=>Archive.Encode(bad),"zero choice index accepted");
        bad=Document();bad.Scene="Combat";Reject(()=>Archive.Encode(bad),"wrong scene accepted");
        bad=Document();bad.Baseline=new string('x',4000001);Reject(()=>Archive.Encode(bad),"oversized field accepted");

        var gate=new OperationGate(); var cursor=new ReplayCursor(gate);
        Reject(()=>cursor.Prepare(Document(),"wrong",RecoveryKind.Story),"client mismatch accepted");
        Check(gate.CanInteract,"rejected preflight changed game state");
        var original=Document();cursor.Prepare(original,"test-client",RecoveryKind.Story);
        original.Events[0].Integer=999;
        var replacement=Document();replacement.Events[0].Integer=123;
        Reject(()=>cursor.Prepare(replacement,"test-client",RecoveryKind.Story),"second recovery replaced active tape");
        Reject(()=>gate.NotifyMutation(),"editing during recovery allowed");
        Reject(()=>gate.Advance(RecoveryPhase.Idle),"verification bypass allowed");
        gate.Advance(RecoveryPhase.Loading);gate.Advance(RecoveryPhase.Replaying);
        var peek=cursor.Peek();
        Check(peek!=null && peek.Kind==EventKind.Choice && peek.Key=="choice:1" &&
            cursor.Position==0,"lookahead consumed or skipped an event");
        peek.Integer=99;
        Check(cursor.Peek().Integer==2,"lookahead exposed mutable replay state");
        Check(cursor.Consume(EventKind.Choice,"choice:1").Integer==2,"mutable replay input");
        cursor.Verify("choice:2","state-2");Check(gate.CanInteract,"successful verification stayed locked");
        cursor.Prepare(Document(),"test-client",RecoveryKind.Story);
        gate.Advance(RecoveryPhase.Loading);gate.Advance(RecoveryPhase.Replaying);
        Reject(()=>cursor.Consume(EventKind.Choice,"wrong"),"mismatched replay continued");
        Check(gate.Phase==RecoveryPhase.Failed && !gate.CanInteract,"failure did not remain locked");
        Reject(()=>gate.NativeLoadCompleted(),"failed gate unlocked without native load start");
        gate.NativeLoadStarted();
        Reject(()=>gate.NotifyMutation(),"native load permitted edits");
        gate.NativeLoadCompleted();Check(gate.CanInteract,"native load did not reset failure");
        Reject(()=>gate.NativeLoadCompleted(),"idle gate accepted duplicate native completion");

        // Recovery controller: the phase sequence the Unity adapter will call.
        var controller=new RecoveryController();var fake=new FakeHost();
        Check(!controller.TryConsume(EventKind.Dialogue,"line-1",out _),"idle controller consumed replay input");
        controller.Start(Story(),fake);
        Check(controller.Replaying && fake.Baseline==Story().Baseline,"baseline not loaded before replay");
        Check(!controller.IsStopPoint(EventKind.Choice,"menu-2"),"stop point recognised with events left");
        Check(controller.TryConsume(EventKind.Dialogue,"line-1",out _),"dialogue replay refused");
        Check(controller.TryConsume(EventKind.Random,"rand:1",out _),"random replay refused");
        Check(controller.IsStopPoint(EventKind.Choice,"menu-2"),"stop point not recognised");
        Check(!controller.TryConsume(EventKind.Choice,"menu-2",out _),"stop point consumed as replay");
        controller.Verify();
        Check(controller.Phase==RecoveryPhase.Idle && fake.Aborts==0,"verified replay stayed open");
        Check(fake.States==1,"state captured a wrong number of times");

        // The Unity adapter and controller must use the SAME operation gate.
        var sharedGate=new OperationGate();
        var sharedController=new RecoveryController(sharedGate);
        var sharedHost=new FakeHost();
        sharedController.Start(Story(),sharedHost);
        Reject(()=>sharedGate.NativeLoadCompleted(),"native completion bypassed active replay");
        Reject(()=>sharedGate.NativeLoadStarted(),"native load did not require replay cancellation");
        Check(sharedGate.Phase==RecoveryPhase.Replaying && !sharedGate.CanInteract,
            "shared mutation gate was not locked by recovery");
        Reject(()=>sharedGate.NotifyMutation(),
            "shared gate permitted an edit during replay");
        sharedController.Cancel("simulated scene switch");
        Check(sharedGate.Phase==RecoveryPhase.Failed && !sharedGate.CanInteract,
            "shared gate forgot failed state");
        Reject(()=>new RecoveryController(sharedGate).Start(Story(),new FakeHost()),
            "fresh controller bypassed shared failure gate");
        Reject(()=>sharedGate.NativeLoadCompleted(),"shared gate unlocked before native load start");
        sharedGate.NativeLoadStarted();
        sharedGate.NativeLoadCompleted();
        Check(sharedGate.CanInteract,"successful native load did not unlock shared gate");

        // A digest of live game state may never degrade to a shared error
        // marker: different item/flag values must cause different fingerprints.
        var liveA=new[]{"stat:Money=3","relation:A=5","item:book:x=1"};
        var liveB=new[]{"item:book:x=1","stat:Money=3","relation:A=5"};
        string fingerprint=ReplayStateDigest.Create("{\"TimeTick\":100,\"game\":1}","{\"flag\":1}",liveA);
        Check(fingerprint.StartsWith(ReplayStateDigest.Prefix,StringComparison.Ordinal),
            "state fingerprint has no version");
        Check(fingerprint==ReplayStateDigest.Create("{\"TimeTick\":100,\"game\":1}","{\"flag\":1}",liveB),
            "state fingerprint depends on list order");
        Check(fingerprint!=ReplayStateDigest.Create("{\"TimeTick\":100,\"game\":1}","{\"flag\":2}",liveA),
            "changed story flag kept the same fingerprint");
        Check(fingerprint!=ReplayStateDigest.Create("{\"TimeTick\":100,\"game\":1}","{\"flag\":1}",
            new[]{"stat:Money=3","relation:A=5","item:book:x=2"}),
            "changed inventory kept the same fingerprint");
        Reject(()=>ReplayStateDigest.Create("{\"TimeTick\":100,\"game\":1}","{}",liveA),
            "unavailable mission state passed validation");
        Reject(()=>ReplayStateDigest.Create(null,"{\"flag\":1}",liveA),
            "missing native state passed validation");

        // Only the native save's top-level wall-clock integer may be ignored.
        Check(fingerprint==ReplayStateDigest.Create("{\"TimeTick\":999999,\"game\":1}","{\"flag\":1}",liveA),
            "fresh native timestamp changed checkpoint fingerprint");
        Check(fingerprint!=ReplayStateDigest.Create("{\"TimeTick\":100,\"game\":2}","{\"flag\":1}",liveA),
            "changed native game field escaped strict verification");
        string nested=ReplayStateDigest.Create("{\"TimeTick\":100,\"nested\":{\"TimeTick\":1},\"text\":\"TimeTick\"}","{\"flag\":1}",liveA);
        Check(nested==ReplayStateDigest.Create("{\"TimeTick\":200,\"nested\":{\"TimeTick\":1},\"text\":\"TimeTick\"}","{\"flag\":1}",liveA),
            "nested field interfered with wall clock normalization");
        Check(nested!=ReplayStateDigest.Create("{\"TimeTick\":100,\"nested\":{\"TimeTick\":2},\"text\":\"TimeTick\"}","{\"flag\":1}",liveA),
            "nested TimeTick was incorrectly ignored");
        foreach(string invalid in new[]{"{}","{\"nested\":{\"TimeTick\":1}}","{\"TimeTick\":0}",
            "{\"TimeTick\":-1}","{\"TimeTick\":1.5}","{\"TimeTick\":\"1\"}",
            "{\"TimeTick\":1,\"TimeTick\":2}","{\"TimeTick\":9999999999999999999999}"})
            Reject(()=>ReplayStateDigest.Create(invalid,"{\"flag\":1}",liveA),"invalid native timestamp accepted: "+invalid);

        // Wrong order locks the gate and releases the host exactly once.
        controller=new RecoveryController();fake=new FakeHost();
        controller.Start(Story(),fake);
        Check(!controller.TryConsume(EventKind.Choice,"menu-2",out _),"out of order replay accepted");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"failed replay did not abort host");
        Check(!controller.TryConsume(EventKind.Dialogue,"line-1",out _),"failed replay kept consuming");
        Reject(()=>controller.Start(Story(),fake),"locked gate accepted a new replay");
        Reject(()=>controller.Acknowledge(),"acknowledgement bypassed native recovery boundary");
        Check(controller.Phase==RecoveryPhase.Failed,"acknowledgement unlocked unsafe state");

        // A different post-replay state is a failure, not a warning.
        controller=new RecoveryController();fake=new FakeHost{State="hp=3"};
        controller.Start(Story(),fake);
        controller.TryConsume(EventKind.Dialogue,"line-1",out _);controller.TryConsume(EventKind.Random,"rand:1",out _);
        controller.IsStopPoint(EventKind.Choice,"menu-2");
        Reject(()=>controller.Verify(),"mismatched state passed verification");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"state mismatch did not abort host");

        // Baseline load failure and explicit cancel both release the host.
        controller=new RecoveryController();fake=new FakeHost{BaselineFails=true};
        Reject(()=>controller.Start(Story(),fake),"failing baseline load accepted");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"baseline failure did not abort host");
        controller=new RecoveryController();fake=new FakeHost();
        controller.Start(Story(),fake);controller.Cancel("player left the scene");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"cancel did not abort host");

        // VerifyAtEnd covers manual checkpoints, never a pending interaction.
        controller=new RecoveryController();fake=new FakeHost();
        controller.Start(Story(),fake);
        controller.TryConsume(EventKind.Dialogue,"line-1",out _);controller.TryConsume(EventKind.Random,"rand:1",out _);
        Reject(()=>controller.VerifyAtEnd(),"end verification bypassed pending stop point");
        Reject(()=>controller.Verify(),"verification bypassed unobserved stop point");
        Check(fake.States==0 && controller.Replaying,"premature verification captured state or unlocked gate");
        Check(controller.IsStopPoint(EventKind.Choice,"menu-2"),"pending stop point unavailable");
        controller.Verify();
        Check(controller.Phase==RecoveryPhase.Idle,"observed stop point could not verify");

        // Both expected state and checkpoint metadata must be frozen at start.
        var mutable=Story();
        controller=new RecoveryController();fake=new FakeHost();controller.Start(mutable,fake);
        mutable.ExpectedState="hp=3";mutable.Checkpoint="2:changed";
        controller.TryConsume(EventKind.Dialogue,"line-1",out _);controller.TryConsume(EventKind.Random,"rand:1",out _);
        Check(controller.IsStopPoint(EventKind.Choice,"menu-2"),"caller changed frozen stop point");
        controller.Verify();Check(controller.Phase==RecoveryPhase.Idle,"caller changed frozen expected state");
        controller=new RecoveryController();fake=new FakeHost();
        var manual=Story();manual.Checkpoint="manual:1";
        controller.Start(manual,fake);
        Reject(()=>controller.VerifyAtEnd(),"verify-at-end ran before any event");
        controller.TryConsume(EventKind.Dialogue,"line-1",out _);
        Reject(()=>controller.VerifyAtEnd(),"verify-at-end ran with events left");
        controller.TryConsume(EventKind.Random,"rand:1",out _);
        Check(controller.Finished,"finished flag stayed false");
        controller.VerifyAtEnd();
        Check(controller.Phase==RecoveryPhase.Idle && fake.Aborts==0,"verify-at-end did not settle");

        controller=new RecoveryController();fake=new FakeHost{State="hp=3"};
        controller.Start(manual,fake);
        controller.TryConsume(EventKind.Dialogue,"line-1",out _);controller.TryConsume(EventKind.Random,"rand:1",out _);
        Reject(()=>controller.VerifyAtEnd(),"verify-at-end accepted a mismatched state");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"verify-at-end mismatch did not abort");

        string folder=Path.Combine(Path.GetTempPath(),"mortal-helper-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new ArchiveStore(folder);store.Save(1,Document());
            Check(store.Load(1).ExpectedState=="state-2","first checkpoint primary not readable");
            Check(!File.Exists(store.PathFor(1)+".bak"),"first checkpoint unexpectedly created backup");
            var next=Document();next.ExpectedState="new";store.Save(1,next);
            Check(store.Load(1).ExpectedState=="new","primary not updated");
            Check(store.Load(1,true).ExpectedState=="state-2","backup lost");
            Reject(()=>store.PathFor(0),"invalid slot allowed");
            File.WriteAllBytes(store.PathFor(1),new byte[]{1,2});
            Reject(()=>store.Load(1),"corrupt primary silently loaded backup");
            Check(store.Load(1,true).ExpectedState=="state-2","backup unavailable after corruption");
            var recovered=Document();recovered.ExpectedState="after-corrupt";store.Save(1,recovered);
            Check(store.Load(1).ExpectedState=="after-corrupt","save over corrupt primary failed");
            Check(store.Load(1,true).ExpectedState=="state-2","corrupt primary promoted to backup");
            ArchiveStore.ForceCopyFallback=true;
            var copied=Document();copied.ExpectedState="after-second";store.Save(1,copied);
            Check(store.Load(1).ExpectedState=="after-second","copy fallback did not update primary");
            Check(store.Load(1,true).ExpectedState=="after-corrupt","copy fallback lost previous primary");
            ArchiveStore.ForceCopyFallback=false;
        }
        finally { if(Directory.Exists(folder))Directory.Delete(folder,true); }
        TransactionChecks();
        LoadGateChecks();
        Check(LegendSlotCapacity.Visible(30, new int[0]) == 12, "empty legend keeps native twelve");
        var legendSlots = new System.Collections.Generic.List<int>();
        for (int slot = 1; slot <= 12; slot++) legendSlots.Add(slot);
        Check(LegendSlotCapacity.Visible(30, legendSlots) == 18, "full native legend adds six");
        for (int slot = 13; slot <= 18; slot++) legendSlots.Add(slot);
        Check(LegendSlotCapacity.Visible(30, legendSlots) == 24, "second full legend page adds six");
        for (int slot = 19; slot <= 30; slot++) legendSlots.Add(slot);
        Check(LegendSlotCapacity.Visible(30, legendSlots) == 30, "full legend respects configured cap");
        legendSlots.Remove(5);
        Check(LegendSlotCapacity.Visible(12, legendSlots) == 30, "lower cap preserves higher saved stories");
        Check(LegendSlotCapacity.Visible(30, new[] { 13 }) == 18, "sparse high legend remains visible");
        Check(LegendSlotCapacity.Visible(30, new[] { 24 }) == 24, "sparse legend does not grow solely from last slot");
        Check(LegendSlotCapacity.Visible(30, new[] { 0, -1, 1000 }) == 12, "invalid legend file indices ignored");
        Check(LegendSlotCapacity.Visible(14, new[] { 13 }) == 14, "partial final legend batch respects cap");
        // Hotkey text parsing: the panel stores what the player pressed, and a hand-edited
        // config must not turn into an unrelated key or a silently dead shortcut.
        Check(!Hotkey.Parse("None").Enabled, "None disables a shortcut");
        Check(!Hotkey.Parse("").Enabled && !Hotkey.Parse(null).Enabled, "empty config disables a shortcut");
        Check(!Hotkey.Parse("Ctrl").Enabled, "a modifier alone is not a shortcut");
        Check(Hotkey.Parse("Tab").Enabled && Hotkey.Parse("tab").Key == "tab", "a bare key keeps its spelling");
        Hotkey combo = Hotkey.Parse("ctrl + tab");
        Check(combo.Enabled && combo.Ctrl && !combo.Alt && !combo.Shift,
            "modifier prefix is recognised, case-insensitively");
        Check(combo.ToString() == "Ctrl+tab", "canonical form puts modifiers first");
        Check(Hotkey.Parse("Tab+Shift").Shift && Hotkey.Parse("Tab+Shift").Key == "Tab",
            "modifier position in the config does not matter");
        Check(Hotkey.Parse("Ctrl+Alt+F9").ToString() == "Ctrl+Alt+F9", "two modifiers round trip");
        Check(Hotkey.Parse("F9+Q").Key == "F9", "a second key cannot silently replace the first");
        Check(Hotkey.Parse("Tab;Drop").Key == "Tab", "trailing junk after the key name is dropped");
        Check(Hotkey.From(true, false, false, "None").ToString() == "Ctrl+None",
            "a disabled combination still round trips");
        passed += StoryContracts.Run();
        passed += DuelJournalTests.Run();
        passed += DuelSaveMenuStateTests.Run();
        Console.WriteLine("PASS: "+passed+" contract checks; no Unity runtime or player save accessed.");
    }

    // The recovery gate is the only thing standing between an unfinished native
    // load and a save written on top of a half-applied state. A timeout must not
    // become a fake success, and there must still be a real way out.
    private static void LoadGateChecks()
    {
        var gate = new OperationGate();
        Check(gate.CanInteract, "a fresh gate blocked interaction");
        gate.NativeLoadStarted();
        Check(gate.AwaitingNativeLoad && !gate.CanInteract, "native load did not close the gate");
        gate.NativeLoadAborted("stalled");
        Check(gate.Phase == RecoveryPhase.Failed, "stalled load did not end in Failed");
        Check(!gate.CanInteract, "stalled load reopened the gate without finishing");
        Check(gate.Reason == "stalled", "stalled load lost its reason");
        Reject(() => gate.NativeLoadAborted("again"), "aborting a load that is not running was allowed");
        Reject(() => gate.NativeLoadCompleted(), "a load that never started was certified complete");
        // The only way out of Failed is a load that actually completes.
        gate.NativeLoadStarted();
        Check(gate.AwaitingNativeLoad, "a load could not start from Failed");
        gate.NativeLoadCompleted();
        Check(gate.CanInteract && gate.Phase == RecoveryPhase.Idle, "a completed load did not reopen the gate");
        Reject(() => gate.NativeLoadCompleted(), "completing twice was allowed");

        Check(SlotTransaction.CanDiscardBackups(SlotTransactionState.Committed),
            "committed transaction backups were treated as unrecoverable");
        Check(SlotTransaction.CanDiscardBackups(SlotTransactionState.RolledBack),
            "rolled back transaction backups were treated as unrecoverable");
        Check(!SlotTransaction.CanDiscardBackups(SlotTransactionState.Prepared),
            "prepared transaction backups were treated as disposable");
        Check(!SlotTransaction.CanDiscardBackups(SlotTransactionState.Broken),
            "broken transaction backups were treated as disposable");
    }

    private sealed class Fixture
    {
        internal readonly string Directory, Native, Sidecar, Marker, Journal;
        internal Fixture(string root, string name)
        {
            Directory = Path.Combine(root, name);
            System.IO.Directory.CreateDirectory(Directory);
            Native = Path.Combine(Directory, "Save_001.dat");
            Sidecar = Native + ".mha";
            Marker = Native + ".mha-required";
            Journal = SlotTransaction.JournalFor(Native);
        }
        internal string[] Paths => new[] { Native, Sidecar, Marker };
        internal void Write(string native, string sidecar, bool marker)
        {
            File.WriteAllText(Native, native);
            File.WriteAllText(Sidecar, sidecar);
            if (marker) File.WriteAllText(Marker, "2"); else if (File.Exists(Marker)) File.Delete(Marker);
        }
    }

    // File-level fault injection over a private temp directory. Every case
    // checks the bytes on disk, not just that an exception was thrown.
    private static void TransactionChecks()    {
        string root = Path.Combine(Path.GetTempPath(), "mhtx-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        try
        {
            // 0. A battle-start save journals its marker too; the journal reader must accept
            //    that name or the slot can never be settled again.
            var started = new Fixture(root, "start");
            started.Write("old-native", "old-side", false);
            string startMarker = started.Native + ".mhstart";
            File.WriteAllText(startMarker, "version=1`nclient=x`nscene=1`nhash=h`nsavedAt=t`n");
            var startedTx = SlotTransaction.Begin(started.Journal, "duel",
                new[] { started.Native, started.Sidecar, startMarker });
            started.Write("new-native", "new-side", true);
            var reread = SlotTransaction.Read(started.Journal);
            Check(reread != null && reread.Entries.Count == 3, "journal with .mhstart could not be read back");
            var startedReport = reread.Rollback();
            Check(startedReport.Complete && File.ReadAllText(started.Native) == "old-native" &&
                File.Exists(startMarker), "journal with .mhstart did not roll back");

            // 1. First creation: a rollback must remove exactly what it created.
            var created = new Fixture(root, "create");
            var transaction = SlotTransaction.Begin(created.Journal, "story", created.Paths);
            created.Write("new-native", "new-side", true);
            var report = transaction.Rollback();
            Check(report.Complete, "first-save rollback reported failure");
            Check(!File.Exists(created.Native) && !File.Exists(created.Sidecar) && !File.Exists(created.Marker),
                "first-save rollback left files behind");
            Check(!File.Exists(created.Journal), "successful rollback kept the journal");
            Check(!File.Exists(created.Journal + ".b0"), "successful rollback kept a backup");

            // 2. Overwrite then rollback restores the original bytes and hashes.
            var overwrite = new Fixture(root, "overwrite");
            overwrite.Write("old-native", "old-side", false);
            string nativeHash = SlotTransaction.HashFile(overwrite.Native);
            string sideHash = SlotTransaction.HashFile(overwrite.Sidecar);
            transaction = SlotTransaction.Begin(overwrite.Journal, "story", overwrite.Paths);
            overwrite.Write("new-native", "new-side", true);
            report = transaction.Rollback();
            Check(report.Complete, "overwrite rollback reported failure");
            Check(File.ReadAllText(overwrite.Native) == "old-native", "overwrite rollback lost the native save");
            Check(SlotTransaction.HashFile(overwrite.Native) == nativeHash, "native hash changed after rollback");
            Check(File.ReadAllText(overwrite.Sidecar) == "old-side" && SlotTransaction.HashFile(overwrite.Sidecar) == sideHash,
                "sidecar not restored");
            Check(!File.Exists(overwrite.Marker), "marker created by the save survived rollback");

            // 3. A marker that existed before the save must survive a rollback.
            var kept = new Fixture(root, "marker-kept");
            kept.Write("old-native", "old-side", true);
            transaction = SlotTransaction.Begin(kept.Journal, "story", kept.Paths);
            kept.Write("new-native", "new-side", true);
            report = transaction.Rollback();
            Check(report.Complete && File.ReadAllText(kept.Marker) == "2", "pre-existing marker not restored");

            // 4. The regression that motivated the journal: a lost backup must
            //    never be read as "the slot was empty" and delete the save.
            var lost = new Fixture(root, "lost-backup");
            lost.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(lost.Journal, "story", lost.Paths);
            lost.Write("torn-native", "new-side", true);
            File.Delete(transaction.Entries[0].BackupPath);
            report = transaction.Rollback();
            Check(!report.Complete, "missing backup reported as a clean rollback");
            Check(File.ReadAllText(lost.Native) == "torn-native", "missing backup deleted the native save");
            Check(File.Exists(lost.Journal), "journal removed although rollback was impossible");
            Check(File.ReadAllText(lost.Journal).Contains("state=broken"), "unrecoverable rollback not recorded as broken");
            Check(report.Blockers.Count == 1, "missing backup did not name the blocked file");

            // 5. A corrupt backup is never written over the live file.
            var corrupt = new Fixture(root, "corrupt-backup");
            corrupt.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(corrupt.Journal, "story", corrupt.Paths);
            corrupt.Write("torn-native", "new-side", true);
            File.WriteAllText(transaction.Entries[1].BackupPath, "garbage");
            report = transaction.Rollback();
            Check(!report.Complete, "corrupt backup accepted as restorable");
            Check(File.ReadAllText(corrupt.Native) == "torn-native" && File.ReadAllText(corrupt.Sidecar) == "new-side",
                "corrupt backup overwrote a live file");
            Check(File.Exists(corrupt.Journal), "journal removed after a corrupt-backup refusal");

            // 6. A backup copy failure aborts before any save file is modified.
            var copyFail = new Fixture(root, "backup-fail");
            copyFail.Write("old-native", "old-side", false);
            SlotTransaction.Fault = step => { if (step == "backup:1") throw new IOException("injected copy failure"); };
            Reject(() => SlotTransaction.Begin(copyFail.Journal, "story", copyFail.Paths),
                "backup copy failure was not reported");
            SlotTransaction.Fault = null;
            Check(File.ReadAllText(copyFail.Native) == "old-native" && File.ReadAllText(copyFail.Sidecar) == "old-side",
                "backup copy failure modified a save file");
            Check(!File.Exists(copyFail.Journal), "backup copy failure left a journal behind");
            Check(System.IO.Directory.GetFiles(copyFail.Directory, "*.b0").Length == 0,
                "backup copy failure left a partial backup behind");

            // 7. A journal write failure also leaves the slot untouched.
            var journalFail = new Fixture(root, "journal-fail");
            journalFail.Write("old-native", "old-side", false);
            SlotTransaction.Fault = step => { if (step == "journal-write") throw new IOException("injected journal failure"); };
            Reject(() => SlotTransaction.Begin(journalFail.Journal, "story", journalFail.Paths),
                "journal write failure was not reported");
            SlotTransaction.Fault = null;
            Check(File.ReadAllText(journalFail.Native) == "old-native", "journal write failure modified the native save");
            Check(System.IO.Directory.GetFiles(journalFail.Directory, "*.b*").Length == 0,
                "journal write failure left backups behind");

            // 8. Process killed between the native write and the sidecar write:
            //    a fresh read of the journal must restore the original pair.
            var crashed = new Fixture(root, "crash-between");
            crashed.Write("old-native", "old-side", false);
            SlotTransaction.Begin(crashed.Journal, "story", crashed.Paths);
            File.WriteAllText(crashed.Native, "new-native");
            File.WriteAllText(crashed.Marker, "2");
            var restarted = SlotTransaction.Read(crashed.Journal);
            Check(restarted != null && restarted.State == SlotTransactionState.Prepared, "interrupted transaction not readable");
            report = restarted.Rollback();
            Check(report.Complete, "restart rollback failed");
            Check(File.ReadAllText(crashed.Native) == "old-native" && File.ReadAllText(crashed.Sidecar) == "old-side",
                "restart rollback did not restore the pre-crash pair");
            Check(!File.Exists(crashed.Marker), "restart rollback kept the interrupted marker");

            // 9. Crash after verification but before commit: the replacement is
            //    durable, so the next run finishes forward instead of discarding it.
            var forward = new Fixture(root, "forward");
            forward.Write("old-native", "old-side", false);
            SlotTransaction.Begin(forward.Journal, "supersede", forward.Paths);
            File.WriteAllText(forward.Native, "verified-native");
            string verified = SlotTransaction.HashFile(forward.Native);
            SlotTransaction.Read(forward.Journal).RecordReplacement(verified);
            var pending = SlotTransaction.Read(forward.Journal);
            Check(pending.ReplacementHash == verified, "verified replacement hash not persisted");
            Check(pending.ReplacementHash == SlotTransaction.HashFile(forward.Native), "forward decision would not match");
            Reject(() => pending.Complete(), "undecided transaction discarded its backups");
            Check(File.Exists(forward.Journal + ".b0"), "undecided cleanup removed a backup");
            pending.Commit();
            pending.Complete();
            Check(File.ReadAllText(forward.Native) == "verified-native", "forward completion discarded the verified save");
            Check(!File.Exists(forward.Journal), "forward completion kept the journal");

            // 10. Crash after the commit marker: the new files are authoritative.
            var committed = new Fixture(root, "committed");
            committed.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(committed.Journal, "story", committed.Paths);
            committed.Write("new-native", "new-side", true);
            transaction.RecordReplacement(SlotTransaction.HashFile(committed.Native));
            transaction.Commit();
            var afterCommit = SlotTransaction.Read(committed.Journal);
            Check(afterCommit.State == SlotTransactionState.Committed, "commit marker not persisted");
            afterCommit.Complete();
            Check(File.ReadAllText(committed.Native) == "new-native", "committed save was rolled back");
            Check(!File.Exists(committed.Journal + ".b0"), "committed cleanup kept a backup");

            // 11. A rollback that fails part way keeps every recovery artefact and
            //     can be retried.
            var retry = new Fixture(root, "retry");
            retry.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(retry.Journal, "story", retry.Paths);
            retry.Write("new-native", "new-side", true);
            SlotTransaction.Fault = step => { if (step == "restore:1") throw new IOException("injected restore failure"); };
            report = transaction.Rollback();
            SlotTransaction.Fault = null;
            Check(!report.Complete, "failed rollback reported success");
            Check(File.Exists(retry.Journal), "failed rollback removed the journal");
            Check(File.Exists(retry.Journal + ".b1"), "failed rollback removed a backup");
            Check(File.ReadAllText(retry.Journal).Contains("state=broken"), "failed rollback not recorded as broken");
            report = SlotTransaction.Read(retry.Journal).Rollback();
            Check(report.Complete, "retry of a failed rollback did not succeed");
            Check(File.ReadAllText(retry.Native) == "old-native" && File.ReadAllText(retry.Sidecar) == "old-side",
                "retried rollback did not restore the pair");

            // 12. A damaged journal never silently degrades into "no transaction".
            var damaged = new Fixture(root, "damaged-journal");
            damaged.Write("old-native", "old-side", false);
            TransactionJournal(damaged.Journal);
            Reject(() => SlotTransaction.Read(damaged.Journal), "damaged journal accepted");

            // A parseable change to the existence flag must never authorize
            // rollback to delete a native save that existed before writing.
            var changed = new Fixture(root, "changed-existence");
            changed.Write("old-native", "old-side", false);
            SlotTransaction.Begin(changed.Journal, "story", changed.Paths);
            string journalText = File.ReadAllText(changed.Journal);
            Check(journalText.Contains("|1|"), "test journal had no existing native entry");
            File.WriteAllText(changed.Journal, journalText.Replace("|1|", "|0|"));
            Reject(() => SlotTransaction.Read(changed.Journal), "changed existence flag passed journal checksum");
            Check(File.ReadAllText(changed.Native) == "old-native" && File.Exists(changed.Journal),
                "damaged journal touched the original native save");

            // Legacy logs have no integrity check. They can still be inspected
            // manually, but may not drive an automatic rollback or cleanup.
            var legacy = new Fixture(root, "legacy-journal");
            legacy.Write("old-native", "old-side", false);
            SlotTransaction.Begin(legacy.Journal, "story", legacy.Paths);
            string legacyText = File.ReadAllText(legacy.Journal);
            legacyText = legacyText.Replace("slot-transaction 2", "slot-transaction 1");
            legacyText = legacyText.Substring(0, legacyText.LastIndexOf("sum=", StringComparison.Ordinal));
            File.WriteAllText(legacy.Journal, legacyText);
            Reject(() => SlotTransaction.Read(legacy.Journal), "unchecked old journal was auto-recovered");
            Check(File.ReadAllText(legacy.Native) == "old-native" && File.Exists(legacy.Journal),
                "old journal rejection changed native progress");

            // 13. Begin refuses to stack a second transaction on a slot.
            var stacked = new Fixture(root, "stacked");
            stacked.Write("old-native", "old-side", false);
            SlotTransaction.Begin(stacked.Journal, "story", stacked.Paths);
            Reject(() => SlotTransaction.Begin(stacked.Journal, "story", stacked.Paths),
                "second transaction started on an unresolved slot");
            SlotTransaction.Read(stacked.Journal).Rollback();

            // 14. A failed durable commit cannot change the in-memory decision,
            // or an error handler's Rollback would discard the only old copy.
            var commitFail = new Fixture(root, "commit-persist-fail");
            commitFail.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(commitFail.Journal, "story", commitFail.Paths);
            commitFail.Write("new-native", "new-side", true);
            transaction.RecordReplacement(SlotTransaction.HashFile(commitFail.Native));
            SlotTransaction.Fault = step => { if (step == "persist:committed") throw new IOException("commit unavailable"); };
            Reject(() => transaction.Commit(), "failed commit was not reported");
            SlotTransaction.Fault = null;
            Check(transaction.State == SlotTransactionState.Prepared, "failed commit changed the in-memory decision");
            Check(SlotTransaction.Read(commitFail.Journal).State == SlotTransactionState.Prepared,
                "failed commit changed the durable decision");
            Reject(() => transaction.Complete(), "failed commit permitted backup cleanup");
            Check(File.Exists(commitFail.Journal + ".b0"), "failed commit lost the original backup");
            report = transaction.Rollback();
            Check(report.Complete && File.ReadAllText(commitFail.Native) == "old-native",
                "failed commit could not restore the old save");

            // 15. Restored bytes alone do not authorize deleting the journal:
            // persist failure must retain every backup for the next attempt.
            var rollbackPersistFail = new Fixture(root, "rollback-persist-fail");
            rollbackPersistFail.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(rollbackPersistFail.Journal, "story", rollbackPersistFail.Paths);
            rollbackPersistFail.Write("new-native", "new-side", true);
            SlotTransaction.Fault = step => { if (step == "persist:rolledback") throw new IOException("rollback decision unavailable"); };
            report = transaction.Rollback();
            SlotTransaction.Fault = null;
            Check(!report.Complete && report.Restored, "failed rollback decision reported a completed transaction");
            Check(File.ReadAllText(rollbackPersistFail.Native) == "old-native", "rollback failed to restore bytes");
            Check(File.Exists(rollbackPersistFail.Journal) && File.Exists(rollbackPersistFail.Journal + ".b0") &&
                File.Exists(rollbackPersistFail.Journal + ".b1"), "failed rollback decision discarded recovery files");
            Check(transaction.State == SlotTransactionState.Prepared, "failed rollback decision escaped into memory");
            report = SlotTransaction.Read(rollbackPersistFail.Journal).Rollback();
            Check(report.Complete, "durable rollback retry failed");

            // 16. A crash part way through cleanup leaves a committed journal,
            // which remains cleanable even after some backups are already gone.
            var cleanupCrash = new Fixture(root, "cleanup-crash");
            cleanupCrash.Write("old-native", "old-side", false);
            transaction = SlotTransaction.Begin(cleanupCrash.Journal, "supersede", cleanupCrash.Paths);
            cleanupCrash.Write("new-native", "new-side", true);
            transaction.RecordReplacement(SlotTransaction.HashFile(cleanupCrash.Native));
            transaction.Commit();
            report = transaction.Rollback();
            Check(!report.Complete && File.Exists(cleanupCrash.Journal + ".b0"),
                "rollback erased an already committed operation awaiting companion retirement");
            SlotTransaction.Fault = step => { if (step == "cleanup-backup:1") throw new IOException("crash during cleanup"); };
            Reject(() => transaction.Complete(), "cleanup fault was not reached");
            SlotTransaction.Fault = null;
            Check(!File.Exists(cleanupCrash.Journal + ".b0") && File.Exists(cleanupCrash.Journal + ".b1"),
                "cleanup crash did not simulate partial cleanup");
            var cleanupRestart = SlotTransaction.Read(cleanupCrash.Journal);
            Check(cleanupRestart.State == SlotTransactionState.Committed, "partial cleanup left an undecided journal");
            cleanupRestart.Complete();
            Check(!File.Exists(cleanupCrash.Journal) && File.ReadAllText(cleanupCrash.Native) == "new-native",
                "cleanup retry damaged the committed save");
        }
        finally
        {
            SlotTransaction.Fault = null;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void TransactionJournal(string path)
    {
        File.WriteAllText(path, "slot-transaction 1\nop=%%%\nstate=prepared\nnew=-\nn=9\n");
    }

    private static void Resign(byte[] data)
    {
        using(var sha=SHA256.Create())Array.Copy(sha.ComputeHash(data,40,data.Length-40),0,data,8,32);
    }

    private static RecoveryDocument Story()
    {
        var d=new RecoveryDocument{Kind=RecoveryKind.Story,Client="game-build-1",Script="story.lua",Scene="Story",
            Checkpoint="2:menu-2",Baseline="{\"native\":1}",ExpectedState="hp=10"};
        d.Events.Add(new RecordedEvent{Kind=EventKind.Dialogue,Key="line-1"});
        d.Events.Add(new RecordedEvent{Kind=EventKind.Random,Key="rand:1",Number=.25});
        return d;
    }

    private sealed class FakeHost : IReplayHost
    {
        public string Baseline;
        public bool BaselineFails;
        public int Aborts;
        public int States;
        public string State="hp=10";
        public string Client => "game-build-1";
        public void LoadBaseline(string baseline)
        {
            if(BaselineFails) throw new InvalidOperationException("原生档读取失败");
            Baseline=baseline;
        }
        public string CaptureState() { States++; return State; }
        public void Abort(string reason) { Aborts++; }
    }
}
