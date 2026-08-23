//------------------------------------------------------------
// EmojiWar GameMain - 主菜单界面
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 主菜单界面：标题 + 开始游戏按钮。
    /// </summary>
    public class MenuForm : UGuiForm
    {
        /// <summary>点击"开始游戏"事件（由 Procedure 订阅处理流程切换）。</summary>
        public static event Action OnStartGameRequested;

        [SerializeField]
        private Button m_StartButton = null;

        [SerializeField]
        private Text m_TitleText = null;

        [SerializeField]
        private Text m_VersionText = null;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (m_StartButton != null)
            {
                m_StartButton.onClick.RemoveAllListeners();
                m_StartButton.onClick.AddListener(OnStartButtonClick);
            }

            if (m_TitleText != null)
            {
                m_TitleText.text = "Emoji War";
            }

            if (m_VersionText != null)
            {
                m_VersionText.text = "v0.2.0 - 单机战斗原型";
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);

            if (m_StartButton != null)
            {
                m_StartButton.onClick.RemoveListener(OnStartButtonClick);
            }
        }

        private void OnStartButtonClick()
        {
            Debug.Log("[MenuForm] 开始游戏");
            TriggerStartGame();
        }

        /// <summary>
        /// 触发开始游戏事件（公开：供测试/流程驱动调用）。
        /// </summary>
        public static void TriggerStartGame()
        {
            OnStartGameRequested?.Invoke();
        }
    }
}
