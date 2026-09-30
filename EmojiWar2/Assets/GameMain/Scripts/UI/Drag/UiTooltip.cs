//------------------------------------------------------------
// EmojiWar GameMain - 悬停信息与提示（P3）
//
// 方案：doc/物品与法杖系统设计.md §5（旧版 HoverInfoPanel 的改进版：延迟/跟随/越界翻转都真正生效）
//   全部运行时创建，不依赖任何预制体；独立顶层 Canvas，不参与布局、不挡射线。
//------------------------------------------------------------

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>物品悬停信息 + 轻量 Toast（拒绝原因等）。</summary>
    public static class UiTooltip
    {
        private const string CanvasName = "UiTooltipCanvas";
        private const int SortingOrder = 29999;
        private const float HoverDelay = 0.25f;
        private const float ToastSeconds = 1.2f;

        private static Canvas s_Canvas = null;
        private static RectTransform s_Panel = null;
        private static Text s_Text = null;
        private static UiTooltipRunner s_Runner = null;
        private static float s_ShowAt = 0f;
        private static Vector2 s_LastPos = Vector2.zero;

        /// <summary>悬停显示（带 0.25s 延迟，跟随指针，靠右越界自动翻转）。</summary>
        public static void Show(string title, string body, Vector2 screenPos)
        {
            Ensure();
            if (s_Panel == null) { return; }
            s_LastPos = screenPos;
            if (!s_Panel.gameObject.activeSelf)
            {
                s_ShowAt = Time.unscaledTime + HoverDelay;
            }
            s_Text.text = string.IsNullOrEmpty(body) ? title : (title + "\n" + body);
        }

        public static void Hide()
        {
            if (s_Panel != null) { s_Panel.gameObject.SetActive(false); }
            s_ShowAt = 0f;
        }

        /// <summary>短提示（放不下/类型不匹配等拒绝原因的反馈）。</summary>
        public static void ShowToast(string message)
        {
            if (string.IsNullOrEmpty(message)) { return; }
            Ensure();
            if (s_Runner == null) { return; }
            s_Runner.ShowToast(message);
        }

        /// <summary>每帧推进：延迟到点后再显示，并把面板贴到指针附近（越界翻转）。</summary>
        internal static void Tick()
        {
            if (s_Panel == null || !s_Panel.gameObject.activeSelf) { return; }
            if (s_ShowAt > 0f && Time.unscaledTime < s_ShowAt) { return; }
            if (s_ShowAt > 0f) { s_ShowAt = 0f; }

            UpdateLayout(s_LastPos);
        }

        internal static void EnsureCanvas()
        {
            Ensure();
        }

        private static void UpdateLayout(Vector2 screenPos)
        {
            var canvasRect = s_Canvas.transform as RectTransform;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, null, out local)) { return; }

            s_Panel.sizeDelta = new Vector2(360f, s_Text.preferredHeight + 24f);
            Vector2 size = s_Panel.sizeDelta;

            float x = local.x + 18f;
            float y = local.y - 18f;
            float halfW = canvasRect.rect.width * 0.5f;
            float halfH = canvasRect.rect.height * 0.5f;

            if (x + size.x > halfW) { x = local.x - 18f - size.x; }          // 靠右越界 → 翻到左侧
            if (y - size.y < -halfH) { y = local.y + 18f + size.y; }         // 靠下越界 → 翻到上方
            s_Panel.anchoredPosition = new Vector2(x, y);
        }

        private static void Ensure()
        {
            if (s_Canvas != null) { return; }

            var canvasGo = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasGroup));
            Object.DontDestroyOnLoad(canvasGo);
            s_Canvas = canvasGo.GetComponent<Canvas>();
            s_Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            s_Canvas.overrideSorting = true;
            s_Canvas.sortingOrder = SortingOrder;

            var group = canvasGo.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var panelGo = new GameObject("Tooltip", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            s_Panel = panelGo.GetComponent<RectTransform>();
            s_Panel.anchorMin = new Vector2(0.5f, 0.5f);
            s_Panel.anchorMax = new Vector2(0.5f, 0.5f);
            s_Panel.pivot = new Vector2(0f, 1f);
            s_Panel.sizeDelta = new Vector2(360f, 80f);
            var bg = panelGo.GetComponent<Image>();
            bg.color = new Color(0.05f, 0.07f, 0.1f, 0.94f);
            bg.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(panelGo.transform, false);
            var tr = textGo.GetComponent<RectTransform>();
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(12f, 10f);
            tr.offsetMax = new Vector2(-12f, -10f);
            s_Text = textGo.GetComponent<Text>();
            s_Text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            s_Text.fontSize = 20;
            s_Text.color = Color.white;
            s_Text.alignment = TextAnchor.UpperLeft;
            s_Text.horizontalOverflow = HorizontalWrapMode.Wrap;
            s_Text.verticalOverflow = VerticalWrapMode.Overflow;
            s_Text.raycastTarget = false;

            s_Runner = canvasGo.AddComponent<UiTooltipRunner>();
            panelGo.SetActive(false);
        }
    }

    /// <summary>驱动 Tooltip 的逐帧更新与 Toast 计时（挂在 Tooltip 画布上）。</summary>
    public sealed class UiTooltipRunner : MonoBehaviour
    {
        private Text m_Toast = null;
        private float m_ToastUntil = 0f;
        private Coroutine m_HideToast = null;

        private void Update()
        {
            UiTooltip.Tick();
            if (m_Toast != null && m_ToastUntil > 0f && Time.unscaledTime > m_ToastUntil)
            {
                m_Toast.text = string.Empty;
                m_ToastUntil = 0f;
            }
        }

        /// <summary>在屏幕中下方显示一条短提示。</summary>
        public void ShowToast(string message)
        {
            if (m_Toast == null)
            {
                var canvas = GetComponent<Canvas>();
                var go = new GameObject("Toast", typeof(RectTransform), typeof(Text));
                go.transform.SetParent(canvas.transform, false);
                var rect = go.GetComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 180f);
                rect.sizeDelta = new Vector2(720f, 44f);

                m_Toast = go.GetComponent<Text>();
                m_Toast.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                m_Toast.fontSize = 24;
                m_Toast.color = new Color(1f, 0.85f, 0.4f, 1f);
                m_Toast.alignment = TextAnchor.MiddleCenter;
                m_Toast.raycastTarget = false;
            }
            m_Toast.text = message;
            m_ToastUntil = Time.unscaledTime + 1.2f;
        }
    }
}
