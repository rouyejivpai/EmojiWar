//------------------------------------------------------------
// EmojiWar GameMain - 主菜单界面
// 布局：标题 + 四个按钮（单人游戏 / 多人游戏 / 设置 / 退出游戏）。
// 事件全部经 ProcedureMenu 处理：
//   - 单人游戏 → 占位提示（暂未实现）
//   - 多人游戏 → ProcedureMultiplayer（创建房间 / 加入游戏）
//   - 设置 → 打开设置页（窗口化等）
//   - 退出游戏 → Application.Quit
// 保留 OnStartGameRequested/TriggerStartGame（兼容 AutoPlay 回归直接进入旧流程）。
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 主菜单界面。
    /// </summary>
    public class MenuForm : UGuiForm
    {
        /// <summary>点击"开始游戏"（旧入口；AutoPlay 回归用，UI 已不挂按钮）。</summary>
        public static event Action OnStartGameRequested;

        /// <summary>点击"单人游戏"（占位）。</summary>
        public static event Action OnSingleGameRequested;

        /// <summary>点击"多人游戏"。</summary>
        public static event Action OnMultiplayerRequested;

        /// <summary>点击"退出游戏"。</summary>
        public static event Action OnQuitRequested;

        [SerializeField]
        private Button m_SingleButton = null;

        [SerializeField]
        private Button m_MultiplayerButton = null;

        [SerializeField]
        private Button m_SettingsButton = null;

        [SerializeField]
        private Button m_QuitButton = null;

        [SerializeField]
        private Text m_TitleText = null;

        [SerializeField]
        private Text m_VersionText = null;

        [SerializeField]
        private Text m_HintText = null;        // 主菜单提示行（单人占位提示等）

        /// <summary>当前打开的设置窗体（防止重复打开）。</summary>
        private SettingsForm m_SettingsForm = null;

        protected override void OnInit(object userData)
        {
            base.OnInit(userData);
        }

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (m_SingleButton != null)
            {
                m_SingleButton.onClick.RemoveAllListeners();
                m_SingleButton.onClick.AddListener(OnSingleClick);
            }
            if (m_MultiplayerButton != null)
            {
                m_MultiplayerButton.onClick.RemoveAllListeners();
                m_MultiplayerButton.onClick.AddListener(OnMultiplayerClick);
            }
            if (m_SettingsButton != null)
            {
                m_SettingsButton.onClick.RemoveAllListeners();
                m_SettingsButton.onClick.AddListener(OnSettingsButtonClick);
            }
            if (m_QuitButton != null)
            {
                m_QuitButton.onClick.RemoveAllListeners();
                m_QuitButton.onClick.AddListener(OnQuitClick);
            }

            if (m_TitleText != null)
            {
                m_TitleText.text = "Emoji War";
            }
            if (m_VersionText != null)
            {
                m_VersionText.text = "v0.3.0 - 多人联机版";
            }

            SettingsEvents.OnBackRequested += OnSettingsBack;
        }

        protected override void OnClose(bool isShutdown, object userData)
        {
            base.OnClose(isShutdown, userData);

            if (m_SingleButton != null)
            {
                m_SingleButton.onClick.RemoveListener(OnSingleClick);
            }
            if (m_MultiplayerButton != null)
            {
                m_MultiplayerButton.onClick.RemoveListener(OnMultiplayerClick);
            }
            if (m_SettingsButton != null)
            {
                m_SettingsButton.onClick.RemoveListener(OnSettingsButtonClick);
            }
            if (m_QuitButton != null)
            {
                m_QuitButton.onClick.RemoveListener(OnQuitClick);
            }

            SettingsEvents.OnBackRequested -= OnSettingsBack;
        }

        private void OnSingleClick()
        {
            Debug.Log("[MenuForm] 单人游戏（占位，暂未实现）");
            ShowHint("单人游戏开发中，敬请期待");
            OnSingleGameRequested?.Invoke();
        }

        private void OnMultiplayerClick()
        {
            Debug.Log("[MenuForm] 多人游戏");
            OnMultiplayerRequested?.Invoke();
        }

        private void OnQuitClick()
        {
            Debug.Log("[MenuForm] 退出游戏");
            OnQuitRequested?.Invoke();
        }

        /// <summary>触发开始游戏事件（旧入口：仅供测试/流程驱动调用）。</summary>
        public static void TriggerStartGame()
        {
            OnStartGameRequested?.Invoke();
        }

        /// <summary>触发"多人游戏"（等效点击多人游戏按钮；自动化/流程驱动用）。</summary>
        public static void TriggerMultiplayer()
        {
            OnMultiplayerRequested?.Invoke();
        }

        /// <summary>触发打开设置页（公开：供自动化验证调用，等效于点"设置"按钮）。</summary>
        public static void TriggerOpenSettings()
        {
            if (GameEntry.UI == null)
            {
                Debug.LogWarning("[MenuForm] TriggerOpenSettings: GameEntry.UI null");
                return;
            }
            if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Default))
            {
                GameEntry.UI.AddUIGroup(Constant.UIGroup.Default);
            }
            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.SettingsForm, Constant.UIGroup.Default, null);
        }

        /// <summary>设置按钮：打开设置页（独立 UIForm，覆盖在主菜单上）。</summary>
        private void OnSettingsButtonClick()
        {
            if (GameEntry.UI == null)
            {
                return;
            }
            if (!GameEntry.UI.HasUIGroup(Constant.UIGroup.Default))
            {
                GameEntry.UI.AddUIGroup(Constant.UIGroup.Default);
            }
            GameEntry.UI.OpenUIForm(Constant.UIFormAssetPath.SettingsForm, Constant.UIGroup.Default, this);
        }

        /// <summary>设置页"返回"：关闭设置窗体回主菜单。</summary>
        private void OnSettingsBack()
        {
            if (GameEntry.UI != null)
            {
                var forms = GameEntry.UI.GetAllUIGroups();
                foreach (var group in forms)
                {
                    foreach (var form in group.GetAllUIForms())
                    {
                        var uiForm = form as UnityGameFramework.Runtime.UIForm;
                        if (uiForm != null && uiForm.Logic != null &&
                            uiForm.Logic.Name == "SettingsForm(Clone)")
                        {
                            GameEntry.UI.CloseUIForm(uiForm);
                        }
                    }
                }
            }
        }

        /// <summary>在主菜单底部显示一行提示（数秒后恢复）。</summary>
        public void ShowHint(string message)
        {
            if (m_HintText != null)
            {
                m_HintText.text = message;
            }
            CancelInvoke(nameof(ClearHint));
            Invoke(nameof(ClearHint), 3f);
        }

        private void ClearHint()
        {
            if (m_HintText != null)
            {
                m_HintText.text = "";
            }
        }
    }
}
