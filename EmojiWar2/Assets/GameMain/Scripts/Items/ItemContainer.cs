//------------------------------------------------------------
// EmojiWar GameMain - 物品容器（P2）
//
// 方案：doc/物品与法杖系统设计.md §3
//   - 固定槽容器：背包(30) / 手部(左右各 1) / 法杖内部法术槽(每把法杖一个) / 商店货架；
//   - 只发"哪个槽变了"（SlotChanged），UI 自己拉取渲染 —— 避免事件风暴与 UI 持有数据；
//   - 法杖槽容器是**法杖实例法术槽的投影**：变化后由 InventoryService 回写法杖实例（单一事实源=容器）。
//------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace EmojiWar.GameMain.Items
{
    /// <summary>定长槽容器。</summary>
    public sealed class ItemContainer
    {
        private readonly ItemInstance[] m_Slots;

        public ItemContainer(string containerId, SlotKind kind, int capacity,
            string displayName = null, int ownerInstanceId = 0, bool readOnly = false)
        {
            if (string.IsNullOrEmpty(containerId)) { throw new ArgumentException("containerId 不能为空"); }
            if (capacity <= 0) { throw new ArgumentException("capacity 必须 > 0"); }
            ContainerId = containerId;
            Kind = kind;
            DisplayName = string.IsNullOrEmpty(displayName) ? containerId : displayName;
            OwnerInstanceId = ownerInstanceId;
            ReadOnly = readOnly;
            m_Slots = new ItemInstance[capacity];
            ActiveCapacity = capacity;   // 默认全槽可用；法杖法术槽由服务改为按杖槽数
        }

        public string ContainerId { get; private set; }
        public SlotKind Kind { get; private set; }
        public string DisplayName { get; private set; }
        /// <summary>WandSpellSlots：该容器归属的法杖实例 Id（其它种类为 0）。</summary>
        public int OwnerInstanceId { get; private set; }
        public bool ReadOnly { get; set; }
        public int Capacity { get { return m_Slots.Length; } }

        /// <summary>
        /// 当前"可用槽数"（≤ Capacity）。法杖法术槽用它表达"槽数由法杖决定、随时可变"：
        /// 换杖时由 InventoryService 调整，超出的法术会被挤回背包。
        /// </summary>
        public int ActiveCapacity { get; set; }

        /// <summary>该下标是否在可用槽范围内（法术槽的越界判定用它）。</summary>
        public bool IsActiveIndex(int index) { return InRange(index) && index < ActiveCapacity; }

        /// <summary>某个槽变化（index）——UI 拉取渲染。</summary>
        public event Action<int> SlotChanged;
        /// <summary>容器有任何变化（商店重排/统计用）。</summary>
        public event Action Changed;

        public bool InRange(int index) { return index >= 0 && index < m_Slots.Length; }

        public ItemInstance Get(int index) { return InRange(index) ? m_Slots[index] : ItemInstance.Empty; }

        public bool IsEmptyAt(int index) { return !InRange(index) || m_Slots[index].IsEmpty; }

        /// <summary>写入槽位（仅 InventoryService 调用；会做范围/引用清理）。</summary>
        public bool Set(int index, ItemInstance item, bool notify = true)
        {
            if (!InRange(index)) { return false; }
            m_Slots[index] = item.Count > 0 && item.ItemId > 0 ? item : ItemInstance.Empty;
            if (notify)
            {
                var h = SlotChanged;
                if (h != null) { h(index); }
                var c = Changed;
                if (c != null) { c(); }
            }
            return true;
        }

        public int FirstEmptyIndex()
        {
            for (int i = 0; i < m_Slots.Length; i++) { if (m_Slots[i].IsEmpty) { return i; } }
            return -1;
        }

        public int EmptyCount()
        {
            int n = 0;
            for (int i = 0; i < m_Slots.Length; i++) { if (m_Slots[i].IsEmpty) { n++; } }
            return n;
        }

        /// <summary>按实例 Id 找槽（法杖用）。</summary>
        public int IndexOfInstance(int instanceId)
        {
            if (instanceId <= 0) { return -1; }
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (!m_Slots[i].IsEmpty && m_Slots[i].InstanceId == instanceId) { return i; }
            }
            return -1;
        }

        /// <summary>指定物品在容器内的总数量（含堆叠）。</summary>
        public int CountOf(int itemId)
        {
            int n = 0;
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (m_Slots[i].ItemId == itemId) { n += m_Slots[i].Count; }
            }
            return n;
        }

        /// <summary>快照（探针/自检打印用；返回副本，外部改动不影响容器）。</summary>
        public ItemInstance[] Snapshot()
        {
            var copy = new ItemInstance[m_Slots.Length];
            Array.Copy(m_Slots, copy, m_Slots.Length);
            return copy;
        }

        public string Dump()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(ContainerId).Append('(').Append(Kind).Append(",cap=").Append(Capacity).Append(')');
            for (int i = 0; i < m_Slots.Length; i++)
            {
                if (m_Slots[i].IsEmpty) { continue; }
                sb.Append(" [").Append(i).Append(']').Append(m_Slots[i].ToString());
            }
            return sb.ToString();
        }
    }
}
