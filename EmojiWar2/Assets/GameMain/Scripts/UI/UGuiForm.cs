//------------------------------------------------------------
// EmojiWar GameMain - UGUI 界面基类
// 继承框架 UIFormLogic，封装 UGUI Canvas 处理 + 打开/关闭过渡动效。
//------------------------------------------------------------

using System.Collections;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// UGUI 界面逻辑基类：Canvas 管理 + 缩放淡入/淡出动效。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UGuiForm : UIFormLogic
    {
        private Canvas m_CachedCanvas = null;
        private CanvasGroup m_CachedCanvasGroup = null;
        private Coroutine m_Transition = null;

        [SerializeField]
        private float m_TransitionDuration = 0.2f;  // 过渡时长

        public Canvas CachedCanvas
        {
            get
            {
                return m_CachedCanvas;
            }
        }

        public CanvasGroup CachedCanvasGroup
        {
            get
            {
                return m_CachedCanvasGroup;
            }
        }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            m_CachedCanvas = gameObject.GetOrAddComponent<Canvas>();
            m_CachedCanvas.overrideSorting = true;
            m_CachedCanvasGroup = gameObject.GetOrAddComponent<CanvasGroup>();
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            // 打开动效：从缩放 0.92 + 透明 0 → 1
            if (m_CachedCanvasGroup != null)
            {
                m_CachedCanvasGroup.alpha = 0f;
                transform.localScale = Vector3.one * 0.92f;
                m_Transition = StartCoroutine(TransitionTo(Vector3.one, 1f));
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            // 关闭动效：淡出（若未在关闭流程中）
            if (m_Transition != null)
            {
                StopCoroutine(m_Transition);
                m_Transition = null;
            }

            base.OnClose(isShutdown, userData);
        }

        protected override void OnRecycle()
        {
            // 重置状态
            if (m_CachedCanvasGroup != null)
            {
                m_CachedCanvasGroup.alpha = 1f;
            }
            transform.localScale = Vector3.one;

            base.OnRecycle();
        }

        protected override void OnDepthChanged(int uiGroupDepth, int depthInUIGroup)
        {
            base.OnDepthChanged(uiGroupDepth, depthInUIGroup);

            if (m_CachedCanvas != null)
            {
                m_CachedCanvas.sortingOrder = uiGroupDepth * 100 + depthInUIGroup;
            }
        }

        /// <summary>
        /// 过渡协程：缩放 + 透明度插值。
        /// </summary>
        private IEnumerator TransitionTo(Vector3 targetScale, float targetAlpha)
        {
            Vector3 startScale = transform.localScale;
            float startAlpha = m_CachedCanvasGroup != null ? m_CachedCanvasGroup.alpha : 0f;

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, m_TransitionDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smooth = t * t * (3f - 2f * t);   // smoothstep

                transform.localScale = Vector3.Lerp(startScale, targetScale, smooth);
                if (m_CachedCanvasGroup != null)
                {
                    m_CachedCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smooth);
                }

                yield return null;
            }

            transform.localScale = targetScale;
            if (m_CachedCanvasGroup != null)
            {
                m_CachedCanvasGroup.alpha = targetAlpha;
            }
            m_Transition = null;
        }
    }
}
