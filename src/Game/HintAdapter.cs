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
            if (Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.DiagnoseScript.Value)
                Plugin.Instance.Status = "剧情脚本：" + (string.IsNullOrEmpty(ScriptName) ? "(未命名)" : ScriptName) +
                    " · 可对应分支 " + branches.Count;
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

        // Branch points and story jumps that follow this option, one per line.
        internal static string Consequences(string key)
        {
            BranchHint branch;
            if (!branches.TryGetValue(key ?? "", out branch) || branch.Consequences.Count == 0) return null;
            return string.Join("\n", branch.Consequences.ToArray());
        }

        internal static void Draw()
        {
            var options = Plugin.Instance == null ? null : Plugin.Instance.Options;
            if (!Plugin.Active || options == null || !options.Hints.Value || Plugin.Instance.Panel.Visible || hovered == null ||
                !hovered.isActiveAndEnabled || hovered.Revision != Revision || hovered.Source == null ||
                Event.current.type != EventType.Repaint) return;
            // Same two sections and wording as the previous overlay: a native
            // condition block and an "已确认部分" effect block, either optional.
            var output = new StringBuilder();
            if (options.ShowConditions.Value)
            {
                bool wrote = false;
                if (hovered.Condition != null && !string.IsNullOrWhiteSpace(hovered.Condition.text))
                { output.Append("选择条件\n").Append(hovered.Condition.text); wrote = true; }
                string consequences = Consequences(hovered.Source.text);
                if (consequences != null)
                {
                    if (wrote) output.Append("\n\n");
                    output.Append("取决于判定的后果\n").Append(consequences);
                }
            }
            if (options.ShowEffects.Value)
            {
                string effects = Description(hovered.Source.text);
                if (!string.IsNullOrWhiteSpace(effects))
                {
                    if (output.Length > 0) output.Append("\n\n");
                    output.Append("选择效果（已确认部分）\n").Append(effects);
                }
            }
            string text = output.ToString();
            if (string.IsNullOrWhiteSpace(text)) return;
            var style = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true, richText = false, alignment = TextAnchor.UpperLeft, fontSize = 18,
                padding = new RectOffset(14, 14, 12, 12)
            };
            if (hovered.Source.font != null) style.font = hovered.Source.font;
            float width = Mathf.Min(360, Screen.width - 24);
            float height = style.CalcHeight(new GUIContent(text), width);
            if (width < 140 || height > Screen.height - 24) return;
            Rect anchor = ScreenRect(hovered.transform as RectTransform);
            Rect[] candidates = {
                new Rect(anchor.xMax + 12, anchor.y, width, height),
                new Rect(anchor.xMin - width - 12, anchor.y, width, height),
                new Rect(anchor.x, anchor.yMin - height - 12, width, height),
                new Rect(anchor.x, anchor.yMax + 12, width, height)
            };
            foreach (var candidate in candidates)
            {
                Rect box = candidate;
                box.x = Mathf.Clamp(box.x, 12, Mathf.Max(12, Screen.width - width - 12));
                box.y = Mathf.Clamp(box.y, 12, Mathf.Max(12, Screen.height - height - 12));
                if (box.Overlaps(anchor)) continue;
                GUI.Box(box, text, style);
                return;
            }
        }

        private static Rect ScreenRect(RectTransform rect)
        {
            if (rect == null) return new Rect(0, 0, Screen.width, Screen.height);
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            return Rect.MinMaxRect(a.x, Screen.height - b.y, b.x, Screen.height - a.y);
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
