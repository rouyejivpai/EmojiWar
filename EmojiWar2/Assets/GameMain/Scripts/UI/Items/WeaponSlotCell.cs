//------------------------------------------------------------
// EmojiWar GameMain - 武器槽 Cell（背包左侧：主武器/副武器）
// 子物体按名称约定：Icon(Image) / Name(Text) / Key(Text，如"左键"/"右键"）
//------------------------------------------------------------

using UnityEngine;
using UnityEngine.UI;

namespace EmojiWar.GameMain.UI
{
    /// <summary>武器槽（只读展示：键位 + 图标 + 武器名）。</summary>
    public class WeaponSlotCell : MonoBehaviour
    {
        private Image m_Icon = null;
        private Text m_Name = null;
        private Text m_Key = null;

        private void Awake() { Cache(); }
        private void OnEnable() { if (m_Icon == null) { Cache(); } }

        private void Cache()
        {
            if (m_Icon == null)
            {
                var t = transform.Find("Icon");
                if (t != null) { m_Icon = t.GetComponent<Image>(); }
            }
            if (m_Name == null)
            {
                var t = transform.Find("Name");
                if (t != null) { m_Name = t.GetComponent<Text>(); }
            }
            if (m_Key == null)
            {
                var t = transform.Find("Key");
                if (t != null) { m_Key = t.GetComponent<Text>(); }
            }
        }

        /// <summary>设置武器槽内容（sprite 为空表示该槽为空）。</summary>
        public void SetContent(string keyLabel, Sprite sprite, string weaponName)
        {
            Cache();

            if (m_Key != null) { m_Key.text = keyLabel ?? string.Empty; }
            if (m_Icon != null)
            {
                m_Icon.sprite = sprite;
                m_Icon.enabled = sprite != null;
            }
            if (m_Name != null)
            {
                m_Name.text = sprite != null ? (weaponName ?? string.Empty) : "空";
            }
        }
    }
}
