using UnityEngine;
using UnityEngine.UI;

namespace MortalHelper.Game
{
    public sealed class DiceWidget : MonoBehaviour
    {
        private InputField input;
        private Text message;
        private Text title, placeholder, clearText;
        private Button clear;
        private bool updating;

        internal void Initialize(Text reference)
        {
            var root = (RectTransform)transform;
            root.anchorMin = root.anchorMax = new Vector2(.5f, 0f);
            root.pivot = new Vector2(.5f, 0f);
            root.anchoredPosition = new Vector2(0, 24);
            root.sizeDelta = new Vector2(420, 96);
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
            message = AddText("", reference, new Vector2(12,8), new Vector2(396,36));
            message.fontSize = 17;
        }

        internal void Refresh()
        {
            bool traditional = Plugin.Instance.Options.TraditionalChinese.Value;
            title.text = UiLanguage.Convert("基础点数", traditional);
            placeholder.text = UiLanguage.Convert("留空为随机", traditional);
            clearText.text = UiLanguage.Convert("自然掷骰", traditional);
            updating = true;
            if (!input.isFocused) input.text = Plugin.Instance.Options.Point.Value;
            updating = false;
            input.interactable = clear.interactable = DiceAdapter.Ready;
            bool valid = DiceAdapter.ValidInput();
            message.color = valid ? new Color(.85f,.85f,.8f) : new Color(1f,.55f,.4f);
            message.text = UiLanguage.Convert(!valid ? "超出当前点数范围，请修改或清空" : DiceAdapter.Ready ?
                "点原生启动使用该点数；清空则自然掷骰" : "正在判定，请等待原生操作恢复", traditional);
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
            label.font = reference.font; label.material = reference.material;
            label.fontSize = 20; label.text = value; label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            return label;
        }
    }
}
