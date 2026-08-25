//------------------------------------------------------------
// EmojiWar GameMain - UI 组件工厂（Editor）
// 统一创建 UI 元素（按钮/文本/面板/列表项），样式来自 UIStyle。
// 所有 Form 生成器复用本工厂，保证界面一致并消除重复代码。
//------------------------------------------------------------

using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.Editor
{
    /// <summary>
    /// UI 组件工厂：统一创建带样式的 UGUI 元素。
    /// </summary>
    public static class UIComponentFactory
    {
        /// <summary>创建带 Canvas 的 Form 根对象。</summary>
        public static GameObject CreateFormRoot(string name, System.Type formType)
        {
            var root = new GameObject(name);
            root.layer = LayerMask.NameToLayer("UI");

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            root.AddComponent<GraphicRaycaster>();
            root.AddComponent<CanvasGroup>();
            root.AddComponent(formType);
            return root;
        }

        /// <summary>创建全屏半透明背景（弹窗用）。</summary>
        public static void CreateBackdrop(Transform parent, Color color)
        {
            var bg = new GameObject("Backdrop");
            bg.transform.SetParent(parent, false);
            var image = bg.AddComponent<Image>();
            image.color = color;
            var rect = bg.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>创建文本。</summary>
        public static GameObject CreateText(string name, Transform parent, string content, int fontSize,
            Vector2 anchoredPos, Vector2 size, TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            var text = go.AddComponent<Text>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color ?? UI.UIStyle.TextColor;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return go;
        }

        /// <summary>创建按钮。</summary>
        public static GameObject CreateButton(string name, Transform parent, string label, Vector2 anchoredPos,
            Vector2? size = null, Color? color = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size ?? UI.UIStyle.ButtonSize;

            var image = go.AddComponent<Image>();
            image.color = color ?? UI.UIStyle.PrimaryColor;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var labelGo = CreateText("Label", go.transform, label, UI.UIStyle.FontButton,
                Vector2.zero, new Vector2(rect.sizeDelta.x - 20, rect.sizeDelta.y - 20));
            return go;
        }

        /// <summary>创建列表项（卡片行：文本 + 可选状态色）。</summary>
        public static GameObject CreateListItem(string name, Transform parent, string content, Color stateColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = UI.UIStyle.ListItemSize;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.1f, 0.12f, 0.16f, 0.9f);

            var text = CreateText("Text", go.transform, content, UI.UIStyle.FontBody,
                Vector2.zero, new Vector2(rect.sizeDelta.x - 30, rect.sizeDelta.y - 10),
                TextAnchor.MiddleLeft, stateColor);
            return go;
        }
    }
}
