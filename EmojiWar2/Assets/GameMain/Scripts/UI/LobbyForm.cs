//------------------------------------------------------------
// EmojiWar GameMain - 大厅界面
// 玩家名输入 + 创建房间（Host）/ 加入房间（Client）。
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 大厅界面：创建/加入多人房间。
    /// </summary>
    public class LobbyForm : UGuiForm
    {
        /// <summary>创建房间事件（参数：玩家名）。</summary>
        public static event Action<string> OnCreateRoomRequested;

        /// <summary>加入房间事件（参数：玩家名, IP, 端口）。</summary>
        public static event Action<string, string, int> OnJoinRoomRequested;

        /// <summary>返回主菜单事件。</summary>
        public static event Action OnBackRequested;

        [SerializeField]
        private InputField m_NameInput = null;

        [SerializeField]
        private InputField m_IpInput = null;

        [SerializeField]
        private Text m_StatusText = null;

        [SerializeField]
        private Button m_CreateButton = null;

        [SerializeField]
        private Button m_JoinButton = null;

        [SerializeField]
        private Button m_BackButton = null;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (m_NameInput != null)
            {
                m_NameInput.text = "玩家" + UnityEngine.Random.Range(100, 999);
            }

            if (m_IpInput != null)
            {
                m_IpInput.text = "127.0.0.1";
            }

            if (m_StatusText != null)
            {
                m_StatusText.text = "创建房间成为房主，或加入好友房间";
            }

            if (m_CreateButton != null)
            {
                m_CreateButton.onClick.RemoveAllListeners();
                m_CreateButton.onClick.AddListener(OnCreateClick);
            }

            if (m_JoinButton != null)
            {
                m_JoinButton.onClick.RemoveAllListeners();
                m_JoinButton.onClick.AddListener(OnJoinClick);
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
                m_JoinButton.onClick.RemoveListener(OnJoinClick);
            }
            if (m_BackButton != null)
            {
                m_BackButton.onClick.RemoveListener(OnBackClick);
            }
            base.OnClose(isShutdown, userData);
        }

        private void OnCreateClick()
        {
            string name = m_NameInput != null ? m_NameInput.text : "玩家";
            if (string.IsNullOrEmpty(name))
            {
                name = "玩家";
            }
            OnCreateRoomRequested?.Invoke(name);
        }

        /// <summary>
        /// 触发创建房间（公开：供测试/流程驱动调用）。
        /// </summary>
        public static void TriggerCreateRoom()
        {
            OnCreateRoomRequested?.Invoke("房主");
        }

        /// <summary>
        /// 触发加入房间（公开：供测试/流程驱动调用）。
        /// </summary>
        public static void TriggerJoinRoom(string ip = "127.0.0.1", int port = Network.NetworkService.DefaultPort)
        {
            OnJoinRoomRequested?.Invoke("玩家", ip, port);
        }

        private void OnJoinClick()
        {
            string name = m_NameInput != null ? m_NameInput.text : "玩家";
            string ip = m_IpInput != null ? m_IpInput.text : "127.0.0.1";
            if (string.IsNullOrEmpty(name))
            {
                name = "玩家";
            }
            if (string.IsNullOrEmpty(ip))
            {
                ip = "127.0.0.1";
            }

            // 防御：IP 输入框可能带端口（"127.0.0.1:7777"），拆出端口
            int port = Network.NetworkService.DefaultPort;
            int colon = ip.LastIndexOf(':');
            if (colon > 0)
            {
                string maybePort = ip.Substring(colon + 1);
                int parsedPort;
                if (int.TryParse(maybePort, out parsedPort) && parsedPort > 0 && parsedPort < 65536)
                {
                    port = parsedPort;
                    ip = ip.Substring(0, colon);
                }
            }

            OnJoinRoomRequested?.Invoke(name, ip, port);
        }

        private void OnBackClick()
        {
            OnBackRequested?.Invoke();
        }

        /// <summary>
        /// 更新状态文本（大厅逻辑调用）。
        /// </summary>
        public void SetStatus(string message)
        {
            if (m_StatusText != null)
            {
                m_StatusText.text = message;
            }
        }
    }
}
