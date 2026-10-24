#if STORY_LUA_RUNTIME_TEST
// Standalone read-only test of production Lua methods against the game assembly.
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter;
public sealed class RootApiProbe { public string Name = "api"; }
class LuaProbe {
 static int checks;
 static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("OK " + label); }
 static string pluginPath, managedPath, loaderPath;
 static void Main(string[] arguments) {
 if (arguments.Length != 3) throw new ArgumentException("Expected plugin DLL, game Managed directory, and loader core directory.");
 pluginPath=arguments[0]; managedPath=arguments[1]; loaderPath=arguments[2];
 AppDomain.CurrentDomain.AssemblyResolve += (sender,args) => {
  foreach(var root in new[]{System.IO.Path.GetDirectoryName(pluginPath),managedPath,loaderPath}) {
   var p = System.IO.Path.Combine(root,new AssemblyName(args.Name).Name + ".dll");
   if(System.IO.File.Exists(p)) return Assembly.LoadFrom(p);
  }
  return null;
 }; Run(); }
 [MethodImpl(MethodImplOptions.NoInlining)] static void Run() {
  var t = Assembly.Load(System.IO.File.ReadAllBytes(pluginPath)).GetType("MortalHelper.Game.AnytimeStory");
  var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
  Func<string,object[],object> call = (name,args) => t.GetMethod(name,flags).Invoke(null,args);
  var script = new Script(CoreModules.Preset_Complete);
  UserData.RegisterType(typeof(RootApiProbe));
  var api=UserData.Create(new RootApiProbe());
  script.Globals.Set("test_api",api);
  script.DoString("original_api = function() return 3 end");
  var originalFunction=script.Globals.Get("original_api");
  var originalPrint=script.Globals.Get("print");
  call("RememberLuaLibraries",new object[]{script});
  call("RememberLuaRootApis",new object[]{script});
  script.DoString("number = 42; text = '中文'; flag = true; sayoptions = {'甲', '乙'}; sayoptions.enabled = true; sayoptions.nested={inner={n=7}}");
  var original = call("CaptureGlobals",new object[]{script});
  Check(original != null,"capture real Preset_Complete modules including package/json/dynamic");
  script.DoString("number = 1; sayoptions[1] = '改'; sayoptions.extra = '删'; sayoptions.nested.inner.n=1; sayoptions.nested.extra='删'");
  call("RestoreGlobals", new object[]{script,original});
  Check(script.DoString("return sayoptions[1] == '甲' and sayoptions.extra == nil and number == 42 and sayoptions.nested.inner.n==7 and sayoptions.nested.extra==nil").Boolean,"restore mutable scalar and table globals");
  var restored = call("CaptureGlobals",new object[]{script});
  Check((bool)call("SameGlobals",new object[]{original,restored}),"restore deletion tombstones preserve exact snapshot");
  script.DoString("sayoptions.child = function() return 1 end");
  bool rejected=false;
  try { call("CaptureGlobals",new object[]{script}); } catch(TargetInvocationException e){ rejected=e.InnerException is System.IO.InvalidDataException; }
  Check(rejected,"unsupported function in nested state rejected");
  script.DoString("sayoptions.child = nil; sayoptions.cycle = sayoptions");
  rejected=false;
  try { call("CaptureGlobals",new object[]{script}); } catch(TargetInvocationException e){ rejected=e.InnerException is System.IO.InvalidDataException; }
  Check(rejected,"cyclic nested state rejected");
  script.DoString("sayoptions.cycle = nil; alias = sayoptions");
  rejected=false;
  try { call("CaptureGlobals",new object[]{script}); } catch(TargetInvocationException e){ rejected=e.InnerException is System.IO.InvalidDataException; }
  Check(rejected,"table alias rejected");
  script.DoString("alias = nil; package = {story = 7}");
  var rebound = (Array)call("CaptureGlobals",new object[]{script});
  bool found=false;
  foreach(var x in rebound) if ((string)x.GetType().GetField("Key").GetValue(x)=="package") found=true;
  Check(found,"rebound library name saved as story state");
  Check(t.GetField("lua",flags).GetValue(null)==null,"probe leaves live interpreter untouched");
  var assembly = t.Assembly;
  var rt=assembly.GetType("MortalHelper.Game.AnytimeStoryRecord");
  var record=Activator.CreateInstance(rt,true);
  rt.GetField("BaselineGlobals").SetValue(record,original); rt.GetField("ExpectedGlobals").SetValue(record,original);
  rt.GetField("Events").SetValue(record,Array.CreateInstance(assembly.GetType("MortalHelper.Game.AnytimeStoryEvent"),0));
  var codec=assembly.GetType("MortalHelper.Game.AnytimeStoryCodec");
  string encoded=(string)codec.GetMethod("Write",flags).Invoke(null,new[]{record});
  var decoded=codec.GetMethod("Read",flags).Invoke(null,new object[]{encoded});
  Check((bool)call("SameGlobals",new[]{original,rt.GetField("BaselineGlobals").GetValue(decoded)}),"nested state survives production sidecar codec");
  Func<bool> rootRejected = () => {
   try { call("CaptureGlobals",new object[]{script}); return false; }
   catch(TargetInvocationException e) { return e.InnerException is System.IO.InvalidDataException; }
  };
  script.DoString("extra_root = function() return 4 end");
  Check(rootRejected(),"new root Lua function rejected");
  script.DoString("extra_root = nil; original_api = function() return 5 end");
  Check(rootRejected(),"rebound root Lua API rejected");
  script.Globals.Set("original_api", originalFunction);
  script.Globals.Set("print", DynValue.NewCallback((ctx,args)=>DynValue.Nil));
  Check(rootRejected(),"rebound root CLR API rejected");
  script.Globals.Set("print",originalPrint);
  script.Globals.Set("new_userdata",api);
  Check(rootRejected(),"root userdata alias under a new name rejected");
  script.Globals.Set("new_userdata",DynValue.Nil);
  script.Globals.Set("test_api",UserData.Create(new RootApiProbe()));
  Check(rootRejected(),"rebound root userdata API rejected");
  script.Globals.Set("test_api",api);
  script.Globals.Set("test_api",DynValue.Nil);
  Check(rootRejected(),"removed root API rejected");
  script.Globals.Set("test_api",api);
  Check(call("CaptureGlobals",new object[]{script})!=null,"restored original API identities accepted");
  Console.WriteLine("PASS: "+checks+" production Lua behavior checks against real game Fungus.dll");
 }
}

#endif
