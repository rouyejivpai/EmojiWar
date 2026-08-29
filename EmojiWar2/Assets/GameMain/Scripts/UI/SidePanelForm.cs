//------------------------------------------------------------
// EmojiWar GameMain - 通用侧边抽屉 UI 基类（DOTween 动效）
// 挂在侧边栏根物体上：提供 展开/收起 两种状态，带滑入/滑出 + 透明度动效。
// 用法：子类重写 GetPanelRect / GetHiddenPos，调用 TogglePanel(show, animate)。
// 依赖：DOTween（Packages/com.gameframex.unity.demigiant.dotween）
//------------------------------------------------------------

using DG.Tweening;
using UnityEngine;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 通用侧边抽屉：展开/收起两态，DOTween 滑动 + 淡入淡出动效。
    /// </summary>
    public abstract class SidePanelForm : UGuiForm
    {
        [Header("SidePanel 动效")]
        [SerializeField]
        private float m_AnimDuration = 0.35f;

        [SerializeField]
        private Ease m_AnimEase = Ease.OutCubic;

        [SerializeField]
        private bool m_StartHidden = true;

        private RectTransform m_PanelRect = null;
        private CanvasGroup m_CanvasGroup = null;
        private Tween m_AnimTween = null;
        private bool m_IsVisible = false;

        /// <summary>面板是否处于展开（可见）状态。</summary>
        public bool IsVisible { get { return m_IsVisible; } }

        /// <summary>面板 RectTransform（根物体）。</summary>
        protected RectTransform PanelRect
        {
            get
            {
                if (m_PanelRect == null)
                {
                    m_PanelRect = transform as RectTransform;
                }
                return m_PanelRect;
            }
        }

        /// <summary>展开时的目标位置（子类按停靠边实现）。</summary>
        protected abstract Vector2 GetVisiblePos();

        /// <summary>收起时的隐藏位置（滑出屏幕外）。</summary>
        protected abstract Vector2 GetHiddenPos();

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
            m_CanvasGroup = GetComponent<CanvasGroup>();
            if (m_CanvasGroup == null)
            {
                m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            if (m_StartHidden)
            {
                // 初始隐藏：直接置于屏外（无动效）
                m_IsVisible = false;
                if (m_CanvasGroup != null)
                {
                    m_CanvasGroup.alpha = 0f;
                    m_CanvasGroup.blocksRaycasts = false;
                }
                PanelRect.anchoredPosition = GetHiddenPos();
            }
            else
            {
                m_IsVisible = true;
                PanelRect.anchoredPosition = GetVisiblePos();
            }
        }

        /// <summary>切换展开/收起（带动效）。</summary>
        public void TogglePanel(bool show, bool animate = true)
        {
            if (show == m_IsVisible)
            {
                return;
            }
            m_IsVisible = show;

            if (m_AnimTween != null)
            {
                m_AnimTween.Kill();
                m_AnimTween = null;
            }

            if (!animate)
            {
                PanelRect.anchoredPosition = show ? GetVisiblePos() : GetHiddenPos();
                if (m_CanvasGroup != null)
                {
                    m_CanvasGroup.alpha = show ? 1f : 0f;
                    m_CanvasGroup.blocksRaycasts = show;
                }
                return;
            }

            // DOTween：位置滑动 + 透明度淡入淡出
            Vector2 targetPos = show ? GetVisiblePos() : GetHiddenPos();
            float targetAlpha = show ? 1f : 0f;
            m_CanvasGroup.blocksRaycasts = false;   // 动画期间不可交互

            m_AnimTween = DOTween.Sequence()
                .Join(PanelRect.DOAnchorPos(targetPos, m_AnimDuration).SetEase(m_AnimEase))
                .Join(m_CanvasGroup.DOFade(targetAlpha, m_AnimDuration))
                .OnComplete(() =>
                {
                    if (m_CanvasGroup != null)
                    {
                        m_CanvasGroup.blocksRaycasts = m_IsVisible;
                    }
                    m_AnimTween = null;
                });
        }

        /// <summary>强制展示（不带动画，供打开窗体时立即定位）。</summary>
        public void ShowImmediate()
        {
            if (m_AnimTween != null)
            {
                m_AnimTween.Kill();
                m_AnimTween = null;
            }
            m_IsVisible = true;
            PanelRect.anchoredPosition = GetVisiblePos();
            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.alpha = 1f;
                m_CanvasGroup.blocksRaycasts = true;
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (m_AnimTween != null)
            {
                m_AnimTween.Kill();
                m_AnimTween = null;
            }
            base.OnClose(isShutdown, userData);
        }
    }
}
