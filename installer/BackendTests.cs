using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using MortalHelperInstaller;
class BackendTests {
 static int n;
 static void Check(bool value,string name) { if(!value)throw new Exception(name); n++; }
 static Dictionary<string,byte[]> Payload(string version) { return new Dictionary<string,byte[]>{{Engine.Plugin,System.Text.Encoding.UTF8.GetBytes(version)},{"winhttp.dll",new byte[]{1,2,3}},{"BepInEx/core/example.dll",new byte[]{4}}}; }
 static string Root() { var r=Path.Combine(Path.GetTempPath(),"mortal-installer-test-"+Guid.NewGuid()); Directory.CreateDirectory(Path.Combine(r,"Mortal_Data")); File.WriteAllText(Path.Combine(r,"Mortal.exe"),""); return r; }
 static void Main(string[] args) {
  string root=Root(); try {
   Engine.Install(root,Payload("v1")); Check(File.Exists(Path.Combine(root,Engine.Plugin)),"plugin installed"); Check(File.Exists(Path.Combine(root,"winhttp.dll")),"loader installed");
   File.WriteAllText(Path.Combine(root,"Mortal_Data","save.dat"),"save"); Directory.CreateDirectory(Path.Combine(root,"BepInEx","config")); File.WriteAllText(Path.Combine(root,"BepInEx","config","user.cfg"),"settings");
   Engine.Uninstall(root); Check(!File.Exists(Path.Combine(root,Engine.Plugin)),"owned plugin removed"); Check(!File.Exists(Path.Combine(root,"winhttp.dll")),"owned loader removed"); Check(File.ReadAllText(Path.Combine(root,"Mortal_Data","save.dat"))=="save","save retained"); Check(File.ReadAllText(Path.Combine(root,"BepInEx","config","user.cfg"))=="settings","settings retained");
  } finally {Directory.Delete(root,true);}
  root=Root();try {
   Engine.Install(root,Payload("v1")); File.WriteAllText(Path.Combine(root,Engine.Plugin),"user modified"); Engine.Uninstall(root); Check(File.ReadAllText(Path.Combine(root,Engine.Plugin))=="user modified","modified plugin retained");
  }finally{Directory.Delete(root,true);}
  root=Root();try {
   Engine.Install(root,Payload("v1")); File.WriteAllText(Path.Combine(root,"BepInEx/plugins/other.dll"),"other"); Engine.Uninstall(root); Check(File.Exists(Path.Combine(root,"winhttp.dll")),"shared loader retained"); Check(File.Exists(Path.Combine(root,"BepInEx/plugins/other.dll")),"other mod retained"); Check(!File.Exists(Path.Combine(root,Engine.Plugin)),"our plugin removed with other mod");
  }finally{Directory.Delete(root,true);}
  root=Root();try {
   File.WriteAllText(Path.Combine(root,"winhttp.dll"),"existing"); bool rejected=false;try{Engine.Install(root,Payload("v1"));}catch(IOException){rejected=true;} Check(rejected,"incompatible loader rejected"); Check(File.ReadAllText(Path.Combine(root,"winhttp.dll"))=="existing","incompatible loader preserved");
  }finally{Directory.Delete(root,true);}
  root=Root();try {
   var payload=Payload("v1");payload.Add("../outside",new byte[]{3});bool rejected=false;try{Engine.Install(root,payload);}catch(InvalidDataException){rejected=true;}Check(rejected,"path traversal rejected"); Check(!File.Exists(Path.Combine(root,Engine.Plugin)),"preflight before write");
  }finally{Directory.Delete(root,true);}
  root=Root();try {
   Engine.Install(root,Payload("v1")); File.WriteAllText(Path.Combine(root,"winhttp.dll"),"changed loader"); Engine.Uninstall(root); Check(File.Exists(Path.Combine(root,"BepInEx/core/example.dll")),"changed loader family retained");
  }finally{Directory.Delete(root,true);}
  root=Root();try {
   Directory.CreateDirectory(Path.Combine(root,"BepInEx/plugins/old")); File.WriteAllText(Path.Combine(root,"BepInEx/plugins/old/DiceMasterAnything.dll"),"conflict");
   bool rejected=false;try{Engine.Install(root,Payload("v1"));}catch(IOException ex){rejected=ex.Message.Contains("DiceMasterAnything.dll");}Check(rejected,"conflicting old mod identified"); Check(!File.Exists(Path.Combine(root,Engine.Plugin)),"conflict rejects before writing");
  }finally{Directory.Delete(root,true);}
  if(args.Length==1) {
   root=Root();try {
    var payload=Payload("v1"); using(var zip=ZipFile.OpenRead(args[0])) foreach(var name in new[]{"winhttp.dll","BepInEx/core/BepInEx.Core.dll","BepInEx/core/BepInEx.Unity.Mono.dll","BepInEx/core/BepInEx.Unity.Mono.Preloader.dll","doorstop_config.ini"}) using(var stream=zip.GetEntry(name).Open()) using(var bytes=new MemoryStream()){stream.CopyTo(bytes);payload[name]=bytes.ToArray();}
    Engine.Install(root,payload); File.AppendAllText(Path.Combine(root,"doorstop_config.ini"),"\n# user setting\n"); string config=File.ReadAllText(Path.Combine(root,"doorstop_config.ini")); var loaderHash=Engine.Hash(File.ReadAllBytes(Path.Combine(root,"winhttp.dll"))); payload[Engine.Plugin]=new byte[]{7,8}; Engine.Install(root,payload);
    Check(Engine.Hash(File.ReadAllBytes(Path.Combine(root,"winhttp.dll")))==loaderHash,"update preserves loader"); Check(File.ReadAllText(Path.Combine(root,"doorstop_config.ini"))==config,"update preserves doorstop settings"); Check(File.ReadAllBytes(Path.Combine(root,Engine.Plugin))[0]==7,"update replaces plugin"); Engine.Uninstall(root); Check(File.Exists(Path.Combine(root,"winhttp.dll")),"modified doorstop settings retain loader family");
   }finally{Directory.Delete(root,true);}
   root=Root();try {
    var payload=Payload("v1"); using(var zip=ZipFile.OpenRead(args[0])) foreach(var name in new[]{"winhttp.dll","BepInEx/core/BepInEx.Core.dll","BepInEx/core/BepInEx.Unity.Mono.dll","BepInEx/core/BepInEx.Unity.Mono.Preloader.dll","doorstop_config.ini"}) {string p=Path.Combine(root,name);Directory.CreateDirectory(Path.GetDirectoryName(p)); using(var stream=zip.GetEntry(name).Open())using(var target=File.Create(p)){stream.CopyTo(target);}}
    Engine.Install(root,payload);Engine.Uninstall(root);Check(File.Exists(Path.Combine(root,"winhttp.dll")),"preexisting compatible loader never adopted");
   }finally{Directory.Delete(root,true);}
  }
  Console.WriteLine(n+" installer backend checks passed");
 }
}
