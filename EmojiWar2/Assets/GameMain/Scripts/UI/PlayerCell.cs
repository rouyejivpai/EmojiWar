//------------------------------------------------------------
// EmojiWar GameMain - 玩家准备状态 Cell（房间玩家列表的列表项）
//
// 说明：
//   - 独立预制体（Assets/GameMain/UI/items/PlayerCell.prefab），由 RoomForm 经
//     GfUiItemPool（GameFramework 对象池）获取/回收。
//   - 内容：{所选角色 Icon(Sprite 引用), 玩家名字, 准备状态}。
//   - Icon 子物体 = **Image**，数据直接使用 CharacterSO.IconSprite（SO 资产引用，
//     不再使用字符串码点）；若旧形态仍为 Text 则隐藏文本（不再渲染字符）。
//   - 子物体按名称约定定位（Icon/Name/Ready），不依赖 QuickBind。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>玩家准备 Cell（房间列表项）。</summary>
    public class PlayerCell : MonoBehaviour
    {
        private Image m_IconImage = null;
        private Text m_IconText = null;     // 兼容旧版：Text 形态时隐藏
        private Text m_Name = null;
        private Text m_Ready = null;

        /// <summary>当前图标精灵名（诊断/验证用；无则空串）。</summary>
        public string CurrentIconSpriteName
        {
            get { return m_IconImage != null && m_IconImage.sprite != null ? m_IconImage.sprite.name : string.Empty; }
        }

        /// <summary>兼容旧探针命名：等价于 CurrentIconSpriteName。</summary>
        public string CurrentIconCode { get { return CurrentIconSpriteName; } }

        public RectTransform Rect
        {
            get { return transform as RectTransform; }
        }

        private void Awake()
        {
            CacheChildren();
        }

        private void OnEnable()
        {
            if (m_IconImage == null && m_IconText == null)
            {
                CacheChildren();
            }
        }

        private void CacheChildren()
        {
            if (m_IconImage == null && m_IconText == null)
            {
                var icon = transform.Find("Icon");
                if (icon != null)
                {
                    m_IconImage = icon.GetComponent<Image>();
                    m_IconText = icon.GetComponent<Text>();
                }
            }
            if (m_Name == null)
            {
                var nameT = transform.Find("Name");
                if (nameT != null)
                {
                    m_Name = nameT.GetComponent<Text>();
                }
            }
            if (m_Ready == null)
            {
                var ready = transform.Find("Ready");
                if (ready != null)
                {
                    m_Ready = ready.GetComponent<Text>();
                }
            }
        }

        /// <summary>
        /// 填充单元格内容。
        /// </summary>
        /// <param name="iconSprite">角色图标（CharacterSO.IconSprite 资产引用）。</param>
        /// <param name="displayName">玩家名。</param>
        /// <param name="ready">是否已准备。</param>
        /// <param name="isLocal">是否本机玩家（黄色高亮 + "（你）"）。</param>
        public void SetContent(Sprite iconSprite, string displayName, bool ready, bool isLocal)
        {
            CacheChildren();

            if (m_IconImage != null)
            {
                m_IconImage.sprite = iconSprite;
                m_IconImage.enabled = iconSprite != null;
            }
            else if (m_IconText != null)
            {
                // 旧 Text 形态：不再用字符串码点，隐藏文本避免残留
                m_IconText.text = string.Empty;
                m_IconText.enabled = false;
            }

            if (m_Name != null)
            {
                m_Name.text = displayName + (isLocal ? "（你）" : "");
                m_Name.color = isLocal
                    ? new Color(1f, 0.85f, 0.3f, 1f)
                    : new Color(0.95f, 0.95f, 0.95f, 1f);
            }
            if (m_Ready != null)
            {
                m_Ready.text = ready ? "✓ 已准备" : "✗ 未准备";
                m_Ready.color = ready
                    ? new Color(0.35f, 0.95f, 0.45f, 1f)
                    : new Color(0.85f, 0.5f, 0.5f, 1f);
            }
        }
    }
}
