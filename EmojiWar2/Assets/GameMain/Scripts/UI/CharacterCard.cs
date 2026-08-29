//------------------------------------------------------------
// EmojiWar GameMain - 角色卡片（动态实例化的独立模板）
// 由 CharacterDockForm 按 character_select.json 动态实例化：
//   每张卡 = emoji 图标 + 名字 + 简略属性（生命/移速，查 Character.txt）
//          + 选中高亮 + 整卡可点（Button）。
// 参考：生产级技能系统「模板复用」——卡片 prefab 改一处，所有角色卡样式统一。
//------------------------------------------------------------

using System;
using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>角色卡片点击事件（参数：角色 ID）。</summary>
    public class CharacterCard : MonoBehaviour
    {
        [SerializeField]
        private Image m_IconImage = null;        // emoji 图标（Sprite）

        [SerializeField]
        private Text m_NameText = null;          // 角色名

        [SerializeField]
        private Text m_StatsText = null;         // 简略属性（"生命150 移速5"）

        [SerializeField]
        private Image m_Highlight = null;        // 选中高亮背景

        [SerializeField]
        private Button m_Button = null;          // 整卡点击

        private int m_CharacterId = 0;
        private Action<int> m_OnClick = null;

        /// <summary>当前卡片角色 ID。</summary>
        public int CharacterId { get { return m_CharacterId; } }

        /// <summary>绑定角色数据 + 点击回调（由 Docked 面板调用）。</summary>
        public void Setup(int characterId, string name, string iconCode, string statsLine,
            Sprite iconSprite, Action<int> onClick)
        {
            m_CharacterId = characterId;
            m_OnClick = onClick;

            if (m_IconImage != null && iconSprite != null)
            {
                m_IconImage.sprite = iconSprite;
                m_IconImage.gameObject.SetActive(true);
            }
            if (m_NameText != null)
            {
                m_NameText.text = name;
            }
            if (m_StatsText != null)
            {
                m_StatsText.text = statsLine;
            }

            if (m_Button != null)
            {
                m_Button.onClick.RemoveAllListeners();
                m_Button.onClick.AddListener(OnCardClick);
            }

            SetHighlight(false);
        }

        /// <summary>设置选中高亮。</summary>
        public void SetHighlight(bool on)
        {
            if (m_Highlight != null)
            {
                m_Highlight.gameObject.SetActive(on);
            }
        }

        private void OnCardClick()
        {
            m_OnClick?.Invoke(m_CharacterId);
        }
    }
}
