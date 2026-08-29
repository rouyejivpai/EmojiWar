//------------------------------------------------------------
// EmojiWar GameMain - 联机房间界面（QuickBind 示例）
// UI 引用由 QuickBind 自动生成绑定（RoomForm.QuickBind.cs）：
//   根物体挂 QuickBind + 子物体按约定命名（btn_/txt_/...），
//   Inspector 点 [Scan & Generate] 生成字段绑定代码。
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
    /// 联机房间界面（partial：UI 字段由 QuickBind 生成）。
    /// </summary>
    public partial class RoomForm : UGuiForm
    {
        private bool m_LocalReady = false;

        /// <summary>最近一次玩家列表字符串（测试/诊断用）。</summary>
        public string LastPlayerList { get; private set; } = string.Empty;

        /// <summary>本机是否已准备。</summary>
        public bool LocalReady { get { return m_LocalReady; } }

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);

            // QuickBind：应用生成的 UI 引用绑定
            var bind = GetComponent<QuickBind>();
            if (bind != null)
            {
                QuickBindApplyBindings(bind);
            }
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            RoomEvents.OnPlayerListUpdated += OnPlayerListUpdated;

            // 诊断：验证按钮引用是否在构建版正确绑定（QuickBind 绑定表 → m_BtnReady）
            // 若为 null，说明 AssetBundle(game.dat) 里的 prefab 绑定表为空（未重建资源），
            // 点击监听不会挂载 → 按钮无反应。
            var bindComp = GetComponent<QuickBind>();
            WriteProbe("[roomform] OnOpen: m_BtnReady=" + (m_BtnReady != null ? "OK" : "NULL")
                + " m_BtnLeave=" + (m_BtnLeave != null ? "OK" : "NULL")
                + " quickBind=" + (bindComp != null ? "YES" : "NO")
                + " bindCount=" + (bindComp != null ? bindComp.Bindings.Count.ToString() : "-"));

            if (m_TxtTitle != null)
            {
                m_TxtTitle.text = "联机房间";
            }

            if (m_BtnReady != null)
            {
                m_BtnReady.onClick.RemoveAllListeners();
                m_BtnReady.onClick.AddListener(OnReadyClick);
            }

            if (m_BtnLeave != null)
            {
                m_BtnLeave.onClick.RemoveAllListeners();
                m_BtnLeave.onClick.AddListener(OnLeaveClick);
            }

            RefreshPlayerList(LastPlayerList);
        }

        /// <summary>运行时探针（按进程分文件）。</summary>
        private static void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
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
            if (m_TxtPlayerList == null)
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

            m_TxtPlayerList.text = sb.ToString();
        }

        /// <summary>更新本机准备按钮文案（流程/事件调用）。</summary>
        public void SetLocalReadyState(bool ready)
        {
            m_LocalReady = ready;
            if (m_BtnReady != null)
            {
                var label = m_BtnReady.GetComponentInChildren<Text>();
                if (label != null)
                {
                    label.text = ready ? "取消准备" : "准备";
                }
            }
        }

        /// <summary>显示状态提示（如"即将开始..."）。</summary>
        public void ShowStatus(string message)
        {
            if (m_TxtStatus != null)
            {
                m_TxtStatus.text = message;
            }
        }
    }
}
