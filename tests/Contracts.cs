using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using MortalHelper.Core;
using MortalHelper.Recovery;

internal static class Contracts
{
    private static int passed;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (Exception e) when (e is InvalidDataException || e is EndOfStreamException ||
            e is ArgumentException || e is InvalidOperationException || e is DecoderFallbackException) { rejected = true; }
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
        hints = ScriptHints.Read(prefix + "if a then\nstatmodifymanager.Player(\"Money\", 999)\nend");
        Check(hints["O_test"].Effects.Count==0 && hints["O_test"].Incomplete,"conditional claimed definite");
        hints = ScriptHints.Read("--[=[\n--index:FAKE\nstatmodifymanager.Player(\"Money\", 999)\n]=]\n" + prefix + "statmodifymanager.AddMisc('a',-1)");
        Check(hints.Count==1 && hints["O_test"].Effects[0].Arguments[1]=="-1","long comment parsed as code");
        hints = ScriptHints.Read(prefix + "statmodifymanager.AddMisc([=[text --index:FAKE]=],1)");
        Check(hints.Count==1 && hints["O_test"].Effects.Count==1,"long literal mishandled");
        Check(ScriptHints.Read(prefix+"x()\n"+prefix+"x()").Count==0,"duplicate marker retained");
        Check(ScriptHints.Read(prefix+"statmodifymanager.AddMisc('broken,1)").Count==0,"unterminated quote accepted");
        hints=ScriptHints.Read(prefix+"unknown()\nstatmodifymanager.Player('Money',5)");
        Check(hints["O_test"].Effects.Count==0,"unknown call did not stop analysis");
        hints=ScriptHints.Read(prefix+"statmodifymanager.Player('Money',calculate())");
        Check(hints["O_test"].Effects.Count==0,"nonliteral effect accepted");
        Check(ScriptHints.Read(new string(' ',2000001)).Count==0,"oversized input parsed");
        Check(ScriptHints.Read("foo() --index:FAKE\nstatmodifymanager.Player('Money',1)").Count==0,"inline comment became branch marker");

        var consequence=ScriptHints.Read(prefix+"checkpointmanager.Switch(\"S12\")\nluamanager.SetNextScript(\"S0017_02\")\nend");
        Check(consequence["O_test"].Consequences.Count==2 && consequence["O_test"].Consequences[0]=="分支点 S12","consequence scan failed");
        Check(ScriptHints.Read(prefix+"statmodifymanager.AddStory(\"S03\")")["O_test"].Consequences[0]=="剧情 S03","story consequence mislabelled");
        Check(ScriptHints.Read("--[=[\ncheckpointmanager.Switch(\"FAKE\")\n]=]\n"+prefix+"x()")["O_test"].Consequences.Count==0,"comment produced a consequence");
        Check(ScriptHints.Read(prefix+"-- checkpointmanager.Switch(\"FAKE\")\nx()")["O_test"].Consequences.Count==0,"line comment produced a consequence");
        Check(ScriptHints.Read(prefix+"checkpointmanager.Switch(\"A\")\ncheckpointmanager.Switch(\"A\")")["O_test"].Consequences.Count==1,"duplicate consequence kept");

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
        bad=Document();bad.Scene="Combat";Reject(()=>Archive.Encode(bad),"wrong scene accepted");
        bad=Document();bad.Baseline=new string('x',4000001);Reject(()=>Archive.Encode(bad),"oversized field accepted");

        var gate=new OperationGate(); var cursor=new ReplayCursor(gate);
        Reject(()=>cursor.Prepare(Document(),"wrong",RecoveryKind.Story),"client mismatch accepted");
        Check(gate.CanInteract,"rejected preflight changed game state");
        var original=Document();cursor.Prepare(original,"test-client",RecoveryKind.Story);
        original.Events[0].Integer=999;
        var replacement=Document();replacement.Events[0].Integer=123;
        Reject(()=>cursor.Prepare(replacement,"test-client",RecoveryKind.Story),"second recovery replaced active tape");
        Reject(()=>gate.NotifyMutation("edit"),"editing during recovery allowed");
        Reject(()=>gate.Advance(RecoveryPhase.Idle),"verification bypass allowed");
        gate.Advance(RecoveryPhase.Loading);gate.Advance(RecoveryPhase.Replaying);
        Check(cursor.Consume(EventKind.Choice,"choice:1").Integer==2,"mutable replay input");
        cursor.Verify("choice:2","state-2");Check(gate.CanInteract,"successful verification stayed locked");
        cursor.Prepare(Document(),"test-client",RecoveryKind.Story);
        gate.Advance(RecoveryPhase.Loading);gate.Advance(RecoveryPhase.Replaying);
        Reject(()=>cursor.Consume(EventKind.Choice,"wrong"),"mismatched replay continued");
        Check(gate.Phase==RecoveryPhase.Failed && !gate.CanInteract,"failure did not remain locked");
        gate.NativeLoadCompleted();Check(gate.CanInteract,"native load did not reset failure");

        var recorder=new RecordingSession();
        Reject(()=>recorder.Capture("state"),"capture without baseline allowed");
        recorder.Begin(Document());
        long token=recorder.Enter(EventKind.Dialogue,"line-1");
        var pending=recorder.Capture("before-line");
        Check(pending.Events.Count==0 && pending.Checkpoint=="1:line-1","unfinished line was recorded as complete");
        recorder.Finish(token,0);
        Reject(()=>recorder.Capture("state"),"capture outside safe stop allowed");
        recorder.Enter(EventKind.Choice,"menu-1");
        var stopped=recorder.Capture("before-menu");
        Check(stopped.Events.Count==1 && stopped.Events[0].Key=="line-1","completed dialogue missing");
        stopped.Events.Clear();Check(recorder.Capture("state").Events.Count==1,"mutable recording escaped");
        recorder.Enter(EventKind.Dialogue,"nested");Check(!recorder.Valid,"unsupported nested call accepted");
        recorder.Begin(Document());token=recorder.Enter(EventKind.Choice,"menu");
        recorder.Finish(token+1,1);Check(!recorder.Valid,"stale completion accepted");
        recorder.Begin(Document());recorder.Random("random:1",.25);
        recorder.Enter(EventKind.Dialogue,"line");Check(recorder.Capture("s").Events[0].Number==.25,"random event lost");
        recorder.Random("nested-random",.75);Check(!recorder.Valid,"nested random silently dropped");
        recorder.Reset();Check(!recorder.Valid && !recorder.Waiting,"recording survived reset");
        recorder.Begin(Document());recorder.Enter(EventKind.Dialogue,"old");
        var invalidSeed=Document();invalidSeed.Client="";
        Reject(()=>recorder.Begin(invalidSeed),"invalid baseline accepted");
        Check(!recorder.Valid,"old baseline survived a failed new baseline");

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

        // Wrong order locks the gate and releases the host exactly once.
        controller=new RecoveryController();fake=new FakeHost();
        controller.Start(Story(),fake);
        Check(!controller.TryConsume(EventKind.Choice,"menu-2",out _),"out of order replay accepted");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"failed replay did not abort host");
        Check(!controller.TryConsume(EventKind.Dialogue,"line-1",out _),"failed replay kept consuming");
        Reject(()=>controller.Start(Story(),fake),"locked gate accepted a new replay");
        controller.Acknowledge();Check(controller.Phase==RecoveryPhase.Idle,"acknowledged failure stayed locked");

        // A different post-replay state is a failure, not a warning.
        controller=new RecoveryController();fake=new FakeHost{State="hp=3"};
        controller.Start(Story(),fake);
        controller.TryConsume(EventKind.Dialogue,"line-1",out _);controller.TryConsume(EventKind.Random,"rand:1",out _);
        Reject(()=>controller.Verify(),"mismatched state passed verification");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"state mismatch did not abort host");

        // Baseline load failure and explicit cancel both release the host.
        controller=new RecoveryController();fake=new FakeHost{BaselineFails=true};
        Reject(()=>controller.Start(Story(),fake),"failing baseline load accepted");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"baseline failure did not abort host");
        controller=new RecoveryController();fake=new FakeHost();
        controller.Start(Story(),fake);controller.Cancel("player left the scene");
        Check(controller.Phase==RecoveryPhase.Failed && fake.Aborts==1,"cancel did not abort host");

        string folder=Path.Combine(Path.GetTempPath(),"mortal-helper-tests-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new ArchiveStore(folder);store.Save(1,Document());
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
        Console.WriteLine("PASS: "+passed+" contract checks; no Unity runtime or player save accessed.");
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
