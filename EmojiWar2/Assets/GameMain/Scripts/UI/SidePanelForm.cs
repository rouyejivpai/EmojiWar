//------------------------------------------------------------
// EmojiWar GameMain - 窗体滑动基类（薄适配层，驱动 SlideMotion 引擎）
//
// 演进说明（框架化）：
//   - 实际的"滑入/滑出 + 淡入淡出 + 收起姿势计算"全部收敛到独立组件 SlideMotion
//     （Assets/GameMain/Scripts/UI/Framework/SlideMotion.cs）——任意 UI 面板都可直接挂载复用。
//   - 本类保留（字段/类型不变，确保既有预制体里保存的序列化配置不丢），只做转发：
//       配置 → 传给内部 SlideMotion；TogglePanel/ShowImmediate/IsVisible → 转发。
//   - 语义（与产品确认一致）：
//       FillCanvas=勾选 → 展开时把滑动主体铺满整块画布（显式几何覆盖）；
//       FillCanvas=不勾选 → **不改动滑动主体的任何预制体数值**（位置/尺寸保持作者设置），
//       仅由 SlideMotion 按方向做滑入/滑出；隐藏位 = 把面板沿方向完全推出画布（自动算）或手动偏移。
//------------------------------------------------------------

using DG.Tweening;
using UnityEngine;

namespace EmojiWar.GameMain.UI
{
    /// <summary>滑动方向枚举（保留原定义：序列化索引 Right=0/Left=1/Top=2/Bottom=3 不变）。</summary>
    public enum SlidePanelDirection
    {
        Right = 0,
        Left = 1,
        Top = 2,
        Bottom = 3,
    }

    /// <summary>
    /// 窗体滑动基类（SlideMotion 适配层）。展开/收起两态 + DOTween 动效均由 SlideMotion 提供。
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

        [Header("SidePanel 停靠/尺寸（编辑器配置）")]
        [Tooltip("决定面板从哪个方向滑入、朝哪个方向滑出隐藏")]
        [SerializeField]
        private SlidePanelDirection m_SlideDirection = SlidePanelDirection.Right;

        [Tooltip("勾选=展开时铺满整块画布并沿方向滑出；不勾选=完全遵循预制体面板摆放（位置/尺寸不改），仅方向决定滑入/滑出")]
        [SerializeField]
        private bool m_FillCanvas = true;

        [Tooltip("（可选）仅 Fill Canvas=未勾选 时用来覆盖面板尺寸：左右方向 x=宽度、上下方向 y=高度；填 0=不改，遵循预制体")]
        [SerializeField]
        private Vector2 m_PanelSize = Vector2.zero;

        [Tooltip("收起时再滑出屏幕的额外余量（像素）")]
        [SerializeField]
        private float m_HiddenMargin = 30f;

        private RectTransform m_PanelRect = null;
        private CanvasGroup m_CanvasGroup = null;
        private SlideMotion m_Motion = null;
        private bool m_IsVisible = false;

        /// <summary>面板是否处于展开（可见）状态。</summary>
        public bool IsVisible
        {
            get { return m_Motion != null ? m_Motion.IsOpen : m_IsVisible; }
        }

        /// <summary>滑动方向（子类诊断用，与 SlideMotion 一致）。</summary>
        protected SlidePanelDirection SlideDirectionSetting { get { return m_SlideDirection; } }

        /// <summary>是否占满画布。</summary>
        protected bool FillCanvasSetting { get { return m_FillCanvas; } }

        /// <summary>滑动目标：实际移动的面板主体（子类可指向子物体，如 panel_PanelSlide）。</summary>
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

        /// <summary>根 CanvasGroup（只读兼容属性；淡出/拦截交给 SlideMotion 的目标组）。</summary>
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

        /// <summary>raycast/淡出组（默认=滑动目标上的 CanvasGroup；子类可改）。</summary>
        protected virtual CanvasGroup RaycastBlocker
        {
            get { return PanelCanvasGroup; }
        }

        /// <summary>内部 SlideMotion（外部脚本可直接驱动/订阅）。</summary>
        public SlideMotion Motion { get { return m_Motion; } }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            // 1) Fill=勾选时按配置铺满（显式几何）；不勾选则不改几何（保留预制体数值）
            if (m_FillCanvas)
            {
                var target = SlideTarget;
                if (target != null)
                {
                    target.anchorMin = Vector2.zero;
                    target.anchorMax = Vector2.one;
                    target.offsetMin = Vector2.zero;
                    target.offsetMax = Vector2.zero;
                    target.pivot = new Vector2(0.5f, 0.5f);
                    target.anchoredPosition = Vector2.zero;
                }
            }
            else
            {
                var target = SlideTarget;
                if (target != null && m_PanelSize.x > 0f)
                {
                    target.sizeDelta = new Vector2(m_PanelSize.x, target.sizeDelta.y);
                }
                if (target != null && m_PanelSize.y > 0f)
                {
                    target.sizeDelta = new Vector2(target.sizeDelta.x, m_PanelSize.y);
                }
            }

            // 2) 创建/配置滑动引擎（引擎收敛所有姿势与动效逻辑）
            EnsureMotion();

            WriteProbe("[sidepanel] motion engine ready, isVisible=" + (m_Motion != null ? m_Motion.IsOpen.ToString() : "null"));
        }

        private void EnsureMotion()
        {
            if (m_Motion != null)
            {
                return;
            }
            m_Motion = GetComponent<SlideMotion>();
            if (m_Motion == null)
            {
                m_Motion = gameObject.AddComponent<SlideMotion>();
            }

            m_Motion.Configure(
                MapDirection(m_SlideDirection),
                SlideTarget,
                RaycastBlocker,   // 淡出组
                RaycastBlocker,   // raycast 门
                Mathf.Max(0.01f, m_AnimDuration),
                m_AnimEase,
                m_HiddenMargin,
                m_StartHidden);

            m_IsVisible = m_Motion.IsOpen;
        }

        private static SlideMotionDirection MapDirection(SlidePanelDirection dir)
        {
            switch (dir)
            {
                case SlidePanelDirection.Left: return SlideMotionDirection.Left;
                case SlidePanelDirection.Top: return SlideMotionDirection.Top;
                case SlidePanelDirection.Bottom: return SlideMotionDirection.Bottom;
                case SlidePanelDirection.Right:
                default: return SlideMotionDirection.Right;
            }
        }

        /// <summary>切换展开/收起（带动效）。</summary>
        public void TogglePanel(bool show, bool animate = true)
        {
            EnsureMotion();
            if (m_Motion == null)
            {
                return;
            }
            m_Motion.SetOpen(show, animate);
            m_IsVisible = m_Motion.IsOpen;
        }

        /// <summary>强制展示（不带动画，供打开窗体时立即定位）。</summary>
        public void ShowImmediate()
        {
            EnsureMotion();
            if (m_Motion == null)
            {
                return;
            }
            m_Motion.SnapOpen();
            m_IsVisible = true;
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (m_Motion != null)
            {
                m_Motion.SnapClose();
                m_IsVisible = false;
            }
            base.OnClose(isShutdown, userData);
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }
    }
}
