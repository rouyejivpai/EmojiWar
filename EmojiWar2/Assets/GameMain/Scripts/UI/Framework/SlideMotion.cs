//------------------------------------------------------------
// EmojiWar GameMain UI Framework - SlideMotion（可复用"滑入/滑出"组件）
//
// 定位：
//   - 独立的纯表现组件：挂在任意带 RectTransform 的物体上即可使用
//     （不必是 GameFramework UIForm；普通 Canvas 子物体也行）。
//   - 布局与动效分离：默认**不改动作者在预制体里摆好的数值**——
//     展开姿势 = 实例初始姿势（预制体值）；收起姿势 = 沿所选方向完全离屏（自动算）或手动偏移。
//   - 公开 API：Open/Close/Toggle/SnapOpen/SnapClose + 事件 OnOpened/OnClosed。
//
// 供 SidePanelForm（窗体滑动基类）内部驱动，也供其它 UI/面板直接挂载复用。
// 依赖 DOTween（项目内已有）。
//------------------------------------------------------------

using System;
using DG.Tweening;
using UnityEngine;

namespace EmojiWar.GameMain.UI
{
    /// <summary>滑出方向：面板朝哪个方向滑出隐藏 / 从哪个方向滑入。</summary>
    public enum SlideMotionDirection
    {
        Right = 0,   // 向右（右缘）
        Left = 1,    // 向左（左缘）
        Top = 2,     // 向上（顶缘）
        Bottom = 3,  // 向下（底缘）
    }

    /// <summary>可复用"滑入/滑出"动效组件。</summary>
    public sealed class SlideMotion : MonoBehaviour
    {
        // ---------- Inspector 配置（独立使用时直接配置；被窗体内部驱动时可用 Configure 覆盖） ----------
        [Header("SlideMotion 方向/姿势")]
        [Tooltip("面板朝哪个方向滑出隐藏 / 从哪个方向滑入")]
        [SerializeField] private SlideMotionDirection m_Direction = SlideMotionDirection.Right;

        [Tooltip("收起方式：OffScreen=沿方向自动算到完全离屏；Manual=用下方手动偏移")]
        [SerializeField] private bool m_UseOffscreenOffset = true;

        [Tooltip("Manual 模式下的手动收起偏移（anchoredPosition 增量）")]
        [SerializeField] private Vector2 m_ManualClosedOffset = new Vector2(-2000f, 0f);

        [Tooltip("OffScreen 模式额外离屏余量（像素/参考单位）")]
        [SerializeField] private float m_OffscreenMargin = 30f;

        [Tooltip("初始是否处于收起（屏外）状态")]
        [SerializeField] private bool m_StartHidden = false;

        [Header("动效")]
        [Tooltip("过渡时长（秒）")]
        [SerializeField] private float m_Duration = 0.35f;

        [SerializeField] private Ease m_Ease = Ease.OutCubic;

        [Header("目标与门控")]
        [Tooltip("实际移动的面板主体（留空=本物体自身）")]
        [SerializeField] private RectTransform m_Target = null;

        [Tooltip("可选：淡入淡出的 CanvasGroup（留空=不淡出；需要淡出时给 Target 挂 CanvasGroup）")]
        [SerializeField] private CanvasGroup m_FadeGroup = null;

        [Tooltip("可选：展开拦截点击/收起释放的 CanvasGroup（留空=用 FadeGroup；再没有则不拦截）")]
        [SerializeField] private CanvasGroup m_RaycastGate = null;

        private RectTransform m_ResolvedTarget = null;
        private CanvasGroup m_ResolvedFadeGroup = null;
        private CanvasGroup m_ResolvedGate = null;

        private Vector2 m_OpenPose;          // 展开姿势（默认 = 实例初始姿势）
        private float m_OpenAlpha = 1f;
        private bool m_HasPose = false;

        private Tween m_Tween = null;
        private bool m_IsOpen = false;

        public event Action OnOpened;
        public event Action OnClosed;

        public bool IsOpen { get { return m_IsOpen; } }
        public bool IsAnimating { get { return m_Tween != null && m_Tween.IsActive(); } }

        private void Awake()
        {
            ResolveRefs();
            CaptureOpenPose();
            if (m_StartHidden)
            {
                m_IsOpen = false;
                ApplyClosedPoseImmediate();
                SyncRaycast(false);
                if (m_ResolvedFadeGroup != null) { m_OpenAlpha = m_ResolvedFadeGroup.alpha; m_ResolvedFadeGroup.alpha = 0f; }
            }
            else
            {
                m_IsOpen = true;
                SyncRaycast(true);
            }
        }

        /// <summary>程序化配置（供窗体基类驱动；优先级高于 Inspector 字段）。调用应在首次 Open/Close 之前。</summary>
        public void Configure(SlideMotionDirection direction, RectTransform target,
            CanvasGroup fadeGroup, CanvasGroup raycastGate,
            float duration, Ease ease, float offscreenMargin, bool startHidden)
        {
            KillTween();

            // 关键：重置姿势缓存/状态。本组件常由 AddComponent 动态添加——
            // Awake 会先以默认字段捕获一次（目标/展开姿势都可能是错的），
            // 必须在正式配置后再按真实目标重新捕获，否则展开姿势错乱。
            m_HasPose = false;
            m_IsOpen = false;

            m_Direction = direction;
            m_Duration = duration > 0f ? duration : 0.35f;
            m_Ease = ease;
            m_OffscreenMargin = offscreenMargin;
            m_StartHidden = startHidden;
            if (target != null) { m_Target = target; }
            if (fadeGroup != null) { m_FadeGroup = fadeGroup; }
            if (raycastGate != null) { m_RaycastGate = raycastGate; }
            ResolveRefs();
            CaptureOpenPose();
            if (m_StartHidden)
            {
                m_IsOpen = false;
                ApplyClosedPoseImmediate();
                SyncRaycast(false);
            }
            else
            {
                m_IsOpen = true;
                SyncRaycast(true);
            }
        }

        private void ResolveRefs()
        {
            m_ResolvedTarget = m_Target != null ? m_Target : (transform as RectTransform);
            m_ResolvedFadeGroup = m_FadeGroup != null ? m_FadeGroup : (m_ResolvedTarget != null ? m_ResolvedTarget.GetComponent<CanvasGroup>() : null);
            m_ResolvedGate = m_RaycastGate != null ? m_RaycastGate : m_ResolvedFadeGroup;
        }

        /// <summary>展开姿势快照：优先用实例初始（=预制体作者值），仅首次有效。</summary>
        private void CaptureOpenPose()
        {
            if (m_HasPose || m_ResolvedTarget == null) { return; }
            m_HasPose = true;
            m_OpenPose = m_ResolvedTarget.anchoredPosition;
            if (m_ResolvedFadeGroup != null) { m_OpenAlpha = m_ResolvedFadeGroup.alpha; }
        }

        /// <summary>打开/收起。</summary>
        public void SetOpen(bool open, bool animate = true)
        {
            if (open == m_IsOpen) { return; }

            KillTween();

            if (!animate)
            {
                if (open) { ApplyOpenPoseImmediate(); }
                else { ApplyClosedPoseImmediate(); }
                m_IsOpen = open;
                SyncRaycast(open);
                RaiseEvent(open);
                return;
            }

            Vector2 targetPos = open ? m_OpenPose : ComputeClosedPose();
            float targetAlpha = open ? m_OpenAlpha : 0f;
            if (m_ResolvedFadeGroup != null) { m_ResolvedGate = m_ResolvedGate ?? m_ResolvedFadeGroup; if (m_ResolvedGate != null) { m_ResolvedGate.blocksRaycasts = false; } }

            m_Tween = DOTween.Sequence()
                .Join(m_ResolvedTarget.DOAnchorPos(targetPos, m_Duration).SetEase(m_Ease))
                .Join(m_ResolvedFadeGroup != null
                    ? m_ResolvedFadeGroup.DOFade(targetAlpha, m_Duration)
                    : DOTween.Sequence())
                .OnComplete(() =>
                {
                    m_Tween = null;
                    m_IsOpen = open;
                    SyncRaycast(open);
                    RaiseEvent(open);
                });
        }

        public void Toggle(bool animate = true) { SetOpen(!m_IsOpen, animate); }

        public void SnapOpen()
        {
            KillTween();
            ApplyOpenPoseImmediate();
            m_IsOpen = true;
            SyncRaycast(true);
            RaiseEvent(true);
        }

        public void SnapClose()
        {
            KillTween();
            ApplyClosedPoseImmediate();
            m_IsOpen = false;
            SyncRaycast(false);
            RaiseEvent(false);
        }

        private void ApplyOpenPoseImmediate()
        {
            if (m_ResolvedTarget == null) { return; }
            m_ResolvedTarget.anchoredPosition = m_OpenPose;
            if (m_ResolvedFadeGroup != null) { m_ResolvedFadeGroup.alpha = m_OpenAlpha; }
        }

        private void ApplyClosedPoseImmediate()
        {
            if (m_ResolvedTarget == null) { return; }
            m_ResolvedTarget.anchoredPosition = ComputeClosedPose();
            if (m_ResolvedFadeGroup != null) { m_ResolvedFadeGroup.alpha = 0f; }
        }

        /// <summary>收起姿势：OffScreen=相对展开姿势沿方向移出画面；Manual=展开姿势+手动偏移。</summary>
        private Vector2 ComputeClosedPose()
        {
            if (m_ResolvedTarget == null) { return Vector2.zero; }

            if (!m_UseOffscreenOffset)
            {
                return m_OpenPose + m_ManualClosedOffset;
            }

            var shift = ComputeOffscreenShift(m_ResolvedTarget, m_Direction, m_OffscreenMargin);
            return m_OpenPose + shift;
        }

        private void SyncRaycast(bool open)
        {
            if (m_ResolvedGate != null)
            {
                m_ResolvedGate.blocksRaycasts = open;
            }
        }

        private void RaiseEvent(bool open)
        {
            try
            {
                if (open) { if (OnOpened != null) { OnOpened(); } }
                else { if (OnClosed != null) { OnClosed(); } }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SlideMotion] event error: " + e.Message);
            }
        }

        private void KillTween()
        {
            if (m_Tween != null)
            {
                m_Tween.Kill();
                m_Tween = null;
            }
        }

        /// <summary>计算把 target 沿 direction 完全推出父画布所需的 anchored 平移量（相对父 pivot 原点换算）。</summary>
        public static Vector2 ComputeOffscreenShift(RectTransform target, SlideMotionDirection direction, float margin)
        {
            var parent = target.parent as RectTransform;
            if (parent == null) { return Vector2.zero; }

            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var p = parent.InverseTransformPoint(corners[i]);
                if (p.x < minX) { minX = p.x; }
                if (p.x > maxX) { maxX = p.x; }
                if (p.y < minY) { minY = p.y; }
                if (p.y > maxY) { maxY = p.y; }
            }

            float pw = parent.rect.width;
            float ph = parent.rect.height;
            float left = -parent.pivot.x * pw;
            float right = (1f - parent.pivot.x) * pw;
            float bottom = -parent.pivot.y * ph;
            float top = (1f - parent.pivot.y) * ph;

            switch (direction)
            {
                case SlideMotionDirection.Left:
                    return new Vector2((left - margin) - maxX, 0f);
                case SlideMotionDirection.Right:
                    return new Vector2((right + margin) - minX, 0f);
                case SlideMotionDirection.Top:
                    return new Vector2(0f, (top + margin) - minY);
                case SlideMotionDirection.Bottom:
                default:
                    return new Vector2(0f, (bottom - margin) - maxY);
            }
        }

        private void OnDestroy()
        {
            KillTween();
        }
    }
}
