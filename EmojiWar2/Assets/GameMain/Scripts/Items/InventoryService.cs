//------------------------------------------------------------
// EmojiWar GameMain - 背包/装备/商店事务服务（P2）
//
// 方案：doc/物品与法杖系统设计.md §3
//   **唯一的物品改动入口**：放入 / 堆叠 / 交换 / 分堆 / 买卖 / 装备法杖 / 装填法术。
//   UI（拖拽）与商店按钮都只调这里；失败必带 MoveReason，UI 据此提示并回弹。
//   规则：
//     · 背包：任何物品；同 Key 且未满堆叠则合并，否则与目标槽交换；
//     · 手部（左/右）：只接受法杖；装入即自动创建该杖的法术槽容器并预填出厂法术；
//     · 法杖法术槽：只接受法术卡（实体卡语义：装填即从背包移出，卸下才回背包）；
//     · 商店货架：只读，买卖走 TryBuy / TrySell（买=钱够且背包放得下，原子操作）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;
using EmojiWar.GameMain.Data;

namespace EmojiWar.GameMain.Items
{
    /// <summary>物品事务服务（背包/装备/商店）。</summary>
    public sealed class InventoryService
    {
        public const string WandContainerPrefix = "wand#";     // 旧命名空间（已废弃，仅兼容常量）

        /// <summary>
        /// 手部法术槽容器的物理上限（D33/P1：**来自 SpellSystemConfigSO.MaxSpellSlots**，
        /// 禁止硬编码；可用槽数仍由法杖 SlotCount 决定，见 ReconcileHand）。
        /// </summary>
        public static int MaxWandSlots { get { return ConfigService.MaxSpellSlots; } }

        private readonly IItemTable m_Table;
        private readonly Dictionary<string, ItemContainer> m_Containers = new Dictionary<string, ItemContainer>(StringComparer.Ordinal);
        private int m_NextInstanceId = 1;

        public InventoryService(IItemTable table)
        {
            m_Table = table != null ? table : ConfigItemTable.Instance;
        }

        public IItemTable Table { get { return m_Table; } }

        /// <summary>槽位变化通知（UI 订阅；含容器 Id 与下标）。</summary>
        public event Action<SlotRef> SlotDirty;

        /// <summary>手部法术槽容器 Id（**法术槽属于"手"，不属于某把法杖**：换杖时法术保留，槽数按新杖调整）。</summary>
        public static string HandSpellContainerId(string handContainerId)
        {
            if (string.Equals(handContainerId, ItemSystem.HandLeftId, StringComparison.Ordinal)) { return "HandSpells_L"; }
            if (string.Equals(handContainerId, ItemSystem.HandRightId, StringComparison.Ordinal)) { return "HandSpells_R"; }
            return null;
        }

        /// <summary>确保两只手的法术槽容器存在（可用槽数由手上的法杖决定）。</summary>
        public void EnsureHandSpellContainers()
        {
            EnsureHandSpellContainer(ItemSystem.HandLeftId, "左手法术槽");
            EnsureHandSpellContainer(ItemSystem.HandRightId, "右手法术槽");
        }

        private ItemContainer EnsureHandSpellContainer(string handId, string displayName)
        {
            string id = HandSpellContainerId(handId);
            if (id == null) { return null; }
            var existing = GetContainer(id);
            if (existing != null) { return existing; }
            var container = RegisterContainer(id, SlotKind.WandSpellSlots, MaxWandSlots, displayName);
            container.ActiveCapacity = 0;   // 没杖 = 没有可用槽
            return container;
        }

        // ==================== 容器注册 ====================

        public ItemContainer RegisterContainer(string containerId, SlotKind kind, int capacity,
            string displayName = null, int ownerInstanceId = 0, bool readOnly = false)
        {
            var c = new ItemContainer(containerId, kind, capacity, displayName, ownerInstanceId, readOnly);
            m_Containers[containerId] = c;
            return c;
        }

        public bool UnregisterContainer(string containerId)
        {
            return !string.IsNullOrEmpty(containerId) && m_Containers.Remove(containerId);
        }

        public ItemContainer GetContainer(string containerId)
        {
            if (string.IsNullOrEmpty(containerId)) { return null; }
            ItemContainer c;
            return m_Containers.TryGetValue(containerId, out c) ? c : null;
        }

        public List<ItemContainer> Containers { get { return new List<ItemContainer>(m_Containers.Values); } }

        /// <summary>法杖的法术槽容器 Id（按实例 Id 派生，生命周期与法杖实例相同）。</summary>
        public static string WandContainerId(int wandInstanceId) { return WandContainerPrefix + wandInstanceId; }

        // ==================== 法杖 ====================

        /// <summary>
        /// 创建法杖实例：分配唯一实例 Id、建法术槽容器、把出厂法术作为"随杖附赠的法术卡"预填进槽
        /// （不占背包）。返回的 ItemInstance 可直接放进手部或背包。
        /// </summary>
        public ItemInstance CreateWandInstance(int wandItemId)
        {
            var item = m_Table.GetItem(wandItemId);
            if (item == null || item.Category != ItemCategory.Wand || item.Wand == null)
            {
                return ItemInstance.Empty;
            }

            int instanceId = m_NextInstanceId++;
            int slotCount = Math.Max(1, item.Wand.SlotCount);

            // 说明：法术槽**不再随法杖实例创建**——法术槽属于"手"（HandSpells_L/R），
            // 换杖时法术原地保留、槽数按新杖调整（见 ReconcileHand）。这里只建实例 + 快照数组。
            var wand = ItemInstance.Of(wandItemId, 1, instanceId);
            int[] slots = new int[slotCount];
            for (int i = 0; i < slots.Length; i++) { slots[i] = 0; }
            wand = wand.WithSpellSlots(slots);
            return wand;
        }

        /// <summary>
        /// 把法杖装到手上（手上已有法杖则交换）。
        /// 换杖规则（需求）：法术槽中的法术**不随法杖移动**，保留给新法杖；
        /// 只有"新杖槽数装不下的那部分"才被挤回背包；背包放不下则整笔拒绝（原子）。
        /// </summary>
        public ItemMoveResult TryEquipWand(SlotRef wandSlot, string handContainerId)
        {
            var hand = GetContainer(handContainerId);
            if (hand == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }

            var newWand = GetContainer(wandSlot.ContainerId) != null ? GetContainer(wandSlot.ContainerId).Get(wandSlot.Index) : ItemInstance.Empty;
            var newRow = newWand.IsEmpty ? null : m_Table.GetItem(newWand.ItemId);
            if (newRow == null || newRow.Category != ItemCategory.Wand || newRow.Wand == null)
            {
                return ItemMoveResult.Fail(MoveReason.NotWand);
            }

            // 预检：换到槽数更少的杖时，多余法术必须能全部挤回背包
            var spells = EnsureHandSpellContainer(handContainerId, null);
            int newSlotCount = Math.Max(1, newRow.Wand.SlotCount);
            var overflow = CollectOverflow(spells, newSlotCount);
            if (overflow.Count > 0 && !CanFitAll(overflow, ItemSystem.BackpackId))
            {
                return ItemMoveResult.Fail(MoveReason.Full);
            }

            var move = TryMove(wandSlot, new SlotRef(handContainerId, 0));
            if (!move.Ok) { return move; }

            PushOverflow(handContainerId, newSlotCount);
            return move;
        }

        /// <summary>装填法术：背包里的法术卡 → 手部法术槽（实体卡：从背包移出）。</summary>
        public ItemMoveResult TryLoadSpell(SlotRef spellSlot, SlotRef handSpellSlot)
        {
            return TryMove(spellSlot, handSpellSlot);
        }

        // ==================== 事务：移动 / 分堆 / 增删 ====================

        /// <summary>移动的预演结果（TryMove 与 CanMove 共用同一套判定，保证 UI 高亮与真实结果一致）。</summary>
        private struct MoveEval
        {
            public MoveReason Reason;
            public ItemContainer Src;
            public ItemContainer Dst;
            public ItemInstance Moving;
            public ItemInstance Target;
            public int MoveCount;
            public bool WholeStack;
            public bool Merge;
            public bool Swap;
        }

        /// <summary>只判定不动数据：UI 用它决定插槽是否高亮/可放。</summary>
        public MoveReason CanMove(SlotRef from, SlotRef to, int count = 0)
        {
            return Evaluate(from, to, count).Reason;
        }

        /// <summary>判定移动是否合法（与 TryMove 完全同源，避免规则漂移）。</summary>
        private MoveEval Evaluate(SlotRef from, SlotRef to, int count)
        {
            var e = new MoveEval();
            e.Reason = MoveReason.Ok;
            e.Src = GetContainer(from.ContainerId);
            e.Dst = GetContainer(to.ContainerId);
            if (e.Src == null || e.Dst == null) { e.Reason = MoveReason.ContainerNotFound; return e; }
            if (!e.Src.InRange(from.Index) || !e.Dst.InRange(to.Index)) { e.Reason = MoveReason.IndexInvalid; return e; }
            if (e.Src.ReadOnly || e.Dst.ReadOnly) { e.Reason = MoveReason.ReadOnlySlot; return e; }
            if (from.Equals(to)) { e.Reason = MoveReason.SelfMove; return e; }

            // 法术槽：可用槽数由法杖决定（超出当前杖槽数的下标不可放）
            if (e.Dst.Kind == SlotKind.WandSpellSlots && !e.Dst.IsActiveIndex(to.Index))
            {
                e.Reason = MoveReason.IndexInvalid;
                return e;
            }

            e.Moving = e.Src.Get(from.Index);
            if (e.Moving.IsEmpty) { e.Reason = MoveReason.EmptySource; return e; }

            // 法杖离开手部：杖内（手部法术槽中）的法术必须能全部挤回背包，否则整笔拒绝
            if (e.Src.Kind == SlotKind.WandHand && e.Dst.Kind != SlotKind.WandHand)
            {
                var handSpells = EnsureHandSpellContainer(from.ContainerId, null);
                var handSpellsLoaded = CollectOverflow(handSpells, 0);   // 全部已装填法术
                if (handSpellsLoaded.Count > 0)
                {
                    string bagId = ResolveBackpackId(e.Dst);
                    if (!CanFitAll(handSpellsLoaded, bagId)) { e.Reason = MoveReason.Full; return e; }
                }
            }

            e.Target = e.Dst.Get(to.Index);

            int moveCount = count <= 0 ? e.Moving.Count : Math.Min(count, e.Moving.Count);
            // 法杖槽每格只放 1 张法术卡：拖整叠进去时只抽 1 张（其余留在原槽）
            if (e.Dst.Kind == SlotKind.WandSpellSlots && moveCount > 1) { moveCount = 1; }
            if (moveCount <= 0) { e.Reason = MoveReason.NothingToDo; return e; }

            e.MoveCount = moveCount;
            e.WholeStack = moveCount >= e.Moving.Count;
            var movingPart = e.Moving.WithCount(moveCount);

            MoveReason reason;
            if (!Accepts(e.Dst, movingPart, e.Src, out reason)) { e.Reason = reason; return e; }

            if (e.Target.IsEmpty)
            {
                return e;   // 放入
            }

            // 同类可堆叠 → 合并（法杖槽不参与堆叠）
            int stackMax = StackMaxOf(movingPart.ItemId);
            if (e.Dst.Kind != SlotKind.WandSpellSlots
                && e.Target.ItemId == movingPart.ItemId && stackMax > 1 && !movingPart.IsWand)
            {
                if (stackMax - e.Target.Count <= 0) { e.Reason = MoveReason.Full; return e; }
                e.Merge = true;
                return e;
            }

            // 目标被占 → 交换（要求整叠、且目标物品能放进来源槽）
            if (!e.WholeStack) { e.Reason = MoveReason.Full; return e; }
            if (!Accepts(e.Src, e.Target, e.Dst, out reason)) { e.Reason = reason; return e; }
            e.Swap = true;
            return e;
        }

        /// <summary>
        /// 移动物品。count=0 表示整叠；目标为空则放入（含堆叠合并），目标被占则尝试交换。
        /// </summary>
        public ItemMoveResult TryMove(SlotRef from, SlotRef to, int count = 0)
        {
            var e = Evaluate(from, to, count);
            if (e.Reason != MoveReason.Ok) { return ItemMoveResult.Fail(e.Reason); }

            var movingPart = e.Moving.WithCount(e.MoveCount);

            if (e.Merge)
            {
                int merged = Math.Min(StackMaxOf(movingPart.ItemId) - e.Target.Count, e.MoveCount);
                e.Dst.Set(to.Index, e.Target.WithCount(e.Target.Count + merged));
                e.Src.Set(from.Index, e.Moving.Count - merged > 0 ? e.Moving.WithCount(e.Moving.Count - merged) : ItemInstance.Empty);
                AfterChange(e.Src, from, e.Dst, to);
                return ItemMoveResult.Success(merged);
            }

            if (e.Swap)
            {
                e.Dst.Set(to.Index, movingPart);
                e.Src.Set(from.Index, e.Target);
                AfterChange(e.Src, from, e.Dst, to);
                return ItemMoveResult.Success(e.MoveCount);
            }

            // 放入空槽
            e.Dst.Set(to.Index, movingPart);
            e.Src.Set(from.Index, e.WholeStack ? ItemInstance.Empty : e.Moving.WithCount(e.Moving.Count - e.MoveCount));
            AfterChange(e.Src, from, e.Dst, to);
            return ItemMoveResult.Success(e.MoveCount);
        }

        /// <summary>分堆：把来源的一半（至少 1）放到空目标槽。</summary>
        public ItemMoveResult TrySplit(SlotRef from, SlotRef to)
        {
            var src = GetContainer(from.ContainerId);
            if (src == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }
            var item = src.Get(from.Index);
            if (item.IsEmpty) { return ItemMoveResult.Fail(MoveReason.EmptySource); }
            if (item.Count < 2 || item.IsWand) { return ItemMoveResult.Fail(MoveReason.NotStackable); }
            return TryMove(from, to, item.Count / 2);
        }

        /// <summary>放置物品（商店购买/奖励发放用；目标放不下则整笔失败，保证原子性）。</summary>
        public ItemMoveResult TryAdd(string containerId, int itemId, int count, int wandInstanceId = 0)
        {
            var dst = GetContainer(containerId);
            if (dst == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }
            if (dst.ReadOnly) { return ItemMoveResult.Fail(MoveReason.ReadOnlySlot); }
            if (itemId <= 0 || count <= 0) { return ItemMoveResult.Fail(MoveReason.NothingToDo); }

            var item = m_Table.GetItem(itemId);
            if (item == null) { return ItemMoveResult.Fail(MoveReason.NotBuyable); }

            var sample = wandInstanceId > 0
                ? ItemInstance.Of(itemId, count, wandInstanceId)
                : ItemInstance.Of(itemId, count);

            MoveReason reason;
            if (!Accepts(dst, sample, null, out reason)) { return ItemMoveResult.Fail(reason); }
            if (CapacityFor(dst, sample) < count) { return ItemMoveResult.Fail(MoveReason.Full); }

            int remaining = count;
            int stackMax = Math.Max(1, item.StackMax);
            if (stackMax > 1 && wandInstanceId <= 0)
            {
                for (int i = 0; i < dst.Capacity && remaining > 0; i++)
                {
                    var cur = dst.Get(i);
                    if (cur.IsEmpty || cur.ItemId != itemId) { continue; }
                    int free = stackMax - cur.Count;
                    if (free <= 0) { continue; }
                    int put = Math.Min(free, remaining);
                    dst.Set(i, cur.WithCount(cur.Count + put));
                    remaining -= put;
                }
            }
            for (int i = 0; i < dst.Capacity && remaining > 0; i++)
            {
                if (!dst.IsEmptyAt(i)) { continue; }
                int put = Math.Min(stackMax > 1 ? stackMax : 1, remaining);
                dst.Set(i, wandInstanceId > 0 ? ItemInstance.Of(itemId, put, wandInstanceId) : ItemInstance.Of(itemId, put));
                remaining -= put;
            }

            AfterChange(dst, new SlotRef(containerId, 0), null, default(SlotRef));
            return remaining == 0 ? ItemMoveResult.Success(count) : ItemMoveResult.Fail(MoveReason.Full);
        }

        /// <summary>
        /// 放置一个**完整实例**（保留 Buffs / 法杖 InstanceId 等字段；S3 / D14 / P5）。
        ///
        /// ⚠️ 为什么必须有这个方法：<see cref="TryAdd"/> 只能从 `itemId + count` **重建**实例，
        ///   而法术卡上的临时 Buff 是挂在实例上的（设计 §3.5 Q4：宿主 = ItemInstance）。
        ///   因此凡是"搬运**既有**物品"的路径（裁槽挤回背包、换杖挤回等）**必须**用本方法，
        ///   否则 buff 会在搬运中静默丢失 —— 这就是 P5「卡拖回背包后 buff 保留」的实现要点。
        ///   只有"凭空造新物品"（商店购买/开局发放/奖励）才该用 <see cref="TryAdd"/>。
        /// </summary>
        public ItemMoveResult TryAddInstance(string containerId, ItemInstance sample)
        {
            var dst = GetContainer(containerId);
            if (dst == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }
            if (dst.ReadOnly) { return ItemMoveResult.Fail(MoveReason.ReadOnlySlot); }
            if (sample.IsEmpty) { return ItemMoveResult.Fail(MoveReason.NothingToDo); }

            var row = m_Table.GetItem(sample.ItemId);
            if (row == null) { return ItemMoveResult.Fail(MoveReason.NotBuyable); }

            MoveReason reason;
            if (!Accepts(dst, sample, null, out reason)) { return ItemMoveResult.Fail(reason); }

            int stackMax = Math.Max(1, row.StackMax);
            int remaining = sample.Count;

            // 先并入同 Id 的已有堆（仅可堆叠物品）。
            // P4 已定"法术卡不可堆叠（StackMax=1）"，所以携带 Buff 的卡**不会**走到这里；
            // 若将来恢复可堆叠物品，按叠加规则合并 buff（设计 §3.4）并计入哈希。
            if (stackMax > 1 && !sample.IsWand)
            {
                for (int i = 0; i < dst.Capacity && remaining > 0; i++)
                {
                    var cur = dst.Get(i);
                    if (cur.IsEmpty || cur.ItemId != sample.ItemId) { continue; }
                    int free = stackMax - cur.Count;
                    if (free <= 0) { continue; }
                    int put = Math.Min(free, remaining);

                    var mergedBuff = cur.Buffs;
                    if (sample.HasBuffs)
                    {
                        var lim = BuffLimits.FromConfig();
                        for (int b = 0; b < sample.Buffs.Count; b++)
                        {
                            mergedBuff = BuffRuntime.Apply(mergedBuff, sample.Buffs.At(b), lim);
                        }
                    }
                    dst.Set(i, cur.WithCount(cur.Count + put).WithBuffs(mergedBuff));
                    remaining -= put;
                }
            }

            for (int i = 0; i < dst.Capacity && remaining > 0; i++)
            {
                if (!dst.IsEmptyAt(i)) { continue; }
                int put = Math.Min(stackMax, remaining);
                // 关键：保留整份实例（含 Buffs），只按 put 调整数量
                var placed = (put == sample.Count) ? sample : sample.WithCount(put);
                dst.Set(i, placed);
                remaining -= put;
            }

            AfterChange(dst, new SlotRef(containerId, 0), null, default(SlotRef));
            return remaining == 0 ? ItemMoveResult.Success(sample.Count) : ItemMoveResult.Fail(MoveReason.Full);
        }

        /// <summary>移除数量（不足则整叠移除）。</summary>
        public ItemMoveResult TryRemove(SlotRef slot, int count)
        {
            var c = GetContainer(slot.ContainerId);
            if (c == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }
            if (!c.InRange(slot.Index)) { return ItemMoveResult.Fail(MoveReason.IndexInvalid); }
            if (c.ReadOnly) { return ItemMoveResult.Fail(MoveReason.ReadOnlySlot); }
            var item = c.Get(slot.Index);
            if (item.IsEmpty) { return ItemMoveResult.Fail(MoveReason.EmptySource); }

            int removed = count <= 0 ? item.Count : Math.Min(count, item.Count);
            c.Set(slot.Index, item.Count - removed > 0 ? item.WithCount(item.Count - removed) : ItemInstance.Empty);
            AfterChange(c, slot, null, default(SlotRef));
            return ItemMoveResult.Success(removed);
        }

        // ==================== 事务：买卖 ====================

        /// <summary>购买商店货架上的物品：钱够 + 背包放得下才成交（整笔原子）。</summary>
        public ItemMoveResult TryBuy(string shopContainerId, int index, IWallet wallet, string backpackContainerId = "Backpack")
        {
            var shop = GetContainer(shopContainerId);
            if (shop == null || wallet == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }
            var offer = shop.Get(index);
            if (offer.IsEmpty) { return ItemMoveResult.Fail(MoveReason.EmptySource); }

            var item = m_Table.GetItem(offer.ItemId);
            if (item == null) { return ItemMoveResult.Fail(MoveReason.NotBuyable); }

            var pack = GetContainer(backpackContainerId);
            if (pack == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }

            MoveReason reason;
            if (!Accepts(pack, offer, null, out reason)) { return ItemMoveResult.Fail(reason); }
            if (CapacityFor(pack, offer) < offer.Count) { return ItemMoveResult.Fail(MoveReason.Full); }
            if (wallet.Coin < item.Price) { return ItemMoveResult.Fail(MoveReason.NotEnoughCoin); }
            if (!wallet.TrySpend(item.Price)) { return ItemMoveResult.Fail(MoveReason.NotEnoughCoin); }

            var added = TryAdd(backpackContainerId, offer.ItemId, offer.Count, offer.InstanceId);
            if (!added.Ok)
            {
                wallet.Add(item.Price);   // 回滚（理论上不会发生：前面已校验容量）
                return ItemMoveResult.Fail(MoveReason.Full);
            }
            shop.Set(index, ItemInstance.Empty);   // 商品一次性（买走即下架）
            AfterChange(shop, new SlotRef(shopContainerId, index), null, default(SlotRef));
            return ItemMoveResult.Success(added.MovedCount);
        }

        /// <summary>
        /// 出售：整叠卖出（法杖则连同杖内法术卡退回背包，退不下则拒绝交易）。
        /// </summary>
        public ItemMoveResult TrySell(SlotRef slot, IWallet wallet, string backpackContainerId = "Backpack")
        {
            var c = GetContainer(slot.ContainerId);
            if (c == null || wallet == null) { return ItemMoveResult.Fail(MoveReason.ContainerNotFound); }
            if (!c.InRange(slot.Index)) { return ItemMoveResult.Fail(MoveReason.IndexInvalid); }
            var item = c.Get(slot.Index);
            if (item.IsEmpty) { return ItemMoveResult.Fail(MoveReason.EmptySource); }

            var row = m_Table.GetItem(item.ItemId);
            if (row == null) { return ItemMoveResult.Fail(MoveReason.NotSellable); }
            if ((row.Flags & ItemFlags.Sellable) == 0) { return ItemMoveResult.Fail(MoveReason.NotSellable); }

            // 法杖：法术槽里的法术属于"手"，卖杖本身不动法术；
            // 若这把杖正握在手上，AfterChange → ReconcileHand 会把手上法术槽裁到 0 并挤回背包。
            // 预检：手上这把杖的法术必须放得下，否则拒绝交易（原子）。
            var spells = new List<ItemInstance>();
            string handId = c.Kind == SlotKind.WandHand ? c.ContainerId : null;
            if (item.IsWand && handId != null)
            {
                var handSpells = EnsureHandSpellContainer(handId, null);
                spells = CollectOverflow(handSpells, 0);
                if (spells.Count > 0 && !CanFitAll(spells, backpackContainerId))
                {
                    return ItemMoveResult.Fail(MoveReason.Full);
                }
            }

            for (int i = 0; i < spells.Count; i++)
            {
                TryAdd(backpackContainerId, spells[i].ItemId, spells[i].Count);
            }
            if (handId != null)
            {
                var handSpells = GetContainer(HandSpellContainerId(handId));
                if (handSpells != null)
                {
                    for (int i = 0; i < handSpells.Capacity; i++) { handSpells.Set(i, ItemInstance.Empty); }
                    handSpells.ActiveCapacity = 0;
                }
            }

            c.Set(slot.Index, ItemInstance.Empty);
            wallet.Add(Math.Max(0, row.SellPrice) * Math.Max(1, item.Count));
            AfterChange(c, slot, null, default(SlotRef));
            return ItemMoveResult.Success(item.Count);
        }

        // ==================== 内部：接受规则与容量 ====================

        /// <summary>目标容器是否接受该物品（source 用于"交换时反向校验"，可为 null）。</summary>
        private bool Accepts(ItemContainer target, ItemInstance item, ItemContainer source, out MoveReason reason)
        {
            reason = MoveReason.Ok;
            if (target == null) { reason = MoveReason.ContainerNotFound; return false; }
            if (target.ReadOnly || target.Kind == SlotKind.Shop) { reason = MoveReason.ReadOnlySlot; return false; }
            if (item.IsEmpty) { reason = MoveReason.EmptySource; return false; }

            var row = m_Table.GetItem(item.ItemId);
            if (row == null) { reason = MoveReason.NotBuyable; return false; }

            switch (target.Kind)
            {
                case SlotKind.Backpack:
                    return true;

                case SlotKind.WandHand:
                    if (row.Category != ItemCategory.Wand) { reason = MoveReason.NotWand; return false; }
                    return true;

                case SlotKind.WandSpellSlots:
                    if (row.Category != ItemCategory.Spell) { reason = MoveReason.NotSpell; return false; }
                    if (item.IsWand) { reason = MoveReason.TypeMismatch; return false; }
                    return true;

                default:
                    reason = MoveReason.TypeMismatch;
                    return false;
            }
        }

        private int StackMaxOf(int itemId)
        {
            var row = m_Table.GetItem(itemId);
            return row != null ? Math.Max(1, row.StackMax) : 1;
        }

        /// <summary>容器还能容纳多少个该物品（考虑堆叠余量；法杖槽每格 1 张、不堆叠）。</summary>
        private int CapacityFor(ItemContainer container, ItemInstance item)
        {
            if (container == null || item.IsEmpty) { return 0; }
            if (container.Kind == SlotKind.WandSpellSlots) { return container.EmptyCount(); }
            int stackMax = item.IsWand ? 1 : StackMaxOf(item.ItemId);
            int room = 0;
            if (stackMax > 1 && !item.IsWand)
            {
                for (int i = 0; i < container.Capacity; i++)
                {
                    var cur = container.Get(i);
                    if (!cur.IsEmpty && cur.ItemId == item.ItemId) { room += Math.Max(0, stackMax - cur.Count); }
                }
            }
            int emptySlots = container.EmptyCount();
            room += emptySlots * (item.IsWand ? 1 : stackMax);
            return room;
        }

        /// <summary>移动完成后的收尾：手部法术槽随法杖对账（裁槽/挤回背包/快照）+ 抛槽位事件。</summary>
        private void AfterChange(ItemContainer src, SlotRef srcRef, ItemContainer dst, SlotRef dstRef)
        {
            // 手部（左/右）的杖变了 → 对账它的法术槽
            if (src != null && src.Kind == SlotKind.WandHand) { ReconcileHand(src.ContainerId); }
            if (dst != null && dst.Kind == SlotKind.WandHand) { ReconcileHand(dst.ContainerId); }

            Notify(srcRef);
            if (dst != null) { Notify(dstRef); }
        }

        private void Notify(SlotRef slot)
        {
            if (!slot.IsValid) { return; }
            var h = SlotDirty;
            if (h != null) { h(slot); }
        }

        // ==================== 手部法术槽对账 ====================

        /// <summary>
        /// 按手上当前法杖重算可用槽数：
        /// · 新槽数 ≥ 原槽数：法术原地保留（保留给新法杖），只是多出空槽；
        /// · 新槽数 &lt; 原槽数：装不下的法术挤回背包；背包放不下时保留原位并播报（上层应先预检）。
        /// </summary>
        private void ReconcileHand(string handContainerId)
        {
            var hand = GetContainer(handContainerId);
            if (hand == null) { return; }
            var spells = EnsureHandSpellContainer(handContainerId, null);
            if (spells == null) { return; }

            var wand = hand.Get(0);
            int active = 0;
            if (!wand.IsEmpty)
            {
                var row = m_Table.GetItem(wand.ItemId);
                if (row != null && row.Category == ItemCategory.Wand && row.Wand != null)
                {
                    active = Math.Max(0, Math.Min(MaxWandSlots, row.Wand.SlotCount));
                }
            }

            if (active < spells.ActiveCapacity)
            {
                PushOverflow(handContainerId, active);
            }
            spells.ActiveCapacity = active;
            SyncHandSpells(handContainerId);
            Notify(new SlotRef(spells.ContainerId, 0));
        }

        /// <summary>手动对账某只手（外部直接改了法术槽内容后调用，用于刷新快照与可用槽数）。</summary>
        public void ResyncHand(string handContainerId)
        {
            ReconcileHand(handContainerId);
        }

        /// <summary>把可用槽数之外的法术挤回背包（就地清空这些槽）。</summary>
        private void PushOverflow(string handContainerId, int keepSlots)
        {
            var spells = GetContainer(HandSpellContainerId(handContainerId));
            if (spells == null) { return; }
            for (int i = Math.Max(0, keepSlots); i < spells.Capacity; i++)
            {
                var item = spells.Get(i);
                if (item.IsEmpty) { continue; }
                // S3/D14/P5：**必须用 TryAddInstance**（保留 buff）。用 TryAdd(id, count) 会把
                // 法术卡上的临时 Buff 静默丢掉 —— 这正是"挤回背包后 buff 消失"的根因。
                var added = TryAddInstance(ItemSystem.BackpackId, item);
                if (added.Ok)
                {
                    spells.Set(i, ItemInstance.Empty);
                }
                else
                {
                    WriteProbe(string.Format("[items] 法术槽溢出但背包放不下：{0} 槽{1} {2}", handContainerId, i, item));
                }
            }
        }

        /// <summary>收集会因裁到 keepSlots 而失去槽位的法术。</summary>
        private List<ItemInstance> CollectOverflow(ItemContainer spells, int keepSlots)
        {
            var list = new List<ItemInstance>();
            if (spells == null) { return list; }
            for (int i = Math.Max(0, keepSlots); i < spells.Capacity; i++)
            {
                var item = spells.Get(i);
                if (!item.IsEmpty) { list.Add(item); }
            }
            return list;
        }

        private bool CanFitAll(List<ItemInstance> items, string containerId)
        {
            var pack = GetContainer(containerId);
            if (pack == null) { return false; }
            int need = 0, room = 0;
            for (int i = 0; i < items.Count; i++)
            {
                need += items[i].Count;
                room += CapacityFor(pack, items[i]);
            }
            return room >= need;
        }

        /// <summary>把手上法杖的 SpellSlots 快照写成手部法术槽的内容（供 LoadoutCompiler/存档）。</summary>
        private void SyncHandSpells(string handContainerId)
        {
            var hand = GetContainer(handContainerId);
            var spells = GetContainer(HandSpellContainerId(handContainerId));
            if (hand == null || spells == null) { return; }
            var wand = hand.Get(0);
            if (wand.IsEmpty) { return; }

            int count = spells.ActiveCapacity;
            int[] slots = new int[count];
            for (int i = 0; i < count; i++)
            {
                var s = spells.Get(i);
                slots[i] = s.IsEmpty ? 0 : s.ItemId;
            }
            hand.Set(0, wand.WithSpellSlots(slots));
        }

        /// <summary>背包容器 Id：优先用目标容器若它本身就是背包，否则用标准背包。</summary>
        private string ResolveBackpackId(ItemContainer dst)
        {
            if (dst != null && dst.Kind == SlotKind.Backpack) { return dst.ContainerId; }
            return ItemSystem.BackpackId;
        }

        private void WriteProbe(string message)
        {
            try
            {
                string path = System.IO.Path.Combine(UnityEngine.Application.dataPath,
                    "../Logs/runtime_probe_" + System.Diagnostics.Process.GetCurrentProcess().Id + ".txt");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.AppendAllText(path, message + "\n");
            }
            catch
            {
            }
        }

        /// <summary>在所有容器里找持有该实例 Id 的槽（法杖实例）。</summary>
        public bool TryFindHostSlot(int instanceId, out SlotRef host)
        {
            host = default(SlotRef);
            if (instanceId <= 0) { return false; }
            foreach (var kv in m_Containers)
            {
                var container = kv.Value;
                if (container.Kind == SlotKind.WandSpellSlots) { continue; }
                int idx = container.IndexOfInstance(instanceId);
                if (idx >= 0)
                {
                    host = new SlotRef(container.ContainerId, idx);
                    return true;
                }
            }
            return false;
        }
    }
}
