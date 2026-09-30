//------------------------------------------------------------
// EmojiWar GameMain - 物品详情行 Cell（ItemDetailCell）
//
// 用途：`ItemDetailPanel` 的通用"一行"（属性行 / 触发顺序行 / 被动行 / 预告行 共用）。
// 结构（按子物体名找，生成器见 Editor/ItemDetailPanelBuilder.cs）：
//   Icon   (Image, 可空)  行首小图标（法术/物品图标；属性行没有图标则隐藏）
//   Name   (Text)         行名（左对齐，占满剩余宽度）
//   Value  (Text)         行值（右对齐；可为空）
//
// 规范：本 Cell 属"列表行"，遵循 doc/UI列表与Cell规范.md —— 作者态模板放在列表容器（正）
//   下方，运行时由 UI/UiListCell.Resolve 取用；刷新一律"先清空再生成"。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>详情面板的一行。</summary>
    public sealed class ItemDetailCell : MonoBehaviour
    {
        private Image m_Icon = null;
        private Text m_Name = null;
        private Text m_Value = null;
        private RectTransform m_IconRect = null;

        private void Awake() { Cache(); }

        private void Cache()
        {
            if (m_Name != null) { return; }
            var iconT = transform.Find("Icon");
            if (iconT != null) { m_Icon = iconT.GetComponent<Image>(); m_IconRect = iconT as RectTransform; }
            var nameT = transform.Find("Name");
            if (nameT != null) { m_Name = nameT.GetComponent<Text>(); }
            var valueT = transform.Find("Value");
            if (valueT != null) { m_Value = valueT.GetComponent<Text>(); }
        }

        /// <summary>填充一行内容（icon 为空则隐藏图标位并让名字占满整行）。</summary>
        public void Set(Sprite icon, string name, string value, Color nameColor)
        {
            Cache();

            bool hasIcon = icon != null;
            if (m_Icon != null)
            {
                m_Icon.sprite = icon;
                m_Icon.enabled = hasIcon;
            }
            if (m_IconRect != null)
            {
                m_IconRect.gameObject.SetActive(hasIcon);
            }

            // 有图标 → 行首留出图标宽度；无图标 → 名字从最左开始（不空洞）
            if (m_Name != null)
            {
                m_Name.text = name ?? string.Empty;
                m_Name.color = nameColor;
                var r = m_Name.rectTransform;
                r.offsetMin = new Vector2(hasIcon ? 34f : 0f, r.offsetMin.y);
            }
            if (m_Value != null)
            {
                m_Value.text = value ?? string.Empty;
            }
        }
    }
}
