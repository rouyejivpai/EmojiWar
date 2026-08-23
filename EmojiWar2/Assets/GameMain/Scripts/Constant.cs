//------------------------------------------------------------
// EmojiWar GameMain - 全局常量
//------------------------------------------------------------

namespace EmojiWar.GameMain
{
    /// <summary>
    /// 全局常量定义。
    /// </summary>
    public static class Constant
    {
        /// <summary>
        /// UI 组名称。
        /// </summary>
        public static class UIGroup
        {
            public const string Default = "Default";
            public const string Popup = "Popup";
            public const string System = "System";
        }

        /// <summary>
        /// UI 表单资源路径（编辑器资源模式下的 Assets 相对路径）。
        /// </summary>
        public static class UIFormAssetPath
        {
            public const string MenuForm = "Assets/GameMain/UI/MenuForm.prefab";
            public const string ShopForm = "Assets/GameMain/UI/ShopForm.prefab";
            public const string LobbyForm = "Assets/GameMain/UI/LobbyForm.prefab";
        }
    }
}
