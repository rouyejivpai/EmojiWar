//------------------------------------------------------------
// EmojiWar GameMain - UI 统一样式（主题）
// 所有 UI 生成与逻辑统一引用此处的颜色/字号/尺寸，保证界面风格一致。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.UI
{
    /// <summary>
    /// UI 主题样式常量。
    /// </summary>
    public static class UIStyle
    {
        // ---- 颜色 ----
        public static readonly Color PrimaryColor = new Color(0.16f, 0.42f, 0.85f, 1f);   // 主色（蓝）
        public static readonly Color SecondaryColor = new Color(0.25f, 0.65f, 0.35f, 1f); // 次色（绿）
        public static readonly Color DangerColor = new Color(0.85f, 0.25f, 0.25f, 1f);    // 危险（红）
        public static readonly Color BackgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.92f); // 深色背景
        public static readonly Color PanelColor = new Color(0.14f, 0.16f, 0.2f, 0.95f);   // 面板
        public static readonly Color TextColor = Color.white;                            // 主文本
        public static readonly Color SubTextColor = new Color(0.75f, 0.78f, 0.85f, 1f);  // 次要文本
        public static readonly Color ReadyColor = new Color(0.35f, 0.8f, 0.4f, 1f);      // 已准备
        public static readonly Color NotReadyColor = new Color(0.75f, 0.3f, 0.3f, 1f);   // 未准备

        // ---- 字号 ----
        public const int FontTitle = 56;
        public const int FontSubTitle = 36;
        public const int FontBody = 24;
        public const int FontButton = 32;

        // ---- 尺寸 ----
        public static readonly Vector2 ButtonSize = new Vector2(320, 80);
        public static readonly Vector2 SmallButtonSize = new Vector2(220, 64);
        public static readonly Vector2 CardSize = new Vector2(360, 300);
        public static readonly Vector2 ListItemSize = new Vector2(600, 60);
    }
}
