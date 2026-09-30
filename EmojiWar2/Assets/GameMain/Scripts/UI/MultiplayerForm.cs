//------------------------------------------------------------
// EmojiWar GameMain - 多人游戏界面
// 主界面点"多人游戏"进入本窗体（独立流程态 ProcedureMultiplayer）：
//   顶部：玩家名输入（创建/加入共用）
//   按钮：创建房间 → 直接进入房间页（Host）
//         加入游戏 → 打开 JoinListForm（局域网房间列表）
//         返回 → 主菜单
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>多人游戏页事件（创建/加入/返回；由 ProcedureMultiplayer 处理）。</summary>
    public static class MultiplayerEvents
    {
        /// <summary>请求创建房间（参数：玩家名）。</summary>
        public static event Action<string> OnCreateRoomRequested;

        /// <summary>请求打开加入游戏（房间列表）页。</summary>
        public static event Action OnJoinGameRequested;

        /// <summary>请求返回主菜单。</summary>
        public static event Action OnBackRequested;

        public static void RequestCreate(string name) { OnCreateRoomRequested?.Invoke(name); }
        public static void RequestJoinGame() { OnJoinGameRequested?.Invoke(); }
        public static void RequestBack() { OnBackRequested?.Invoke(); }
    }

    /// <summary>
    /// 多人游戏界面。
    /// </summary>
    public class MultiplayerForm : UGuiForm
    {
        [SerializeField]
        private InputField m_NameInput = null;

        [SerializeField]
        private Button m_CreateButton = null;

        [SerializeField]
        private Button m_JoinButton = null;

        [SerializeField]
        private Button m_BackButton = null;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (m_NameInput != null)
            {
                m_NameInput.text = "玩家" + UnityEngine.Random.Range(100, 999);
            }
            if (m_CreateButton != null)
            {
                m_CreateButton.onClick.RemoveAllListeners();
                m_CreateButton.onClick.AddListener(OnCreateClick);
            }
            if (m_JoinButton != null)
            {
                m_JoinButton.onClick.RemoveAllListeners();
                m_JoinButton.onClick.AddListener(OnJoinGameClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveAllListeners();
                m_BackButton.onClick.AddListener(OnBackClick);
            }
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            if (m_CreateButton != null)
            {
                m_CreateButton.onClick.RemoveListener(OnCreateClick);
            }
            if (m_JoinButton != null)
            {
                m_JoinButton.onClick.RemoveListener(OnJoinGameClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveListener(OnBackClick);
            }
            base.OnClose(isShutdown, userData);
        }

        private void OnCreateClick()
        {
            MultiplayerEvents.RequestCreate(GetPlayerName());
        }

        private void OnJoinGameClick()
        {
            MultiplayerEvents.RequestJoinGame();
        }

        private void OnBackClick()
        {
            MultiplayerEvents.RequestBack();
        }

        /// <summary>读取玩家名（空则默认"玩家"）。</summary>
        public string GetPlayerName()
        {
            string name = m_NameInput != null ? m_NameInput.text : "";
            if (string.IsNullOrEmpty(name))
            {
                name = "玩家";
            }
            return name;
        }

        /// <summary>自动化/测试：模拟点创建房间。</summary>
        public static void TriggerCreate(string name = "房主")
        {
            MultiplayerEvents.RequestCreate(name);
        }

        /// <summary>自动化/测试：模拟点加入游戏（打开列表页）。</summary>
        public static void TriggerJoinGame()
        {
            MultiplayerEvents.RequestJoinGame();
        }
    }
}
