//------------------------------------------------------------
// EmojiWar GameMain - UGUI 界面基类
// 继承框架 UIFormLogic，封装 UGUI Canvas 相关处理。
//------------------------------------------------------------

using UnityEngine;
using UnityGameFramework.Runtime;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// UGUI 界面逻辑基类：自动管理 Canvas 组件。
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public abstract class UGuiForm : UIFormLogic
    {
        private Canvas m_CachedCanvas = null;
        private CanvasGroup m_CachedCanvasGroup = null;

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
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);
        }

        protected override void OnRecycle()
        {
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
    }
}
