using System;
using System.Collections.Generic;
using System.Text;
using Fungus;
using HarmonyLib;
using Mortal.Core;
using Mortal.Story;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    internal static class HintAdapter
    {
        private static Dictionary<string, BranchHint> branches = new Dictionary<string, BranchHint>();
        private static HintPointer hovered;
        internal static int Revision;
        internal static string ScriptName = "";
        internal static int Count => branches.Count;
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string> {
            { "Player", "属性变化" }, { "SetPlayer", "设置属性" }, { "Character", "角色关系变化" },
            { "SetCharacter", "设置角色关系" }, { "AddBook", "获得书籍" }, { "AddMisc", "获得物品" },
            { "AddSpecial", "获得特殊物品" }, { "SetFlag", "设置剧情标记" }, { "AddFlag", "剧情标记变化" },
            { "RemoveBook", "移除书籍" }, { "RemoveMisc", "移除物品" }, { "RemoveSpecial", "移除特殊物品" },
            { "Mission", "任务变化" }, { "AddTalent", "天赋变化" }, { "AddStory", "解锁故事" }
        };

        internal static void Load(string source, string name)
        {
            Revision++; hovered = null; ScriptName = name ?? ""; branches = ScriptHints.Read(source);
        }
        internal static void Clear() { Revision++; hovered = null; branches.Clear(); ScriptName = ""; }
        internal static void Bind(Component target, Text source, Text condition = null)
        {
            var pointer = target.GetComponent<HintPointer>() ?? target.gameObject.AddComponent<HintPointer>();
            pointer.Source = source; pointer.Condition = condition; pointer.Revision = Revision;
        }
        internal static void Enter(HintPointer pointer) { hovered = pointer; }
        internal static void Leave(HintPointer pointer) { if (hovered == pointer) hovered = null; }

        internal static string Description(string key)
        {
            BranchHint branch;
            if (!branches.TryGetValue(key ?? "", out branch)) return null;
            var output = new StringBuilder();
            foreach (var effect in branch.Effects)
            {
                string name;
                if (!Names.TryGetValue(effect.Method, out name)) break;
                output.Append(name).Append("：").Append(string.Join("，", effect.Arguments)).AppendLine();
            }
            if (output.Length == 0) return null;
            output.Append("仅列出已识别的直接效果；不代表全部后果。");
            return output.ToString();
        }

        internal static void Draw()
        {
            var options = Plugin.Instance == null ? null : Plugin.Instance.Options;
            if (!Plugin.Active || options == null || !options.Hints.Value || Plugin.Instance.Panel.Visible || hovered == null ||
                !hovered.isActiveAndEnabled || hovered.Revision != Revision || hovered.Source == null) return;
            // The old panel had one master switch plus independent effect/condition
            // preferences; keep them independent so either can be used alone.
            var output = new StringBuilder();
            if (options.ShowConditions.Value && hovered.Condition != null && !string.IsNullOrWhiteSpace(hovered.Condition.text))
                output.Append("原生条件：").Append(hovered.Condition.text).AppendLine();
            if (options.ShowEffects.Value)
            {
                string effects = Description(hovered.Source.text);
                if (effects != null) output.Append(effects);
            }
            string text = output.ToString();
            if (string.IsNullOrWhiteSpace(text)) return;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, wordWrap = true, fontSize = 18, richText = false };
            float width = Mathf.Min(440, Screen.width - 24);
            float height = Mathf.Min(Screen.height - 24, style.CalcHeight(new GUIContent(text), width) + 16);
            Vector2 mouse = Event.current.mousePosition;
            GUI.Box(new Rect(Mathf.Clamp(mouse.x + 20,12,Screen.width-width-12),
                Mathf.Clamp(mouse.y + 16,12,Screen.height-height-12),width,height),text,style);
        }
    }

    public sealed class HintPointer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        internal Text Source, Condition;
        internal int Revision;
        public void OnPointerEnter(PointerEventData data) { HintAdapter.Enter(this); }
        public void OnPointerExit(PointerEventData data) { HintAdapter.Leave(this); }
        private void OnDisable() { HintAdapter.Leave(this); }
    }

    [HarmonyPatch(typeof(LuaEnvironment), "LoadLuaFunction")]
    internal static class ScriptHintPatch
    {
        private static void Prefix(string __0, string __1) { HintAdapter.Load(__0, __1); }
    }

    [HarmonyPatch(typeof(NormalOptionButton), "UpdateContent")]
    internal static class NormalHintPatch
    {
        private static void Postfix(NormalOptionButton __instance)
        { HintAdapter.Bind(__instance, GameAccess.Field<Text>(__instance, "_source")); }
    }

    [HarmonyPatch(typeof(DiceOption), "UpdateContent")]
    internal static class DiceHintPatch
    {
        private static void Postfix(DiceOption __instance)
        { HintAdapter.Bind(__instance, GameAccess.Field<Text>(__instance, "_sourceText"), GameAccess.Field<Text>(__instance, "_conditionText")); }
    }
}
