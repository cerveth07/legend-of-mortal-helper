using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Mortal.Core;
using MortalHelper.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    internal static class LegendSlots
    {
        private static readonly MethodInfo GetPath = AccessTools.Method(typeof(SaveSystem), "GetLegendDataFile");
        // The handle is resolved by name once. A game update must degrade this single
        // feature, not throw a NullReferenceException out of a prefix.
        private static bool missingPathReported;

        private static int VisibleCount()
        {
            if (GetPath == null || SaveSystem.Instance == null)
            {
                if (!missingPathReported)
                {
                    missingPathReported = true;
                    Plugin.Instance?.Log("[LegendSlots] SaveSystem.GetLegendDataFile unavailable; legend slot expansion disabled.");
                }
                // With no readable slot list the occupancy scan is meaningless: the
                // configured limit only ever grows past the native 12 once slots fill.
                return LegendSlotCapacity.Original;
            }
            string path = (string)GetPath.Invoke(SaveSystem.Instance, new object[] { 1 });
            string directory = Path.GetDirectoryName(path);
            var saved = new List<int>();
            if (Directory.Exists(directory))
                foreach (string file in Directory.EnumerateFiles(directory, "Legend_*.dat"))
                {
                    int slot;
                    if (int.TryParse(Path.GetFileNameWithoutExtension(file).Substring(7), out slot)) saved.Add(slot);
                }
            return LegendSlotCapacity.Visible(Plugin.Instance.Options.LegendSlotCount.Value, saved);
        }

        internal static void Save(EndGamePanel owner)
        {
            var slots = GameAccess.Field<SaveLegendSlotPanel[]>(owner, "_saveSlots");
            if (slots == null || slots.Length == 0) return;
            GameAccess.Set(owner, "_saveSlots", Expand(slots[0], slot => slot.Setup()));
        }

        internal static void Load(LibraryPanel owner)
        {
            var root = GameAccess.Field<Transform>(owner, "_legendPanelTransform");
            var slot = root == null ? null : root.GetComponentInChildren<LoadLegendSlotPanel>(true);
            if (slot != null) Expand(slot, item => item.Setup());
        }

        private static T[] Expand<T>(T template, Action<T> setup) where T : MonoBehaviour
        {
            int count = VisibleCount();
            var content = (RectTransform)template.transform.parent;
            var list = content.GetComponent<LegendSlotList>() ?? content.gameObject.AddComponent<LegendSlotList>();
            list.Initialize((RectTransform)template.transform);
            var slots = new List<T>(content.GetComponentsInChildren<T>(true));
            slots.Sort((a, b) => GameAccess.Field<int>(a, "_slot").CompareTo(GameAccess.Field<int>(b, "_slot")));
            while (slots.Count < count)
            {
                T added = UnityEngine.Object.Instantiate(template, content, false);
                GameAccess.Set(added, "_slot", slots.Count + 1);
                slots.Add(added);
            }
            for (int index = 0; index < slots.Count; index++)
            {
                T slot = slots[index];
                bool visible = index < count;
                slot.gameObject.SetActive(visible);
                if (!visible) continue;
                setup(slot);
                var button = GameAccess.Field<Button>(slot, "_button");
                var navigation = button.navigation;
                navigation.mode = Navigation.Mode.Horizontal;
                button.navigation = navigation;
                var reveal = button.GetComponent<LegendSlotReveal>() ?? button.gameObject.AddComponent<LegendSlotReveal>();
                reveal.Scroll = list.Scroll;
            }
            list.Resize(count);
            Plugin.Instance.Log("[LegendSlots] " + typeof(T).Name + " visible=" + count);
            return slots.ToArray();
        }
    }

    public sealed class LegendSlotList : MonoBehaviour
    {
        internal ScrollRect Scroll;
        private float bookWidth;
        private HorizontalLayoutGroup layout;

        internal void Initialize(RectTransform book)
        {
            if (Scroll != null) return;
            var content = (RectTransform)transform;
            bookWidth = book.rect.width;
            layout = GetComponent<HorizontalLayoutGroup>();
            var viewportObject = new GameObject("MortalHelper.LegendViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(content.parent, false);
            viewport.SetSiblingIndex(content.GetSiblingIndex());
            viewport.anchorMin = content.anchorMin; viewport.anchorMax = content.anchorMax;
            viewport.pivot = content.pivot; viewport.sizeDelta = content.sizeDelta;
            viewport.anchoredPosition3D = content.anchoredPosition3D;
            viewport.localRotation = content.localRotation; viewport.localScale = content.localScale;
            viewportObject.GetComponent<Image>().color = Color.clear;
            content.SetParent(viewport, false);
            content.localScale = Vector3.one; content.localRotation = Quaternion.identity;
            content.anchorMin = Vector2.zero; content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, .5f); content.anchoredPosition = Vector2.zero;
            layout.childControlWidth = false; layout.childForceExpandWidth = false;
            Scroll = viewportObject.GetComponent<ScrollRect>();
            Scroll.viewport = viewport; Scroll.content = content;
            Scroll.horizontal = true; Scroll.vertical = false;
            Scroll.movementType = ScrollRect.MovementType.Clamped;
            Scroll.scrollSensitivity = bookWidth;
        }

        internal void Resize(int count)
        {
            float width = count * bookWidth + (count - 1) * layout.spacing + layout.padding.horizontal;
            Scroll.content.sizeDelta = new Vector2(Mathf.Max(Scroll.viewport.rect.width, width), 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(Scroll.content);
        }
    }

    public sealed class LegendSlotReveal : MonoBehaviour, ISelectHandler
    {
        internal ScrollRect Scroll;
        public void OnSelect(BaseEventData data)
        {
            if (Scroll == null) return;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(Scroll.viewport, transform);
            Rect view = Scroll.viewport.rect;
            Vector2 position = Scroll.content.anchoredPosition;
            if (bounds.min.x < view.xMin) position.x += view.xMin - bounds.min.x;
            else if (bounds.max.x > view.xMax) position.x -= bounds.max.x - view.xMax;
            Scroll.StopMovement();
            Scroll.content.anchoredPosition = position;
        }
    }

    [HarmonyPatch(typeof(LibraryPanel), "OnLegendPanelOpen")]
    internal static class LoadLegendSlotsPatch
    {
        private static void Prefix(LibraryPanel __instance) { LegendSlots.Load(__instance); }
    }

    [HarmonyPatch(typeof(EndGamePanel), "OnSaveLegendPanelOpen")]
    internal static class OpenSaveLegendSlotsPatch
    {
        private static void Prefix(EndGamePanel __instance) { LegendSlots.Save(__instance); }
    }

    [HarmonyPatch]
    internal static class GrowLegendSlotsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EndGamePanel), "OnSaveLegendHandler");
            yield return AccessTools.Method(typeof(EndGamePanel), "OnConfirmSubmitClick");
        }
        private static void Postfix(EndGamePanel __instance) { LegendSlots.Save(__instance); }
    }
}
