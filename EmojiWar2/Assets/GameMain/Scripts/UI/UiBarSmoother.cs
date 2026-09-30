//------------------------------------------------------------
// EmojiWar GameMain - UI 进度条平滑插值（lerp）
//
// 统一规范：**所有进度条（血条/能量条/冷却条/后续任何 Fill 条）都走本组件**，
// 宿主只负责"给目标比例"，绝不再直接写 sizeDelta 跳变：
//     var bar = UiBarSmoother.Attach(m_HpFill, lerpSpeed: 8f, gap: 4f);
//     bar.SetTarget(ratio);                 // 常规：lerp 平滑逼近
//     bar.SetTarget(ratio, immediate: true);// 首帧/复位：直接就位，避免从初值扫过去
//
// 几何约定（沿用现有预制体）：填充条锚左，宽 = 底条宽 - gap，按比例缩放宽度；
// 平滑：帧率无关指数插值 current = Lerp(current, target, 1 - exp(-speed * dt))，
//       仅在像素变化 ≥ 0.1 时写 sizeDelta，避免每帧触发 Canvas 重建。
// 探针：CurrentRatio / TargetRatio / IsSettled 供自动化断言。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>宽度型进度条的 lerp 平滑插值组件（运行时 Attach 到填充 Image 上）。</summary>
    public sealed class UiBarSmoother : MonoBehaviour
    {
        private const float MinWidthDelta = 0.1f;   // 小于此像素变化不写布局（防 Canvas 重建）

        [SerializeField] private float m_LerpSpeed = 8f;   // 每秒收敛速度（越大越快）
        [SerializeField] private float m_Gap = 4f;         // 相对底条宽度的内边距

        private Image m_Fill = null;
        private RectTransform m_FillRect = null;
        private RectTransform m_BgRect = null;

        private float m_Current = 1f;    // 当前显示比例（0..1）
        private float m_Target = 1f;     // 目标比例（0..1）
        private float m_AppliedWidth = -1f;
        private bool m_HasTarget = false;

        public float LerpSpeed { get { return m_LerpSpeed; } }
        public float CurrentRatio { get { return m_Current; } }
        public float TargetRatio { get { return m_Target; } }
        public bool HasTarget { get { return m_HasTarget; } }
        public bool IsSettled { get { return Mathf.Abs(m_Target - m_Current) <= 0.001f; } }

        /// <summary>在填充 Image 上取/加平滑组件（同一 Image 只加一个，重复调用返回既有实例）。</summary>
        public static UiBarSmoother Attach(Image fill, float lerpSpeed = 8f, float gap = 4f)
        {
            if (fill == null)
            {
                return null;
            }
            var bar = fill.GetComponent<UiBarSmoother>();
            if (bar == null)
            {
                bar = fill.gameObject.AddComponent<UiBarSmoother>();
            }
            bar.Configure(fill, lerpSpeed, gap);
            return bar;
        }

        /// <summary>绑定填充条与参数（幂等；保留当前显示值，不重置动画）。</summary>
        public void Configure(Image fill, float lerpSpeed = 8f, float gap = 4f)
        {
            m_Fill = fill;
            m_FillRect = fill != null ? fill.rectTransform : null;
            m_BgRect = m_FillRect != null ? m_FillRect.parent as RectTransform : null;
            m_LerpSpeed = Mathf.Max(0.1f, lerpSpeed);
            m_Gap = gap;
        }

        /// <summary>设置目标比例；immediate=true 直接就位（首帧/复位用）。</summary>
        public void SetTarget(float ratio, bool immediate = false)
        {
            m_Target = Mathf.Clamp01(ratio);
            if (!m_HasTarget || immediate)
            {
                m_HasTarget = true;
                m_Current = m_Target;
                Apply();
            }
        }

        /// <summary>立即拉到目标（不做动画），例如关窗/重开时复位。</summary>
        public void SnapToTarget()
        {
            m_Current = m_Target;
            Apply();
        }

        private void Update()
        {
            if (!m_HasTarget || m_FillRect == null)
            {
                return;
            }
            if (IsSettled)
            {
                return;
            }

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                return;
            }

            // 帧率无关的 lerp：speed 越大越快收敛
            float k = 1f - Mathf.Exp(-m_LerpSpeed * dt);
            m_Current = Mathf.Lerp(m_Current, m_Target, k);
            if (Mathf.Abs(m_Target - m_Current) <= 0.001f)
            {
                m_Current = m_Target;
            }
            Apply();
        }

        private void Apply()
        {
            if (m_Fill == null || m_FillRect == null)
            {
                return;
            }
            float maxWidth = m_BgRect != null ? m_BgRect.sizeDelta.x - m_Gap : 0f;
            if (maxWidth <= 0f)
            {
                return;
            }

            float width = maxWidth * Mathf.Clamp01(m_Current);
            if (m_AppliedWidth >= 0f && Mathf.Abs(width - m_AppliedWidth) < MinWidthDelta)
            {
                return;   // 变化过小：不写布局
            }
            m_AppliedWidth = width;

            var size = m_FillRect.sizeDelta;
            size.x = width;
            m_FillRect.sizeDelta = size;
        }
    }
}
