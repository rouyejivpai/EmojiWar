//------------------------------------------------------------
// EmojiWar GameMain - 背包物品槽 Cell（5×5 物品栏的一项）
// 子物体按名称约定：Icon(Image) / Label(Text)；数据用资产引用（Sprite）。
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>背包物品槽（只读展示：图标 + 名称）。</summary>
    public class InventorySlotCell : MonoBehaviour
    {
        private Image m_Bg = null;
        private Image m_Icon = null;
        private Text m_Label = null;

        private void Awake() { Cache(); }
        private void OnEnable() { if (m_Icon == null && m_Label == null) { Cache(); } }

        private void Cache()
        {
            if (m_Bg == null) { m_Bg = GetComponent<Image>(); }
            if (m_Icon == null)
            {
                var t = transform.Find("Icon");
                if (t != null) { m_Icon = t.GetComponent<Image>(); }
            }
            if (m_Label == null)
            {
                var t = transform.Find("Label");
                if (t != null) { m_Label = t.GetComponent<Text>(); }
            }
        }

        /// <summary>设置槽位内容（sprite 为空表示空槽）。</summary>
        public void SetContent(Sprite sprite, string label)
        {
            Cache();
            bool empty = sprite == null;

            if (m_Bg != null)
            {
                m_Bg.color = empty
                    ? new Color(1f, 1f, 1f, 0.06f)
                    : new Color(1f, 1f, 1f, 0.16f);
            }
            if (m_Icon != null)
            {
                m_Icon.sprite = sprite;
                m_Icon.enabled = !empty;
            }
            if (m_Label != null)
            {
                m_Label.text = empty ? string.Empty : (label ?? string.Empty);
            }
        }
    }
}
