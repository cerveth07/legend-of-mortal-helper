using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    public sealed class DiceWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private InputField input;
        private Text message;
        private Text title, placeholder, clearText;
        private Button clear, save;
        private Text saveText;
        private bool updating;
        private bool dragging;
        private Vector2 dragOffset;
        private readonly Vector3[] corners = new Vector3[4];

        private Camera UiCamera
        {
            get
            {
                var canvas = GetComponentInParent<Canvas>();
                return canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }
        }

        internal void RestorePosition()
        {
            var options = Plugin.Instance.Options;
            if (options.DiceX.Value >= 0f && options.DiceY.Value >= 0f)
                MoveTo(new Vector2(options.DiceX.Value * Screen.width, options.DiceY.Value * Screen.height));
        }

        public void OnBeginDrag(PointerEventData data)
        {
            // Only the background/title area drags; editing and buttons keep their own gestures.
            if (data.button != PointerEventData.InputButton.Left || data.pointerPressRaycast.gameObject != gameObject) return;
            dragging = true;
            dragOffset = RectTransformUtility.WorldToScreenPoint(UiCamera, transform.position) - data.position;
        }

        public void OnDrag(PointerEventData data)
        {
            if (dragging) MoveTo(data.position + dragOffset);
        }

        public void OnEndDrag(PointerEventData data)
        {
            if (!dragging) return;
            dragging = false;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(UiCamera, transform.position);
            Plugin.Instance.Options.DiceX.Value = point.x / Screen.width;
            Plugin.Instance.Options.DiceY.Value = point.y / Screen.height;
        }

        private void MoveTo(Vector2 point)
        {
            var rect = (RectTransform)transform;
            Camera camera = UiCamera;
            Vector2 current = RectTransformUtility.WorldToScreenPoint(camera, transform.position);
            rect.GetWorldCorners(corners);
            Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            point.x = Mathf.Clamp(point.x, current.x - min.x + 8f, Screen.width - (max.x - current.x) - 8f);
            point.y = Mathf.Clamp(point.y, current.y - min.y + 8f, Screen.height - (max.y - current.y) - 8f);
            Vector3 world;
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle((RectTransform)transform.parent, point, camera, out world))
                transform.position = world;
        }

        internal void Initialize(Text reference)
        {
            var root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = new Vector2(.5f, 0f);
            root.pivot = new Vector2(.5f, 0f);
            // Keep helper controls beside the wheel, clear of its face and the options below.
            root.anchoredPosition = new Vector2(560, 120);
            root.sizeDelta = new Vector2(540, 96);
            var background = gameObject.AddComponent<Image>();
            background.color = new Color(.08f, .09f, .1f, .95f);
            title = AddText("", reference, new Vector2(12, 51), new Vector2(110, 34));
            var fieldObject = Box("Point", new Vector2(124, 51), new Vector2(164, 34));
            var fieldBackground = fieldObject.AddComponent<Image>(); fieldBackground.color = new Color(.23f,.24f,.25f);
            input = fieldObject.AddComponent<InputField>(); input.targetGraphic = fieldBackground;
            input.characterLimit = 12;
            var text = AddText("", reference, Vector2.zero, new Vector2(154,34), fieldObject.transform);
            input.textComponent = text;
            placeholder = AddText("", reference, Vector2.zero, new Vector2(154,34), fieldObject.transform);
            placeholder.color = Color.gray; input.placeholder = placeholder;
            input.onValueChanged.AddListener(value => { if (!updating) Plugin.Instance.Options.Point.Value = value; });
            var buttonObject = Box("Natural", new Vector2(300,51), new Vector2(108,34));
            var buttonImage = buttonObject.AddComponent<Image>(); buttonImage.color = new Color(.35f,.30f,.19f);
            clear = buttonObject.AddComponent<Button>(); clear.targetGraphic = buttonImage;
            clearText = AddText("", reference, Vector2.zero, new Vector2(108,34), buttonObject.transform);
            clear.onClick.AddListener(() => { Plugin.Instance.Options.Point.Value = ""; input.text = ""; });
            var saveObject = Box("SaveDice", new Vector2(420, 51), new Vector2(108, 34));
            var saveImage = saveObject.AddComponent<Image>(); saveImage.color = new Color(.35f,.30f,.19f);
            save = saveObject.AddComponent<Button>(); save.targetGraphic = saveImage;
            saveText = AddText("", reference, Vector2.zero, new Vector2(108,34), saveObject.transform);
            save.onClick.AddListener(AnytimeDice.OpenSaveMenu);
            message = AddText("", reference, new Vector2(12,8), new Vector2(516,36));
            message.fontSize = 17;
        }

        internal void Refresh()
        {
            var language = Plugin.Instance.Options.LanguageKind;
            title.text = UiLanguage.Translate("基础点数", language);
            placeholder.text = UiLanguage.Translate("留空为随机", language);
            clearText.text = UiLanguage.Translate("自然掷骰", language);
            saveText.text = UiLanguage.Translate("存档", language);
            save.interactable = AnytimeNative.CanOffer;
            updating = true;
            if (!input.isFocused) input.text = Plugin.Instance.Options.Point.Value;
            updating = false;
            input.interactable = clear.interactable = DiceAdapter.Ready;
            bool valid = DiceAdapter.ValidInput();
            message.color = valid ? new Color(.85f,.85f,.8f) : new Color(1f,.55f,.4f);
            message.text = UiLanguage.Translate(!valid ? "超出当前点数范围，请修改或清空" : DiceAdapter.Ready ?
                "点击「启动」使用指定点数；留空则随机。" : "正在判定，请稍候。", language);
        }

        private GameObject Box(string name, Vector2 position, Vector2 size, Transform parent = null)
        {
            var item = new GameObject(name, typeof(RectTransform));
            item.transform.SetParent(parent ?? transform, false);
            var rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return item;
        }

        private Text AddText(string value, Text reference, Vector2 position, Vector2 size, Transform parent = null)
        {
            var label = Box("Label", position, size, parent).AddComponent<Text>();
            // Same rule as the option tooltip: our translated labels need the panel face when
            // the game's font has no Hangul/kana coverage.
            var panelFont = Plugin.Instance?.Panel == null ? null : Plugin.Instance.Panel.BodyFont;
            label.font = panelFont != null && Plugin.Instance.Options.LanguageKind != UiLang.Hans
                ? panelFont : reference.font;
            label.material = reference.material;
            label.fontSize = 20; label.text = value; label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            return label;
        }
    }
}
