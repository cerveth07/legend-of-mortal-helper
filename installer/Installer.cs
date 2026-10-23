using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace MortalHelperInstaller {
 public class OwnedFile { public string Path; public string Hash; public bool Loader; }
 public class Receipt { public List<OwnedFile> Files = new List<OwnedFile>(); }
 public static class Engine {
  public const string Plugin = "BepInEx/plugins/cerveth.mortal.helper/MortalHelper.dll";
  const string ReceiptPath = "BepInEx/mortal-helper-install.xml";
  public static string Hash(byte[] bytes) { using(var h=SHA256.Create()) return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
  static string Safe(string root,string relative) {
   string path=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,relative.Replace('/',System.IO.Path.DirectorySeparatorChar)));
   string prefix=System.IO.Path.GetFullPath(root).TrimEnd('\\','/')+System.IO.Path.DirectorySeparatorChar;
   if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装包路径无效。");
   // Never follow directory links into unrelated locations.
   for(string p=System.IO.Path.GetDirectoryName(path);p!=null && p.Length>=prefix.Length;p=System.IO.Path.GetDirectoryName(p))
    if(Directory.Exists(p) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("目标目录包含链接，请改用实际游戏目录。");
   if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0) throw new IOException("目标文件为链接，请先检查："+relative);
   return path;
  }
  public static string Normalize(string path) { path=path.Trim().Trim('"'); return System.IO.Path.GetFullPath(File.Exists(path)?System.IO.Path.GetDirectoryName(path):path); }
  public static void Validate(string root) {
   if(!File.Exists(System.IO.Path.Combine(root,"Mortal.exe")) || !Directory.Exists(System.IO.Path.Combine(root,"Mortal_Data"))) throw new IOException("请选择包含 Mortal.exe 和 Mortal_Data 的活侠传游戏目录。");
   foreach(var p in Process.GetProcessesByName("Mortal")) using(p) {
    try { if(string.Equals(p.MainModule.FileName,System.IO.Path.Combine(root,"Mortal.exe"),StringComparison.OrdinalIgnoreCase)) throw new IOException("请先退出这个目录中的游戏，再安装或卸载。"); }
    catch(System.ComponentModel.Win32Exception) { throw new IOException("无法确认游戏是否退出，请关闭游戏后重试。"); }
    catch(InvalidOperationException) { }
   }
  }
  static bool X64(string file) { using(var r=new BinaryReader(File.OpenRead(file))) { if(r.ReadUInt16()!=0x5a4d) return false; r.BaseStream.Position=0x3c; int offset=r.ReadInt32(); if(offset<0 || offset>r.BaseStream.Length-6) return false; r.BaseStream.Position=offset; return r.ReadUInt32()==0x4550 && r.ReadUInt16()==0x8664; } }
  static Receipt Read(string root) { string p=Safe(root,ReceiptPath); if(!File.Exists(p)) return new Receipt(); using(var s=File.OpenRead(p)) return (Receipt)new XmlSerializer(typeof(Receipt)).Deserialize(s); }
  static void Atomic(string path,byte[] bytes) {
   Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); string temp=path+".mh-"+Guid.NewGuid().ToString("N")+".tmp";
   try { File.WriteAllBytes(temp,bytes); if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); } finally { if(File.Exists(temp)) File.Delete(temp); }
  }
  static byte[] Serialize(Receipt receipt) { using(var s=new MemoryStream()) { new XmlSerializer(typeof(Receipt)).Serialize(s,receipt); return s.ToArray(); } }
  public static string Install(string root,Dictionary<string,byte[]> payload) {
   Validate(root);
   string plugins=Safe(root,"BepInEx/plugins");
   if(Directory.Exists(plugins)) {
    var conflicts=Directory.GetFiles(plugins,"*.dll",SearchOption.AllDirectories).Where(p=>{string f=System.IO.Path.GetFileName(p); return f.Equals("MortalFateHelper.dll",StringComparison.OrdinalIgnoreCase)||f.Equals("LOMSave2.dll",StringComparison.OrdinalIgnoreCase)||f.StartsWith("DiceMaster",StringComparison.OrdinalIgnoreCase)||f.Equals("ControlMyDestiny.dll",StringComparison.OrdinalIgnoreCase);}).Select(p=>System.IO.Path.GetFileName(p)).Distinct().ToArray();
    if(conflicts.Length>0) throw new IOException("请先将这些冲突插件移出 BepInEx/plugins，再安装："+string.Join("、",conflicts));
   }
   var old=Read(root); var next=new Receipt(); bool existingLoader=File.Exists(Safe(root,"winhttp.dll")) || Directory.Exists(Safe(root,"BepInEx/core"));
   if(existingLoader) {
    string core=Safe(root,"BepInEx/core/BepInEx.Core.dll"); string mono=Safe(root,"BepInEx/core/BepInEx.Unity.Mono.dll");
    string preloader=Safe(root,"BepInEx/core/BepInEx.Unity.Mono.Preloader.dll"); string config=Safe(root,"doorstop_config.ini");
    if(!File.Exists(core)||!File.Exists(mono)||!File.Exists(preloader)||!File.Exists(config)||!File.Exists(Safe(root,"winhttp.dll"))||AssemblyName.GetAssemblyName(core).Version.Major!=6 || !X64(Safe(root,"winhttp.dll")))
     throw new IOException("现有加载器不是兼容的 BepInEx 6 Mono x64，请先处理旧加载器；安装器不会覆盖它。");
    string doorstop=File.ReadAllText(config);
    if(!Regex.IsMatch(doorstop,@"(?im)^\s*enabled\s*=\s*true\s*(?:[#;].*)?$")) throw new IOException("现有加载器未启用，请在 doorstop_config.ini 中将 enabled 设为 true 后重试；安装器保留原设置。");
    // Preserve existing shared loader, including future releases.
    foreach(var f in old.Files.Where(f=>f.Loader)) next.Files.Add(f);
   }
   var changes=new Dictionary<string,byte[]>();
   foreach(var item in payload) {
    bool loader=!item.Key.StartsWith("BepInEx/plugins/cerveth.mortal.helper/",StringComparison.OrdinalIgnoreCase) || item.Key.StartsWith("BepInEx/plugins/cerveth.mortal.helper/third-party/",StringComparison.OrdinalIgnoreCase);
    if(loader && existingLoader) continue;
    string target=Safe(root,item.Key);
    if(loader && File.Exists(target)) throw new IOException("发现已有文件，未覆盖："+item.Key);
    changes.Add(item.Key,item.Value); next.Files.Add(new OwnedFile{Path=item.Key,Hash=Hash(item.Value),Loader=loader});
   }
   // Back up a previously manually installed plugin before adopting it.
   if(old.Files.Count==0 && File.Exists(Safe(root,Plugin))) {
    string backup=Safe(root,"BepInEx/mortal-helper-backup/"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-MortalHelper.dll");
    Atomic(backup,File.ReadAllBytes(Safe(root,Plugin)));
   }
   changes.Add(ReceiptPath,Serialize(next)); var rollback=new Dictionary<string,byte[]>();
   try {
    foreach(var item in changes) { string p=Safe(root,item.Key); rollback[p]=File.Exists(p)?File.ReadAllBytes(p):null; Atomic(p,item.Value); }
   } catch { foreach(var item in rollback.Reverse()) { if(item.Value==null) { if(File.Exists(item.Key)) File.Delete(item.Key); } else Atomic(item.Key,item.Value); } throw; }
   return existingLoader?"已安装 / 更新万象录，保留了现有加载器。启动游戏后按 Tab 打开。":"已安装万象录及加载器。启动游戏后按 Tab 打开。";
  }
  public static string Uninstall(string root) {
   Validate(root); var receipt=Read(root); if(receipt.Files.Count==0) return "没有找到本安装器的安装记录。手动安装的文件请按说明移除。";
   var owned=new HashSet<string>(receipt.Files.Select(f=>Safe(root,f.Path)),StringComparer.OrdinalIgnoreCase);
   bool shared=new[]{"BepInEx/plugins","BepInEx/patchers"}.Any(d=>Directory.Exists(Safe(root,d)) && Directory.GetFiles(Safe(root,d),"*.dll",SearchOption.AllDirectories).Any(p=>!owned.Contains(p)));
   shared=shared || receipt.Files.Where(f=>f.Loader || f.Path.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)).Any(f=>File.Exists(Safe(root,f.Path)) && Hash(File.ReadAllBytes(Safe(root,f.Path)))!=f.Hash);
   var retained=new Receipt(); int changed=0;
   foreach(var f in receipt.Files.OrderByDescending(f=>f.Path.Length)) {
    string path=Safe(root,f.Path); if(!File.Exists(path)) continue;
    if(f.Loader && shared) { retained.Files.Add(f); continue; }
    if(Hash(File.ReadAllBytes(path))!=f.Hash) { retained.Files.Add(f); changed++; continue; }
    File.Delete(path);
   }
   string rp=Safe(root,ReceiptPath); if(retained.Files.Count>0) Atomic(rp,Serialize(retained)); else File.Delete(rp);
   // Remove only empty directories formerly containing our files. Leave settings, logs and all saves.
   foreach(var f in receipt.Files) { string d=System.IO.Path.GetDirectoryName(Safe(root,f.Path)); while(d!=null && !string.Equals(d,root,StringComparison.OrdinalIgnoreCase) && Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) { Directory.Delete(d); d=System.IO.Path.GetDirectoryName(d); } }
   return "卸载完成，保留游戏存档和设置。"+(shared?"共用或已修改的加载器已保留。":"")+(changed>0?"已修改的文件已保留（"+changed+" 个）。":"");
  }
  public static Dictionary<string,byte[]> Payload() {
   var result=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
   using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")) using(var z=new ZipArchive(s,ZipArchiveMode.Read)) foreach(var e in z.Entries) {
    if(e.Name.Length==0) continue; using(var m=new MemoryStream()) { using(var source=e.Open()) source.CopyTo(m); result.Add(e.FullName,m.ToArray()); }
   } return result;
  }
  public static IEnumerable<string> Discover() {
   var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) { var p=k==null?null:k.GetValue("SteamPath") as string; if(!string.IsNullOrEmpty(p)) roots.Add(p); }
   using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")) { var p=k==null?null:k.GetValue("InstallPath") as string; if(!string.IsNullOrEmpty(p)) roots.Add(p); }
   foreach(var r in roots.ToArray()) { string vdf=System.IO.Path.Combine(r,"steamapps","libraryfolders.vdf"); if(File.Exists(vdf)) foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\"")) roots.Add(m.Groups[1].Value.Replace("\\\\","\\")); }
   foreach(var r in roots) { string game=System.IO.Path.Combine(r,"steamapps","common","LegendOfMortal"); if(File.Exists(System.IO.Path.Combine(game,"Mortal.exe"))) yield return game; }
  }
 }
 public sealed class Window : Form {
  readonly TextBox path=new TextBox(); readonly Label status=new Label(); readonly Button install=new Button(),remove=new Button(),browse=new Button(),open=new Button();
  public Window() {
   Text="活侠 · 万象录 — 安装与卸载"; ClientSize=new Size(620,330); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen;
   AutoScaleDimensions=new SizeF(96,96); AutoScaleMode=AutoScaleMode.Dpi; Font=new Font("Microsoft YaHei UI",10); BackColor=Color.FromArgb(246,248,250);
   var heading=new Label{Text="活侠 · 万象录",Font=new Font("Microsoft YaHei UI",21,FontStyle.Bold),ForeColor=Color.FromArgb(34,65,85),Bounds=new Rectangle(26,22,560,44)}; Controls.Add(heading);
   Controls.Add(new Label{Text="选择游戏目录，安装后从 Steam 启动游戏。",Bounds=new Rectangle(28,74,560,25)});
   Controls.Add(new Label{Text="游戏目录",Bounds=new Rectangle(28,114,100,24)}); path.SetBounds(28,142,465,30); Controls.Add(path);
   browse.Text="选择…"; browse.SetBounds(502,141,90,32); browse.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="活侠传|Mortal.exe",Title="选择游戏目录中的 Mortal.exe"}) if(d.ShowDialog()==DialogResult.OK) path.Text=System.IO.Path.GetDirectoryName(d.FileName);}; Controls.Add(browse);
   install.Text="安装 / 更新"; install.SetBounds(28,192,170,40); install.BackColor=Color.FromArgb(34,65,85); install.ForeColor=Color.White; install.FlatStyle=FlatStyle.Flat; install.Click+=(s,e)=>Run(false); Controls.Add(install);
   remove.Text="卸载万象录"; remove.SetBounds(212,192,170,40); remove.Click+=(s,e)=>Run(true); Controls.Add(remove);
   open.Text="打开游戏目录"; open.SetBounds(396,192,196,40); open.Click+=(s,e)=>{try{string p=Engine.Normalize(path.Text); if(Directory.Exists(p)) Process.Start("explorer.exe",p);}catch(Exception ex){status.Text=ex.Message;}}; Controls.Add(open);
   status.SetBounds(28,252,564,65); status.ForeColor=Color.FromArgb(60,72,82); status.Text="无需管理员权限。卸载会保留游戏存档和设置。"; Controls.Add(status);
   MinimumSize=Size;
   path.TextChanged+=(s,e)=>{ if(path.Text.Length==0) return; try { string p=Engine.Normalize(path.Text); status.Text=File.Exists(System.IO.Path.Combine(p,Engine.Plugin))?(File.Exists(System.IO.Path.Combine(p,"BepInEx/mortal-helper-install.xml"))?"已安装万象录，可更新或卸载。卸载会保留游戏存档和设置。":"已手动安装万象录，可更新后由安装器管理卸载。"):File.Exists(System.IO.Path.Combine(p,"Mortal.exe"))?"已找到游戏。点击「安装 / 更新」即可安装。":"请选择游戏目录中的 Mortal.exe。"; }catch { status.Text="请选择游戏目录中的 Mortal.exe。"; } };
   try { path.Text=Engine.Discover().Select(Engine.Normalize).FirstOrDefault()??""; } catch { status.Text="请点击「选择」找到游戏目录中的 Mortal.exe。"; }
  }
  void Run(bool uninstall) { install.Enabled=remove.Enabled=false; try { string p=Engine.Normalize(path.Text); path.Text=p; if(uninstall && MessageBox.Show(this,"卸载万象录？游戏存档和设置会保留。","卸载",MessageBoxButtons.OKCancel,MessageBoxIcon.Question)!=DialogResult.OK) return; status.Text=uninstall?Engine.Uninstall(p):Engine.Install(p,Engine.Payload()); }catch(Exception ex){status.Text=ex.Message;}finally{install.Enabled=remove.Enabled=true;} }
 }
 static class Program {
  [STAThread] static void Main(string[] args) {
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   if(args.Length==2 && args[0]=="--smoke-ui") {
    // Render an invisible, never activated form. No Show, focus call, install or game operation.
    using(var window=new Window()) {
     window.CreateControl(); window.PerformAutoScale(); window.PerformLayout();
     IntPtr windowHandle=window.Handle; foreach(Control child in window.Controls) { IntPtr childHandle=child.Handle; }
     using(var bitmap=new Bitmap(window.Width,window.Height)) {
      window.DrawToBitmap(bitmap,new Rectangle(Point.Empty,window.Size));
      // A hidden parent suppresses native child painting. Render each child explicitly.
      Point client=window.PointToScreen(Point.Empty); Point outer=new Point(window.Left,window.Top);
      using(var graphics=Graphics.FromImage(bitmap)) foreach(Control child in window.Controls) using(var image=new Bitmap(child.Width,child.Height)) {
       child.DrawToBitmap(image,new Rectangle(Point.Empty,child.Size));
       graphics.DrawImageUnscaled(image,child.Left+client.X-outer.X,child.Top+client.Y-outer.Y);
      }
      bitmap.Save(System.IO.Path.GetFullPath(args[1]),System.Drawing.Imaging.ImageFormat.Png);
     }
    }
    return;
   }
   Application.Run(new Window());
  }
 }
}
