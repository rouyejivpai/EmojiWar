//------------------------------------------------------------
// EmojiWar GameMain - 主菜单界面
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// 主菜单界面：标题 + 开始游戏按钮。
    /// </summary>
    public class MenuForm : UGuiForm
    {
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
                m_VersionText.text = "v0.1.0 - 架构重构版";
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
            // TODO(Phase 2): 进入角色选择/匹配流程
            Log("开始游戏（占位）");
        }

        private static void Log(string message)
        {
            Debug.Log($"[MenuForm] {message}");
        }
    }
}
