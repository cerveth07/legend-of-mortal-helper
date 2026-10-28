using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Read-only probe. The travel map draws its place names as scene text objects, and
    // that placement exists nowhere else, so it is dumped once: text, screen point,
    // anchored point, font size and colour. Diagnostics/DumpMapLabels turns it on.
    internal static class MapLabelDump
    {
        private static bool done;
        private static float nextAt;

        internal static void Tick()
        {
            if (done || Plugin.Instance == null || Plugin.Instance.Options == null) return;
            if (!Plugin.Instance.Options.DumpMapLabels.Value) return;
            if (Time.unscaledTime < nextAt) return;
            nextAt = Time.unscaledTime + 2f;
            try
            {
                Transform root = FindNameRoot();
                if (root == null) return;
                done = true;
                Dump(root);
            }
            catch (Exception error)
            {
                done = true;
                Plugin.Instance.Log("[MapLabels] failed: " + error);
            }
        }

        // The label container is a child named "[Name]" of the map background; other
        // scenes reuse the name, so require a plausible number of children.
        private static Transform FindNameRoot()
        {
            foreach (Transform candidate in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (candidate == null || candidate.name != "[Name]" || candidate.childCount < 5) continue;
                Transform parent = candidate.parent;
                if (parent == null) continue;
                if (parent.name != "Background" && parent.parent == null) continue;
                return candidate;
            }
            return null;
        }

        private static void Dump(Transform root)
        {
            var background = root.parent as RectTransform;
            Plugin.Instance.Log("[MapLabels] root=" + Path(root) + " children=" + root.childCount +
                " background=" + (background != null ? background.rect.size.ToString() : "?") +
                " screen=" + Screen.width + "x" + Screen.height);
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                var rect = child as RectTransform;
                string text = "";
                int size = 0;
                string colour = "";
                string font = "";
                var ui = child.GetComponent<Text>();
                if (ui != null)
                {
                    text = ui.text;
                    size = ui.fontSize;
                    colour = ColorUtility.ToHtmlStringRGBA(ui.color);
                    font = ui.font != null ? ui.font.name : "";
                }
                else
                {
                    var tmp = child.GetComponent<TMP_Text>();
                    if (tmp != null)
                    {
                        text = tmp.text;
                        size = Mathf.RoundToInt(tmp.fontSize);
                        colour = ColorUtility.ToHtmlStringRGBA(tmp.color);
                        font = tmp.font != null ? tmp.font.name : "";
                    }
                }
                Vector2 screen = rect != null
                    ? RectTransformUtility.WorldToScreenPoint(null, rect.position)
                    : Vector2.zero;
                Plugin.Instance.Log("[MapLabels] " + child.name +
                    " text=\"" + (text ?? "").Replace("\n", "\\n") + "\"" +
                    " screen=(" + screen.x.ToString("0.0") + "," + screen.y.ToString("0.0") + ")" +
                    " anchored=" + (rect != null ? rect.anchoredPosition.ToString() : "?") +
                    " size=" + size + " color=#" + colour + " font=" + font +
                    " active=" + child.gameObject.activeInHierarchy);
            }
        }

        private static string Path(Transform transform)
        {
            var builder = new StringBuilder();
            for (Transform cursor = transform; cursor != null; cursor = cursor.parent)
                builder.Insert(0, "/" + cursor.name);
            return builder.ToString();
        }
    }
}
