using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using MortalHelper.Core;
using HarmonyLib;
using Mortal.Core;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    // Combat has no SaveGamePanel or save button in its native scene. Reuse its
    // numbered slot list with an explicit save mode and the native confirmation
    // dialog. The ordinary load path is restored when the list closes.
    internal static class DuelSaveMenu
    {
        private static readonly DuelSaveMenuState state = new DuelSaveMenuState();
        private static readonly Dictionary<GameObject, bool> visibility = new Dictionary<GameObject, bool>();
        private static readonly Dictionary<Button, bool> interactability = new Dictionary<Button, bool>();
        private static readonly Dictionary<Button, Navigation> navigation = new Dictionary<Button, Navigation>();
        private static readonly Dictionary<TMP_Text, string> tmpText = new Dictionary<TMP_Text, string>();
        private static readonly Dictionary<Text, string> uiText = new Dictionary<Text, string>();
        private static MenuPanel owner;
        private static LoadGamePanel panel;
        private static Button saveButton;
        private static GameObject title;
        private static bool closing;
        internal static bool Active => state.Active;
        private static string Label(string text) => UiLanguage.Convert(text, Plugin.Instance.Options.TraditionalChinese.Value);

        internal static void Ensure(MenuPanel menu, bool available)
        {
            try
            {
                if (owner != menu || saveButton == null)
                {
                    Close();
                    owner = menu;
                    var load = GameAccess.Field<Button>(menu, "_loadButton");
                    var common = GameAccess.Field<CommonPanel>(menu, "_loadPanel");
                    if (load == null || common == null || common.GetComponent<LoadGamePanel>() == null)
                        throw new InvalidOperationException("单挑菜单的原生编号槽位列表不可用。");
                    saveButton = UnityEngine.Object.Instantiate(load, load.transform.parent, false);
                    saveButton.name = "MortalHelper_DuelSave";
                    saveButton.transform.SetSiblingIndex(load.transform.GetSiblingIndex());
                    // Native button groups normally use a layout. If this prefab
                    // uses fixed rows, insert one row and move the following rows.
                    if (load.transform.parent.GetComponent<LayoutGroup>() == null)
                    {
                        var rect = (RectTransform)load.transform;
                        float step = Mathf.Max(48f, rect.rect.height);
                        foreach (Transform child in load.transform.parent)
                            if (child != saveButton.transform && child.GetSiblingIndex() > saveButton.transform.GetSiblingIndex())
                                ((RectTransform)child).anchoredPosition += Vector2.down * step;
                    }
                    saveButton.onClick = new Button.ButtonClickedEvent();
                    saveButton.onClick.AddListener(() => Open(menu));
                    // Clone only the native button's visual/pointer behavior.
                    // Its load shortcut and localization binding would otherwise
                    // turn this save action back into a "load"-labelled control.
                    var input = saveButton.GetComponent<InputButton>();
                    if (input != null) input.enabled = false;
                    foreach (var binding in saveButton.GetComponentsInChildren<Behaviour>(true))
                        if (binding.GetType().Name == "LeanLocalizedText") binding.enabled = false;
                    foreach (var text in saveButton.GetComponentsInChildren<TMP_Text>(true)) text.text = Label("储存游戏");
                    foreach (var text in saveButton.GetComponentsInChildren<Text>(true)) text.text = Label("储存游戏");
                    Plugin.Instance.Log("[Anytime] duel native slot picker save button created");
                }
                saveButton.gameObject.SetActive(true);
                saveButton.interactable = available;
                var toggle = saveButton.GetComponent<MenuToggleButton>();
                if (toggle != null) { toggle.SetActive(available); toggle.Init(); }
                foreach (var text in saveButton.GetComponentsInChildren<Text>(true)) text.text = Label("储存游戏");
                foreach (var text in saveButton.GetComponentsInChildren<TMP_Text>(true)) text.text = Label("储存游戏");
            }
            catch (Exception error)
            {
                Close();
                AnytimeNative.SaveFailed("单挑储存菜单准备失败：" + error.GetBaseException().Message);
            }
        }

        private static void Open(MenuPanel menu)
        {
            try
            {
                Close();
                string blocked = AnytimeNative.Availability;
                if (blocked != null) throw new InvalidOperationException(blocked);
                var common = GameAccess.Field<CommonPanel>(menu, "_loadPanel");
                panel = common.GetComponent<LoadGamePanel>();
                state.Begin();
                common.Show(true);
                PrepareSlots();
                Plugin.Instance.Log("[Anytime] duel save slot picker opened");
            }
            catch (Exception error) { Abort(error); }
        }

        private static void Hide(GameObject obj)
        {
            if (obj == null) return;
            if (!visibility.ContainsKey(obj)) visibility.Add(obj, obj.activeSelf);
            obj.SetActive(false);
        }

        private static void PrepareSlots()
        {
            foreach (string name in new[] { "Container/RecentSave", "Container/AutoSave", "Container/Recent", "Container/Auto" })
            { var child = panel.transform.Find(name); if (child != null) Hide(child.gameObject); }
            var slots = GameAccess.Field<LoadSlotPanel[]>(panel, "_saveSlots")
                .Where(slot => slot != null && slot.gameObject.activeInHierarchy)
                .OrderBy(slot => slot.transform.GetSiblingIndex()).ToArray();
            for (int i = 0; i < slots.Length; i++)
            {
                Hide(slots[i].DeleteButton.gameObject);
                var button = slots[i].SlotButton;
                if (!navigation.ContainsKey(button)) navigation.Add(button, button.navigation);
                if (!interactability.ContainsKey(button)) interactability.Add(button, button.interactable);
                button.interactable = true;
                var nav = new Navigation { mode = Navigation.Mode.Explicit,
                    selectOnUp = i == 0 ? null : slots[i - 1].SlotButton,
                    selectOnDown = i + 1 == slots.Length ? null : slots[i + 1].SlotButton };
                button.navigation = nav;
            }
            var header = panel.transform.Find("Container/Header");
            if (header == null) throw new InvalidOperationException("编号槽位列表标题不可用。");
            // Reuse the actual native header Text, font and transform. Its
            // localized Text can coexist with an Image on the original object;
            // hiding only the Image leaves the old load heading visible.
            title = UnityEngine.Object.Instantiate(header.gameObject, header.parent, false);
            title.name = "MortalHelper_DuelSaveTitle";
            Hide(header.gameObject);
            foreach (var binding in title.GetComponentsInChildren<Behaviour>(true))
                if (binding.GetType().Name == "LeanLocalizedText") binding.enabled = false;
            var rect = (RectTransform)title.transform;
            rect.sizeDelta = new Vector2(Mathf.Max(rect.rect.width, 900f), rect.sizeDelta.y);
            bool labelled = false;
            foreach (var text in title.GetComponentsInChildren<Text>(true))
            {
                text.text = Label("储存游戏 · 单挑回合");
                text.color = Color.white; text.alignment = TextAnchor.MiddleLeft;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.raycastTarget = false; labelled = true;
            }
            foreach (var text in title.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = Label("储存游戏 · 单挑回合");
                text.color = Color.white; text.alignment = TextAlignmentOptions.MidlineLeft;
                text.raycastTarget = false; labelled = true;
            }
            if (!labelled) throw new InvalidOperationException("原生标题文字不可用。");
            if (slots.Length > 0) slots[0].SlotButton.Select();
        }

        private static bool Owns(LoadSlotPanel slot) => state.Active && panel != null && slot.transform.IsChildOf(panel.transform);

        private static void Choose(LoadSlotPanel slot)
        {
            try
            {
                string number = GameAccess.Field<string>(slot, "_slot");
                string path = AnytimeNative.PathFor(number);
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("无法确认编号槽位文件路径。");
                bool occupied = DuelSaveMenuState.NeedsOverwriteConfirmation(slot.HasSaveData,
                    File.Exists(path), SlotTransaction.Exists(SlotTransaction.JournalFor(path)));
                if (state.Select(number, occupied)) { Save(number); return; }
                panel.OpenDeletePanel(number);
                var confirm = GameAccess.Field<CommonPanel>(panel, "_deletePanel");
                var message = confirm.transform.Find("ConfirmDeletePanel/Message");
                if (message == null) throw new InvalidOperationException("原生覆盖确认文字不可用。");
                bool changed = false;
                foreach (var text in message.GetComponentsInChildren<TMP_Text>(true))
                { tmpText[text] = text.text; text.text = Label("确认覆盖编号槽 " + number + " 的存档？"); changed = true; }
                foreach (var text in message.GetComponentsInChildren<Text>(true))
                { uiText[text] = text.text; text.text = Label("确认覆盖编号槽 " + number + " 的存档？"); changed = true; }
                if (!changed) throw new InvalidOperationException("无法标明覆盖确认，已取消保存。");
            }
            catch (Exception error) { Abort(error); }
        }

        private static void Save(string number)
        {
            try
            {
                if (!AnytimeDuel.SaveToSlot(number)) { Close(); return; }
                Plugin.Instance.Log("[Anytime] duel save slot picker completed slot=" + number);
                Close();
            }
            catch (Exception error) { Abort(error); }
        }

        private static void Abort(Exception error)
        {
            Close();
            AnytimeNative.SaveFailed("单挑储存已取消：" + error.GetBaseException().Message);
        }

        internal static void Close()
        {
            if (closing) return;
            closing = true;
            var current = panel;
            state.Close();
            try
            {
                // Retire native delete intent before restoring objects or
                // triggering any close/enable callbacks.
                if (current != null)
                {
                    GameAccess.Set(current, "_currentDeleteSlot", "");
                    GameAccess.Set<GameObject>(current, "_currentSelectableObject", null);
                }
                foreach (var item in visibility) if (item.Key != null) item.Key.SetActive(item.Value);
                foreach (var item in navigation) if (item.Key != null) item.Key.navigation = item.Value;
                foreach (var item in interactability) if (item.Key != null) item.Key.interactable = item.Value;
                foreach (var item in tmpText) if (item.Key != null) item.Key.text = item.Value;
                foreach (var item in uiText) if (item.Key != null) item.Key.text = item.Value;
                if (title != null) UnityEngine.Object.Destroy(title);
                if (current != null)
                {
                    var confirm = GameAccess.Field<CommonPanel>(current, "_deletePanel");
                    if (confirm != null) confirm.Show(false, false);
                    GameAccess.Field<InputButton>(current, "_closeButton").SetActive(true);
                    current.GetComponent<CommonPanel>().Show(false);
                    foreach (var slot in GameAccess.Field<LoadSlotPanel[]>(current, "_saveSlots")) slot.Setup();
                }
            }
            catch (Exception error) { Plugin.Instance?.Log("[Anytime] duel save menu close: " + error.GetBaseException().Message); }
            finally
            {
                visibility.Clear(); navigation.Clear(); interactability.Clear(); tmpText.Clear(); uiText.Clear();
                panel = null; title = null; closing = false;
            }
        }

        [HarmonyPatch(typeof(LoadSlotPanel), "OnTitleClick")]
        private static class SlotClickPatch
        {
            private static bool Prefix(LoadSlotPanel __instance)
            {
                if (!Owns(__instance)) return true;
                // Only the Selecting phase may start a slot choice. The slot list stays
                // clickable while the native overwrite panel is up, and letting that
                // click reach Select() made it throw and abort the whole save.
                if (state.Phase != DuelSaveMenuPhase.Selecting) return false;
                Choose(__instance);
                return false;
            }
        }
        [HarmonyPatch(typeof(LoadSlotPanel), "DeleteButtonClick")]
        private static class DeleteClickPatch
        { private static bool Prefix(LoadSlotPanel __instance) => !Owns(__instance); }
        [HarmonyPatch(typeof(LoadGamePanel), "DeleteActionPerformed")]
        private static class DeleteKeyPatch
        { private static bool Prefix(LoadGamePanel __instance) => !state.Active || __instance != panel; }
        [HarmonyPatch(typeof(LoadGamePanel), "ConfirmDeleteSlot")]
        private static class ConfirmPatch
        {
            private static bool Prefix(LoadGamePanel __instance)
            {
                var route = state.ConfirmationRoute(__instance == panel,
                    GameAccess.Field<string>(__instance, "_currentDeleteSlot"));
                if (route == DuelConfirmationRoute.NativeDelete) return true;
                if (route == DuelConfirmationRoute.Save)
                    try { Save(state.Confirm()); } catch (Exception error) { Abort(error); }
                return false;
            }
        }
        [HarmonyPatch(typeof(LoadGamePanel), "CloseDeletePanel")]
        private static class CancelConfirmationPatch
        { private static void Postfix(LoadGamePanel __instance) { if (state.Active && __instance == panel) Close(); } }
        [HarmonyPatch(typeof(LoadGamePanel), "OnDisable")]
        private static class ClosePatch
        { private static void Postfix(LoadGamePanel __instance) { if (state.Active && __instance == panel) Close(); } }
        [HarmonyPatch(typeof(SaveSystem), "LoadGameData")]
        private static class NeverLoadInSaveModePatch
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                if (state.AllowNativeLoad) return true;
                Abort(new InvalidOperationException("储存模式不能读取存档。"));
                return false;
            }
        }
        [HarmonyPatch(typeof(SceneController), "LoadScene")]
        private static class SceneChangePatch
        { private static void Prefix() { if (state.Active) Close(); } }
    }
}
