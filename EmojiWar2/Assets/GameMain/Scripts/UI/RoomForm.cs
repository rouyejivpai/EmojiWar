//------------------------------------------------------------
// EmojiWar GameMain - 联机房间界面
// 显示：房间标题、玩家列表（名字+准备状态）、准备按钮、离开/解散按钮。
// 全部玩家准备后由 Host 广播开始（RoomEvents.OnBattleStart）。
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>房间事件（准备/离开）。</summary>
    public static class RoomFormEvents
    {
        public static event Action OnReadyRequested;
        public static event Action OnLeaveRequested;

        public static void RequestReady() { OnReadyRequested?.Invoke(); }
        public static void RequestLeave() { OnLeaveRequested?.Invoke(); }
    }

    /// <summary>
    /// 联机房间界面。
    /// </summary>
    public class RoomForm : UGuiForm
    {
        [SerializeField]
        private Text m_RoomTitleText = null;

        [SerializeField]
        private Text m_PlayerListText = null;

        [SerializeField]
        private Text m_StatusText = null;

        [SerializeField]
        private Button m_ReadyButton = null;

        [SerializeField]
        private Button m_LeaveButton = null;

        private bool m_LocalReady = false;

        /// <summary>最近一次玩家列表字符串（测试/诊断用）。</summary>
        public string LastPlayerList { get; private set; } = string.Empty;

        /// <summary>本机是否已准备。</summary>
        public bool LocalReady { get { return m_LocalReady; } }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            RoomEvents.OnPlayerListUpdated += OnPlayerListUpdated;

            if (m_RoomTitleText != null)
            {
                m_RoomTitleText.text = "联机房间";
            }

            if (m_ReadyButton != null)
            {
                m_ReadyButton.onClick.RemoveAllListeners();
                m_ReadyButton.onClick.AddListener(OnReadyClick);
            }

            if (m_LeaveButton != null)
            {
                m_LeaveButton.onClick.RemoveAllListeners();
                m_LeaveButton.onClick.AddListener(OnLeaveClick);
            }

            RefreshPlayerList(LastPlayerList);
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            RoomEvents.OnPlayerListUpdated -= OnPlayerListUpdated;
            base.OnClose(isShutdown, userData);
        }

        private void OnReadyClick()
        {
            RoomFormEvents.RequestReady();
        }

        private void OnLeaveClick()
        {
            RoomFormEvents.RequestLeave();
        }

        private void OnPlayerListUpdated(string players)
        {
            LastPlayerList = players;
            RefreshPlayerList(players);
        }

        /// <summary>刷新玩家列表显示（"名字:1;名字:0" → 多行文本）。</summary>
        private void RefreshPlayerList(string players)
        {
            if (m_PlayerListText == null)
            {
                return;
            }

            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrEmpty(players))
            {
                string[] entries = players.Split(';');
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry))
                    {
                        continue;
                    }
                    string[] kv = entry.Split(':');
                    string name = kv.Length > 0 ? kv[0] : "玩家";
                    bool ready = kv.Length > 1 && kv[1] == "1";
                    sb.AppendLine(string.Format("{0}  {1}", name, ready ? "✓ 已准备" : "✗ 未准备"));
                }
            }

            m_PlayerListText.text = sb.ToString();
        }

        /// <summary>更新本机准备按钮文案（流程/事件调用）。</summary>
        public void SetLocalReadyState(bool ready)
        {
            m_LocalReady = ready;
            if (m_ReadyButton != null)
            {
                var label = m_ReadyButton.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = ready ? "取消准备" : "准备";
                }
            }
        }

        /// <summary>显示状态提示（如"即将开始..."）。</summary>
        public void ShowStatus(string message)
        {
            if (m_StatusText != null)
            {
                m_StatusText.text = message;
            }
        }
    }
}
