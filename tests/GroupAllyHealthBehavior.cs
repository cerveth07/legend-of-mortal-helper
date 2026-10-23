// Runs the production group-battle patches against managed stand-ins.
// Checks routing and protection boundaries; does not simulate Unity lifecycle.
using System;
using System.Reflection;
using System.Reflection.Emit;
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)] public class HarmonyPatch : Attribute { public HarmonyPatch() {} public HarmonyPatch(Type t,string n) {} }
 public class CodeInstruction { public OpCode opcode; public object operand; }
 public static class AccessTools {
  public static MethodBase EnumeratorMoveNext(MethodInfo m) { return m; }
  public static MethodInfo Method(Type t,string n) { return t.GetMethod(n,BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); }
  public static FieldInfo Field(Type t,string n) { return t.GetField(n,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic); }
 }
}
namespace Mortal.Battle {
 public enum EntityType { Player, Friend, Enemy, Neutral }
 public class Entity { public EntityType CurrentType; }
 public class Character { public object gameObject; public Entity _entity; public bool IsDead; public CharacterHealth Health; }
 public class CharacterHealth {
  public int CurrentHealth; public Character Owner;
  public T GetComponent<T>() where T:class { return Owner as T; }
 }
 public class CharacterMana { public object gameObject; public int MaxMana,CurrentMana; public void Modify(int n) {} }
 public class SkillButton {}
 public class GameLevelManager { public static GameLevelManager Instance; public bool IsGameOver; }
}
namespace MortalHelper.Game {
 public class Flag { public bool Value; }
 public class Options { public Flag GroupAllyHealth=new Flag(),GroupHealth=new Flag(),GroupDown=new Flag(),GroupMana=new Flag(),GroupCooldown=new Flag(),GroupFreeze=new Flag(); }
 public class Plugin { public static bool CanModifyProgress; public static Plugin Instance=new Plugin(); public Options Options=new Options(); }
 public static class GameAccess {
  public static T Field<T>(object o,string n) { return (T)o.GetType().GetField(n).GetValue(o); }
  public static void Set<T>(object o,string n,T v) { o.GetType().GetField(n).SetValue(o,v); }
 }
}
class GroupAllyHealthBehavior {
 static int checks;
 static void Check(bool ok) { if(!ok)throw new Exception("Ally protection boundary failed at "+checks); checks++; }
 static void Main() {
  var patch=typeof(MortalHelper.Game.GroupAllyHealthPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
  foreach(Mortal.Battle.EntityType team in Enum.GetValues(typeof(Mortal.Battle.EntityType)))
  foreach(bool gate in new[]{false,true}) foreach(bool enabled in new[]{false,true})
  foreach(int health in new[]{0,50}) foreach(int delta in new[]{-100,-5,0,10}) {
   MortalHelper.Game.Plugin.CanModifyProgress=gate;
   MortalHelper.Game.Plugin.Instance.Options.GroupAllyHealth.Value=enabled;
   var c=new Mortal.Battle.Character { _entity=new Mortal.Battle.Entity{CurrentType=team} };
   var h=new Mortal.Battle.CharacterHealth{CurrentHealth=health,Owner=c};
   object[] args={h,delta};patch.Invoke(null,args);
   bool protect=gate&&enabled&&health>0&&delta<0&&team==Mortal.Battle.EntityType.Friend;
   Check((int)args[1]==(protect?0:delta));
  }
  MortalHelper.Game.Plugin.CanModifyProgress=true;
  MortalHelper.Game.Plugin.Instance.Options.GroupAllyHealth.Value=true;
  foreach(var owner in new Mortal.Battle.Character[]{null,new Mortal.Battle.Character()}) {
   object[] args={new Mortal.Battle.CharacterHealth{CurrentHealth=50,Owner=owner},-5};patch.Invoke(null,args);Check((int)args[1]==-5);
  }
  Console.WriteLine("PASS: "+checks+" production ally-health routing checks (managed stand-ins; no Unity runtime)");
 }
}
