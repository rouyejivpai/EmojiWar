//------------------------------------------------------------
// EmojiWar GameMain - 角色选择界面
// 显示 4 个角色（名字/描述/属性），点击选择后进入大厅。
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>角色选择事件。</summary>
    public static class CharacterSelectEvents
    {
        public static event Action<int> OnCharacterSelected;

        public static void Select(int characterId) { OnCharacterSelected?.Invoke(characterId); }
    }

    /// <summary>
    /// 角色选择界面。
    /// </summary>
    public class CharacterSelectForm : UGuiForm
    {
        [SerializeField]
        private Text m_TitleText = null;

        [SerializeField]
        private Text m_InfoText = null;

        [SerializeField]
        private Button m_Char1Button = null;

        [SerializeField]
        private Button m_Char2Button = null;

        [SerializeField]
        private Button m_Char3Button = null;

        [SerializeField]
        private Button m_Char4Button = null;

        protected override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            if (m_TitleText != null)
            {
                m_TitleText.text = "选择你的角色";
            }

            if (m_InfoText != null)
            {
                m_InfoText.text = "不同角色拥有不同的生命值与移动速度";
            }

            BindButton(m_Char1Button, 1, "流汗黄豆（均衡）");
            BindButton(m_Char2Button, 2, "好吃黄豆（均衡）");
            BindButton(m_Char3Button, 3, "硬汉黄豆（高血量/霰弹）");
            BindButton(m_Char4Button, 4, "快枪黄豆（高速/速射）");
        }

        private void BindButton(Button button, int characterId, string label)
        {
            if (button == null)
            {
                return;
            }

            var text = button.GetComponentInChildren<Text>();
            if (text != null)
            {
                text.text = label;
            }

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => CharacterSelectEvents.Select(characterId));
        }
    }
}
