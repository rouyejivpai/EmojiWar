//------------------------------------------------------------
// EmojiWar GameMain - 物品配置（Item）
//
// 方案：doc/物品与法杖系统设计.md（v1.0 冻结）§2
//   - 本轮物品仅两类：法杖（Wand）/ 法术（Spell）；物品卡是"背包与商店的入口"，
//     真正行为由 WandSO / SpellSO 承担（**资产引用，禁止字符串路径**）；
//   - 法术是**实体卡**：装到法杖槽就从背包移出，卸下才回背包（StackMax>1 时可堆叠）。
//------------------------------------------------------------

using UnityEngine;

namespace EmojiWar.GameMain.Data
{
    /// <summary>物品类别（本轮只有法杖与法术）。</summary>
    public enum ItemCategory
    {
        Wand = 0,   // 法杖
        Spell = 1,  // 法术（实体卡）
    }

    /// <summary>稀有度（商店权重/定价用）。</summary>
    public enum ItemRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3,
    }

    /// <summary>物品标志位。</summary>
    [System.Flags]
    public enum ItemFlags
    {
        None = 0,
        Stackable = 1,   // 可堆叠（StackMax>1）
        Sellable = 2,    // 可出售
        Unique = 8,      // 唯一实例（法杖）
    }

    /// <summary>物品卡配置。</summary>
    [CreateAssetMenu(fileName = "Item_", menuName = "EmojiWar/Data/Item")]
    public sealed class ItemSO : ScriptableObject
    {
        [Header("基础")]
        public int Id = 0;                 // 主键
        public string ItemKey = "";        // 稳定标识（"item_spell_fireball"）
        public string DisplayName = "";    // 名称
        [TextArea] public string Description = "";

        [Header("分类 / 稀有度 / 权重")]
        public ItemCategory Category = ItemCategory.Spell;
        public ItemRarity Rarity = ItemRarity.Common;
        public float Weight = 1f;          // 商店抽取权重

        [Header("展示（资产引用）")]
        public Sprite IconSprite = null;

        [Header("背包 / 交易")]
        public int StackMax = 1;           // 最大堆叠（1=不可堆叠）
        public int Price = 10;             // 商店售价（买）
        public int SellPrice = 5;          // 回收价（卖）
        public ItemFlags Flags = ItemFlags.Sellable;

        [Header("行为引用（按 Category 二选一）")]
        public WandSO Wand = null;         // Category=Wand
        public SpellSO Spell = null;       // Category=Spell
    }
}
