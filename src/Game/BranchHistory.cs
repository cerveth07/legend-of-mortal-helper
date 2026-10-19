using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Mortal.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Branch bookkeeping: remembers which option texts were chosen in each script
    // and marks them on the option itself, like the previous panel. Stored as plain
    // lines next to the config; never inside a save file.
    internal static class BranchHistory
    {
        private const int MaximumLines = 20000;
        private static readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> Order = new List<string>();
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static bool loaded;

        internal static string FilePath
        {
            get
            {
                try { return Path.Combine(Path.GetDirectoryName(Plugin.Instance.Options.Config.ConfigFilePath), "cerveth.mortal.helper.choices.txt"); }
                catch (Exception) { return null; }
            }
        }

        internal static bool Enabled => Plugin.Instance != null && Plugin.Active && Plugin.Instance.Options.ShowBranches.Value;

        private static string Key(string script, string option)
        {
            return (script ?? "") + "\u0001" + (option ?? "").Trim();
        }

        internal static bool Has(string script, string option)
        {
            Load();
            return Seen.Contains(Key(script, option));
        }

        internal static void Record(string script, string option)
        {
            if (string.IsNullOrEmpty(script) || string.IsNullOrWhiteSpace(option)) return;
            Load();
            string key = Key(script, option);
            if (!Seen.Add(key)) return;
            Order.Add(key);
            try
            {
                string path = FilePath;
                if (path != null) File.AppendAllText(path, key + "\n", Utf8);
            }
            catch (Exception) { }
        }

        internal static IReadOnlyList<string> Recent(int maximum)
        {
            Load();
            var list = new List<string>();
            for (int i = Math.Max(0, Order.Count - maximum); i < Order.Count; i++)
            {
                string[] parts = Order[i].Split('\u0001');
                list.Add(parts.Length > 1 ? parts[0] + "：" + parts[1] : Order[i]);
            }
            return list;
        }

        internal static void Clear()
        {
            Seen.Clear(); Order.Clear();
            try { string path = FilePath; if (path != null && File.Exists(path)) File.Delete(path); }
            catch (Exception) { }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                string path = FilePath;
                if (path == null || !File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path, Utf8))
                {
                    if (string.IsNullOrEmpty(line) || !Seen.Add(line)) continue;
                    if (Order.Count < MaximumLines) Order.Add(line);
                }
            }
            catch (Exception) { }
        }
    }

    // Attached to each story option: draws the 〔已走〕/〔未记录〕 label and records
    // the click. Observation only; the native choice flow is untouched.
    internal sealed class BranchMarker : MonoBehaviour, IPointerClickHandler
    {
        private Text label, source;
        private string script = "";

        internal void Bind(Text text, string scriptName)
        {
            source = text;
            script = scriptName ?? "";
            if (source == null) return;
            if (label == null)
            {
                var item = new GameObject("MortalHelper.Branch",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                item.transform.SetParent(source.transform, false);
                label = item.GetComponent<Text>();
                label.raycastTarget = false;
                label.supportRichText = false;
                label.alignment = TextAnchor.MiddleRight;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                var rect = label.rectTransform;
                rect.anchorMin = new Vector2(0, 0);
                rect.anchorMax = new Vector2(0, 1);
                rect.pivot = new Vector2(1, .5f);
                rect.anchoredPosition = new Vector2(-8, 0);
                rect.sizeDelta = new Vector2(96, 0);
            }
            Refresh();
        }

        private void Update() { Refresh(); }

        private void Refresh()
        {
            if (label == null) return;
            bool visible = BranchHistory.Enabled && source != null && source.isActiveAndEnabled &&
                Plugin.Instance != null && !Plugin.Instance.Panel.Visible;
            label.enabled = visible;
            if (!visible) return;
            label.font = source.font;
            label.fontSize = Mathf.Max(12, Mathf.Min(source.fontSize, 18));
            label.color = source.color;
            label.text = BranchHistory.Has(script, source.text) ? "〔已走〕" : "〔未记录〕";
        }

        public void OnPointerClick(PointerEventData data)
        {
            if (data == null || data.button != PointerEventData.InputButton.Left) return;
            if (!BranchHistory.Enabled || source == null) return;
            BranchHistory.Record(script, source.text);
        }
    }

    [HarmonyPatch(typeof(NormalOptionButton), "UpdateContent")]
    internal static class BranchOptionPatch
    {
        private static void Postfix(NormalOptionButton __instance)
        {
            try
            {
                var source = GameAccess.Field<Text>(__instance, "_source");
                if (source == null) return;
                var marker = __instance.GetComponent<BranchMarker>() ?? __instance.gameObject.AddComponent<BranchMarker>();
                marker.Bind(source, HintAdapter.ScriptName);
            }
            catch (System.Exception) { }
        }
    }
}
