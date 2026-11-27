using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Lament.VisualNovel
{
    public sealed class NovelNameInput
    {
        public GameObject Panel { get; }
        public TMP_InputField Field { get; }
        public Button Confirm { get; }
        private readonly TMP_Text prompt;

        public NovelNameInput(Transform parent, TMP_FontAsset font, Action submit)
        {
            var root = Rect(parent, "剧情文本输入", new Vector2(.2f, .34f), new Vector2(.8f, .67f));
            Panel = root.gameObject;
            var image = Panel.AddComponent<Image>(); image.color = new Color(.025f, .03f, .045f, 1); image.raycastTarget = true;
            prompt = Label(root, font, "提示", new Vector2(.05f, .7f), new Vector2(.95f, .95f));
            var inputRect = Rect(root, "输入框", new Vector2(.05f, .32f), new Vector2(.95f, .65f));
            var inputBg = inputRect.gameObject.AddComponent<Image>(); inputBg.color = new Color(.12f, .14f, .19f);
            Field = inputRect.gameObject.AddComponent<TMP_InputField>();
            var viewport = Rect(inputRect, "文字视口", new Vector2(.03f, .05f), new Vector2(.97f, .95f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var text = Label(viewport, font, "输入文字", Vector2.zero, Vector2.one);
            text.richText = false;
            Field.targetGraphic = inputBg; Field.textViewport = viewport; Field.textComponent = text;
            Field.lineType = TMP_InputField.LineType.SingleLine;
            var buttonRect = Rect(root, "确认", new Vector2(.35f, .06f), new Vector2(.65f, .25f));
            var buttonBg = buttonRect.gameObject.AddComponent<Image>(); buttonBg.color = new Color(.2f, .24f, .32f);
            Confirm = buttonRect.gameObject.AddComponent<Button>(); Confirm.targetGraphic = buttonBg;
            Label(buttonRect, font, "确认文字", Vector2.zero, Vector2.one).text = "确认";
            Confirm.onClick.AddListener(() => submit());
            // Only the explicit confirm button submits; IME's Return must not advance the story.
            Panel.SetActive(false);
        }

        public void Show(string text, string initial, int limit)
        {
            prompt.text = text;
            Field.characterLimit = 0; // The session validates Unicode text elements, not UTF-16 code units.
            Field.SetTextWithoutNotify(initial ?? "");
            Panel.SetActive(true);
            Field.ActivateInputField();
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
        }
        private static TMP_Text Label(Transform parent, TMP_FontAsset font, string name, Vector2 min, Vector2 max)
        {
            var text = Rect(parent, name, min, max).gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = 30; text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; return text;
        }
    }
}
