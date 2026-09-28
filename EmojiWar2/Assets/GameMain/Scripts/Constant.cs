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
            public const string Top = "Top";   // 最高层：房间准备条等"附加悬浮 UI"，不被其它窗体遮挡

            // UI 组深度（canvas sortingOrder = depth*100 + 组内序号）。
            // 约定：Default(0) < Top(100) < Popup(200)。
            // 背包/设置等"弹出层"必须高于商店（Default）与 HUD，否则会被商店盖住。
            public const int DepthDefault = 0;
            public const int DepthTop = 100;
            public const int DepthPopup = 200;
        }

        /// <summary>
        /// UI 表单资源路径（编辑器资源模式下的 Assets 相对路径）。
        /// </summary>
        public static class UIFormAssetPath
        {
            public const string MenuForm = "Assets/GameMain/UI/MenuForm.prefab";
            public const string MultiplayerForm = "Assets/GameMain/UI/MultiplayerForm.prefab";
            public const string JoinListForm = "Assets/GameMain/UI/JoinListForm.prefab";
            public const string SettingsForm = "Assets/GameMain/UI/SettingsForm.prefab";
            public const string ShopForm = "Assets/GameMain/UI/ShopForm.prefab";
            public const string LobbyForm = "Assets/GameMain/UI/LobbyForm.prefab";
            public const string BattleHudForm = "Assets/GameMain/UI/BattleHudForm.prefab";
            public const string GameOverForm = "Assets/GameMain/UI/GameOverForm.prefab";
            public const string RoomForm = "Assets/GameMain/UI/RoomForm.prefab";
            public const string CharacterSelectForm = "Assets/GameMain/UI/CharacterSelectForm.prefab";
            public const string CharacterDockForm = "Assets/GameMain/UI/CharacterDockForm.prefab";
            public const string BackpackForm = "Assets/GameMain/UI/BackpackForm.prefab";
        }

        /// <summary>
        /// 小预制体（Cell/列表项等）资源路径——统一放 Assets/GameMain/UI/items/，
        /// 属 'game' AssetBundle，运行时经 GameEntry.Resource（UiPrefab）按全路径加载。
        /// </summary>
        public static class UIItemAssetPath
        {
            public const string PlayerCell = "Assets/GameMain/UI/items/PlayerCell.prefab";         // 房间玩家准备 Cell
            public const string CharacterCard = "Assets/GameMain/UI/items/CharacterCard.prefab";  // 选角卡片
            public const string RoomItemRow = "Assets/GameMain/UI/items/RoomItemRow.prefab";      // 加入房间列表行
            public const string WeaponSlot = "Assets/GameMain/UI/items/WeaponSlot.prefab";          // 背包武器槽
            public const string InventorySlot = "Assets/GameMain/UI/items/InventorySlot.prefab";    // 背包物品槽
            // 物品系统（P3）：可拖拽插槽 Cell
            public const string SlotCell = "Assets/GameMain/UI/items/SlotCell.prefab";              // 通用插槽（可拖拽/可放置）
            public const string WandHandCell = "Assets/GameMain/UI/items/WandHandCell.prefab";      // 手部法杖槽（图标+名称+键位）
            // 法术编程系统（S6）：悬停详情面板（内含 Attr/Follow/Buff/Forecast/Loaded 行容器 + 行 Cell 模板）
            public const string ItemDetailPanel = "Assets/GameMain/UI/items/ItemDetailPanel.prefab";  // 物品/法杖详情面板
            public const string ItemDetailCell = "Assets/GameMain/UI/items/ItemDetailCell.prefab";    // 详情面板行 Cell（Icon/Name/Value）
        }
    }
}
