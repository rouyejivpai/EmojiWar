//------------------------------------------------------------
// EmojiWar GameMain - 通用侧边抽屉 UI 基类（DOTween 动效）
// 根物体 = 全屏 Canvas（raycast 区域），SlideTarget = 实际滑动的面板主体。
// 提供 展开/收起 两种状态，带滑入/滑出 + 透明度动效。
// 子类重写 GetVisiblePos/GetHiddenPos 决定停靠方向与滑出量。
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

        /// <summary>
        /// 滑动目标：实际移动的面板主体（子类可改为子物体，根 Canvas 保持全屏 raycast）。
        /// </summary>
        protected virtual RectTransform SlideTarget
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

        /// <summary>根 CanvasGroup（控制整体透明度与 raycast 开关）。</summary>
        protected CanvasGroup PanelCanvasGroup
        {
            get
            {
                if (m_CanvasGroup == null)
                {
                    m_CanvasGroup = GetComponent<CanvasGroup>();
                    if (m_CanvasGroup == null)
                    {
                        m_CanvasGroup = gameObject.AddComponent<CanvasGroup>();
                    }
                }
                return m_CanvasGroup;
            }
        }

        /// <summary>展开时的目标位置（子类按停靠边实现）。</summary>
        protected abstract Vector2 GetVisiblePos();

        /// <summary>收起时的隐藏位置（滑出屏幕外）。</summary>
        protected abstract Vector2 GetHiddenPos();

        /// <summary>
        /// raycast 控制器：展开时拦截点击（面板可交互），收起时释放（不挡下层 UI）。
        /// 默认根 CanvasGroup；若根需常驻可点元素（如收起 Tab），子类可改为 SlideTarget 上的 CanvasGroup。
        /// </summary>
        protected virtual CanvasGroup RaycastBlocker
        {
            get { return PanelCanvasGroup; }
        }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            if (m_StartHidden)
            {
                // 初始隐藏：直接置于屏外（无动效）
                m_IsVisible = false;
                PanelCanvasGroup.alpha = 0f;
                RaycastBlocker.blocksRaycasts = false;
                SlideTarget.anchoredPosition = GetHiddenPos();
            }
            else
            {
                m_IsVisible = true;
                SlideTarget.anchoredPosition = GetVisiblePos();
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
                SlideTarget.anchoredPosition = show ? GetVisiblePos() : GetHiddenPos();
                PanelCanvasGroup.alpha = 1f;                      // 根保持可见（收起后 Tab 等常驻元素仍显示）
                RaycastBlocker.blocksRaycasts = show;
                return;
            }

            // DOTween：位置滑动 + 滑动主体透明度淡入淡出
            // 注意：淡出目标是 RaycastBlocker（SlideTarget 上的 CanvasGroup），不是根——
            // 根 alpha 归 0 会把整个 UIForm 淡掉（含常驻 Tab），且全屏根的位置动画无效。
            Vector2 targetPos = show ? GetVisiblePos() : GetHiddenPos();
            float targetAlpha = show ? 1f : 0f;
            RaycastBlocker.blocksRaycasts = false;   // 动画期间不可交互

            m_AnimTween = DOTween.Sequence()
                .Join(SlideTarget.DOAnchorPos(targetPos, m_AnimDuration).SetEase(m_AnimEase))
                .Join(RaycastBlocker.DOFade(targetAlpha, m_AnimDuration))
                .OnComplete(() =>
                {
                    RaycastBlocker.blocksRaycasts = m_IsVisible;
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
            SlideTarget.anchoredPosition = GetVisiblePos();
            PanelCanvasGroup.alpha = 1f;
            RaycastBlocker.blocksRaycasts = true;
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
