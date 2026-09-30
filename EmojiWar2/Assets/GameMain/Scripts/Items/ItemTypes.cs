//------------------------------------------------------------
// EmojiWar GameMain - 物品系统基础类型（P2）
//
// 方案：doc/物品与法杖系统设计.md §3（v1.0 冻结）
//   - ItemInstance 是**值类型**；带数组字段的法杖必须"整体替换"（WithXxx 一律复制数组），
//     禁止原地改数组 —— 旧版正是"拷贝构造共享 buffs 引用"导致委托逐发累积事故；
//   - SlotRef 是拖拽与事务的统一寻址方式（containerId + index），UI 只传它，不传 GameObject；
//   - 所有失败都有 MoveReason，UI 据此提示并回弹，**绝不静默失败**。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>容器种类（决定插槽接受什么）。</summary>
    public enum SlotKind
    {
        Backpack = 0,       // 背包：任何物品卡
        WandHand = 1,       // 手部（左键/右键）：只接受法杖
        WandSpellSlots = 2, // 法杖内部法术槽：只接受法术卡（归属某把法杖实例）
        Shop = 3,           // 商店货架：只读（买卖走 TryBuy/TrySell）
    }

    /// <summary>槽位寻址（容器 Id + 下标）。</summary>
    public readonly struct SlotRef : IEquatable<SlotRef>
    {
        public readonly string ContainerId;
        public readonly int Index;

        public SlotRef(string containerId, int index)
        {
            ContainerId = containerId;
            Index = index;
        }

        public bool IsValid { get { return !string.IsNullOrEmpty(ContainerId) && Index >= 0; } }

        public bool Equals(SlotRef other)
        {
            return Index == other.Index && string.Equals(ContainerId, other.ContainerId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) { return obj is SlotRef && Equals((SlotRef)obj); }
        public override int GetHashCode() { return (ContainerId != null ? ContainerId.GetHashCode() : 0) * 397 ^ Index; }
        public override string ToString() { return (ContainerId ?? "?") + "[" + Index + "]"; }
    }

    /// <summary>
    /// 运行期物品实例。法杖（IsWand=true）额外携带法术槽装填（元素 = 法术**物品卡** Id，0=空）。
    /// </summary>
    [Serializable]
    public struct ItemInstance
    {
        public int ItemId;          // 物品卡 Id（ItemSO.Id）
        public int Count;           // 数量（>=1）
        public int InstanceId;      // 唯一实例 Id（法杖用；0=普通堆叠物）
        public int[] SpellSlots;    // 法杖专用；null = 非法杖

        /// <summary>
        /// 临时 Buff（第三层，S3/D14；设计 §3.5 Q4：**宿主 = 物品卡实例**）。
        /// 随卡移动（背包↔手部法术槽↔换手/换杖）、跨施法保留、进状态哈希。
        ///
        /// ⚠️ **值语义**：`BuffSet` 内部是数组，`WithXxx` 复制结构体时**只复制引用**。
        ///   任何要改 buff 的路径都必须走 <see cref="WithBuffs"/>（内部 Clone），
        ///   或 `BuffRuntime` 的纯函数式操作 —— 这正是本文件头警示的"共享 buffs 引用导致逐发累积"。
        ///   只读遍历（如 LoadoutCompiler 编译快照）直接读 Buffs 即可，不要写回。
        /// </summary>
        public BuffSet Buffs;

        public static readonly ItemInstance Empty = new ItemInstance();

        public bool IsEmpty { get { return ItemId <= 0 || Count <= 0; } }
        public bool IsWand { get { return SpellSlots != null; } }

        /// <summary>是否挂着临时 Buff（UI 角标/编译快照用）。</summary>
        public bool HasBuffs { get { return !Buffs.IsEmpty; } }

        public static ItemInstance Of(int itemId, int count, int instanceId = 0)
        {
            ItemInstance it = new ItemInstance();
            it.ItemId = itemId;
            it.Count = count;
            it.InstanceId = instanceId;
            it.SpellSlots = null;
            return it;
        }

        /// <summary>复制并改数量（Count<=0 返回 Empty）。</summary>
        public ItemInstance WithCount(int count)
        {
            if (count <= 0 || ItemId <= 0) { return Empty; }
            ItemInstance it = this;
            it.Count = count;
            return it;
        }

        public ItemInstance WithInstanceId(int instanceId)
        {
            ItemInstance it = this;
            it.InstanceId = instanceId;
            return it;
        }

        /// <summary>复制并替换法术槽（内部复制数组，绝不共享引用）。</summary>
        public ItemInstance WithSpellSlots(int[] slots)
        {
            ItemInstance it = this;
            it.SpellSlots = slots != null ? (int[])slots.Clone() : null;
            return it;
        }

        /// <summary>复制并替换单个法术槽（越界/非法索引返回原值）。</summary>
        public ItemInstance WithSpellAt(int index, int spellItemId)
        {
            if (SpellSlots == null || index < 0 || index >= SpellSlots.Length) { return this; }
            int[] copy = (int[])SpellSlots.Clone();
            copy[index] = spellItemId;
            ItemInstance it = this;
            it.SpellSlots = copy;
            return it;
        }

        /// <summary>已装填法术数量。</summary>
        public int LoadedSpellCount()
        {
            if (SpellSlots == null) { return 0; }
            int n = 0;
            for (int i = 0; i < SpellSlots.Length; i++) { if (SpellSlots[i] > 0) { n++; } }
            return n;
        }

        /// <summary>
        /// 复制并整体替换 Buff 集合（**内部 Clone，绝不共享引用**；对齐 <see cref="WithSpellSlots"/>）。
        /// 所有改 buff 的路径都必须走这里 —— 直接 `it.Buffs = x` 会共享数组，属缺陷。
        /// </summary>
        public ItemInstance WithBuffs(BuffSet buffs)
        {
            ItemInstance it = this;
            it.Buffs = buffs.Clone();
            return it;
        }

        public override string ToString()
        {
            string buffText = HasBuffs ? (" " + Buffs.ToString()) : "";
            return IsEmpty ? "empty" : ("item#" + ItemId + " x" + Count + buffText
                + (IsWand ? (" wand(spells=" + LoadedSpellCount() + "/" + SpellSlots.Length + ")") : ""));
        }
    }

    /// <summary>事务失败原因。</summary>
    public enum MoveReason
    {
        Ok = 0,
        EmptySource,
        IndexInvalid,
        ContainerNotFound,
        ReadOnlySlot,
        SelfMove,
        TypeMismatch,       // 目标槽不接受这类物品
        Full,               // 目标（或背包）放不下
        NotStackable,
        NotEnoughCoin,
        NotSellable,
        NotBuyable,
        NotSpell,
        NotWand,
        NothingToDo,
    }

    /// <summary>事务结果。</summary>
    public readonly struct ItemMoveResult
    {
        public readonly bool Ok;
        public readonly MoveReason Reason;
        public readonly int MovedCount;

        private ItemMoveResult(bool ok, MoveReason reason, int moved)
        {
            Ok = ok;
            Reason = reason;
            MovedCount = moved;
        }

        public static ItemMoveResult Success(int moved) { return new ItemMoveResult(true, MoveReason.Ok, moved); }
        public static ItemMoveResult Fail(MoveReason reason) { return new ItemMoveResult(false, reason, 0); }

        public override string ToString()
        {
            return Ok ? ("OK(moved=" + MovedCount + ")") : ("FAIL(" + Reason + ")");
        }
    }

    /// <summary>钱包抽象（金币由局内会话持有；自检用 SimpleWallet）。</summary>
    public interface IWallet
    {
        int Coin { get; }
        bool TrySpend(int amount);
        void Add(int amount);
    }

    /// <summary>简单钱包（自检/测试用）。</summary>
    public sealed class SimpleWallet : IWallet
    {
        private int m_Coin;

        public SimpleWallet(int coin) { m_Coin = coin; }
        public int Coin { get { return m_Coin; } }
        public bool TrySpend(int amount)
        {
            if (amount < 0 || m_Coin < amount) { return false; }
            m_Coin -= amount;
            return true;
        }
        public void Add(int amount) { if (amount > 0) { m_Coin += amount; } }
    }

    /// <summary>
    /// 物品表查询抽象：运行时走 ConfigService，编辑器自检走 AssetDatabase，
    /// 服务本身不依赖 GameEntry/Resources（旧版"不可测"的问题在这里被切断）。
    /// </summary>
    public interface IItemTable
    {
        ItemSO GetItem(int itemId);
        /// <summary>按法术 Id 找对应物品卡（出厂装填用；找不到返回 0）。</summary>
        int FindItemIdBySpell(int spellId);
        /// <summary>按法杖 Id 找对应物品卡（找不到返回 0）。</summary>
        int FindItemIdByWand(int wandId);
    }

    /// <summary>运行时实现：走 ConfigService。</summary>
    public sealed class ConfigItemTable : IItemTable
    {
        public static readonly ConfigItemTable Instance = new ConfigItemTable();

        public ItemSO GetItem(int itemId) { return ConfigService.GetItem(itemId); }

        public int FindItemIdBySpell(int spellId)
        {
            var all = GameEntry.Data != null ? GameEntry.Data.GetAllItems() : null;
            if (all == null) { return 0; }
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it != null && it.Category == ItemCategory.Spell && it.Spell != null && it.Spell.Id == spellId) { return it.Id; }
            }
            return 0;
        }

        public int FindItemIdByWand(int wandId)
        {
            var all = GameEntry.Data != null ? GameEntry.Data.GetAllItems() : null;
            if (all == null) { return 0; }
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it != null && it.Category == ItemCategory.Wand && it.Wand != null && it.Wand.Id == wandId) { return it.Id; }
            }
            return 0;
        }
    }

    /// <summary>字典实现（编辑器自检/单元测试）。</summary>
    public sealed class DictionaryItemTable : IItemTable
    {
        private readonly Dictionary<int, ItemSO> m_ById = new Dictionary<int, ItemSO>();

        public void Add(ItemSO item)
        {
            if (item != null && item.Id > 0) { m_ById[item.Id] = item; }
        }

        public int Count { get { return m_ById.Count; } }

        public ItemSO GetItem(int itemId)
        {
            ItemSO so;
            return m_ById.TryGetValue(itemId, out so) ? so : null;
        }

        public int FindItemIdBySpell(int spellId)
        {
            foreach (var kv in m_ById)
            {
                var it = kv.Value;
                if (it.Category == ItemCategory.Spell && it.Spell != null && it.Spell.Id == spellId) { return it.Id; }
            }
            return 0;
        }

        public int FindItemIdByWand(int wandId)
        {
            foreach (var kv in m_ById)
            {
                var it = kv.Value;
                if (it.Category == ItemCategory.Wand && it.Wand != null && it.Wand.Id == wandId) { return it.Id; }
            }
            return 0;
        }
    }
}
