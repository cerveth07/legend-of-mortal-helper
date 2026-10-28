using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
    // Option consequences: the short forms are read from the script text that the game
    // hands to Lua, so nothing here runs game code or writes save data. Locked rows also
    // read their named condition from the game's checkpoint table.
    internal static class HintAdapter
    {
        private static Dictionary<string, BranchHint> branches = new Dictionary<string, BranchHint>();
        private static Dictionary<string, OptionRequirement> requirements = new Dictionary<string, OptionRequirement>();
        private static HintPointer hovered;
        internal static int Revision;
        internal static string ScriptName = "";
        // Options the current script exposes, and how many of those have something a
        // player can actually read. The panel shows both so an empty tooltip is never
        // mistaken for a broken plugin.
        internal static int Count => branches.Count;
        internal static int WithContent { get; private set; }

        private static readonly HashSet<EffectKind> PlainKinds = new HashSet<EffectKind> {
            EffectKind.Stat, EffectKind.Relation, EffectKind.ItemGain, EffectKind.ItemLoss };

        internal static void Load(string source, string name)
        {
            Revision++; hovered = null; ScriptName = name ?? "";
            branches.Clear();
            requirements.Clear();
            cachedKey = null;
            applyNote = null;
            armedApply = null;
            // Both parsers run inside a native script load, i.e. inside a scene transition.
            // A substring test costs microseconds and skips the work for scripts that cannot
            // produce hints at all; the feature switch skips it for players who turned it off.
            var options = Plugin.Instance == null ? null : Plugin.Instance.Options;
            bool wanted = Plugin.Active && options != null && options.HintMode.Value != OptionHintMode.Off;
            if (wanted) HintNames.RegisterScript(name, source);
            if (wanted && source != null && source.IndexOf("--index:", StringComparison.Ordinal) >= 0)
            {
                long started = DateTime.UtcNow.Ticks;
                // Nothing before the first marker can be a hint, so scan from that line on:
                // this keeps the tokenizer off the (usually much larger) lead-in.
                int first = source.IndexOf("--index:", StringComparison.Ordinal);
                string hintSource = first > 0 ? source.Substring(source.LastIndexOf('\n', first) + 1) : source;
                try { branches = ScriptHints.Read(hintSource); }
                catch (Exception error)
                {
                    // Hints are optional: a parser failure must never stop native Lua loading.
                    Plugin.Instance?.Report(error);
                }
                try
                {
                    if (source.IndexOf("checkpointmanager.Condition(", StringComparison.Ordinal) >= 0 ||
                        source.IndexOf("checkpointmanager.Switch(", StringComparison.Ordinal) >= 0 ||
                        source.IndexOf("checkpointmanager.Position(", StringComparison.Ordinal) >= 0)
                        requirements = ScriptOptions.Read(source);
                }
                catch (Exception error) { Plugin.Instance?.Report(error); }
                long ms = (DateTime.UtcNow.Ticks - started) / TimeSpan.TicksPerMillisecond;
                if (ms >= 8)
                    Plugin.Instance?.Log("[Hints] parse " + ms + "ms script=" + ScriptName +
                        " branches=" + branches.Count + " conditions=" + requirements.Count);
            }
            int content = 0;
            foreach (var pair in branches) if (HasContent(pair.Value)) content++;
            WithContent = content;
            if (Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.DiagnoseScript.Value)
                Plugin.Instance.Status = "剧情脚本：" + (string.IsNullOrEmpty(ScriptName) ? "(未命名)" : HintNames.Natural(HintNames.Script(ScriptName), Plugin.Instance.Options.LanguageKind)) +
                    " · 可对应分支 " + branches.Count + " · 条件选项 " + requirements.Count;
        }

        internal static void Clear()
        {
            HintNames.Clear();
            Revision++; hovered = null; branches.Clear(); requirements.Clear(); ScriptName = ""; WithContent = 0;
            cachedKey = null; applyNote = null; armedApply = null;
        }

        private static bool HasContent(BranchHint hint) => hint != null && (hint.Effects.Count > 0 || hint.Consequences.Count > 0);

        // Patch bodies run inside native game code, so a failure is reported once and
        // swallowed instead of escaping into the native method.
        private static bool patchFailureReported;
        internal static void PatchFailed(Exception error)
        {
            if (patchFailureReported) return;
            patchFailureReported = true;
            try { Plugin.Instance?.Report(error); } catch (Exception) { }
        }

        internal static void Bind(Component target, Text source, Text condition = null)
        {
            var pointer = target.GetComponent<HintPointer>() ?? target.gameObject.AddComponent<HintPointer>();
            pointer.Source = source; pointer.Condition = condition; pointer.Revision = Revision;
        }

        internal static void Enter(HintPointer pointer)
        {
            if (hovered != pointer) armedApply = null;
            hovered = pointer; leaving = null;
        }
        // The tooltip has to survive the pointer travelling to its own button, so leaving
        // a row only marks it; Draw clears it once the pointer is genuinely away.
        internal static void Leave(HintPointer pointer) { if (hovered == pointer) leaving = pointer; }

        // Dice rows carry "localisation key|condition"; story rows carry the bare key.
        private static string Key(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            int bar = raw.IndexOf('|');
            return (bar < 0 ? raw : raw.Substring(0, bar)).Trim();
        }

        private static BranchHint Find(string raw)
        {
            BranchHint hint;
            return branches.TryGetValue(Key(raw), out hint) ? hint : null;
        }

        private static OptionRequirement FindRequirement(string raw)
        {
            OptionRequirement requirement;
            return requirements.TryGetValue(Key(raw), out requirement) ? requirement : null;
        }

        // ---- inline summary ("always" mode) -------------------------------------

        // Weak keys: a cleared dictionary would forget which rows already carry a suffix and
        // the next decoration would append it again. Entries must die with the Text object.
        private static readonly ConditionalWeakTable<Text, string> decorated = new ConditionalWeakTable<Text, string>();

        internal static void Decorate(Text source, Text[] texts)
        {
            var options = Plugin.Instance == null ? null : Plugin.Instance.Options;
            if (!Plugin.Active || options == null || options.HintMode.Value != OptionHintMode.Always) return;
            if (source == null || texts == null) return;
            string summary = Summary(source.text);
            if (summary.Length == 0) return;
            string suffix = "　" + summary;
            foreach (Text text in texts)
            {
                if (text == null) continue;
                string current = text.text ?? "";
                string previous;
                if (decorated.TryGetValue(text, out previous) && previous.Length > 0 &&
                    current.EndsWith(previous, StringComparison.Ordinal))
                    current = current.Substring(0, current.Length - previous.Length);
                else if (current.EndsWith(suffix, StringComparison.Ordinal))
                    current = current.Substring(0, current.Length - suffix.Length);   // already decorated
                text.text = current + suffix;
                decorated.Remove(text);
                decorated.Add(text, suffix);
            }
        }

        // Two plain effects at most: the row has to stay readable next to the option.
        private static string Summary(string raw)
        {
            BranchHint hint = Find(raw);
            if (hint == null) return "";
            var parts = new List<string>();
            foreach (EffectCall effect in hint.Effects)
            {
                if (!PlainKinds.Contains(effect.Kind) || effect.Condition != null) continue;
                string line = HintNames.Natural(EffectText(effect, false, UiLang.Hans), UiLang.Hans);
                if (line.Length == 0) continue;
                parts.Add(line);
                if (parts.Count >= 2) break;
            }
            return string.Join(" · ", parts.ToArray());
        }

        // ---- hover detail -------------------------------------------------------

        private static string cachedKey;
        private static int cachedRevision = -1;
        private static ConditionReport cachedReport;

        // Reading a condition touches the game's tables; keep one answer per row until the
        // script changes or a value is written.
        private static ConditionReport Report(string key, OptionRequirement requirement)
        {
            if (requirement == null) return null;
            if (cachedKey == key && cachedRevision == Revision && cachedReport != null) return cachedReport;
            cachedKey = key;
            cachedRevision = Revision;
            cachedReport = OptionConditions.Describe(requirement);
            return cachedReport;
        }

        // Labels are translated as they are emitted: the assembled text carries dynamic
        // values, so translating the finished string can never match a table entry.
        private static string L(UiLang language, string text) => UiLanguage.Translate(text, language);

        private static string Detail(BranchHint hint, OptionRequirement requirement, ConditionReport report,
            bool locked, OptionHintDetail detail, UiLang language)
        {
            var output = new StringBuilder();
            // A locked row always says why; an open row only explains itself at the detailed level.
            if (requirement != null && report != null && (locked || detail == OptionHintDetail.Full))
            {
                output.Append(L(language, locked ? "解锁条件（未满足）" : "解锁条件（已满足）")).Append('\n');
                if (report.Lines.Count == 0) output.Append(HintNames.Natural(HintNames.Checkpoint(requirement.Name, requirement.Kind.ToString()), language)).Append('\n');
                foreach (string line in report.Lines) output.Append(HintNames.Natural(line, language)).Append('\n');
                if (report.Block.Length > 0 && !report.CanApply) output.Append("（").Append(report.Block).Append("）\n");
                else if (locked) output.Append(L(language, "（满足后需重新打开选项才生效）\n"));
            }
            if (hint == null) return Trim(output);
            var plain = new List<string>();
            var groups = new List<KeyValuePair<string, List<string>>>();
            foreach (EffectCall effect in hint.Effects)
            {
                if (detail == OptionHintDetail.Simple && !PlainKinds.Contains(effect.Kind)) continue;
                string line = HintNames.Natural(EffectText(effect, true, language), language);
                if (line.Length == 0) continue;
                if (effect.Condition == null) { plain.Add(line); continue; }
                int at = groups.FindIndex(g => g.Key == effect.Condition);
                if (at < 0) groups.Add(new KeyValuePair<string, List<string>>(effect.Condition, new List<string> { line }));
                else groups[at].Value.Add(line);
            }
            if (plain.Count > 0)
            {
                if (output.Length > 0) output.Append('\n');
                output.Append(L(language, "选择效果\n")).Append(string.Join("\n", plain.ToArray()));
            }
            if (groups.Count > 0)
            {
                if (output.Length > 0) output.Append("\n\n");
                output.Append(L(language, "条件效果（满足时才生效）"));
                foreach (var group in groups)
                {
                    output.Append('\n')
                        .Append(HintText.Condition(group.Key, language))
                        .Append("：").Append(string.Join(" · ", group.Value.ToArray()));
                }
            }
            if (detail == OptionHintDetail.Full && hint.Consequences.Count > 0)
            {
                if (output.Length > 0) output.Append("\n\n");
                output.Append(L(language, "去向\n"));
                for (int i = 0; i < hint.Consequences.Count; i++)
                {
                    if (i > 0) output.Append('\n');
                    output.Append(HintNames.Consequence(hint.Consequences[i], language));
                }
                if (hint.Incomplete) output.Append(L(language, "\n（另有按条件执行的部分未列出）"));
            }
            return Trim(output);
        }

        private static string Trim(StringBuilder output)
        {
            return output.ToString().TrimEnd('\n');
        }

        private static string EffectText(EffectCall effect, bool rich, UiLang language) => HintText.Effect(effect, rich, language);

        // ---- drawing ------------------------------------------------------------

        private static GUIStyle overlayStyle, overlayButtonStyle;
        private static string armedApply;
        private static string applyNote;
        private static float applyNoteUntil;
        private static HintPointer leaving;
        private static Rect lastAnchor, lastBox, lastButton;
        private static bool hasButton;

        internal static void Draw()
        {
            var options = Plugin.Instance == null ? null : Plugin.Instance.Options;
            if (!Plugin.Active || options == null || options.HintMode.Value == OptionHintMode.Off ||
                Plugin.Instance.Panel.Visible || hovered == null || !hovered.isActiveAndEnabled ||
                hovered.Revision != Revision || hovered.Source == null)
            {
                if (hovered != null && (!hovered.isActiveAndEnabled || hovered.Revision != Revision)) hovered = null;
                return;
            }
            if (leaving == hovered && !NearTooltip(Input.mousePosition))
            {
                hovered = null; leaving = null; hasButton = false; armedApply = null;
                return;
            }
            string rawKey = hovered.Source.text;
            string key = Key(rawKey);
            BranchHint hint = Find(rawKey);
            OptionRequirement requirement = FindRequirement(rawKey);
            if (hint == null && requirement == null) return;
            bool locked = requirement != null && requirement.HasLocked && key == requirement.LockedKey;
            ConditionReport report = Report(key, requirement);
            string text = Detail(hint, requirement, report, locked, options.HintDetail.Value, options.LanguageKind);
            if (applyNote != null && Time.unscaledTime < applyNoteUntil)
            {
                if (text.Length > 0) text += "\n\n";
                text += applyNote;
            }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (overlayStyle == null)
                overlayStyle = new GUIStyle(GUI.skin.box)
                {
                    wordWrap = true, richText = true, alignment = TextAnchor.UpperLeft, fontSize = 18,
                    padding = new RectOffset(14, 14, 12, 12)
                };
            if (overlayButtonStyle == null)
                overlayButtonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 18, alignment = TextAnchor.MiddleCenter, richText = false,
                    padding = new RectOffset(8, 8, 4, 4)
                };
            // The game's option font carries Chinese glyphs only; a translated tooltip drawn
            // with it turns into empty boxes on a Korean panel.
            Font overlayFont = hovered.Source.font;
            var bodyFont = Plugin.Instance?.Panel == null ? null : Plugin.Instance.Panel.BodyFont;
            if (bodyFont != null && options.LanguageKind != UiLang.Hans) overlayFont = bodyFont;
            if (overlayFont != null)
            {
                overlayStyle.font = overlayFont;
                overlayButtonStyle.font = overlayFont;
            }
            float width = Mathf.Min(360, Screen.width - 24);
            float height = overlayStyle.CalcHeight(new GUIContent(text), width);
            if (height > Screen.height - 24 && options.HintDetail.Value == OptionHintDetail.Full)
            {
                // Too tall to show at all: fall back to the compact detail instead of showing
                // nothing, which read as "this option has no hint".
                string compact = Detail(hint, requirement, report, locked, OptionHintDetail.Simple,
                    options.LanguageKind);
                float compactHeight = overlayStyle.CalcHeight(new GUIContent(compact), width);
                if (compactHeight < height) { text = compact; height = compactHeight; }
            }
            if (width < 140 || height > Screen.height - 24) return;
            Rect anchor = ScreenRect(hovered.transform as RectTransform);
            lastAnchor = anchor;
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
                lastBox = box;
                if (Event.current.type == EventType.Repaint) GUI.Box(box, text, overlayStyle);
                DrawApply(box, key, report, requirement, locked);
                return;
            }
        }

        // The tooltip and its button sit beside the row; the pointer must be able to cross
        // the gap between them without the tooltip vanishing under it.
        private static bool NearTooltip(Vector2 mouse)
        {
            Vector2 point = new Vector2(mouse.x, Screen.height - mouse.y);
            const float slack = 28f;
            if (Grow(lastAnchor, slack).Contains(point) || Grow(lastBox, slack).Contains(point)) return true;
            return hasButton && Grow(lastButton, slack).Contains(point);
        }

        private static Rect Grow(Rect rect, float slack)
        {
            return new Rect(rect.x - slack, rect.y - slack, rect.width + slack * 2f, rect.height + slack * 2f);
        }

        // The one write this overlay offers: a simple condition's single stat. It never
        // touches the option row, so a locked row cannot be made to run another branch.
        private static void DrawApply(Rect box, string key, ConditionReport report, OptionRequirement requirement, bool locked)
        {
            // Nothing to unlock on an open row.
            if (!locked || report == null || !report.CanApply)
            {
                hasButton = false;
                return;
            }
            bool armed = armedApply == key;
            Rect button = new Rect(box.x, box.yMax + 6f, box.width, 30f);
            if (button.yMax > Screen.height - 6f)
            {
                button.y = box.y - 36f;
                if (button.y < 6f) { hasButton = false; return; }
            }
            hasButton = true;
            lastButton = button;
            if (Event.current.type == EventType.Repaint)
                GUI.Box(button, armed ? "确认设为 " + report.Needed + "？" : "一键设为 " + report.Needed, overlayButtonStyle);
            else if (Event.current.type == EventType.MouseUp && button.Contains(
                new Vector2(Event.current.mousePosition.x, Event.current.mousePosition.y)))
            {
                Event.current.Use();
                if (armed)
                {
                    armedApply = null;
                    OptionConditions.ApplyResult result = OptionConditions.Apply(report, requirement);
                    applyNote = result.Message;
                    applyNoteUntil = Time.unscaledTime + 5f;
                    if (Plugin.Instance != null) Plugin.Instance.Status = applyNote;
                    cachedKey = null;
                    // Same in-place unlock the reference mods use: the row's own listener runs
                    // the option's branch, so only the lock state and the shown text change.
                    if (result.Wrote && result.Satisfied) OpenRow(requirement);
                }
                else armedApply = key;
            }
        }

        // Turns the locked row into the open one the game would have drawn: the displayed
        // text becomes the unlocked key and the button becomes selectable again.
        private static void OpenRow(OptionRequirement requirement)
        {
            if (hovered == null || requirement == null || string.IsNullOrEmpty(requirement.TextKey)) return;
            GameObject row = hovered.gameObject;
            if (row == null) return;
            Button button = row.GetComponent<Button>();
            if (button != null) button.interactable = true;
            if (hovered.Source != null) hovered.Source.text = requirement.TextKey;
            var option = row.GetComponent<IMenuDialogOption>();
            if (option != null) option.UpdateContent();
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
        // A renamed private field must cost the hint, not leave UpdateContent
        // half-executed: Harmony never catches postfix exceptions.
        private static void Postfix(NormalOptionButton __instance)
        {
            try
            {
                Text source = GameAccess.Field<Text>(__instance, "_source");
                HintAdapter.Bind(__instance, source);
                HintAdapter.Decorate(source, GameAccess.Field<Text[]>(__instance, "_text"));
                OptionClickRecorder.Attach(__instance, source);
                StoryReplay.NoteOptionContentUpdated();
            }
            catch (Exception error) { HintAdapter.PatchFailed(error); }
        }
    }

    [HarmonyPatch(typeof(DiceOption), "UpdateContent")]
    internal static class DiceHintPatch
    {
        private static void Postfix(DiceOption __instance)
        {
            try
            {
                HintAdapter.Bind(__instance, GameAccess.Field<Text>(__instance, "_sourceText"),
                    GameAccess.Field<Text>(__instance, "_conditionText"));
            }
            catch (Exception error) { HintAdapter.PatchFailed(error); }
        }
    }
}





