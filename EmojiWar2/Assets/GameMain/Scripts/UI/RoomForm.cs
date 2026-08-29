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
    /// 布局：停靠屏幕右侧边缘（窄条），包含：标题、玩家列表、准备/离开按钮、
    /// "切换角色"按钮（展开/收起右侧角色抽屉 CharacterDockForm，准备阶段可随时换角色）。
    /// </summary>
    public partial class RoomForm : UGuiForm
    {
        private bool m_LocalReady = false;

        /// <summary>最近一次玩家列表字符串（测试/诊断用）。</summary>
        public string LastPlayerList { get; private set; } = string.Empty;

        /// <summary>本机是否已准备。</summary>
        public bool LocalReady { get { return m_LocalReady; } }

        /// <summary>角色抽屉当前是否展开。</summary>
        public bool IsCharDockOpen { get; private set; } = false;

        /// <summary>角色抽屉（右侧，与房间面板共存；打开时滑入，关闭时滑出）。</summary>
        private CharacterDockForm m_CharDock = null;

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
            CharacterDockEvents.OnDockOpened += OnCharDockOpened;
            CharacterDockEvents.OnCharacterChanged += OnDockCharChanged;

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

            if (m_BtnChangeChar != null)
            {
                m_BtnChangeChar.onClick.RemoveAllListeners();
                m_BtnChangeChar.onClick.AddListener(OnChangeCharClick);
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
            CharacterDockEvents.OnDockOpened -= OnCharDockOpened;
            CharacterDockEvents.OnCharacterChanged -= OnDockCharChanged;

            // 关闭房间时一并收起/关闭角色抽屉
            if (m_CharDock != null)
            {
                var dockForm = m_CharDock;
                m_CharDock = null;
                if (dockForm != null && dockForm.gameObject != null)
                {
                    UnityEngine.Object.Destroy(dockForm.gameObject);
                }
            }

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

        /// <summary>切换角色按钮：展开/收起角色抽屉（DOTween 滑入/滑出）。</summary>
        private void OnChangeCharClick()
        {
            ToggleCharDock(!IsCharDockOpen);
        }

        /// <summary>展开/收起角色抽屉（懒创建，挂到本窗体下保持共存）。</summary>
        public void ToggleCharDock(bool show)
        {
            IsCharDockOpen = show;

            if (show)
            {
                if (m_CharDock == null)
                {
                    m_CharDock = CreateCharDock();
                }
                if (m_CharDock != null)
                {
                    m_CharDock.gameObject.SetActive(true);
                    m_CharDock.TogglePanel(true, true);
                }
            }
            else
            {
                if (m_CharDock != null)
                {
                    m_CharDock.TogglePanel(false, true);
                }
            }
        }

        /// <summary>
        /// 创建角色抽屉：经 UI 框架异步加载 CharacterDockForm.prefab，
        /// 打开完成（OnDockOpened 事件）后由 OnCharDockOpened 绑定实例并展开。
        /// </summary>
        private CharacterDockForm CreateCharDock()
        {
            if (GameEntry.UI != null)
            {
                GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.CharacterDockForm, Constant.UIGroup.Default, this);
            }
            return null;   // 实例在打开完成后经事件绑定
        }

        /// <summary>角色抽屉打开完成回调（DockOpened 事件）。</summary>
        private void OnCharDockOpened(CharacterDockForm dock)
        {
            if (dock == null)
            {
                return;
            }
            m_CharDock = dock;
            dock.RefreshCurrentLabel(Procedure.ProcedureBattle.SelectedCharacterId);
            dock.TogglePanel(true, true);
            IsCharDockOpen = true;
        }

        /// <summary>角色抽屉内切换角色：更新标签并收起抽屉（网络同步由流程层处理）。</summary>
        private void OnDockCharChanged(int characterId)
        {
            if (m_CharDock != null)
            {
                m_CharDock.RefreshCurrentLabel(characterId);
                m_CharDock.TogglePanel(false, true);   // 选完自动收起
            }
            IsCharDockOpen = false;
        }

        /// <summary>关闭角色抽屉（供抽屉自身"收起"按钮回调）。</summary>
        public void CloseCharDock()
        {
            ToggleCharDock(false);
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
